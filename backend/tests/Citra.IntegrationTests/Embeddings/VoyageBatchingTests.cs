using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Citra.Application.Common.Embeddings;
using Citra.Domain.Documents;
using Citra.Infrastructure.Chunking;
using Citra.Infrastructure.Embeddings;
using Citra.Infrastructure.Options;

namespace Citra.IntegrationTests.Embeddings;

/// <summary>Batching: many chunks per request, limited by count and tokens, results kept in order.</summary>
public sealed class VoyageBatchingTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly TiktokenTokenCounter Tokens = new();

    [Fact]
    public async Task Sends_many_chunks_per_request_and_keeps_the_order()
    {
        var server = new EchoServer();
        var texts = Texts(150);

        var result = await Service(server, batchSize: 64).EmbedDocumentsAsync(texts, Ct);

        server.Batches.Select(b => b.Count).ShouldBe([64, 64, 22]);
        server.Batches.SelectMany(b => b).ShouldBe(texts, "every text sent once, in order");
        server.InputTypes.ShouldAllBe(t => t == "document");
        result.Vectors.Select(v => (int)v[0]).ShouldBe(Enumerable.Range(0, 150), "vector i belongs to text i");
        result.TotalTokens.ShouldBe(150 * EchoServer.TokensPerText, "token usage of all batches is summed");
    }

    [Theory]
    [InlineData(1, new[] { 1 })]
    [InlineData(64, new[] { 64 })]
    [InlineData(65, new[] { 64, 1 })]
    [InlineData(128, new[] { 64, 64 })]
    public async Task Batch_boundaries_are_exact(int count, int[] expected)
    {
        var server = new EchoServer();

        await Service(server, batchSize: 64).EmbedDocumentsAsync(Texts(count), Ct);

        server.Batches.Select(b => b.Count).ShouldBe(expected);
    }

    [Fact]
    public async Task Requests_stay_within_the_token_budget()
    {
        var server = new EchoServer();
        var paragraph = string.Join(" ", Enumerable.Repeat("employees receive clear guidance on leave", 40));
        var texts = Enumerable.Range(0, 20).Select(i => $"t{i} {paragraph}").ToList();
        var perText = Tokens.Count(texts[0]);

        var result = await Service(server, batchSize: 64, maxTokens: 3 * perText + 5).EmbedDocumentsAsync(texts, Ct);

        server.Batches.Select(b => b.Count).ShouldBe([3, 3, 3, 3, 3, 3, 2]);
        server.Batches.ShouldAllBe(b => b.Sum(Tokens.Count) <= 3 * perText + 5);
        result.Vectors.Select(v => (int)v[0]).ShouldBe(Enumerable.Range(0, 20));
    }

    [Fact]
    public async Task A_text_bigger_than_the_budget_goes_alone()
    {
        var server = new EchoServer();
        var huge = "t1 " + string.Join(" ", Enumerable.Repeat("a very long chunk of text", 400));
        var texts = new List<string> { "t0 small", huge, "t2 small", "t3 small" };

        var result = await Service(server, batchSize: 64, maxTokens: 1_000).EmbedDocumentsAsync(texts, Ct);

        server.Batches.Select(b => b.Count).ShouldBe([1, 1, 2]);
        server.Batches[1].ShouldBe([huge]);
        result.Vectors.Select(v => (int)v[0]).ShouldBe([0, 1, 2, 3]);
    }

    [Fact]
    public async Task A_failing_batch_fails_the_whole_call_and_stops_sending()
    {
        var server = new EchoServer(failOnRequest: 2);

        var error = await Should.ThrowAsync<EmbeddingException>(
            () => Service(server, batchSize: 10).EmbedDocumentsAsync(Texts(35), Ct));

        error.Message.ShouldContain("server error (500)");
        server.Batches.Count.ShouldBe(2, "batches go one after another; nothing is sent after a failure");
    }

    [Fact]
    public async Task A_batch_with_a_missing_vector_fails_the_call()
    {
        var server = new EchoServer(dropOneVectorOnRequest: 2);

        var error = await Should.ThrowAsync<EmbeddingException>(
            () => Service(server, batchSize: 10).EmbedDocumentsAsync(Texts(30), Ct));

        error.Message.ShouldContain("wrong inputs");
    }

    [Fact]
    public async Task Cancelling_between_batches_stops_further_requests()
    {
        using var cancel = new CancellationTokenSource();
        var server = new EchoServer(afterRequest: n => { if (n == 1) { cancel.Cancel(); } });

        await Should.ThrowAsync<OperationCanceledException>(
            () => Service(server, batchSize: 10).EmbedDocumentsAsync(Texts(50), cancel.Token));

        server.Batches.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_question_is_one_request_regardless_of_batch_settings()
    {
        var server = new EchoServer();

        await Service(server, batchSize: 1, maxTokens: 1_000).EmbedQueryAsync("t0 How many vacation days?", Ct);

        server.Batches.ShouldHaveSingleItem().ShouldBe(["t0 How many vacation days?"]);
        server.InputTypes.ShouldBe(["query"]);
    }

    // ---------- helpers ----------

    private static List<string> Texts(int count) => Enumerable.Range(0, count).Select(i => $"t{i} chunk text").ToList();

    private static VoyageEmbeddingService Service(EchoServer server, int batchSize, int maxTokens = 120_000) =>
        new(
            new HttpClient(server) { BaseAddress = new Uri("https://voyage.test/v1/") },
            Microsoft.Extensions.Options.Options.Create(new VoyageOptions
            {
                ApiKey = "pa-test-key",
                BaseUrl = "https://voyage.test/v1/",
                EmbeddingModel = "voyage-3.5",
                EmbeddingDimensions = Chunk.EmbeddingDimensions,
                BatchSize = batchSize,
                MaxTokensPerRequest = maxTokens,
            }),
            Tokens);

    /// <summary>
    /// Answers each request with one vector per input, filled with the number in the text ("t17 ..." → 17), so the
    /// test can tell which vector belongs to which text. Records every batch it receives.
    /// </summary>
    private sealed class EchoServer(int? failOnRequest = null, int? dropOneVectorOnRequest = null, Action<int>? afterRequest = null)
        : HttpMessageHandler
    {
        public const int TokensPerText = 3;

        public List<List<string>> Batches { get; } = [];

        public List<string> InputTypes { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
            var inputs = body["input"]!.AsArray().Select(n => n!.GetValue<string>()).ToList();
            Batches.Add(inputs);
            InputTypes.Add(body["input_type"]!.GetValue<string>());
            var number = Batches.Count;
            afterRequest?.Invoke(number);

            if (number == failOnRequest)
            {
                return new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("") };
            }

            var results = inputs.Select((text, index) => new
            {
                @object = "embedding",
                embedding = Enumerable.Repeat((float)int.Parse(text[1..text.IndexOf(' ')]), Chunk.EmbeddingDimensions),
                index,
            }).ToList();
            if (number == dropOneVectorOnRequest)
            {
                results.RemoveAt(results.Count - 1);
            }

            var json = JsonSerializer.Serialize(new
            {
                @object = "list",
                data = results,
                model = "voyage-3.5",
                usage = new { total_tokens = inputs.Count * TokensPerText },
            });
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}

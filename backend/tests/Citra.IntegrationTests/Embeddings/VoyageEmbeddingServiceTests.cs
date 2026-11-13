using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Citra.Application.Common.Embeddings;
using Citra.Domain.Documents;
using Citra.Infrastructure.Embeddings;
using Citra.Infrastructure.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Citra.IntegrationTests.Embeddings;

/// <summary>The Voyage AI client, mostly against a fake HTTP handler, plus one real smoke test.</summary>
public sealed class VoyageEmbeddingServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const int Dimensions = Chunk.EmbeddingDimensions;

    // ---------- the request ----------

    [Fact]
    public async Task Sends_the_documented_request()
    {
        var handler = FakeHandler.Returning(SuccessBody(2, tokens: 12));
        var service = Service(handler);

        await service.EmbedDocumentsAsync(["First text.", "Second text."], Ct);

        var request = handler.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Post);
        request.Uri.ShouldBe(new Uri("https://voyage.test/v1/embeddings"));
        request.Authorization.ShouldBe("Bearer pa-test-key");
        var body = JsonNode.Parse(request.Body)!.AsObject();
        body["input"]!.AsArray().Select(n => n!.GetValue<string>()).ShouldBe(["First text.", "Second text."]);
        body["model"]!.GetValue<string>().ShouldBe("voyage-3.5");
        body["output_dimension"]!.GetValue<int>().ShouldBe(Dimensions);
        body["truncation"]!.GetValue<bool>().ShouldBeTrue();
    }

    [Fact]
    public async Task Chunks_are_sent_as_documents_and_questions_as_queries()
    {
        var documents = FakeHandler.Returning(SuccessBody(2));
        var queries = FakeHandler.Returning(SuccessBody(1));

        await Service(documents).EmbedDocumentsAsync(["Chunk one.", "Chunk two."], Ct);
        await Service(queries).EmbedQueryAsync("How many vacation days?", Ct);

        InputType(documents.Requests.ShouldHaveSingleItem()).ShouldBe("document");
        InputType(queries.Requests.ShouldHaveSingleItem()).ShouldBe("query");
        var query = JsonNode.Parse(queries.Requests[0].Body)!.AsObject();
        query["input"]!.AsArray().Select(n => n!.GetValue<string>()).ShouldBe(["How many vacation days?"]);
    }

    [Fact]
    public async Task Nothing_to_embed_means_no_request()
    {
        var handler = FakeHandler.Returning(SuccessBody(0));

        var result = await Service(handler).EmbedDocumentsAsync([], Ct);

        result.Vectors.ShouldBeEmpty();
        handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Blank_texts_are_refused_before_sending()
    {
        var handler = FakeHandler.Returning(SuccessBody(2));

        await Should.ThrowAsync<ArgumentException>(() => Service(handler).EmbedDocumentsAsync(["ok", "  "], Ct));
        await Should.ThrowAsync<ArgumentException>(() => Service(handler).EmbedQueryAsync("", Ct));
        handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_missing_api_key_is_explained_without_calling_voyage()
    {
        var handler = FakeHandler.Returning(SuccessBody(1));

        var error = await Should.ThrowAsync<EmbeddingException>(
            () => Service(handler, apiKey: "").EmbedDocumentsAsync(["text"], Ct));

        error.Message.ShouldContain("API key is not configured");
        error.Message.ShouldContain("dotnet user-secrets set");
        handler.Requests.ShouldBeEmpty();
    }

    // ---------- the response ----------

    [Fact]
    public async Task Returns_vectors_in_input_order_even_if_voyage_lists_them_differently()
    {
        // Three results listed as index 2, 0, 1; each vector is filled with its index so we can tell them apart.
        var body = Response([2, 0, 1], tokens: 30);
        var service = Service(FakeHandler.Returning(body));

        var result = await service.EmbedDocumentsAsync(["a", "b", "c"], Ct);

        result.Vectors.Select(v => v[0]).ShouldBe([0f, 1f, 2f]);
        result.Vectors.ShouldAllBe(v => v.Length == Dimensions);
        result.TotalTokens.ShouldBe(30);
    }

    [Fact]
    public async Task A_question_gets_one_vector_and_its_token_count()
    {
        var result = await Service(FakeHandler.Returning(SuccessBody(1, tokens: 4))).EmbedQueryAsync("How many vacation days?", Ct);

        result.Vector.Length.ShouldBe(Dimensions);
        result.TotalTokens.ShouldBe(4);
    }

    [Theory]
    [InlineData("missing", "wrong inputs")]          // 2 texts, 1 result
    [InlineData("duplicate", "wrong inputs")]        // indexes 0, 0
    [InlineData("small", "instead of 1024")]         // vectors of 512
    [InlineData("null-vector", "instead of 1024")]
    [InlineData("no-data", "empty response")]
    [InlineData("not-json", "could not be read")]
    public async Task Rejects_broken_responses(string kind, string expected)
    {
        var body = kind switch
        {
            "missing" => Response([0]),
            "duplicate" => Response([0, 0]),
            "small" => Response([0, 1], dimensions: 512),
            "null-vector" => """{"object":"list","data":[{"object":"embedding","embedding":null,"index":0},{"object":"embedding","embedding":null,"index":1}],"model":"voyage-3.5","usage":{"total_tokens":3}}""",
            "no-data" => """{"object":"list","model":"voyage-3.5"}""",
            _ => "<html>Bad gateway</html>",
        };

        var error = await Should.ThrowAsync<EmbeddingException>(
            () => Service(FakeHandler.Returning(body)).EmbedDocumentsAsync(["a", "b"], Ct));

        error.Message.ShouldContain(expected);
    }

    // ---------- errors ----------

    [Theory]
    [InlineData(401, """{"detail":"Provided API key is invalid."}""", "rejected the API key (401): Provided API key is invalid.")]
    [InlineData(429, """{"detail":"You have exceeded your rate limit."}""", "rate limit reached (429): You have exceeded your rate limit.")]
    [InlineData(500, "", "had a server error (500).")]
    [InlineData(503, "<html>down</html>", "had a server error (503).")]
    [InlineData(400, """{"detail":"Input too long."}""", "refused the request (400): Input too long.")]
    public async Task Explains_http_errors(int status, string body, string expected)
    {
        var handler = FakeHandler.Returning(body, (HttpStatusCode)status);

        var error = await Should.ThrowAsync<EmbeddingException>(() => Service(handler).EmbedDocumentsAsync(["a"], Ct));

        error.Message.ShouldBe($"Voyage AI {expected}");
    }

    [Fact]
    public async Task Network_problems_and_timeouts_become_embedding_errors()
    {
        var unreachable = await Should.ThrowAsync<EmbeddingException>(() =>
            Service(FakeHandler.Throwing(new HttpRequestException("connection refused"))).EmbedDocumentsAsync(["a"], Ct));
        unreachable.Message.ShouldContain("could not be reached");

        var slow = await Should.ThrowAsync<EmbeddingException>(() =>
            Service(FakeHandler.Throwing(new TaskCanceledException("timeout"))).EmbedDocumentsAsync(["a"], Ct));
        slow.Message.ShouldContain("did not respond in time");
    }

    [Fact]
    public async Task Cancelling_stops_the_call_without_turning_it_into_an_error()
    {
        using var cancel = new CancellationTokenSource();
        await cancel.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            Service(FakeHandler.Returning(SuccessBody(1))).EmbedDocumentsAsync(["a"], cancel.Token));
    }

    // ---------- settings ----------

    [Theory]
    [InlineData(1024, true)]
    [InlineData(512, false)]
    [InlineData(2048, false)]
    public void Only_the_database_vector_size_is_accepted(int dimensions, bool valid)
    {
        var options = Options(dimensions: dimensions);

        var errors = options.Validate(new ValidationContext(options)).ToList();

        errors.Count.ShouldBe(valid ? 0 : 1);
        if (!valid)
        {
            errors[0].ErrorMessage!.ShouldContain("must be 1024");
        }
    }

    // ---------- the real thing (opt-in: CITRA_REAL_API_TESTS=1) ----------

    [Fact]
    public async Task Real_voyage_embeds_documents_and_queries_differently()
    {
        var apiKey = RealVoyage.ApiKey();
        Assert.SkipWhen(apiKey is null, RealVoyage.SkipReason);

        // Exactly as the app builds it (typed client, retries, settings); only the key comes from user-secrets.
        await using var provider = RealVoyage.Services(apiKey!);
        var service = provider.GetRequiredService<IEmbeddingService>();
        const string text = "How many vacation days do employees get?";
        const string unrelated = "The warehouse forklift must be charged overnight.";

        // Two requests only (free accounts allow 3 per minute): both documents in one batch, then the question.
        var documents = await service.EmbedDocumentsAsync([text, unrelated], Ct);
        var query = await service.EmbedQueryAsync(text, Ct);

        documents.Vectors.Count.ShouldBe(2);
        documents.Vectors.ShouldAllBe(v => v.Length == Dimensions);
        documents.TotalTokens.ShouldBeGreaterThan(0);
        query.Vector.Length.ShouldBe(Dimensions);
        query.TotalTokens.ShouldBeGreaterThan(0);

        var asQuery = query.Vector;
        var vectors = documents.Vectors;

        // Voyage adds a different instruction for each type, so the same text gives a different vector...
        var sameText = Cosine(vectors[0], asQuery);
        sameText.ShouldBeLessThan(0.9999, "input_type must change the embedding, or Voyage ignored it");

        // ...that is still closer to its own text than to an unrelated one. (Measured: about 0.59 for the same text;
        // absolute values depend on the model, so only the order is checked.)
        sameText.ShouldBeGreaterThan(Cosine(vectors[1], asQuery));
    }

    // ---------- helpers ----------

    private static string? InputType(RecordedRequest request) =>
        JsonNode.Parse(request.Body)!.AsObject()["input_type"]?.GetValue<string>();

    private static double Cosine(float[] a, float[] b)
    {
        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }

        return dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }

    private static VoyageOptions Options(string apiKey = "pa-test-key", string baseUrl = "https://voyage.test/v1/", int dimensions = Dimensions) => new()
    {
        ApiKey = apiKey,
        BaseUrl = baseUrl,
        EmbeddingModel = "voyage-3.5",
        EmbeddingDimensions = dimensions,
        BatchSize = 64,
    };

    private static readonly Citra.Infrastructure.Chunking.TiktokenTokenCounter Tokens = new();

    private static VoyageEmbeddingService Service(FakeHandler handler, string apiKey = "pa-test-key") =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("https://voyage.test/v1/") },
            Microsoft.Extensions.Options.Options.Create(Options(apiKey)),
            Tokens);

    private static string SuccessBody(int count, int tokens = 5) => Response(Enumerable.Range(0, count).ToArray(), tokens);

    /// <summary>A Voyage response listing one result per given index; each vector is filled with its index.</summary>
    private static string Response(int[] indexes, int tokens = 5, int dimensions = Dimensions) =>
        JsonSerializer.Serialize(new
        {
            @object = "list",
            data = indexes.Select(i => new { @object = "embedding", embedding = Enumerable.Repeat((float)i, dimensions), index = i }),
            model = "voyage-3.5",
            usage = new { total_tokens = tokens },
        });

    private sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Authorization, string Body);

    /// <summary>Records every request and answers with a fixed response (or throws).</summary>
    private sealed class FakeHandler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        public static FakeHandler Returning(string body, HttpStatusCode status = HttpStatusCode.OK) =>
            new(() => new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });

        public static FakeHandler Throwing(Exception exception) => new(() => throw exception);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri!,
                request.Headers.Authorization?.ToString(),
                request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken)));
            return respond();
        }
    }
}

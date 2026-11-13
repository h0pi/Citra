using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Citra.Application.Common.Embeddings;
using Citra.Domain.Documents;
using Citra.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Citra.IntegrationTests.Embeddings;

/// <summary>
/// Retries with backoff, through the real DI registration (the same resilience pipeline the app uses);
/// only the network is replaced by a scripted fake. Waits are shortened to milliseconds via settings.
/// </summary>
public sealed class VoyageRetryTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    [InlineData(408)]
    public async Task Temporary_failures_are_retried_until_they_succeed(int status)
    {
        var network = new ScriptedNetwork(Fail(status), Fail(status), Ok());

        var result = await Embedding(network).EmbedDocumentsAsync(["t0 text"], Ct);

        result.Vectors.Count.ShouldBe(1);
        network.Calls.ShouldBe(3);
    }

    [Fact]
    public async Task A_dropped_connection_is_retried()
    {
        var network = new ScriptedNetwork(Throw(new HttpRequestException("connection reset")), Ok());

        await Embedding(network).EmbedDocumentsAsync(["t0 text"], Ct);

        network.Calls.ShouldBe(2);
    }

    [Theory]
    [InlineData(400, "refused the request (400)")]
    [InlineData(401, "rejected the API key (401)")]
    [InlineData(403, "rejected the API key (403)")]
    [InlineData(404, "refused the request (404)")]
    public async Task Client_errors_are_not_retried(int status, string expected)
    {
        var network = new ScriptedNetwork(Fail(status), Ok());

        var error = await Should.ThrowAsync<EmbeddingException>(() => Embedding(network).EmbedDocumentsAsync(["t0"], Ct));

        error.Message.ShouldContain(expected);
        network.Calls.ShouldBe(1, "repeating a wrong request or a bad key cannot help");
    }

    [Fact]
    public async Task Gives_up_after_the_configured_number_of_retries()
    {
        var network = new ScriptedNetwork(Enumerable.Repeat(Fail(429), 20).ToArray());

        var error = await Should.ThrowAsync<EmbeddingException>(
            () => Embedding(network, maxRetries: 4).EmbedDocumentsAsync(["t0"], Ct));

        error.Message.ShouldContain("rate limit reached (429)");
        network.Calls.ShouldBe(5, "1 attempt + 4 retries");
    }

    [Fact]
    public async Task Retries_can_be_turned_off()
    {
        var network = new ScriptedNetwork(Fail(503), Ok());

        await Should.ThrowAsync<EmbeddingException>(() => Embedding(network, maxRetries: 0).EmbedDocumentsAsync(["t0"], Ct));

        network.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task Waits_as_long_as_voyage_asks_in_retry_after()
    {
        Func<HttpResponseMessage> tooMany = () =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("{}") };
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(1));
            return response;
        };
        var network = new ScriptedNetwork(tooMany, Ok());
        var stopwatch = Stopwatch.StartNew();

        await Embedding(network).EmbedDocumentsAsync(["t0"], Ct);

        stopwatch.Elapsed.ShouldBeGreaterThan(TimeSpan.FromMilliseconds(900), "Retry-After: 1 must be respected, not the 10 ms base delay");
        network.Calls.ShouldBe(2);
    }

    [Fact]
    public async Task Waits_grow_between_retries()
    {
        var network = new ScriptedNetwork(Fail(503), Fail(503), Fail(503), Fail(503), Fail(503), Fail(503), Ok());

        await Embedding(network, maxRetries: 6, baseDelaySeconds: 0.02).EmbedDocumentsAsync(["t0"], Ct);

        // Exponential with jitter: each wait is random around 20, 40, 80, 160, 320, 640 ms, so single waits can
        // overlap (the 3rd was once shorter than the 1st). Sums smooth the jitter out: measured, the last three
        // waits took 3.3–7.2× as long as the first three; waits that don't grow would give about 1×.
        var gaps = network.CallTimes.Zip(network.CallTimes.Skip(1), (a, b) => b - a).ToList();
        gaps.Count.ShouldBe(6);
        var early = gaps.Take(3).Sum(g => g.TotalMilliseconds);
        var late = gaps.Skip(3).Sum(g => g.TotalMilliseconds);
        late.ShouldBeGreaterThan(early * 2, $"waits (ms): {string.Join(", ", gaps.Select(g => (int)g.TotalMilliseconds))}");
    }

    [Fact]
    public async Task A_retried_batch_still_returns_every_vector_in_order()
    {
        // Second batch fails once, then succeeds: the caller never notices.
        var network = new ScriptedNetwork(Ok(), Fail(503), Ok(), Ok());
        var texts = Enumerable.Range(0, 25).Select(i => $"t{i} chunk").ToList();

        var result = await Embedding(network, batchSize: 10).EmbedDocumentsAsync(texts, Ct);

        result.Vectors.Select(v => (int)v[0]).ShouldBe(Enumerable.Range(0, 25));
        network.Calls.ShouldBe(4);
    }

    [Fact]
    public async Task Cancelling_during_a_wait_stops_immediately()
    {
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var network = new ScriptedNetwork(Enumerable.Repeat(Fail(503), 5).ToArray());
        var stopwatch = Stopwatch.StartNew();

        await Should.ThrowAsync<OperationCanceledException>(
            () => Embedding(network, baseDelaySeconds: 10).EmbedDocumentsAsync(["t0"], cancel.Token));

        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5), "no one waits out a 10 s backoff after cancelling");
    }

    [Fact]
    public async Task Production_settings_retry_six_times_starting_at_two_seconds()
    {
        var voyage = new Citra.Infrastructure.Options.VoyageOptions();

        voyage.MaxRetryAttempts.ShouldBe(6);
        voyage.RetryBaseDelaySeconds.ShouldBe(2);
        (await File.ReadAllTextAsync(AppSettings("Citra.Api"), Ct)).ShouldNotContain("MaxRetryAttempts\": 0");
    }

    // ---------- helpers ----------

    /// <summary>The embedding service exactly as the app builds it, with the network replaced.</summary>
    private static IEmbeddingService Embedding(ScriptedNetwork network, int maxRetries = 6, double baseDelaySeconds = 0.01, int batchSize = 64)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Database"] = "Host=unused;Database=unused",
                ["Anthropic:ChatModel"] = "test-model",
                ["Anthropic:MaxOutputTokens"] = "1024",
                ["Voyage:ApiKey"] = "pa-test-key",
                ["Voyage:BaseUrl"] = "https://voyage.test/v1/",
                ["Voyage:EmbeddingModel"] = "voyage-3.5",
                ["Voyage:EmbeddingDimensions"] = "1024",
                ["Voyage:BatchSize"] = batchSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["Voyage:MaxRetryAttempts"] = maxRetries.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["Voyage:RetryBaseDelaySeconds"] = baseDelaySeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["Storage:LocalRootPath"] = Path.GetTempPath(),
                ["Storage:MaxUploadSizeMb"] = "20",
            })
            .Build();

        var services = new ServiceCollection().AddLogging();
        services.AddInfrastructure(configuration);
        services.AddHttpClient<IEmbeddingService, Citra.Infrastructure.Embeddings.VoyageEmbeddingService>()
            .ConfigurePrimaryHttpMessageHandler(() => network);

        return services.BuildServiceProvider().GetRequiredService<IEmbeddingService>();
    }

    private static string AppSettings(string project)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(directory!.FullName, "src", project, "appsettings.json");
    }

    private static Func<HttpResponseMessage> Fail(int status) =>
        () => new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("""{"detail":"try later"}""") };

    private static Func<HttpResponseMessage> Throw(Exception exception) => () => throw exception;

    /// <summary>A successful response; filled in per request by <see cref="ScriptedNetwork"/>.</summary>
    private static Func<HttpResponseMessage> Ok() => () => null!;

    /// <summary>"t7 chunk" → 7.</summary>
    private static int Number(string text)
    {
        var space = text.IndexOf(' ');
        return int.Parse(space > 0 ? text[1..space] : text[1..], System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Answers calls from a script, in order; "Ok" steps echo one vector per input ("t7 …" → 7).</summary>
    private sealed class ScriptedNetwork(params Func<HttpResponseMessage>[] script) : HttpMessageHandler
    {
        private readonly Queue<Func<HttpResponseMessage>> _script = new(script);

        public int Calls { get; private set; }

        public List<DateTimeOffset> CallTimes { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            CallTimes.Add(DateTimeOffset.UtcNow);
            var step = _script.Count > 0 ? _script.Dequeue() : throw new InvalidOperationException("Script exhausted.");
            var response = step();
            if (response is not null)
            {
                return response;
            }

            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
            var inputs = body["input"]!.AsArray().Select(n => n!.GetValue<string>()).ToList();
            var json = JsonSerializer.Serialize(new
            {
                @object = "list",
                data = inputs.Select((text, index) => new
                {
                    @object = "embedding",
                    embedding = Enumerable.Repeat((float)Number(text), Chunk.EmbeddingDimensions),
                    index,
                }),
                model = "voyage-3.5",
                usage = new { total_tokens = inputs.Count },
            });
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}

using System.Net;
using System.Text;
using Citra.Api.ErrorHandling;
using Citra.Application.Common.Chat;
using Citra.Application.Features.Bots;
using Citra.Infrastructure;
using Citra.Infrastructure.Chat;
using Citra.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Citra.IntegrationTests.Chat;

/// <summary>C-172/C-173: which model answers (settings), Claude's retries, and how chat failures reach the browser.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class ChatSetupTests(DatabaseFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly ChatRequest Question = new("Rules", [new ChatMessage(ChatRole.User, "Hi")]);

    [Fact]
    public async Task The_free_stand_in_answers_unless_claude_is_chosen()
    {
        await using var standIn = Services([]);
        standIn.GetRequiredService<IChatModel>().ShouldBeOfType<ExtractiveChatModel>();

        await using var configured = Services([("Chat:Provider", "Extractive")]);
        configured.GetRequiredService<IChatModel>().ShouldBeOfType<ExtractiveChatModel>();

        await using var claude = Services([("Chat:Provider", "Anthropic")]);
        claude.GetRequiredService<IChatModel>().ShouldBeOfType<ClaudeChatModel>();
    }

    [Fact]
    public async Task An_unknown_provider_is_refused()
    {
        await using var services = Services([("Chat:Provider", "OpenAI")]);

        Should.Throw<InvalidOperationException>(() => services.GetRequiredService<IChatModel>());
    }

    [Fact]
    public async Task Claude_is_retried_while_overloaded_before_the_answer_starts()
    {
        var network = new ScriptedNetwork(
            () => Status((HttpStatusCode)529),
            () => Status(HttpStatusCode.TooManyRequests),
            () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ClaudeChatModelTests.Answer("Hello."), Encoding.UTF8, "text/event-stream"),
            });
        await using var services = Services([("Chat:Provider", "Anthropic")], network);

        var events = await services.GetRequiredService<IChatModel>().StreamAsync(Question, Ct).ToListAsync(Ct);

        network.Calls.ShouldBe(3);
        events.OfType<ChatTextDelta>().ShouldHaveSingleItem().Text.ShouldBe("Hello.");
    }

    [Fact]
    public async Task A_refused_request_is_not_retried()
    {
        var network = new ScriptedNetwork(() => Status(HttpStatusCode.BadRequest), () => Status(HttpStatusCode.OK));
        await using var services = Services([("Chat:Provider", "Anthropic")], network);

        await Should.ThrowAsync<ChatModelException>(
            () => services.GetRequiredService<IChatModel>().StreamAsync(Question, Ct).ToListAsync(Ct).AsTask());
        network.Calls.ShouldBe(1);
    }

    [Fact]
    public void Retries_are_on_by_default()
    {
        var anthropic = new Citra.Infrastructure.Options.AnthropicOptions();

        (anthropic.MaxRetryAttempts, anthropic.RetryBaseDelaySeconds, anthropic.BaseUrl)
            .ShouldBe((3, 2.0, "https://api.anthropic.com/"));
    }

    [Theory]
    [InlineData("Citra.Api")]
    [InlineData("Citra.Worker")]
    public void The_apps_default_model_is_one_a_bot_can_choose(string project)
    {
        var configuration = new ConfigurationBuilder().AddJsonFile(AppSettings(project)).Build();

        BotModels.IsSupported(configuration["Anthropic:ChatModel"]!).ShouldBeTrue();
        configuration["Anthropic:BaseUrl"].ShouldBe("https://api.anthropic.com/");
    }

    [Fact]
    public void The_api_starts_with_the_free_stand_in()
    {
        new ConfigurationBuilder().AddJsonFile(AppSettings("Citra.Api")).Build()["Chat:Provider"].ShouldBe("Extractive");
    }

    [Fact]
    public void Bots_can_choose_the_real_claude_model_ids()
    {
        BotModels.Supported.ShouldBe(["claude-opus-5-5", "claude-sonnet-5-5", "claude-haiku-4-5"]);
    }

    [Fact]
    public async Task A_failing_chat_model_tells_the_browser_the_ai_service_is_unavailable()
    {
        await using var api = new ApiFactory(fixture);
        var handler = new GlobalExceptionHandler(
            api.Services.GetRequiredService<IProblemDetailsService>(),
            api.Services.GetRequiredService<IHostEnvironment>(),
            NullLogger<GlobalExceptionHandler>.Instance);
        var context = new DefaultHttpContext { RequestServices = api.Services };
        context.Response.Body = new MemoryStream();

        (await handler.TryHandleAsync(context, new ChatModelException("Claude is overloaded (529)."), Ct)).ShouldBeTrue();

        context.Response.StatusCode.ShouldBe(StatusCodes.Status503ServiceUnavailable);
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync(Ct);
        body.ShouldContain("The AI service is not available right now.");
        body.ShouldNotContain("529");
    }

    // ---------- helpers ----------

    private static HttpResponseMessage Status(HttpStatusCode status) =>
        new(status) { Content = new StringContent("""{"type":"error","error":{"type":"x","message":"try later"}}""") };

    private static ServiceProvider Services((string Key, string Value)[] values, ScriptedNetwork? network = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Database"] = "Host=unused;Database=unused",
            ["Anthropic:ApiKey"] = "sk-test-key",
            ["Anthropic:BaseUrl"] = "https://anthropic.test/",
            ["Anthropic:ChatModel"] = "claude-default",
            ["Anthropic:MaxOutputTokens"] = "1024",
            ["Anthropic:RetryBaseDelaySeconds"] = "0.01",
        };
        foreach (var (key, value) in values)
        {
            settings[key] = value;
        }

        var services = new ServiceCollection().AddLogging();
        services.AddInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        if (network is not null)
        {
            services.AddHttpClient<ClaudeChatModel>().ConfigurePrimaryHttpMessageHandler(() => network);
        }

        return services.BuildServiceProvider();
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

    /// <summary>Answers calls from a script, in order.</summary>
    private sealed class ScriptedNetwork(params Func<HttpResponseMessage>[] script) : HttpMessageHandler
    {
        private readonly Queue<Func<HttpResponseMessage>> _script = new(script);

        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(_script.Dequeue()());
        }
    }
}

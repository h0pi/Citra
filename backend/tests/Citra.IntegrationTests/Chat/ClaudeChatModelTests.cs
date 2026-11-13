using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Citra.Application.Common.Chat;
using Citra.Infrastructure.Chat;
using Citra.Infrastructure.Options;

namespace Citra.IntegrationTests.Chat;

/// <summary>The Claude client against a fake Anthropic server (no network, no cost).</summary>
public sealed class ClaudeChatModelTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly ChatRequest Question = new(
        "Answer from the sources.",
        [new ChatMessage(ChatRole.User, "Earlier question"), new ChatMessage(ChatRole.Assistant, "Earlier answer"), new ChatMessage(ChatRole.User, "How many days?")]);

    // ---------- the request ----------

    [Fact]
    public async Task Sends_the_documented_streaming_request()
    {
        var handler = FakeAnthropic.Streaming(Answer("Hi."));

        await Model(handler).StreamAsync(Question, Ct).ToListAsync(Ct);

        var request = handler.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Post);
        request.Uri.ShouldBe(new Uri("https://anthropic.test/v1/messages"));
        request.ApiKey.ShouldBe("sk-test-key");
        request.Version.ShouldBe("2023-06-01");
        var body = JsonNode.Parse(request.Body)!.AsObject();
        body["model"]!.GetValue<string>().ShouldBe("claude-default");
        body["max_tokens"]!.GetValue<int>().ShouldBe(2048);
        body["system"]!.GetValue<string>().ShouldBe("Answer from the sources.");
        body.ContainsKey("temperature").ShouldBeFalse("current models refuse a temperature");
        body["output_config"]!["effort"]!.GetValue<string>().ShouldBe("low");
        body["stream"]!.GetValue<bool>().ShouldBeTrue();
        body["messages"]!.AsArray().Select(m => (m!["role"]!.GetValue<string>(), m["content"]!.GetValue<string>()))
            .ShouldBe([("user", "Earlier question"), ("assistant", "Earlier answer"), ("user", "How many days?")]);
    }

    [Fact]
    public async Task Uses_the_requested_model_and_answer_length()
    {
        var handler = FakeAnthropic.Streaming(Answer("Hi."));

        await Model(handler).StreamAsync(Question with { Model = "claude-haiku-5-5", MaxOutputTokens = 300 }, Ct).ToListAsync(Ct);

        var body = JsonNode.Parse(handler.Requests[0].Body)!.AsObject();
        body["model"]!.GetValue<string>().ShouldBe("claude-haiku-5-5");
        body["max_tokens"]!.GetValue<int>().ShouldBe(300);
    }

    [Fact]
    public void The_default_model_comes_from_the_settings()
    {
        Model(FakeAnthropic.Streaming("")).DefaultModel.ShouldBe("claude-default");
    }

    // ---------- the stream ----------

    [Fact]
    public async Task Streams_the_text_in_order_then_the_tokens_used()
    {
        var events = await Model(FakeAnthropic.Streaming(Answer("Employees get ", "25 days [1].", model: "claude-sonnet-5-5-20260901", input: 812, output: 9)))
            .StreamAsync(Question, Ct).ToListAsync(Ct);

        events.ShouldBe([
            new ChatTextDelta("Employees get "),
            new ChatTextDelta("25 days [1]."),
            new ChatCompleted(new ChatUsage("claude-sonnet-5-5-20260901", 812, 9), "end_turn"),
        ]);
    }

    [Fact]
    public async Task Counts_prompt_cache_tokens_as_input_and_skips_what_it_does_not_need()
    {
        var stream = """
            event: message_start
            data: {"type":"message_start","message":{"model":"claude-x","usage":{"input_tokens":10,"cache_creation_input_tokens":5,"cache_read_input_tokens":3,"output_tokens":1}}}

            event: ping
            data: {"type":"ping"}

            event: content_block_start
            data: {"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}

            event: content_block_delta
            data: {"type":"content_block_delta","index":0,"delta":{"type":"thinking_delta","thinking":"hmm"}}

            event: content_block_delta
            data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"Yes."}}

            event: content_block_stop
            data: {"type":"content_block_stop","index":0}

            event: message_delta
            data: {"type":"message_delta","delta":{"stop_reason":"max_tokens","stop_sequence":null},"usage":{"output_tokens":4}}

            event: message_stop
            data: {"type":"message_stop"}

            """.Replace("\n", "\r\n"); // servers may end lines with CRLF

        var events = await Model(FakeAnthropic.Streaming(stream)).StreamAsync(Question, Ct).ToListAsync(Ct);

        events.ShouldBe([new ChatTextDelta("Yes."), new ChatCompleted(new ChatUsage("claude-x", 18, 4), "max_tokens")]);
    }

    [Fact]
    public async Task Shows_the_first_words_before_claude_has_finished_writing()
    {
        // The server sends the start of the answer, then waits: the first piece must arrive without waiting for the rest.
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var parts = Answer("First words. ", "The rest.").Split("event: content_block_delta");
        var handler = new FakeAnthropic(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new GatedStream(
                Encoding.UTF8.GetBytes(parts[0] + "event: content_block_delta" + parts[1]),
                gate.Task,
                Encoding.UTF8.GetBytes("event: content_block_delta" + parts[2]))),
        });

        await using var answer = Model(handler).StreamAsync(Question, Ct).GetAsyncEnumerator(Ct);
        var first = answer.MoveNextAsync().AsTask();
        (await Task.WhenAny(first, Task.Delay(TimeSpan.FromSeconds(10), Ct))).ShouldBe(first, "the first piece came before the rest was sent");
        answer.Current.ShouldBe(new ChatTextDelta("First words. "));

        gate.SetResult();
        (await answer.MoveNextAsync()).ShouldBeTrue();
        answer.Current.ShouldBe(new ChatTextDelta("The rest."));
    }

    [Fact]
    public async Task An_error_while_answering_is_reported_after_the_text_so_far()
    {
        var stream = Answer("Part one. ").Split("event: message_delta")[0] + """
            event: error
            data: {"type":"error","error":{"type":"overloaded_error","message":"Overloaded"}}

            """;
        var received = new List<ChatEvent>();

        var error = await Should.ThrowAsync<ChatModelException>(async () =>
        {
            await foreach (var e in Model(FakeAnthropic.Streaming(stream)).StreamAsync(Question, Ct))
            {
                received.Add(e);
            }
        });

        received.ShouldBe([new ChatTextDelta("Part one. ")]);
        error.Message.ShouldBe("Claude stopped with an error (overloaded_error): Overloaded");
    }

    [Fact]
    public async Task A_stream_that_breaks_off_is_an_error_not_a_short_answer()
    {
        var stream = Answer("Half an ans").Split("event: message_stop")[0];

        var error = await Should.ThrowAsync<ChatModelException>(
            () => Model(FakeAnthropic.Streaming(stream)).StreamAsync(Question, Ct).ToListAsync(Ct).AsTask());

        error.Message.ShouldBe("Claude's answer ended before it was complete.");
    }

    [Fact]
    public async Task Unreadable_event_data_is_an_error()
    {
        var error = await Should.ThrowAsync<ChatModelException>(
            () => Model(FakeAnthropic.Streaming("data: {not json\n\n")).StreamAsync(Question, Ct).ToListAsync(Ct).AsTask());

        error.Message.ShouldBe("Claude sent a streaming event that could not be read.");
    }

    // ---------- failures before the answer ----------

    [Theory]
    [InlineData(401, """{"type":"error","error":{"type":"authentication_error","message":"invalid x-api-key"}}""", "Anthropic rejected the API key (401): invalid x-api-key")]
    [InlineData(400, """{"type":"error","error":{"type":"invalid_request_error","message":"Your credit balance is too low."}}""", "Anthropic refused the request (400): Your credit balance is too low.")]
    [InlineData(429, """{"type":"error","error":{"type":"rate_limit_error","message":"Slow down"}}""", "Anthropic rate limit reached (429): Slow down")]
    [InlineData(529, """{"type":"error","error":{"type":"overloaded_error","message":"Overloaded"}}""", "Claude is overloaded (529): Overloaded")]
    [InlineData(500, "<html>oops</html>", "Anthropic had a server error (500).")]
    public async Task A_refused_call_says_why(int status, string body, string message)
    {
        var handler = FakeAnthropic.Failing((HttpStatusCode)status, body);

        var error = await Should.ThrowAsync<ChatModelException>(
            () => Model(handler).StreamAsync(Question, Ct).ToListAsync(Ct).AsTask());

        error.Message.ShouldBe(message);
    }

    [Fact]
    public async Task Without_an_api_key_nothing_is_sent()
    {
        var handler = FakeAnthropic.Streaming(Answer("Hi."));

        var error = await Should.ThrowAsync<ChatModelException>(
            () => Model(handler, apiKey: " ").StreamAsync(Question, Ct).ToListAsync(Ct).AsTask());

        error.Message.ShouldContain("dotnet user-secrets set \"Anthropic:ApiKey\"");
        handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_unreachable_server_is_a_chat_error()
    {
        var handler = new FakeAnthropic(() => throw new HttpRequestException("No such host"));

        var error = await Should.ThrowAsync<ChatModelException>(
            () => Model(handler).StreamAsync(Question, Ct).ToListAsync(Ct).AsTask());

        error.Message.ShouldBe("Anthropic could not be reached.");
    }

    [Fact]
    public async Task Invalid_requests_are_refused_before_sending()
    {
        var handler = FakeAnthropic.Streaming(Answer("Hi."));
        var model = Model(handler);

        await Should.ThrowAsync<ArgumentException>(() => model.StreamAsync(Question with { SystemPrompt = " " }, Ct).ToListAsync(Ct).AsTask());
        await Should.ThrowAsync<ArgumentException>(() => model.StreamAsync(new ChatRequest("Rules", []), Ct).ToListAsync(Ct).AsTask());
        handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Stops_when_the_caller_cancels()
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var received = new List<ChatEvent>();

        await Should.ThrowAsync<OperationCanceledException>(async () =>
        {
            await foreach (var e in Model(FakeAnthropic.Streaming(Answer("One ", "two ", "three."))).StreamAsync(Question, cancel.Token))
            {
                received.Add(e);
                await cancel.CancelAsync();
            }
        });

        received.ShouldHaveSingleItem();
    }

    // ---------- helpers ----------

    /// <summary>A complete Anthropic stream that writes <paramref name="parts"/>.</summary>
    internal static string Answer(string part, string? second = null, string model = "claude-default", int input = 20, int output = 5)
    {
        var text = new StringBuilder($$$$"""
            event: message_start
            data: {"type":"message_start","message":{"id":"msg_1","type":"message","role":"assistant","content":[],"model":"{{{{model}}}}","stop_reason":null,"usage":{"input_tokens":{{{{input}}}},"output_tokens":1}}}

            event: content_block_start
            data: {"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}

            event: ping
            data: {"type":"ping"}


            """);
        foreach (var piece in second is null ? [part] : new[] { part, second })
        {
            text.Append($$$"""
                event: content_block_delta
                data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":{{{System.Text.Json.JsonSerializer.Serialize(piece)}}}}}


                """);
        }

        text.Append($$$"""
            event: content_block_stop
            data: {"type":"content_block_stop","index":0}

            event: message_delta
            data: {"type":"message_delta","delta":{"stop_reason":"end_turn","stop_sequence":null},"usage":{"output_tokens":{{{output}}}}}

            event: message_stop
            data: {"type":"message_stop"}

            """);
        return text.ToString();
    }

    private static ClaudeChatModel Model(FakeAnthropic handler, string apiKey = "sk-test-key") =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("https://anthropic.test/") },
            Microsoft.Extensions.Options.Options.Create(new AnthropicOptions
            {
                ApiKey = apiKey,
                ChatModel = "claude-default",
                MaxOutputTokens = 2048,
            }));

    /// <summary>A response body that sends its first part at once and the second only after <c>gate</c> opens.</summary>
    private sealed class GatedStream(byte[] first, Task gate, byte[] second) : Stream
    {
        private readonly MemoryStream _first = new(first);
        private readonly MemoryStream _second = new(second);

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = _first.Read(buffer.Span);
            if (read > 0)
            {
                return read;
            }

            await gate.WaitAsync(cancellationToken);
            return _second.Read(buffer.Span);
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override void Flush() => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    internal sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? ApiKey, string? Version, string Body);

    /// <summary>Records every request and answers with a fixed response (or throws).</summary>
    internal sealed class FakeAnthropic(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        public static FakeAnthropic Streaming(string events) =>
            new(() => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(events, Encoding.UTF8, "text/event-stream"),
            });

        public static FakeAnthropic Failing(HttpStatusCode status, string body) =>
            new(() => new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri!,
                request.Headers.TryGetValues("x-api-key", out var key) ? key.Single() : null,
                request.Headers.TryGetValues("anthropic-version", out var version) ? version.Single() : null,
                request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken)));
            return respond();
        }
    }
}

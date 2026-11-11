using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Citra.Application.Common.Chat;
using Citra.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Citra.Infrastructure.Chat;

/// <summary>
/// Claude through the Anthropic Messages API (POST {BaseUrl}v1/messages with "stream": true), used through a
/// typed HttpClient. The answer arrives as server-sent events; only the parts Citra needs are read:
/// message_start (model, input tokens), content_block_delta (text), message_delta (stop reason, output tokens),
/// message_stop, and error. See https://docs.anthropic.com/en/api/messages-streaming.
/// </summary>
internal sealed class ClaudeChatModel(HttpClient http, IOptions<AnthropicOptions> options) : IChatModel
{
    /// <summary>The API version this client was written against (sent with every request).</summary>
    internal const string ApiVersion = "2023-06-01";

    private readonly AnthropicOptions _options = options.Value;

    public string DefaultModel => _options.ChatModel;

    public async IAsyncEnumerable<ChatEvent> StreamAsync(
        ChatRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.EnsureValid();
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new ChatModelException(
                "The Anthropic API key is not configured. Set it with: dotnet user-secrets set \"Anthropic:ApiKey\" \"<key>\"");
        }

        var model = request.Model ?? _options.ChatModel;
        using var response = await SendAsync(request, model, cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        var usedModel = model;
        int inputTokens = 0, outputTokens = 0;
        string? stopReason = null;

        // Server-sent events: "event: …" and "data: {json}" lines; the JSON's "type" says what happened.
        while (await ReadLineAsync(reader, cancellationToken) is { } line)
        {
            // Text already received is still buffered after a cancel: stop here, not only at the next network read.
            cancellationToken.ThrowIfCancellationRequested();
            if (!line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            using var data = Parse(line["data:".Length..]);
            var root = data.RootElement;
            switch (root.GetProperty("type").GetString())
            {
                case "message_start":
                    var message = root.GetProperty("message");
                    usedModel = message.TryGetProperty("model", out var m) && m.GetString() is { } name ? name : model;
                    inputTokens = InputTokens(message.GetProperty("usage"));
                    break;

                case "content_block_delta":
                    var delta = root.GetProperty("delta");
                    if (delta.GetProperty("type").GetString() == "text_delta"
                        && delta.GetProperty("text").GetString() is { Length: > 0 } text)
                    {
                        yield return new ChatTextDelta(text);
                    }

                    break;

                case "message_delta":
                    if (root.GetProperty("delta").TryGetProperty("stop_reason", out var reason)
                        && reason.ValueKind == JsonValueKind.String)
                    {
                        stopReason = reason.GetString();
                    }

                    if (root.TryGetProperty("usage", out var usage) && usage.TryGetProperty("output_tokens", out var output))
                    {
                        outputTokens = output.GetInt32(); // cumulative: the last value is the total
                    }

                    break;

                case "message_stop":
                    yield return new ChatCompleted(new ChatUsage(usedModel, inputTokens, outputTokens), stopReason);
                    yield break;

                case "error":
                    var error = root.GetProperty("error");
                    throw new ChatModelException(
                        $"Claude stopped with an error ({error.GetProperty("type").GetString()}): {error.GetProperty("message").GetString()}");

                // "ping", "content_block_start", "content_block_stop" and future event types carry nothing Citra needs.
            }
        }

        throw new ChatModelException("Claude's answer ended before it was complete.");
    }

    private async Task<HttpResponseMessage> SendAsync(ChatRequest request, string model, CancellationToken cancellationToken)
    {
        var body = new
        {
            model,
            max_tokens = request.MaxOutputTokens ?? _options.MaxOutputTokens,
            system = request.SystemPrompt,
            messages = request.Messages.Select(message => new
            {
                role = message.Role == ChatRole.User ? "user" : "assistant",
                content = message.Content,
            }),
            // Current models choose their own sampling (they refuse "temperature") and think before answering.
            // Answering from a few given passages is simple work: low effort keeps it fast, cheap and within
            // max_tokens, which thinking counts toward.
            output_config = new { effort = "low" },
            stream = true,
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "v1/messages") { Content = JsonContent.Create(body) };
        httpRequest.Headers.Add("x-api-key", _options.ApiKey);
        httpRequest.Headers.Add("anthropic-version", ApiVersion);

        HttpResponseMessage response;
        try
        {
            // Headers only: the body is the stream, read piece by piece as Claude writes.
            response = await http.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new ChatModelException("Anthropic could not be reached.", ex);
        }
        catch (Exception ex) when (ex is TaskCanceledException or Polly.Timeout.TimeoutRejectedException
            && !cancellationToken.IsCancellationRequested)
        {
            throw new ChatModelException("Claude did not start answering in time.", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            using (response)
            {
                throw new ChatModelException(await ErrorMessageAsync(response, cancellationToken));
            }
        }

        return response;
    }

    private static async Task<string?> ReadLineAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        try
        {
            return await reader.ReadLineAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException)
        {
            throw new ChatModelException("The connection to Claude broke off while it was answering.", ex);
        }
    }

    private static JsonDocument Parse(string json)
    {
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new ChatModelException("Claude sent a streaming event that could not be read.", ex);
        }
    }

    /// <summary>All billed input: plain input plus prompt-cache writes and reads (if caching is ever used).</summary>
    private static int InputTokens(JsonElement usage) =>
        Tokens(usage, "input_tokens") + Tokens(usage, "cache_creation_input_tokens") + Tokens(usage, "cache_read_input_tokens");

    private static int Tokens(JsonElement usage, string name) =>
        usage.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : 0;

    /// <summary>A readable reason for a refused call; Anthropic puts details in {"error": {"type", "message"}}.</summary>
    private static async Task<string> ErrorMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var detail = string.Empty;
        try
        {
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (json.RootElement.TryGetProperty("error", out var error) && error.TryGetProperty("message", out var message))
            {
                detail = message.GetString() ?? string.Empty;
            }
        }
        catch (JsonException)
        {
            // Not JSON (e.g. an HTML error page from a proxy): the status code is enough.
        }

        var status = (int)response.StatusCode;
        var reason = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "Anthropic rejected the API key",
            HttpStatusCode.TooManyRequests => "Anthropic rate limit reached",
            (HttpStatusCode)529 => "Claude is overloaded",
            >= HttpStatusCode.InternalServerError => "Anthropic had a server error",
            _ => "Anthropic refused the request",
        };

        return string.IsNullOrWhiteSpace(detail) ? $"{reason} ({status})." : $"{reason} ({status}): {detail}";
    }
}

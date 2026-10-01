using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Citra.Application.Common.Chat;
using Citra.Infrastructure.Chat;
using Citra.Infrastructure.Chunking;

namespace Citra.IntegrationTests.Infrastructure;

/// <summary>
/// The chat model database tests use: the free stand-in (<see cref="ExtractiveChatModel"/>, real prompts in, real
/// quotes out), with every request recorded. A test can take over the answers (<see cref="Script"/>): stream
/// chosen pieces, fail midway, or answer rewrite requests differently from answer requests.
/// </summary>
public sealed class FakeChatModel : IChatModel
{
    private readonly ExtractiveChatModel _standIn = new(new TiktokenTokenCounter());
    private readonly ConcurrentQueue<ChatRequest> _requests = new();
    private Func<ChatRequest, IAsyncEnumerable<ChatEvent>>? _script;

    public string DefaultModel => _standIn.DefaultModel;

    /// <summary>Every request, in call order.</summary>
    public IReadOnlyList<ChatRequest> Requests => [.. _requests];

    /// <summary>Answers requests with <paramref name="answer"/> until the returned handle is disposed.</summary>
    public IDisposable Script(Func<ChatRequest, IAsyncEnumerable<ChatEvent>> answer)
    {
        _script = answer;
        return new Restore(this);
    }

    public async IAsyncEnumerable<ChatEvent> StreamAsync(
        ChatRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        _requests.Enqueue(request);
        var answer = _script is { } script ? script(request) : _standIn.StreamAsync(request, cancellationToken);
        await foreach (var chatEvent in answer.WithCancellation(cancellationToken))
        {
            yield return chatEvent;
        }
    }

    /// <summary>Streams <paramref name="pieces"/>, then completes with the given usage.</summary>
    public static async IAsyncEnumerable<ChatEvent> Stream(ChatUsage usage, params string[] pieces)
    {
        foreach (var piece in pieces)
        {
            await Task.Yield();
            yield return new ChatTextDelta(piece);
        }

        yield return new ChatCompleted(usage, "end_turn");
    }

    /// <summary>Streams <paramref name="pieces"/>, then throws <paramref name="failure"/>.</summary>
    public static async IAsyncEnumerable<ChatEvent> Failing(Exception failure, params string[] pieces)
    {
        foreach (var piece in pieces)
        {
            await Task.Yield();
            yield return new ChatTextDelta(piece);
        }

        throw failure;
    }

    private sealed class Restore(FakeChatModel model) : IDisposable
    {
        public void Dispose() => model._script = null;
    }
}

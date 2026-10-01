using System.Text;

namespace Citra.Application.Common.Chat;

/// <summary>
/// Hides <see cref="ChatPrompts.NotFoundMarker"/> from the streamed answer. The model puts the marker at the very
/// start when the documents do not contain the answer; the start of an answer is therefore held back until it is
/// clear whether it is the marker. Any other answer passes through as soon as its first characters differ.
/// One filter per answer.
/// </summary>
public sealed class NotFoundFilter
{
    private const string Marker = ChatPrompts.NotFoundMarker;

    private readonly StringBuilder _held = new();
    private bool _decided;
    private bool _trimNext;

    /// <summary>True once the answer started with the marker.</summary>
    public bool Found { get; private set; }

    /// <summary>The part of <paramref name="delta"/> (and of the held-back start) that can be shown now.</summary>
    public string Push(string delta)
    {
        ArgumentNullException.ThrowIfNull(delta);
        if (_decided)
        {
            return _trimNext ? TrimStartOnce(delta) : delta;
        }

        _held.Append(delta);
        var text = _held.ToString();
        var start = text.TrimStart();
        if (start.Length < Marker.Length && Marker.StartsWith(start, StringComparison.Ordinal))
        {
            return string.Empty; // could still become the marker: wait for more
        }

        _decided = true;
        _held.Clear();
        if (!start.StartsWith(Marker, StringComparison.Ordinal))
        {
            return text;
        }

        Found = true;
        _trimNext = true;
        return TrimStartOnce(start[Marker.Length..]);
    }

    /// <summary>Whatever is still held back when the answer ends (an answer shorter than the marker).</summary>
    public string Finish()
    {
        _decided = true;
        var rest = _held.ToString();
        _held.Clear();
        return rest;
    }

    /// <summary>
    /// The answer as it is saved: without the marker (wherever the model put it). <paramref name="found"/> tells
    /// whether it was there, i.e. whether the documents lacked the answer.
    /// </summary>
    public static string Clean(string answer, out bool found)
    {
        ArgumentNullException.ThrowIfNull(answer);
        found = answer.Contains(Marker, StringComparison.Ordinal);
        return found ? answer.Replace(Marker, string.Empty, StringComparison.Ordinal).Trim() : answer.Trim();
    }

    /// <summary>The space after the marker is not part of the answer.</summary>
    private string TrimStartOnce(string text)
    {
        var trimmed = text.TrimStart();
        if (trimmed.Length > 0)
        {
            _trimNext = false;
        }

        return trimmed;
    }
}

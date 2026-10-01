using System.Text.RegularExpressions;

namespace Citra.Application.Features.Chat.SendMessage;

/// <summary>
/// A new conversation is named after its first question, so the chat list is readable at a glance: on one line,
/// shortened at a word boundary. No model is asked (it would cost money and time for every new chat).
/// </summary>
public static partial class ConversationTitle
{
    /// <summary>Titles longer than this are shortened (the list shows about this much).</summary>
    public const int MaxLength = 60;

    public static string FromQuestion(string question)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);

        var oneLine = Whitespace().Replace(question, " ").Trim();
        if (oneLine.Length <= MaxLength)
        {
            return oneLine;
        }

        // Cut at the last space, unless that would leave a very short title (one long word).
        var cut = oneLine[..MaxLength];
        var space = cut.LastIndexOf(' ');
        if (space >= MaxLength / 2)
        {
            cut = cut[..space];
        }

        return cut.TrimEnd(' ', ',', ';', ':', '-', '.') + "…";
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}

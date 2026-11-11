namespace Citra.Application.Features.Bots;

/// <summary>
/// Claude models a bot may be switched to. A bot without a model uses the app default (Anthropic:ChatModel).
/// A fixed list, so a typo cannot break a bot's chat and nobody can pick a model we have not priced.
/// </summary>
public static class BotModels
{
    public static readonly IReadOnlyList<string> Supported =
    [
        "claude-opus-5-5",
        "claude-sonnet-5-5",
        "claude-haiku-5-5",
    ];

    public static bool IsSupported(string model) => Supported.Contains(model, StringComparer.Ordinal);
}

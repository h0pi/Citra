namespace Citra.Application.Common.Chat;

/// <summary>Chat rules that come from configuration (section "Chat").</summary>
public interface IChatSettings
{
    /// <summary>At most this many earlier messages (questions and answers) are sent with a new question.</summary>
    int MaxHistoryMessages { get; }

    /// <summary>...and together at most about this many tokens: long conversations cost more with every question.</summary>
    int MaxHistoryTokens { get; }

    /// <summary>The cheap, fast model that turns follow-up questions into standalone ones (e.g. "claude-haiku-4-5").</summary>
    string RewriteModel { get; }

    /// <summary>How many passages the search gives the model for one answer (numbered [1], [2], …).</summary>
    int SourcesPerAnswer { get; }
}

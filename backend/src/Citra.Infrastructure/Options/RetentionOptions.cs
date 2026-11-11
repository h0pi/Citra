using System.ComponentModel.DataAnnotations;
using Citra.Application.Features.Chat.Conversations;

namespace Citra.Infrastructure.Options;

/// <summary>How long data is kept (section "Retention").</summary>
public sealed class RetentionOptions : IRetentionSettings
{
    public const string SectionName = "Retention";

    /// <summary>Chats unused for longer than this many days are deleted every day; empty = kept forever.</summary>
    [Range(1, 3650)]
    public int? ConversationDays { get; init; }
}

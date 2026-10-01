using Citra.Application.Common.Chat;
using Citra.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Citra.Infrastructure.Chat;

internal sealed class ConfiguredChatSettings(IOptions<ChatOptions> options) : IChatSettings
{
    public int MaxHistoryMessages { get; } = options.Value.MaxHistoryMessages;

    public int MaxHistoryTokens { get; } = options.Value.MaxHistoryTokens;

    public string RewriteModel { get; } = options.Value.RewriteModel;

    public int SourcesPerAnswer { get; } = options.Value.SourcesPerAnswer;
}

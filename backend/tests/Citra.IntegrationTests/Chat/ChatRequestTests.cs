using Citra.Application.Common.Chat;

namespace Citra.IntegrationTests.Chat;

/// <summary>Requests no model could answer are refused before anything is sent.</summary>
public sealed class ChatRequestTests
{
    private static readonly ChatRequest Valid = new(
        "Rules", [new ChatMessage(ChatRole.User, "Hi"), new ChatMessage(ChatRole.Assistant, "Hello"), new ChatMessage(ChatRole.User, "Question")]);

    [Fact]
    public void A_normal_request_is_fine_with_sensible_defaults()
    {
        Valid.EnsureValid();
        (Valid.Model, Valid.MaxOutputTokens).ShouldBe(((string?)null, (int?)null));
        (Valid with { MaxOutputTokens = 1 }).EnsureValid();
    }

    public static TheoryData<string, ChatRequest> Invalid => new()
    {
        { "no rules", Valid with { SystemPrompt = "  " } },
        { "no messages", Valid with { Messages = [] } },
        { "ends with the assistant", Valid with { Messages = [new ChatMessage(ChatRole.User, "Hi"), new ChatMessage(ChatRole.Assistant, "Hello")] } },
        { "an empty message", Valid with { Messages = [new ChatMessage(ChatRole.Assistant, " "), new ChatMessage(ChatRole.User, "Hi")] } },
        { "no room for an answer", Valid with { MaxOutputTokens = 0 } },
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public void Impossible_requests_are_refused(string reason, ChatRequest request)
    {
        Should.Throw<ArgumentException>(request.EnsureValid, reason);
    }
}

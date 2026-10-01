using Citra.Application.Common.Chat;
using Citra.Application.Common.Chunking;

namespace Citra.IntegrationTests.Chat;

/// <summary>C-183: how much of a conversation goes with a new question (newest messages first, within limits).</summary>
public sealed class ChatHistoryTests
{
    private static readonly ChatMessage Q1 = User("one two"), A1 = Assistant("three four five"),
        Q2 = User("six"), A2 = Assistant("seven eight"), Q3 = User("nine ten eleven twelve"), A3 = Assistant("thirteen");

    private static readonly IReadOnlyList<ChatMessage> Conversation = [Q1, A1, Q2, A2, Q3, A3];

    [Fact]
    public void Keeps_everything_that_fits()
    {
        ChatHistory.Trim(Conversation, maxMessages: 10, maxTokens: 100, Words.Instance).ShouldBe(Conversation);
    }

    [Fact]
    public void Keeps_the_newest_messages_up_to_the_limit_starting_with_a_question()
    {
        // The 3 newest are A2, Q3, A3: an answer first would be without its question, so A2 goes too.
        ChatHistory.Trim(Conversation, maxMessages: 3, maxTokens: 100, Words.Instance).ShouldBe([Q3, A3]);
        ChatHistory.Trim(Conversation, maxMessages: 4, maxTokens: 100, Words.Instance).ShouldBe([Q2, A2, Q3, A3]);
    }

    [Fact]
    public void Stops_before_the_message_that_would_go_over_the_token_budget()
    {
        // From the newest: A3 1, Q3 4, A2 2, Q2 1 = 8 words; A1 (3 more) does not fit into 10.
        ChatHistory.Trim(Conversation, maxMessages: 10, maxTokens: 10, Words.Instance).ShouldBe([Q2, A2, Q3, A3]);
        ChatHistory.Trim(Conversation, maxMessages: 10, maxTokens: 8, Words.Instance).ShouldBe([Q2, A2, Q3, A3]);
        ChatHistory.Trim(Conversation, maxMessages: 10, maxTokens: 7, Words.Instance).ShouldBe([Q3, A3]);
    }

    [Fact]
    public void Counts_each_message_by_its_own_size_from_the_newest()
    {
        var longQuestion = User("one two three four five six seven eight nine ten");
        var shortAnswer = Assistant("a");
        var shortQuestion = User("b");
        var lastAnswer = Assistant("c");

        ChatHistory.Trim([longQuestion, shortAnswer, shortQuestion, lastAnswer], maxMessages: 10, maxTokens: 3, Words.Instance)
            .ShouldBe([shortQuestion, lastAnswer]);
    }

    [Fact]
    public void Never_skips_a_big_message_to_fit_older_small_ones()
    {
        // Q3 (4 words) does not fit into 3; the older, smaller messages are not used either (the gap would mislead).
        ChatHistory.Trim(Conversation, maxMessages: 10, maxTokens: 3, Words.Instance).ShouldBeEmpty();
    }

    [Fact]
    public void No_history_or_no_room_gives_nothing()
    {
        ChatHistory.Trim([], 10, 100, Words.Instance).ShouldBeEmpty();
        ChatHistory.Trim(Conversation, maxMessages: 0, maxTokens: 100, Words.Instance).ShouldBeEmpty();
        ChatHistory.Trim(Conversation, maxMessages: 10, maxTokens: 0, Words.Instance).ShouldBeEmpty();
    }

    [Fact]
    public void Refuses_negative_limits()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => ChatHistory.Trim(Conversation, -1, 100, Words.Instance));
        Should.Throw<ArgumentOutOfRangeException>(() => ChatHistory.Trim(Conversation, 10, -1, Words.Instance));
    }

    internal static ChatMessage User(string text) => new(ChatRole.User, text);

    internal static ChatMessage Assistant(string text) => new(ChatRole.Assistant, text);

    /// <summary>Counts words as tokens, so the tests can count by eye.</summary>
    internal sealed class Words : ITokenCounter
    {
        public static readonly Words Instance = new();

        public int Count(string text) => text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

        public int IndexAfterTokens(string text, int maxTokens) => throw new NotSupportedException();

        public int IndexBeforeLastTokens(string text, int maxTokens) => throw new NotSupportedException();
    }
}

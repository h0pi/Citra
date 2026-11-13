using Citra.Application.Common.Chat;
using Citra.Infrastructure.Chat;
using Citra.Infrastructure.Chunking;
using static Citra.IntegrationTests.Chat.ChatPromptsTests;

namespace Citra.IntegrationTests.Chat;

/// <summary>The free stand-in model, which answers by quoting the sources it is given, with citations.</summary>
public sealed class ExtractiveChatModelTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly TiktokenTokenCounter Tokens = new();

    [Fact]
    public void Quotes_the_sentences_sharing_the_most_words_with_the_question_in_source_order()
    {
        var answer = Answer(
            "How many vacation days do employees get?",
            Chunk("# Leave\n\nSick days need a doctor's note. Employees get 25 vacation days per year."),
            Chunk("Parking is free."),
            Chunk("Unused vacation days carry over until March."));

        answer.ShouldBe("Employees get 25 vacation days per year [1]. Unused vacation days carry over until March [3].");
    }

    [Fact]
    public void Leaves_out_sentences_much_less_relevant_than_the_best()
    {
        // Best: 3 shared words; "Business travel ..." shares only 1 ("travel") and is left out.
        Answer(
                "How much money do I get back for meals during travel?",
                Chunk("Business travel is booked through the travel desk. Meals during travel are reimbursed up to 40 euros."))
            .ShouldBe("Meals during travel are reimbursed up to 40 euros [1].");

        // Half as relevant is enough (2 of 4 shared words).
        Answer("vacation days carry over", Chunk("Vacation days carry over until March."), Chunk("Ten vacation days."))
            .ShouldBe("Vacation days carry over until March [1]. Ten vacation days [2].");
    }

    [Fact]
    public void Quotes_at_most_two_sentences()
    {
        var answer = Answer(
            "vacation days",
            Chunk("Vacation days one."),
            Chunk("Vacation days two."),
            Chunk("Vacation days three."));

        answer.ShouldBe("Vacation days one [1]. Vacation days two [2].");
        ExtractiveChatModel.MaxSentences.ShouldBe(2);
    }

    [Fact]
    public void Without_shared_words_quotes_the_first_sentence_of_the_best_source()
    {
        // E.g. a question in Bosnian about English documents: the search already chose source 1.
        var answer = Answer(
            "Koliko dana godišnjeg odmora imam?",
            Chunk("## Vacation\nEmployees get 25 days of paid vacation. Ask your manager."),
            Chunk("Parking is free."));

        answer.ShouldBe("Employees get 25 days of paid vacation [1].");
    }

    [Fact]
    public void Joins_lines_broken_in_the_middle_of_a_sentence_like_pdf_text()
    {
        // As the PDF extractor gives a page: title line, then the text wrapped at about 80 characters.
        var page = "Expenses\nBusiness travel is booked through the travel desk. Meals during\n"
            + "travel are reimbursed up to 40 euros per day with receipts. Expense reports\n"
            + "must be submitted within 30 days of the trip.";

        Answer("How much money do I get back for meals during travel?", Chunk(page))
            .ShouldBe("Meals during travel are reimbursed up to 40 euros per day with receipts [1].");
    }

    [Fact]
    public void Leaves_out_titles_but_keeps_short_lines_that_continue_a_sentence()
    {
        // "Team Lead or HR" looks like a title but continues an unfinished sentence; a blank line ends a paragraph.
        ExtractiveChatModel.Sentences(
                "Sick leave\nIf you are ill, tell your\nTeam Lead or HR\nbefore 9 am, from\nMonday to Friday.\n\n"
                + "Payment terminal errors\nRestart it.\n- press the green key\n\nthen wait.")
            .ShouldBe([
                "If you are ill, tell your Team Lead or HR before 9 am, from Monday to Friday.",
                "Restart it.",
                "- press the green key",
                "then wait.",
            ]);
        ExtractiveChatModel.MaxTitleWords.ShouldBe(5);
    }

    [Fact]
    public void Without_shared_words_never_quotes_a_title()
    {
        var page = "Vacation\nEvery full-time employee receives 25 days of paid vacation per\ncalendar year.";

        Answer("Koliko dana godišnjeg odmora imam?", Chunk(page))
            .ShouldBe("Every full-time employee receives 25 days of paid vacation per calendar year [1].");
    }

    [Fact]
    public void Answers_not_found_when_nothing_was_found()
    {
        Answer("How many vacation days?").ShouldBe($"{ChatPrompts.NotFoundMarker} I could not find this in the documents.");
    }

    [Fact]
    public void Shows_document_text_as_written_not_escaped()
    {
        Answer("rules for <tags>", Chunk("Use <b> & <i> for \"tags\" rules."))
            .ShouldBe("Use <b> & <i> for \"tags\" rules [1].");
    }

    [Fact]
    public void Cites_sentences_ending_in_other_punctuation_or_none()
    {
        Answer("vacation question", Chunk("Is vacation paid? Yes, vacation is paid!\nthe vacation question list"))
            .ShouldBe("Is vacation paid [1]? the vacation question list [1].");
    }

    [Fact]
    public void A_prompt_without_sources_gets_the_question_back()
    {
        // E.g. "rewrite this follow-up as a standalone question": the stand-in keeps the question as it is.
        var turn = "Conversation so far...\n<question>\nAnd how many &amp; when?\n</question>";

        ExtractiveChatModel.Answer(turn).ShouldBe("And how many & when?");
        ExtractiveChatModel.Answer("  Just a message.  ").ShouldBe("Just a message.");
    }

    [Fact]
    public async Task Streams_the_answer_word_by_word_then_the_tokens_used()
    {
        var request = Request("How many vacation days?", Chunk("Employees get 25 vacation days."));
        var model = new ExtractiveChatModel(Tokens);

        var events = await model.StreamAsync(request, Ct).ToListAsync(Ct);

        var text = events.OfType<ChatTextDelta>().Select(d => d.Text).ToList();
        text.ShouldBe(["Employees ", "get ", "25 ", "vacation ", "days ", "[1]."]);
        var done = events[^1].ShouldBeOfType<ChatCompleted>();
        done.StopReason.ShouldBe("end_turn");
        done.Usage.ShouldBe(new ChatUsage(
            ExtractiveChatModel.ModelName,
            Tokens.Count(request.SystemPrompt) + Tokens.Count(request.Messages[0].Content),
            Tokens.Count(string.Concat(text))));
        events.OfType<ChatCompleted>().ShouldHaveSingleItem();
        model.DefaultModel.ShouldBe("citra-extractive");
    }

    [Fact]
    public async Task Stops_when_the_caller_cancels()
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var received = 0;

        await Should.ThrowAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in new ExtractiveChatModel(Tokens).StreamAsync(Request("vacation", Chunk("Vacation days are many.")), cancel.Token))
            {
                received++;
                await cancel.CancelAsync();
            }
        });

        received.ShouldBe(1);
    }

    [Fact]
    public async Task Refuses_invalid_requests_like_claude()
    {
        var model = new ExtractiveChatModel(Tokens);
        var valid = Request("vacation", Chunk("Vacation days."));

        await Should.ThrowAsync<ArgumentException>(() => model.StreamAsync(
            valid with { Messages = [.. valid.Messages, new ChatMessage(ChatRole.Assistant, "An answer.")] }, Ct).ToListAsync(Ct).AsTask());
    }

    private static string Answer(string question, params Citra.Application.Common.Retrieval.RetrievedChunk[] chunks) =>
        ExtractiveChatModel.Answer(ChatPrompts.UserTurn(SourceList.Number(chunks), question));

    private static ChatRequest Request(string question, params Citra.Application.Common.Retrieval.RetrievedChunk[] chunks) =>
        new(ChatPrompts.System("HR bot", null), [new ChatMessage(ChatRole.User, ChatPrompts.UserTurn(SourceList.Number(chunks), question))]);
}

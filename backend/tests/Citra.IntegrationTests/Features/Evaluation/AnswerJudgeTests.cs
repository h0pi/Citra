using Citra.Application.Common.Chat;
using Citra.Application.Features.Evaluation.Runs;
using Citra.Domain.Evaluation;
using Citra.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Citra.IntegrationTests.Features.Evaluation;

/// <summary>C-244: answers graded correct / partly correct / wrong with a reason — the free word judge and an AI judge.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class AnswerJudgeTests(DatabaseFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("Employees get 25 days of paid vacation.", "You get 25 Days of PAID vacation, employees [1].", EvalGrade.Correct, "100%")]
    [InlineData("Employees get 25 days of paid vacation.", "Employees get 25 days.", EvalGrade.Partial, "57%")]
    [InlineData("Employees get 25 days of paid vacation.", "Ask your manager.", EvalGrade.Wrong, "0%")]
    [InlineData("Zaposleni imaju 25 dana godišnjeg odmora.", "Imate 25 dana godišnjeg odmora.", EvalGrade.Partial, "67%")]
    public async Task The_word_judge_compares_the_expected_words(string expected, string actual, EvalGrade grade, string share)
    {
        var verdict = await new WordOverlapJudge().GradeAsync("Vacation?", expected, actual, Ct);

        verdict.Grade.ShouldBe(grade);
        verdict.Reason.ShouldBe($"{share} of the expected answer's words are in the answer (word comparison, no AI).");
        verdict.CostUsd.ShouldBe(0);
    }

    [Fact]
    public async Task The_ai_judge_reads_its_grade_and_reason_and_pays_for_it()
    {
        await using var scope = fixture.OpenScope(tenantId: null);
        var judge = new ModelAnswerJudge(fixture.Chat, scope.Services.GetRequiredService<IChatPricing>());
        ChatRequest? asked = null;

        JudgeVerdict verdict;
        using (fixture.Chat.Script(request =>
        {
            asked = request;
            return FakeChatModel.Stream(new ChatUsage("claude-test", 100, 20), "Sure: {\"grade\": \"Partial\", ", "\"reason\": \"The number is missing.\"}");
        }))
        {
            verdict = await judge.GradeAsync("Vacation?", "25 days.", "Some days.", Ct);
        }

        verdict.ShouldBe(new JudgeVerdict(EvalGrade.Partial, "The number is missing.", 0.0004m));
        asked!.SystemPrompt.ShouldBe(ModelAnswerJudge.SystemPrompt);
        asked.Messages[0].Content.ShouldContain("<expected_answer>25 days.</expected_answer>");
        asked.Messages[0].Content.ShouldContain("<actual_answer>Some days.</actual_answer>");
    }

    [Theory]
    [InlineData("I think it is fine.")]
    [InlineData("{\"grade\": \"great\"}")]
    [InlineData("{\"grade\": ")]
    public async Task An_unreadable_ai_reply_counts_as_wrong(string reply)
    {
        await using var scope = fixture.OpenScope(tenantId: null);
        var judge = new ModelAnswerJudge(fixture.Chat, scope.Services.GetRequiredService<IChatPricing>());

        using (fixture.Chat.Script(_ => FakeChatModel.Stream(new ChatUsage("claude-test", 10, 5), reply)))
        {
            var verdict = await judge.GradeAsync("Q?", "A.", "B.", Ct);
            (verdict.Grade, verdict.Reason).ShouldBe((EvalGrade.Wrong, "The judge's reply could not be read."));
        }
    }

    [Fact]
    public async Task Without_a_real_model_the_free_word_judge_grades()
    {
        await using var scope = fixture.OpenScope(tenantId: null);

        scope.Services.GetRequiredService<IAnswerJudge>().ShouldBeOfType<WordOverlapJudge>();
    }
}

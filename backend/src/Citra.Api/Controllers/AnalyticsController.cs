using Citra.Application.Features.Analytics;
using Citra.Application.Features.Chat.Conversations;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Citra.Api.Controllers;

/// <summary>The analytics dashboard: how the company's bots are used, for a period of days.</summary>
[ApiController]
[Authorize]
[Route("api/analytics")]
[Produces("application/json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
public sealed class AnalyticsController(ISender sender) : ControllerBase
{
    /// <summary>Headline numbers from <paramref name="from"/> to <paramref name="to"/> (UTC days), for one bot or all.</summary>
    [HttpGet("stats")]
    [ProducesResponseType<DashboardStatsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<DashboardStatsDto> GetStats(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] Guid? botId, CancellationToken cancellationToken) =>
        sender.Send(new GetDashboardStatsQuery(botId, from, to), cancellationToken);

    /// <summary>Questions per day from <paramref name="from"/> to <paramref name="to"/> (UTC days), every day included.</summary>
    [HttpGet("messages-per-day")]
    [ProducesResponseType<IReadOnlyList<DayCountDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<IReadOnlyList<DayCountDto>> GetMessagesPerDay(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] Guid? botId, CancellationToken cancellationToken) =>
        sender.Send(new GetMessagesPerDayQuery(botId, from, to), cancellationToken);

    /// <summary>The most asked questions of the period; wordings that differ only in case or punctuation count as one.</summary>
    [HttpGet("top-questions")]
    [ProducesResponseType<IReadOnlyList<TopQuestionDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<IReadOnlyList<TopQuestionDto>> GetTopQuestions(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] Guid? botId,
        [FromQuery] int limit = GetTopQuestionsQuery.DefaultLimit,
        CancellationToken cancellationToken = default) =>
        sender.Send(new GetTopQuestionsQuery(botId, from, to, limit), cancellationToken);

    /// <summary>Questions of the period the documents could not answer ("knowledge gaps"), newest first.</summary>
    [HttpGet("unanswered-questions")]
    [ProducesResponseType<IReadOnlyList<UnansweredQuestionDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<IReadOnlyList<UnansweredQuestionDto>> GetUnansweredQuestions(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] Guid? botId,
        [FromQuery] int limit = GetUnansweredQuestionsQuery.DefaultLimit,
        CancellationToken cancellationToken = default) =>
        sender.Send(new GetUnansweredQuestionsQuery(botId, from, to, limit), cancellationToken);

    /// <summary>AI cost of the period per bot: answers and embeddings, the most expensive bot first.</summary>
    [HttpGet("costs")]
    [ProducesResponseType<IReadOnlyList<BotCostDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<IReadOnlyList<BotCostDto>> GetCosts(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] Guid? botId, CancellationToken cancellationToken) =>
        sender.Send(new GetCostBreakdownQuery(botId, from, to), cancellationToken);

    /// <summary>Good and bad ratings of the period's answers, and the badly rated answers (newest first).</summary>
    [HttpGet("feedback")]
    [ProducesResponseType<FeedbackSummaryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<FeedbackSummaryDto> GetFeedback(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] Guid? botId, CancellationToken cancellationToken) =>
        sender.Send(new GetFeedbackSummaryQuery(botId, from, to), cancellationToken);

    /// <summary>Any chat of the company, read-only (owners and admins), e.g. to see why a question went unanswered.</summary>
    [HttpGet("conversations/{id:guid}")]
    [ProducesResponseType<ConversationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<ConversationDto> GetConversation(Guid id, CancellationToken cancellationToken) =>
        sender.Send(new GetCompanyConversationQuery(id), cancellationToken);
}

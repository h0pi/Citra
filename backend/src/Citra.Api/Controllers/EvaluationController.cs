using Citra.Application.Features.Evaluation;
using Citra.Application.Features.Evaluation.Runs;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Citra.Api.Controllers;

/// <summary>
/// Eval sets: test questions with the answers a correct bot should give, to measure a bot's accuracy. Everyone in
/// the company may read them; owners and admins change them.
/// </summary>
[ApiController]
[Authorize]
[Route("api")]
[Produces("application/json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
public sealed class EvaluationController(ISender sender) : ControllerBase
{
    /// <summary>A bot's eval sets, newest first.</summary>
    [HttpGet("bots/{botId:guid}/eval-sets")]
    [ProducesResponseType<IReadOnlyList<EvalSetSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<IReadOnlyList<EvalSetSummaryDto>> GetSets(Guid botId, CancellationToken cancellationToken) =>
        sender.Send(new GetEvalSetsQuery(botId), cancellationToken);

    /// <summary>Creates an empty eval set for a bot.</summary>
    [HttpPost("bots/{botId:guid}/eval-sets")]
    [ProducesResponseType<EvalSetDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EvalSetDto>> CreateSet(Guid botId, EvalSetRequest request, CancellationToken cancellationToken)
    {
        var set = await sender.Send(new CreateEvalSetCommand(botId, request.Name ?? string.Empty, request.Description), cancellationToken);
        return CreatedAtAction(nameof(GetSet), new { id = set.Id }, set);
    }

    /// <summary>One eval set with its questions.</summary>
    [HttpGet("eval-sets/{id:guid}")]
    [ProducesResponseType<EvalSetDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<EvalSetDto> GetSet(Guid id, CancellationToken cancellationToken) =>
        sender.Send(new GetEvalSetQuery(id), cancellationToken);

    /// <summary>Renames an eval set.</summary>
    [HttpPut("eval-sets/{id:guid}")]
    [ProducesResponseType<EvalSetDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<EvalSetDto> UpdateSet(Guid id, EvalSetRequest request, CancellationToken cancellationToken) =>
        sender.Send(new UpdateEvalSetCommand(id, request.Name ?? string.Empty, request.Description), cancellationToken);

    /// <summary>Deletes an eval set with its questions and runs.</summary>
    [HttpDelete("eval-sets/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteSet(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new DeleteEvalSetCommand(id), cancellationToken);
        return NoContent();
    }

    /// <summary>Adds a test question to an eval set.</summary>
    [HttpPost("eval-sets/{id:guid}/questions")]
    [ProducesResponseType<EvalQuestionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<EvalQuestionDto> AddQuestion(Guid id, EvalQuestionRequest request, CancellationToken cancellationToken) =>
        sender.Send(
            new AddEvalQuestionCommand(id, request.Question ?? string.Empty, request.ExpectedAnswer ?? string.Empty, request.ExpectedDocumentId),
            cancellationToken);

    /// <summary>
    /// Adds test questions from a CSV file's text (columns question, expected_answer and optionally
    /// expected_document); nothing is added unless every row is valid.
    /// </summary>
    [HttpPost("eval-sets/{id:guid}/questions/import")]
    [RequestSizeLimit(2_000_000)]
    [ProducesResponseType<EvalSetDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<EvalSetDto> ImportQuestions(Guid id, EvalImportRequest request, CancellationToken cancellationToken) =>
        sender.Send(new ImportEvalQuestionsCommand(id, request.Csv ?? string.Empty), cancellationToken);

    /// <summary>Starts a run of the eval set; the questions are answered in the background (poll the run for progress).</summary>
    [HttpPost("eval-sets/{id:guid}/runs")]
    [ProducesResponseType<EvalRunDto>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EvalRunDto>> StartRun(Guid id, EvalRunRequest request, CancellationToken cancellationToken)
    {
        var run = await sender.Send(new StartEvalRunCommand(id, request.Notes), cancellationToken);
        return AcceptedAtAction(nameof(GetRun), new { id = run.Id }, run);
    }

    /// <summary>An eval set's runs, newest first.</summary>
    [HttpGet("eval-sets/{id:guid}/runs")]
    [ProducesResponseType<IReadOnlyList<EvalRunDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<IReadOnlyList<EvalRunDto>> GetRuns(Guid id, CancellationToken cancellationToken) =>
        sender.Send(new GetEvalRunsQuery(id), cancellationToken);

    /// <summary>A run's result per question: the bot's answer, its grade and the reason, in the set's order.</summary>
    [HttpGet("eval-runs/{id:guid}/results")]
    [ProducesResponseType<IReadOnlyList<EvalResultDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<IReadOnlyList<EvalResultDto>> GetResults(Guid id, CancellationToken cancellationToken) =>
        sender.Send(new GetEvalRunResultsQuery(id), cancellationToken);

    /// <summary>One eval run: its state, progress (answered questions), scores and cost.</summary>
    [HttpGet("eval-runs/{id:guid}")]
    [ProducesResponseType<EvalRunDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<EvalRunDto> GetRun(Guid id, CancellationToken cancellationToken) =>
        sender.Send(new GetEvalRunQuery(id), cancellationToken);

    /// <summary>Changes a test question.</summary>
    [HttpPut("eval-sets/{id:guid}/questions/{questionId:guid}")]
    [ProducesResponseType<EvalQuestionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<EvalQuestionDto> UpdateQuestion(
        Guid id, Guid questionId, EvalQuestionRequest request, CancellationToken cancellationToken) =>
        sender.Send(
            new UpdateEvalQuestionCommand(
                id, questionId, request.Question ?? string.Empty, request.ExpectedAnswer ?? string.Empty, request.ExpectedDocumentId),
            cancellationToken);

    /// <summary>Removes a test question.</summary>
    [HttpDelete("eval-sets/{id:guid}/questions/{questionId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteQuestion(Guid id, Guid questionId, CancellationToken cancellationToken)
    {
        await sender.Send(new DeleteEvalQuestionCommand(id, questionId), cancellationToken);
        return NoContent();
    }
}

public sealed record EvalSetRequest(string? Name, string? Description);

public sealed record EvalQuestionRequest(string? Question, string? ExpectedAnswer, Guid? ExpectedDocumentId);

public sealed record EvalImportRequest(string? Csv);

public sealed record EvalRunRequest(string? Notes);

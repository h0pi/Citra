using System.Text;
using Citra.Application.Common.Persistence;
using Citra.Application.Common.Security;
using Citra.Domain.Evaluation;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ValidationException = Citra.Application.Common.Exceptions.ValidationException;

namespace Citra.Application.Features.Evaluation;

/// <summary>
/// Adds test questions from a CSV file (owners and admins). The first row names the columns: <c>question</c>,
/// <c>expected_answer</c> and optionally <c>expected_document</c> (a file name of one of the bot's documents).
/// Commas or semicolons (Excel in many countries) separate the values; quotes work as usual. Nothing is added
/// unless every row is valid, and the errors name the rows.
/// </summary>
public sealed record ImportEvalQuestionsCommand(Guid EvalSetId, string Csv) : IRequest<EvalSetDto>
{
    public const int MaxQuestions = 500;
    public const int MaxCsvLength = 1_000_000;
}

internal sealed class ImportEvalQuestionsCommandValidator : AbstractValidator<ImportEvalQuestionsCommand>
{
    public ImportEvalQuestionsCommandValidator() =>
        RuleFor(x => x.Csv)
            .NotEmpty().WithMessage("Choose a CSV file with questions.")
            .MaximumLength(ImportEvalQuestionsCommand.MaxCsvLength).WithMessage("The file is too large (at most 1 MB).");
}

internal sealed class ImportEvalQuestionsCommandHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<ImportEvalQuestionsCommand, EvalSetDto>
{
    private const string Field = "Csv";
    private const int MaxReportedErrors = 10;

    public async Task<EvalSetDto> Handle(ImportEvalQuestionsCommand request, CancellationToken cancellationToken)
    {
        currentUser.RequireEditorTenantId();
        var set = await db.FindEvalSetAsync(request.EvalSetId, cancellationToken);

        var rows = EvalCsv.Parse(request.Csv);
        var header = rows.Count > 0 ? rows[0].Select(h => h.Trim().ToLowerInvariant()).ToList() : [];
        var (question, answer, document) =
            (header.IndexOf("question"), header.IndexOf("expected_answer"), header.IndexOf("expected_document"));
        if (question < 0 || answer < 0)
        {
            throw new ValidationException(Field, "The first row must name the columns question and expected_answer.");
        }

        var lines = rows.Skip(1).Select((cells, i) => (Row: i + 2, Cells: cells)).Where(r => r.Cells.Any(c => c.Trim().Length > 0)).ToList();
        if (lines.Count == 0 || lines.Count > ImportEvalQuestionsCommand.MaxQuestions)
        {
            throw new ValidationException(
                Field, $"The file must have between 1 and {ImportEvalQuestionsCommand.MaxQuestions} questions.");
        }

        // File names of the bot's documents, so a row can name its expected document.
        var documents = await db.Documents
            .Where(d => d.BotId == set.BotId)
            .Select(d => new { d.Id, d.FileName })
            .ToListAsync(cancellationToken);

        var errors = new List<string>();
        var parsed = new List<(string Question, string Answer, Guid? DocumentId)>();
        foreach (var (row, cells) in lines)
        {
            string Cell(int index) => index >= 0 && index < cells.Length ? cells[index].Trim() : string.Empty;
            var (text, expected, documentName) = (Cell(question), Cell(answer), Cell(document));
            var documentId = documents.FirstOrDefault(d => string.Equals(d.FileName, documentName, StringComparison.OrdinalIgnoreCase))?.Id;

            if (text.Length is 0 or > EvalQuestion.QuestionMaxLength)
            {
                errors.Add($"Row {row}: the question must have 1 to {EvalQuestion.QuestionMaxLength} characters.");
            }
            else if (expected.Length is 0 or > EvalQuestion.ExpectedAnswerMaxLength)
            {
                errors.Add($"Row {row}: the expected answer must have 1 to {EvalQuestion.ExpectedAnswerMaxLength} characters.");
            }
            else if (documentName.Length > 0 && documentId is null)
            {
                errors.Add($"Row {row}: the bot has no document named \"{documentName}\".");
            }
            else
            {
                parsed.Add((text, expected, documentId));
            }
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(new Dictionary<string, string[]> { [Field] = [.. errors.Take(MaxReportedErrors)] });
        }

        foreach (var (text, expected, documentId) in parsed)
        {
            set.AddQuestion(text, expected, documentId);
        }

        await db.SaveChangesAsync(cancellationToken);
        return EvalSets.ToDto(set);
    }
}

/// <summary>A small CSV reader: quoted values may hold separators, line breaks and doubled quotes ("").</summary>
internal static class EvalCsv
{
    public static List<string[]> Parse(string csv)
    {
        csv = csv.TrimStart('﻿');
        var firstLine = csv.Split('\n', 2)[0];
        var separator = firstLine.Contains(';') && !firstLine.Contains(',') ? ';' : ',';

        var rows = new List<string[]>();
        var row = new List<string>();
        var value = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < csv.Length; i++)
        {
            var c = csv[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < csv.Length && csv[i + 1] == '"')
                {
                    value.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    value.Append(c);
                }
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == separator)
            {
                row.Add(value.ToString());
                value.Clear();
            }
            else if (c is '\n' or '\r')
            {
                if (c == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n')
                {
                    i++;
                }

                row.Add(value.ToString());
                value.Clear();
                rows.Add([.. row]);
                row.Clear();
            }
            else
            {
                value.Append(c);
            }
        }

        if (value.Length > 0 || row.Count > 0)
        {
            row.Add(value.ToString());
            rows.Add([.. row]);
        }

        return rows;
    }
}

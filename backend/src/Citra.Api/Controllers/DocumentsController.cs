using Citra.Api.Filters;
using Citra.Application.Common.Exceptions;
using Citra.Application.Common.Paging;
using Citra.Application.Common.Storage;
using Citra.Application.Features.Documents;
using Citra.Application.Features.Documents.AddUrl;
using Citra.Application.Features.Documents.Delete;
using Citra.Application.Features.Documents.GetById;
using Citra.Application.Features.Documents.GetFile;
using Citra.Application.Features.Documents.List;
using Citra.Application.Features.Documents.Reprocess;
using Citra.Application.Features.Documents.Upload;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Citra.Api.Controllers;

/// <summary>
/// A bot's knowledge: uploaded files and web pages. Lists and uploads live under the bot
/// (/api/bots/{botId}/documents); a single document is addressed directly (/api/documents/{id}).
/// Viewing is open to every logged-in user of the company; changes need Owner or Admin (403 otherwise).
/// </summary>
[ApiController]
[Authorize]
[Route("api")]
[Produces("application/json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
public sealed class DocumentsController(ISender sender, IUploadLimits uploadLimits) : ControllerBase
{
    /// <summary>
    /// Hard cap on the upload request, a little above the default 20 MB file limit (multipart adds some bytes).
    /// The exact limit (Storage:MaxUploadSizeMb) is enforced on the real bytes by the upload command;
    /// raise this too if you raise that setting above ~24 MB.
    /// </summary>
    private const long MaxUploadRequestBytes = 25L * 1024 * 1024;

    /// <summary>A bot's documents, newest first. Filter by status group and search by name.</summary>
    [HttpGet("bots/{botId:guid}/documents")]
    [ProducesResponseType<PagedResult<DocumentDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<PagedResult<DocumentDto>> GetAll(
        Guid botId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = GetDocumentsQuery.DefaultPageSize,
        [FromQuery] DocumentStatusFilter? status = null,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default) =>
        sender.Send(new GetDocumentsQuery(botId, page, pageSize, status, search), cancellationToken);

    /// <summary>
    /// Uploads a file (.pdf, .docx, .txt, .md; max 20 MB) as multipart/form-data in the field "file".
    /// The document starts as Uploaded and is processed in the background.
    /// </summary>
    [HttpPost("bots/{botId:guid}/documents")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxUploadRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadRequestBytes)]
    [DisableFormValueModelBinding]
    [ProducesResponseType<DocumentDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    public async Task<ActionResult<DocumentDto>> Upload(Guid botId, CancellationToken cancellationToken)
    {
        // The form is read here rather than bound to an IFormFile parameter: when the web server stops an
        // oversized upload, model binding would answer with a cryptic generic 400. This way it becomes the same
        // "larger than the 20 MB limit" message on the "file" field as any other size problem.
        IFormFile? file;
        try
        {
            var form = await Request.ReadFormAsync(cancellationToken);
            file = form.Files.GetFile("file");
        }
        catch (Exception ex) when (ex is BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge }
            or InvalidDataException)
        {
            throw new ValidationException(
                UploadDocumentCommandValidator.FileField, UploadDocumentCommandValidator.TooLargeMessage(uploadLimits));
        }

        // No file: let the command's validation answer ("Choose a file to upload.") like any other field error.
        await using var content = file?.OpenReadStream() ?? Stream.Null;
        var document = await sender.Send(
            new UploadDocumentCommand(botId, file?.FileName ?? string.Empty, file?.Length ?? 0, content),
            cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = document.Id }, document);
    }

    /// <summary>Adds a public web page by its address. It is downloaded and read in the background.</summary>
    [HttpPost("bots/{botId:guid}/documents/url")]
    [ProducesResponseType<DocumentDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DocumentDto>> AddUrl(
        Guid botId, AddUrlRequest request, CancellationToken cancellationToken)
    {
        var document = await sender.Send(new AddUrlDocumentCommand(botId, request.Url), cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = document.Id }, document);
    }

    /// <summary>One document with its processing state (page and chunk counts, or the error).</summary>
    [HttpGet("documents/{id:guid}")]
    [ProducesResponseType<DocumentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<DocumentDto> GetById(Guid id, CancellationToken cancellationToken) =>
        sender.Send(new GetDocumentByIdQuery(id), cancellationToken);

    /// <summary>
    /// The original file. Shown in the browser (e.g. the PDF viewer); add ?download=true to save it instead.
    /// Supports range requests, so large PDFs can load page by page.
    /// </summary>
    [HttpGet("documents/{id:guid}/file")]
    [Produces("application/octet-stream")]
    [ProducesResponseType<FileStreamResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetFile(
        Guid id, [FromQuery] bool download = false, CancellationToken cancellationToken = default)
    {
        var file = await sender.Send(new GetDocumentFileQuery(id), cancellationToken);

        // User-uploaded content served from the API's own origin: never let the browser guess another type,
        // and sandbox it so nothing in it (e.g. script in a PDF or HTML-looking text) can act as this site.
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.ContentSecurityPolicy = "sandbox; default-src 'none'; style-src 'unsafe-inline'";

        return download
            ? File(file.Content, file.ContentType, file.FileName, enableRangeProcessing: true)
            : InlineFile(file);
    }

    /// <summary>Removes the document, its chunks and its file. Past answers keep their citation text.</summary>
    [HttpDelete("documents/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new DeleteDocumentCommand(id), cancellationToken);
        return NoContent();
    }

    /// <summary>Processes a Ready or Failed document again (409 while it is queued or processing).</summary>
    [HttpPost("documents/{id:guid}/reprocess")]
    [ProducesResponseType<DocumentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<DocumentDto> Reprocess(Guid id, CancellationToken cancellationToken) =>
        sender.Send(new ReprocessDocumentCommand(id), cancellationToken);

    /// <summary>File(...) with a name makes it an attachment; for inline display the header is set by hand.</summary>
    private FileStreamResult InlineFile(DocumentFile file)
    {
        var disposition = new Microsoft.Net.Http.Headers.ContentDispositionHeaderValue("inline");
        disposition.SetHttpFileName(file.FileName);
        Response.Headers.ContentDisposition = disposition.ToString();
        return File(file.Content, file.ContentType, enableRangeProcessing: true);
    }
}

/// <summary>Body of POST /api/bots/{botId}/documents/url.</summary>
public sealed record AddUrlRequest(string Url);

using Citra.Application.Common.Exceptions;
using Citra.Application.Common.Persistence;
using Citra.Application.Common.Security;
using Citra.Application.Common.Storage;
using Citra.Domain.Bots;
using Citra.Domain.Documents;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Citra.Application.Features.Documents.Upload;

internal sealed class UploadDocumentCommandHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IFileStorage storage,
    IUploadLimits limits) : IRequestHandler<UploadDocumentCommand, DocumentDto>
{
    private const string FileField = UploadDocumentCommandValidator.FileField;

    public async Task<DocumentDto> Handle(UploadDocumentCommand request, CancellationToken cancellationToken)
    {
        var tenantId = currentUser.RequireEditorTenantId();

        // The tenant filter hides other companies' bots, so uploading into one is a plain 404.
        if (!await db.Bots.AnyAsync(b => b.Id == request.BotId, cancellationToken))
        {
            throw new NotFoundException(nameof(Bot), request.BotId);
        }

        var fileName = CleanFileName(request.FileName);
        var type = SupportedFileTypes.FromFileName(fileName)
            ?? throw new ValidationException(FileField, $"This file type is not supported. Upload {SupportedFileTypes.AllowedList}.");

        // Check the content before storing anything.
        var header = new byte[SupportedFileTypes.HeaderLength];
        var headerLength = await request.Content.ReadAtLeastAsync(
            header, header.Length, throwOnEndOfStream: false, cancellationToken);
        Array.Resize(ref header, headerLength);

        if (headerLength == 0)
        {
            throw new ValidationException(FileField, "The file is empty.");
        }

        if (!SupportedFileTypes.LooksLike(type, header))
        {
            throw new ValidationException(
                FileField, $"This file does not look like a real {type.Description} file. It may be damaged or renamed.");
        }

        var documentId = Guid.CreateVersion7();
        var path = StoragePaths.OriginalFile(tenantId, request.BotId, documentId, fileName);

        long size;
        string hash;
        using (var upload = new UploadStream(header, request.Content, limits.MaxUploadBytes))
        {
            try
            {
                size = await storage.SaveAsync(path, upload, cancellationToken);
            }
            catch (FileTooLargeException ex)
            {
                throw new ValidationException(FileField, ex.Message);
            }

            hash = upload.Sha256Hex();
        }

        var documentFolder = StoragePaths.DocumentFolder(tenantId, request.BotId, documentId);

        // The hash is only known once the whole file was read, so the duplicate check comes after storing.
        if (await FindDuplicateAsync(request.BotId, hash, cancellationToken) is { } existing)
        {
            await storage.DeleteFolderAsync(documentFolder, CancellationToken.None);
            throw DuplicateError(existing);
        }

        var document = Document.FromUpload(
            tenantId, request.BotId, fileName, type.ContentType, size, type.SourceType, path, hash, documentId);
        db.Documents.Add(document);
        db.ProcessingJobs.Add(ProcessingJob.For(document.Id));

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // No row points to the file, so nothing would ever clean it up: remove it now.
            await storage.DeleteFolderAsync(documentFolder, CancellationToken.None);

            // Two uploads of the same file at the same moment both pass the check above; the unique index
            // (bot_id, content_hash) lets only one in. The loser gets the same friendly message.
            if (ex is DbUpdateException
                && await FindDuplicateAsync(request.BotId, hash, CancellationToken.None) is { } winner)
            {
                throw DuplicateError(winner);
            }

            throw;
        }

        return DocumentDto.From(document);
    }

    private Task<string?> FindDuplicateAsync(Guid botId, string hash, CancellationToken cancellationToken) =>
        db.Documents
            .Where(d => d.BotId == botId && d.ContentHash == hash)
            .Select(d => d.FileName)
            .FirstOrDefaultAsync(cancellationToken);

    private static ValidationException DuplicateError(string existingFileName) =>
        new(FileField, $"This file is already in this bot as \"{existingFileName}\".");

    /// <summary>
    /// "C:\Users\me\Handbook.pdf" or "../../Handbook.pdf" → "Handbook.pdf" (display only, never a path), without
    /// invisible characters, so the file type is decided on the name people will see.
    /// </summary>
    private static string CleanFileName(string fileName)
    {
        var name = fileName.Replace('\\', '/');
        return Document.CleanName(name[(name.LastIndexOf('/') + 1)..]);
    }
}

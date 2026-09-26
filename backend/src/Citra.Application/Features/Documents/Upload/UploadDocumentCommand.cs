using MediatR;

namespace Citra.Application.Features.Documents.Upload;

/// <summary>
/// Adds a file to a bot's knowledge: stores it and queues it for processing (text extraction, chunking,
/// embeddings) by the Worker. <paramref name="Length"/> is the size the client announced; the real size is
/// checked again while the file is read.
/// </summary>
public sealed record UploadDocumentCommand(Guid BotId, string FileName, long Length, Stream Content)
    : IRequest<DocumentDto>;

using Citra.Domain.Documents;

namespace Citra.Application.Features.Documents;

/// <summary>A document and its processing state, as the documents page shows it.</summary>
public sealed record DocumentDto(
    Guid Id,
    Guid BotId,
    string FileName,
    string ContentType,
    long SizeBytes,
    DocumentSourceType SourceType,
    string? SourceUrl,
    DocumentStatus Status,
    string? ErrorMessage,
    int PageCount,
    int ChunkCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ProcessedAt)
{
    public static DocumentDto From(Document document) => new(
        document.Id,
        document.BotId,
        document.FileName,
        document.ContentType,
        document.SizeBytes,
        document.SourceType,
        document.SourceUrl,
        document.Status,
        document.ErrorMessage,
        document.PageCount,
        document.ChunkCount,
        document.CreatedAt,
        document.UpdatedAt,
        document.ProcessedAt);
}

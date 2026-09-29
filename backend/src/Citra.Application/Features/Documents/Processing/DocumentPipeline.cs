using System.Diagnostics;
using Citra.Application.Common.Chunking;
using Citra.Application.Common.Embeddings;
using Citra.Application.Common.Extraction;
using Citra.Application.Common.Persistence;
using Citra.Domain.Documents;
using Citra.Domain.Usage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Citra.Application.Features.Documents.Processing;

/// <summary>
/// Turns one document into searchable chunks: extract → chunk → embed → ready. Each step first saves the
/// document's new status, so the documents page shows how far processing is while the step runs; the last one
/// saves the chunks, the Ready document and the finished job together.
/// </summary>
internal sealed class DocumentPipeline(
    IApplicationDbContext db,
    ITextExtractorFactory extractors,
    TextChunker chunker,
    IEmbeddingService embeddings,
    TimeProvider timeProvider,
    ILogger<DocumentPipeline> logger)
{
    internal const string NoTextMessage =
        "No readable text was found in this document. Check that it contains text (not only images) and upload it again.";

    public async Task RunAsync(ProcessingJob job, CancellationToken cancellationToken)
    {
        // The Worker processes every tenant's documents and has no logged-in user: opt out of the tenant filter.
        var document = await db.Documents
            .IgnoreQueryFilters([QueryFilters.Tenant])
            .SingleAsync(d => d.Id == job.DocumentId, cancellationToken);

        var text = await ExtractAsync(document, cancellationToken);
        var chunks = await ChunkAsync(document, text, cancellationToken);
        var embedded = await EmbedAsync(document, chunks, cancellationToken);
        await CompleteAsync(job, document, text, embedded, cancellationToken);
    }

    /// <summary>
    /// Step 1: the document's text, page by page, cleaned (see <see cref="TextCleaner"/>). A web page's title
    /// replaces its address as the document name. Throws <see cref="TextExtractionException"/> with a message
    /// for the user when the document cannot be read or has no text.
    /// </summary>
    internal async Task<ExtractedDocument> ExtractAsync(Document document, CancellationToken cancellationToken)
    {
        document.StartExtracting();
        await db.SaveChangesAsync(cancellationToken);

        var started = Stopwatch.GetTimestamp();
        var extracted = await extractors.For(document.SourceType).ExtractAsync(document, cancellationToken);
        var text = TextCleaner.Clean(extracted);

        if (!text.HasText)
        {
            throw new TextExtractionException(NoTextMessage);
        }

        // Only a web page is renamed: its "name" is just the address. An uploaded file keeps its file name
        // (downloads use it); a Word file's built-in title is often a leftover like "Document1".
        if (document.SourceType == DocumentSourceType.Url && text.Title is { } title)
        {
            document.SetTitle(title);
        }

        logger.LogInformation(
            "Extracted {PageCount} page(s), {CharacterCount} characters from document {DocumentId} in {ElapsedMs:0} ms",
            text.Pages.Count, text.CharacterCount, document.Id, Stopwatch.GetElapsedTime(started).TotalMilliseconds);

        return text;
    }

    /// <summary>
    /// Step 2: the text split into chunks (see <see cref="TextChunker"/>): page numbers for PDF/Word, and every
    /// chunk's embedding text starts with the document title. The chunks are saved together with their
    /// embeddings in the next step, so a document never has chunks that cannot be found.
    /// </summary>
    internal async Task<IReadOnlyList<TextChunk>> ChunkAsync(
        Document document, ExtractedDocument text, CancellationToken cancellationToken)
    {
        document.StartChunking();
        await db.SaveChangesAsync(cancellationToken); // also saves a web page's new name from step 1

        var started = Stopwatch.GetTimestamp();
        var title = ChunkContext.DocumentTitle(document.SourceType, document.FileName, text.Title);
        var chunks = chunker.Chunk(text, TextChunker.HasPages(document.SourceType), title);

        if (chunks.Count == 0)
        {
            throw new TextExtractionException(NoTextMessage);
        }

        logger.LogInformation(
            "Split document {DocumentId} into {ChunkCount} chunk(s), {TokenCount} tokens, in {ElapsedMs:0} ms",
            document.Id, chunks.Count, chunks.Sum(c => c.TokenCount), Stopwatch.GetElapsedTime(started).TotalMilliseconds);

        return chunks;
    }

    /// <summary>
    /// Step 3: every chunk's embedding text turned into a vector. Returns the chunk entities with their vectors,
    /// saved by <see cref="CompleteAsync"/>. Throws <see cref="EmbeddingException"/> when the embedding provider
    /// fails (after its own retries).
    /// </summary>
    internal async Task<IReadOnlyList<Chunk>> EmbedAsync(
        Document document, IReadOnlyList<TextChunk> chunks, CancellationToken cancellationToken)
    {
        document.StartEmbedding();
        await db.SaveChangesAsync(cancellationToken);

        var started = Stopwatch.GetTimestamp();
        var result = await embeddings.EmbedDocumentsAsync([.. chunks.Select(c => c.EmbeddingText)], cancellationToken);
        result.Validate(chunks.Count, Chunk.EmbeddingDimensions); // vector i must belong to chunk i

        // The provider bills these tokens even if saving the chunks fails below, so the cost is recorded first.
        db.EmbeddingUsages.Add(EmbeddingUsage.ForDocument(
            document.TenantId, document.BotId, document.Id, embeddings.Model, result.TotalTokens,
            EmbeddingCost.Calculate(result.TotalTokens, embeddings.PricePerMillionTokensUsd)));
        await db.SaveChangesAsync(cancellationToken);

        var entities = new List<Chunk>(chunks.Count);
        for (var i = 0; i < chunks.Count; i++)
        {
            var entity = chunks[i].ToEntity(document.Id, document.BotId);
            entity.SetEmbedding(result.Vectors[i]);
            entities.Add(entity);
        }

        logger.LogInformation(
            "Embedded {ChunkCount} chunk(s) of document {DocumentId}: {Tokens} tokens with {Model} in {ElapsedMs:0} ms",
            entities.Count, document.Id, result.TotalTokens, embeddings.Model, Stopwatch.GetElapsedTime(started).TotalMilliseconds);

        return entities;
    }

    /// <summary>
    /// Final step, in one transaction — all or nothing, so a search never sees half a document and a document is
    /// never Ready without its chunks: any old chunks of the document removed, the new chunks with their vectors
    /// added, the document Ready with its page and chunk counts, and the job Succeeded. If another worker took the
    /// job over meanwhile, saving the job fails (its attempt number is a concurrency token) and nothing of this is
    /// kept — not even the removal, so old chunks are never lost without their replacement.
    /// </summary>
    internal async Task CompleteAsync(
        ProcessingJob job, Document document, ExtractedDocument text, IReadOnlyList<Chunk> chunks, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        // Pages only mean something for PDF and Word files; text files and web pages are one flowing text.
        var pageCount = TextChunker.HasPages(document.SourceType) ? text.Pages.Count : 0;

        db.Chunks.AddRange(chunks);
        document.MarkReady(pageCount, chunks.Count, now);
        job.Succeed(now);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Reprocessing already removes a document's chunks when it is queued; this makes the step safe on its own,
        // whatever left chunks behind: new chunks take positions 0, 1, 2… again, which must be free.
        await db.Chunks.Where(c => c.DocumentId == document.Id).ExecuteDeleteAsync(cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation(
            "Document {DocumentId} is ready: {PageCount} page(s), {ChunkCount} chunk(s)", document.Id, pageCount, chunks.Count);
    }
}

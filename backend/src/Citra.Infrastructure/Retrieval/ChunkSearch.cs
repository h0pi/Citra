using Citra.Application.Common.Retrieval;
using Citra.Domain.Documents;
using Citra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Pgvector;

namespace Citra.Infrastructure.Retrieval;

/// <summary>
/// Chunk searches in PostgreSQL. Raw SQL (not LINQ) for the pgvector and full-text operators; bots are chosen by
/// id, so the tenant query filter does not apply here — the retriever's callers check access to the bot.
/// Column names are snake_case: EF maps them to <see cref="ChunkHit"/>'s properties by the naming convention.
/// </summary>
internal sealed class ChunkSearch(CitraDbContext db) : IChunkSearch
{
    public async Task<IReadOnlyList<ChunkHit>> NearestAsync(
        Guid botId, float[] queryVector, int limit, CancellationToken cancellationToken)
    {
        var vector = ToVector(queryVector);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        // The HNSW index returns the nearest chunks of ALL bots, and only then are other bots' chunks filtered
        // out: with its default candidate list (40) a bot among many bigger ones could get few or no results.
        // Iterative scan keeps searching until enough of this bot's chunks are found. "relaxed_order" is the fast
        // mode, whose order can be slightly off, so the nearest rows are sorted again outside the CTE.
        // ef_search: candidates the index keeps per step (default 40); more = better recall for a little time.
        // SET LOCAL only lasts until the end of the transaction: open one unless the caller already has one.
        await using var transaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;
        await db.Database.ExecuteSqlRawAsync(
            "SET LOCAL hnsw.iterative_scan = relaxed_order; SET LOCAL hnsw.ef_search = 100", cancellationToken);

        // <=> is pgvector's cosine distance (1 - cosine similarity), the operator the HNSW index is built for.
        var chunks = await db.Database
            .SqlQuery<ChunkHit>($"""
                WITH nearest AS MATERIALIZED (
                    SELECT id, embedding <=> {vector} AS distance
                    FROM chunks
                    WHERE bot_id = {botId} AND embedding IS NOT NULL
                    ORDER BY embedding <=> {vector}
                    LIMIT {limit}
                )
                SELECT c.id AS chunk_id, c.document_id, d.file_name AS document_name, c.chunk_index AS "index",
                       c.page_number, c.heading, c.content, c.char_start, c.char_end,
                       1 - n.distance AS similarity, NULL::double precision AS keyword_score
                FROM nearest n
                JOIN chunks c ON c.id = n.id
                JOIN documents d ON d.id = c.document_id
                ORDER BY n.distance, c.id
                """)
            .ToListAsync(cancellationToken);

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return chunks;
    }

    public async Task<IReadOnlyList<ChunkHit>> KeywordAsync(
        Guid botId, string query, int limit, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        // Questions are sentences, and PostgreSQL's own parsers (plainto_/websearch_to_tsquery) require ALL their
        // words, which almost no chunk has. So the question is normalized the same way as the chunks' search_vector
        // (english: lowercase, stemmed, common words dropped) and its words are OR-ed: 'vacat' | 'day'. The cast
        // to tsquery keeps these words as they are (no second normalization); quote_literal makes every character
        // safe; a question of only common words becomes an empty query, which matches nothing. Ranking (ts_rank)
        // then prefers chunks with more of the words, and heading matches (weight A).
        return await db.Database
            .SqlQuery<ChunkHit>($"""
                WITH q AS (
                    SELECT array_to_string(
                        ARRAY(SELECT quote_literal(lexeme) FROM unnest(to_tsvector('english'::regconfig, {query}))),
                        ' | ')::tsquery AS query
                )
                SELECT c.id AS chunk_id, c.document_id, d.file_name AS document_name, c.chunk_index AS "index",
                       c.page_number, c.heading, c.content, c.char_start, c.char_end,
                       NULL::double precision AS similarity,
                       ts_rank(c.search_vector, q.query)::double precision AS keyword_score
                FROM q
                JOIN chunks c ON c.search_vector @@ q.query
                JOIN documents d ON d.id = c.document_id
                WHERE c.bot_id = {botId}
                ORDER BY keyword_score DESC, c.id
                LIMIT {limit}
                """)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, double>> SimilaritiesAsync(
        Guid botId, IReadOnlyCollection<Guid> chunkIds, float[] queryVector, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(chunkIds);
        var vector = ToVector(queryVector);
        if (chunkIds.Count == 0)
        {
            return new Dictionary<Guid, double>();
        }

        var ids = chunkIds.Distinct().ToArray();
        var rows = await db.Database
            .SqlQuery<ChunkSimilarity>($"""
                SELECT id AS chunk_id, 1 - (embedding <=> {vector}) AS similarity
                FROM chunks
                WHERE id = ANY({ids}) AND bot_id = {botId} AND embedding IS NOT NULL
                """)
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => r.ChunkId, r => r.Similarity);
    }

    private static Vector ToVector(float[] queryVector)
    {
        ArgumentNullException.ThrowIfNull(queryVector);
        if (queryVector.Length != Chunk.EmbeddingDimensions)
        {
            throw new ArgumentException(
                $"The question's vector must have {Chunk.EmbeddingDimensions} numbers, got {queryVector.Length}.",
                nameof(queryVector));
        }

        return new Vector(queryVector);
    }

    private sealed record ChunkSimilarity(Guid ChunkId, double Similarity);
}

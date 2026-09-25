using Citra.Domain.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;
using Pgvector;

namespace Citra.Infrastructure.Persistence.Configurations;

internal sealed class ChunkConfiguration : IEntityTypeConfiguration<Chunk>
{
    /// <summary>Name of the database-only full-text column (search_vector) — used by raw SQL search queries.</summary>
    public const string SearchVectorProperty = "SearchVector";

    public void Configure(EntityTypeBuilder<Chunk> builder)
    {
        // "index" is an SQL keyword — a clearer column name avoids quoting in raw SQL.
        builder.Property(c => c.Index).HasColumnName("chunk_index");

        builder.Property(c => c.Content).IsRequired();
        builder.Property(c => c.Heading).HasMaxLength(Chunk.HeadingMaxLength);

        // Domain uses float[]; the database column is pgvector's vector(1024).
        builder.Property(c => c.Embedding)
            .HasColumnType($"vector({Chunk.EmbeddingDimensions})")
            .HasConversion(
                floats => floats == null ? null : new Vector(floats),
                vector => vector == null ? null : vector.ToArray(),
                new ValueComparer<float[]?>(
                    (a, b) => a == b || (a != null && b != null && a.SequenceEqual(b)),
                    floats => floats == null ? 0 : floats.Length,
                    floats => floats == null ? null : floats.ToArray()));

        // HNSW index for fast nearest-neighbour search. Operator class must match the search operator:
        // vector_cosine_ops ↔ cosine distance (<=>). m / ef_construction are pgvector's recommended defaults.
        // Note for search queries (Phase 12): results are filtered by bot, so use pgvector's iterative scan
        // (SET hnsw.iterative_scan = relaxed_order) to avoid returning too few rows after filtering.
        builder.HasIndex(c => c.Embedding)
            .HasDatabaseName("ix_chunks_embedding_hnsw")
            .HasMethod("hnsw")
            .HasOperators("vector_cosine_ops")
            .HasStorageParameter("m", 16)
            .HasStorageParameter("ef_construction", 64);

        // Full-text (keyword) search, combined with vector search in Phase 12 ("hybrid search").
        // A database-only column: PostgreSQL computes it from heading + content on every insert/update.
        // Heading matches weigh more (A) than body matches (B). 'english' = stemming (refunds → refund)
        // and stop words; exact terms (codes, names, numbers) still match in any language.
        builder.Property<NpgsqlTsVector>(SearchVectorProperty)
            .HasComputedColumnSql(
                "setweight(to_tsvector('english'::regconfig, coalesce(heading, '')), 'A') || " +
                "setweight(to_tsvector('english'::regconfig, content), 'B')",
                stored: true);

        builder.HasIndex(SearchVectorProperty)
            .HasDatabaseName("ix_chunks_search_vector_gin")
            .HasMethod("gin");

        builder.HasOne<Document>()
            .WithMany()
            .HasForeignKey(c => c.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        // One chunk per position in a document (also covers the document_id foreign key).
        builder.HasIndex(c => new { c.DocumentId, c.Index }).IsUnique();

        // Every search is limited to one bot's chunks.
        builder.HasIndex(c => c.BotId);
    }
}

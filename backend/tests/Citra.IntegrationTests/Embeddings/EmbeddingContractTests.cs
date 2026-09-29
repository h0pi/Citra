using Citra.Application.Common.Embeddings;
using Citra.Domain.Documents;

namespace Citra.IntegrationTests.Embeddings;

/// <summary>The promises every embedding implementation must keep (C-132).</summary>
public sealed class EmbeddingContractTests
{
    private const int Dimensions = Chunk.EmbeddingDimensions;

    private static float[] Vector(float value = 0.1f) => Enumerable.Repeat(value, Dimensions).ToArray();

    [Fact]
    public void A_correct_result_passes()
    {
        var result = new DocumentEmbeddings([Vector(), Vector(0.2f)], TotalTokens: 42);

        result.Validate(expectedCount: 2, Dimensions).ShouldBeSameAs(result);
        new QueryEmbedding(Vector(), 7).Validate(Dimensions).TotalTokens.ShouldBe(7);
        DocumentEmbeddings.Empty.Validate(0, Dimensions).Vectors.ShouldBeEmpty();
    }

    [Fact]
    public void A_missing_or_extra_vector_is_rejected()
    {
        // One vector too few would attach every following vector to the wrong chunk.
        Should.Throw<EmbeddingException>(() => new DocumentEmbeddings([Vector()], 5).Validate(2, Dimensions))
            .Message.ShouldContain("1 vectors for 2 texts");
        Should.Throw<EmbeddingException>(() => new DocumentEmbeddings([Vector(), Vector(), Vector()], 5).Validate(2, Dimensions));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(512)]
    [InlineData(1023)]
    [InlineData(1025)]
    [InlineData(1536)]
    public void A_vector_of_the_wrong_size_is_rejected(int length)
    {
        var wrong = new float[length];

        Should.Throw<EmbeddingException>(() => new DocumentEmbeddings([Vector(), wrong], 5).Validate(2, Dimensions))
            .Message.ShouldContain($"vector 1 with {length} numbers instead of {Dimensions}");
        Should.Throw<EmbeddingException>(() => new QueryEmbedding(wrong, 1).Validate(Dimensions));
    }

    [Fact]
    public void A_missing_vector_is_rejected()
    {
        Should.Throw<EmbeddingException>(() => new DocumentEmbeddings([Vector(), null!], 5).Validate(2, Dimensions));
        Should.Throw<EmbeddingException>(() => new QueryEmbedding(null!, 1).Validate(Dimensions));
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void A_vector_with_an_invalid_number_is_rejected(float bad)
    {
        var vector = Vector();
        vector[500] = bad;

        Should.Throw<EmbeddingException>(() => new DocumentEmbeddings([vector], 1).Validate(1, Dimensions))
            .Message.ShouldContain("invalid number");
        Should.Throw<EmbeddingException>(() => new QueryEmbedding(vector, 1).Validate(Dimensions));
    }

    [Fact]
    public void Negative_token_usage_is_rejected()
    {
        Should.Throw<EmbeddingException>(() => new DocumentEmbeddings([Vector()], -1).Validate(1, Dimensions));
        Should.Throw<EmbeddingException>(() => new QueryEmbedding(Vector(), -1).Validate(Dimensions));
    }

    [Fact]
    public void The_interface_size_matches_the_database_column()
    {
        // The Chunk entity and the vector(1024) column are the source of truth that implementations must follow.
        Chunk.EmbeddingDimensions.ShouldBe(1024);
    }
}

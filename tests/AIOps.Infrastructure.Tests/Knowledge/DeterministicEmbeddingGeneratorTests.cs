using AIOps.Infrastructure.Knowledge;

namespace AIOps.Infrastructure.Tests.Knowledge;

public sealed class DeterministicEmbeddingGeneratorTests
{
    [Fact]
    public async Task Same_text_produces_same_embedding()
    {
        var generator = new DeterministicEmbeddingGenerator();

        var first = await generator.GenerateAsync(
            "PostgreSQL incident runbook");

        var second = await generator.GenerateAsync(
            "PostgreSQL incident runbook");

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Embedding_has_expected_dimension()
    {
        var generator = new DeterministicEmbeddingGenerator();

        var embedding = await generator.GenerateAsync(
            "database incident");

        Assert.Equal(64, embedding.Count);
        Assert.Equal(64, generator.Dimensions);
    }

    [Fact]
    public async Task Embedding_is_normalized()
    {
        var generator = new DeterministicEmbeddingGenerator();

        var embedding = await generator.GenerateAsync(
            "network outage");

        var magnitude = Math.Sqrt(
            embedding.Sum(value => value * value));

        Assert.InRange(magnitude, 0.999, 1.001);
    }

    [Fact]
    public async Task Empty_text_returns_zero_vector()
    {
        var generator = new DeterministicEmbeddingGenerator();

        var embedding = await generator.GenerateAsync(
            string.Empty);

        Assert.Equal(64, embedding.Count);

        Assert.All(
            embedding,
            value => Assert.Equal(0f, value));
    }
}
namespace AIOps.Abstractions.Knowledge;

public interface IEmbeddingGenerator
{
    int Dimensions { get; }

    Task<IReadOnlyList<float>> GenerateAsync(
        string text,
        CancellationToken cancellationToken = default);
}
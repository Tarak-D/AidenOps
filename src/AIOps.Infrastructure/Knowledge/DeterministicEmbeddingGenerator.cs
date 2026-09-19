using System.Security.Cryptography;
using System.Text;
using AIOps.Abstractions.Knowledge;

namespace AIOps.Infrastructure.Knowledge;

public sealed class DeterministicEmbeddingGenerator : IEmbeddingGenerator
{
    public const int DefaultDimensions = 64;

    public int Dimensions => DefaultDimensions;

    public Task<IReadOnlyList<float>> GenerateAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var values = new float[DefaultDimensions];

        var normalized = text.Trim().ToLowerInvariant();

        if (normalized.Length == 0)
        {
            return Task.FromResult<IReadOnlyList<float>>(values);
        }

        var tokenBytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));

        for (var i = 0; i < values.Length; i++)
        {
            var byteValue = tokenBytes[i % tokenBytes.Length];

            values[i] =
                (byteValue / 255f) * 2f - 1f;
        }

        var magnitude = MathF.Sqrt(values.Sum(x => x * x));

        if (magnitude > 0)
        {
            for (var i = 0; i < values.Length; i++)
            {
                values[i] /= magnitude;
            }
        }

        return Task.FromResult<IReadOnlyList<float>>(values);
    }
}
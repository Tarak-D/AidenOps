using System.Security.Cryptography;
using System.Text.Json;

namespace AIOps.Orchestration.Evaluation;

public static class EvaluationInputFingerprint
{
    public static string ComputeSha256<T>(T value)
    {
        var bytes = SerializeToUtf8Bytes(value);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    public static byte[] SerializeToUtf8Bytes<T>(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return JsonSerializer.SerializeToUtf8Bytes(value);
    }

    public static bool IsSha256(string? value) =>
        value is { Length: 64 } &&
        value.All(Uri.IsHexDigit);
}

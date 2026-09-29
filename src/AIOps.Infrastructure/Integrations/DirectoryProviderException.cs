namespace AIOps.Infrastructure.Integrations;

/// <summary>A sanitized directory-provider failure safe for tool and audit output.</summary>
public sealed class DirectoryProviderException : Exception
{
    public DirectoryProviderException(string code, string message)
        : base($"{code}: {message}")
    {
        Code = code;
    }

    public string Code { get; }
}

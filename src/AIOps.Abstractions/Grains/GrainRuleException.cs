using Orleans;

namespace AIOps.Abstractions.Grains;

/// <summary>
/// Business-rule violation raised inside a grain. Serializable across the Orleans
/// boundary (deep-copied) so API layers can map deterministic codes to HTTP statuses.
/// </summary>
[GenerateSerializer]
public sealed class GrainRuleException : Exception
{
    [Id(0)] public string Code { get; set; } = "";

    public GrainRuleException() { }

    public GrainRuleException(string code, string message) : base(message)
    {
        Code = code;
    }

    public const string NotFound = "NotFound";
    public const string AlreadyExists = "AlreadyExists";
    public const string InvalidTransition = "InvalidTransition";
    public const string InvalidState = "InvalidState";
}

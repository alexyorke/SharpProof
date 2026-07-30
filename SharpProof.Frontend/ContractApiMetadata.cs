namespace SharpProof.Frontend;

/// <summary>
/// Canonical metadata identities for the compiler-elided SharpProof contract API.
/// Consumers resolve these names through the active compilation and compare symbols.
/// </summary>
internal static class ContractApiMetadata
{
    private const string Namespace = "SharpProof.Attributes.";

    internal const string Attribute = "System.Attribute";
    internal const string ConditionalAttribute =
        "System.Diagnostics.ConditionalAttribute";
    internal const string ConditionalSymbol = "SHARPPROOF_CONTRACTS";
    internal const string AttributesPayloadSha256MetadataKey =
        Namespace + "SHA256";
    internal const string Contract = Namespace + "Contract";
    internal const string RequiresMethodName = "Requires";
    internal const string EnsuresMethodName = "Ensures";
    internal const string AssumeMethodName = "Assume";
    internal const string OldMethodName = "Old";
    internal const string ResultMethodName = "Result";
    internal const string ContractFor = Namespace + "ContractForAttribute";
    internal const string EnforcePure = Namespace + "EnforcePureAttribute";
    internal const string ZeroAllocations = Namespace + "ZeroAllocationsAttribute";
    internal const string AllowedCapabilities = Namespace + "AllowedCapabilitiesAttribute";
    internal const string DoesNotThrow = Namespace + "DoesNotThrowAttribute";
    internal const string AllowedExceptions = Namespace + "AllowedExceptionsAttribute";
    internal const string EffectContract = Namespace + "EffectContractAttribute";
    internal const string NotNull = Namespace + "NotNullAttribute";
    internal const string Positive = Namespace + "PositiveAttribute";
    internal const string InRange = Namespace + "InRangeAttribute";
    internal const string Suppress = Namespace + "SharpProofSuppressAttribute";
    internal const string Trusted = Namespace + "SharpProofTrustedAttribute";
    internal static ImmutableArray<string> ContractMethodCandidateNames { get; } =
    [
        RequiresMethodName,
        EnsuresMethodName,
        AssumeMethodName,
        OldMethodName,
        ResultMethodName
    ];

    internal static bool IsContractMethodCandidateName(string name)
    {
        return ContractMethodCandidateNames.Contains(
            name,
            StringComparer.Ordinal);
    }
}

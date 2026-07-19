using SharpProof.Identity;
using System.Text.Json;

namespace SharpProof.Schema;

internal static class EffectSummarySchemaContract
{
    internal const int CurrentVersion = 5;
}

internal sealed record EffectSummaryPurityContract(
    string? Classification,
    ImmutableArray<string> Categories,
    string? PrimaryCategory,
    bool HasFreshArrayAllocationEvidence,
    string? FreshnessClassification,
    bool HasUnsupportedEffects,
    string? EffectVisibilityClassification);

internal sealed record EffectSummaryExceptionProvenanceContract(
    string? ExceptionType,
    string? SourcePath);

internal sealed record EffectSummaryExceptionEdgeContract(
    string? ExceptionType,
    string? SourcePath,
    ImmutableArray<StructuralMethodIdentity> CallChain,
    StructuralMethodIdentity? CalleeIdentity,
    int? Depth);

internal sealed record EffectSummaryArtifactSourceContract(
    string? Kind,
    string? Framework,
    string? PackageId,
    string? PackageVersion,
    string? PackageAssemblyRelativePath);

internal sealed record EffectSummaryAssemblyContract(
    string? AssemblyName,
    string? AssemblySha256,
    string? ModuleVersionId,
    EffectSummaryArtifactSourceContract? ArtifactSource,
    ImmutableArray<JsonElement> Methods);

internal sealed record EffectSummaryMethodContract(
    string? DisplayName,
    StructuralMethodIdentity? Identity,
    string? CanonicalKey,
    string? AssemblyName,
    string? AssemblySha256,
    string? ModuleVersionId,
    string? MetadataToken,
    string? MethodBodySha256,
    EffectSummaryArtifactSourceContract? ArtifactSource,
    string? Classification,
    ImmutableArray<string> Categories,
    string? PrimaryCategory,
    bool HasFreshArrayAllocationEvidence,
    string? FreshnessClassification,
    bool HasUnsupportedEffects,
    string? EffectVisibilityClassification,
    EffectSummaryPurityContract? PurityClassification,
    ImmutableArray<string> ThrownExceptionTypes,
    ImmutableArray<string> TransitiveThrownExceptionTypes,
    ImmutableArray<EffectSummaryExceptionProvenanceContract> ThrownExceptionProvenance,
    ImmutableArray<EffectSummaryExceptionProvenanceContract> TransitiveThrownExceptionProvenance,
    ImmutableArray<EffectSummaryExceptionEdgeContract> TransitiveThrownExceptionEdges)
{
    internal EffectSummaryPurityContract FlatPurity => new(
        Classification,
        Categories,
        PrimaryCategory,
        HasFreshArrayAllocationEvidence,
        FreshnessClassification,
        HasUnsupportedEffects,
        EffectVisibilityClassification);

    internal bool HasConsistentIdentity =>
        Identity != null &&
        StructuralMethodIdentity.TryParseCanonicalKey(CanonicalKey?.Trim(), out var parsed) &&
        parsed.Equals(Identity) &&
        string.Equals(CanonicalKey?.Trim(), Identity.ToCanonicalKey(), StringComparison.Ordinal);
}

internal static class EffectSummaryContractReader
{
    internal static bool TryReadMethod(JsonElement element, out EffectSummaryMethodContract contract) =>
        TryRead(element, out contract) && contract.HasConsistentIdentity;

    internal static bool TryReadAssembly(JsonElement element, out EffectSummaryAssemblyContract contract) =>
        TryRead(element, out contract);

    private static bool TryRead<T>(JsonElement element, out T contract) where T : class
    {
        contract = null!;
        if (element.ValueKind != JsonValueKind.Object) return false;
        try
        {
            contract = element.Deserialize<T>()!;
            return contract != null;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }
}

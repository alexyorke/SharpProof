using SharpProof.Identity;

namespace SharpProof.Inference;

internal enum InferredPurity {
    Unknown,
    Pure,
    Impure
}

internal enum InferredSummarySource {
    SymbolicBody,
    MetadataContract,
    ManualOverride
}

internal enum InferredFreshness {
    Unknown,
    None,
    FreshOwnedArray,
    FreshOwnedObject,
    FreshImmutable,
    FreshByRefLikeView
}

internal enum InferredEffectVisibility {
    Unknown,
    None,
    InternalOnly,
    CallerVisible
}

internal enum InferredSummaryUnknownReason {
    None,
    MissingBody,
    MissingSummary,
    UnsupportedOperation,
    UnresolvedDispatch,
    RecursiveCycle,
    BudgetExhausted,
    Cancelled,
    UntrustedMetadata
}

[Flags]
internal enum InferredMethodEffects : long {
    None = 0,
    AllocatesObject = 1L << 0,
    AllocatesArray = 1L << 1,
    Boxes = 1L << 2,
    CallsMethod = 1L << 3,
    DynamicDispatch = 1L << 4,
    IndirectCall = 1L << 5,
    ReadsInstanceField = 1L << 6,
    ReadsStaticField = 1L << 7,
    WritesInstanceField = 1L << 8,
    WritesStaticField = 1L << 9,
    WritesIndirectMemory = 1L << 10,
    BlockMemoryWrite = 1L << 11,
    LoadsMethodPointer = 1L << 12,
    Throws = 1L << 13,
    NativeOrInternalCall = 1L << 14,
    MissingBody = 1L << 15,
    Unknown = 1L << 16
}

internal sealed record MethodSummaryCacheKey(
    string Method,
    string AssemblyIdentity,
    string MethodBodySha256,
    string ConfigurationHash,
    string TargetFramework,
    int SchemaVersion) {
    internal static MethodSummaryCacheKey Create(
        StructuralMethodIdentity method,
        string? assemblyIdentity,
        string? methodBodySha256,
        string? configurationHash,
        string? targetFramework,
        int schemaVersion) {
        if (method == null) throw new ArgumentNullException(nameof(method));
        if (schemaVersion <= 0) throw new ArgumentOutOfRangeException(nameof(schemaVersion));

        return new MethodSummaryCacheKey(
            method.ToCanonicalKey(),
            Normalize(assemblyIdentity),
            Normalize(methodBodySha256),
            Normalize(configurationHash),
            Normalize(targetFramework),
            schemaVersion);
    }

    private static string Normalize(string? value) => value?.Trim() ?? string.Empty;
}

internal sealed class InferredMethodSummary {
    internal const int SchemaVersion = 1;

    internal InferredMethodSummary(
        StructuralMethodIdentity identity,
        InferredSummarySource source,
        InferredPurity purity,
        InferredMethodEffects effects,
        InferredFreshness freshness,
        InferredEffectVisibility effectVisibility,
        IEnumerable<string>? thrownExceptionTypes = null,
        IEnumerable<string>? blockingCallChain = null,
        InferredSummaryUnknownReason unknownReason = InferredSummaryUnknownReason.None) {
        Identity = identity ?? throw new ArgumentNullException(nameof(identity));
        if (purity == InferredPurity.Unknown && unknownReason == InferredSummaryUnknownReason.None)
            throw new ArgumentException("Unknown purity requires an explicit reason.", nameof(unknownReason));
        if (purity != InferredPurity.Unknown && unknownReason != InferredSummaryUnknownReason.None)
            throw new ArgumentException("Known purity cannot carry an unknown reason.", nameof(unknownReason));

        Source = source;
        Purity = purity;
        Effects = effects;
        Freshness = freshness;
        EffectVisibility = effectVisibility;
        ThrownExceptionTypes = Normalize(thrownExceptionTypes);
        BlockingCallChain = Normalize(blockingCallChain);
        UnknownReason = unknownReason;
    }

    internal StructuralMethodIdentity Identity { get; }

    internal InferredSummarySource Source { get; }

    internal InferredPurity Purity { get; }

    internal InferredMethodEffects Effects { get; }

    internal InferredFreshness Freshness { get; }

    internal InferredEffectVisibility EffectVisibility { get; }

    internal ImmutableArray<string> ThrownExceptionTypes { get; }

    internal ImmutableArray<string> BlockingCallChain { get; }

    internal InferredSummaryUnknownReason UnknownReason { get; }

    private static ImmutableArray<string> Normalize(IEnumerable<string>? values) {
        return values == null
            ? ImmutableArray<string>.Empty
            : values.Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value.Trim())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static value => value, StringComparer.Ordinal)
                .ToImmutableArray();
    }
}

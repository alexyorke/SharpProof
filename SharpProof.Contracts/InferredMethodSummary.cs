using System.Collections.Immutable;
using SharpProof.Identity;

namespace SharpProof.Inference;

internal enum InferredPurity
{
    Unknown,
    Pure,
    Impure
}

internal enum InferredSummarySource
{
    SymbolicBody,
    EffectSummary,
    MetadataContract,
    ManualOverride
}

internal enum InferredFreshness
{
    Unknown,
    None,
    FreshOwnedArray,
    FreshOwnedObject,
    FreshImmutable,
    FreshByRefLikeView
}

internal enum InferredEffectVisibility
{
    Unknown,
    None,
    InternalOnly,
    CallerVisible
}

internal enum InferredSummaryUnknownReason
{
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
internal enum InferredMethodEffects : long
{
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
    int SchemaVersion)
{
    internal static MethodSummaryCacheKey Create(
        StructuralMethodIdentity method,
        string? assemblyIdentity,
        string? methodBodySha256,
        string? configurationHash,
        string? targetFramework,
        int schemaVersion)
    {
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

internal sealed class InferredMethodSummary
{
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
        InferredSummaryUnknownReason unknownReason = InferredSummaryUnknownReason.None)
    {
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

    internal bool HasSameSemanticsAs(InferredMethodSummary other)
    {
        if (other == null) throw new ArgumentNullException(nameof(other));

        return Identity.Equals(other.Identity) &&
               Purity == other.Purity &&
               Effects == other.Effects &&
               Freshness == other.Freshness &&
               EffectVisibility == other.EffectVisibility &&
               UnknownReason == other.UnknownReason &&
               ThrownExceptionTypes.SequenceEqual(other.ThrownExceptionTypes, StringComparer.Ordinal) &&
               BlockingCallChain.SequenceEqual(other.BlockingCallChain, StringComparer.Ordinal);
    }

    internal static InferredMethodSummary FromEffectSummary(
        StructuralMethodIdentity identity,
        string? classification,
        IEnumerable<string> effects,
        string? freshness,
        string? effectVisibility,
        IEnumerable<string>? thrownExceptionTypes,
        IEnumerable<string>? blockingCallChain,
        IEnumerable<string>? categories)
    {
        if (effects == null) throw new ArgumentNullException(nameof(effects));

        var effectValues = effects.ToImmutableArray();
        var categoryValues = categories?.ToImmutableArray() ?? ImmutableArray<string>.Empty;
        var purity = classification switch
        {
            "pure" => InferredPurity.Pure,
            "impure" => InferredPurity.Impure,
            _ => InferredPurity.Unknown
        };
        return new InferredMethodSummary(
            identity,
            InferredSummarySource.EffectSummary,
            purity,
            GetEffects(effectValues),
            GetFreshness(freshness),
            GetEffectVisibility(effectVisibility),
            thrownExceptionTypes,
            blockingCallChain,
            purity == InferredPurity.Unknown
                ? GetUnknownReason(effectValues, categoryValues)
                : InferredSummaryUnknownReason.None);
    }

    private static InferredMethodEffects GetEffects(ImmutableArray<string> effects)
    {
        var result = InferredMethodEffects.None;
        foreach (var effect in effects)
            result |= effect switch
            {
                "allocates_object" => InferredMethodEffects.AllocatesObject,
                "allocates_array" => InferredMethodEffects.AllocatesArray,
                "allocates_box" => InferredMethodEffects.Boxes,
                "calls_method" => InferredMethodEffects.CallsMethod,
                "virtual_call" => InferredMethodEffects.DynamicDispatch,
                "indirect_call" => InferredMethodEffects.IndirectCall,
                "reads_instance_field" => InferredMethodEffects.ReadsInstanceField,
                "reads_static_field" => InferredMethodEffects.ReadsStaticField,
                "writes_instance_field" => InferredMethodEffects.WritesInstanceField,
                "writes_static_field" => InferredMethodEffects.WritesStaticField,
                "writes_indirect_memory" => InferredMethodEffects.WritesIndirectMemory,
                "block_memory_write" => InferredMethodEffects.BlockMemoryWrite,
                "loads_method_pointer" => InferredMethodEffects.LoadsMethodPointer,
                "throws" => InferredMethodEffects.Throws,
                "native_or_internal_call" or "pinvoke" => InferredMethodEffects.NativeOrInternalCall,
                "no_il_body" or "abstract" => InferredMethodEffects.MissingBody,
                _ when effect.StartsWith("unknown_opcode_at_", StringComparison.Ordinal) =>
                    InferredMethodEffects.Unknown,
                _ => InferredMethodEffects.None
            };

        return result;
    }

    private static InferredFreshness GetFreshness(string? freshness)
    {
        return freshness switch
        {
            "none" => InferredFreshness.None,
            "fresh_owned_array_write" or "direct_fresh_array_allocation" =>
                InferredFreshness.FreshOwnedArray,
            "fresh_owned_object_write" => InferredFreshness.FreshOwnedObject,
            _ => InferredFreshness.Unknown
        };
    }

    private static InferredEffectVisibility GetEffectVisibility(string? visibility)
    {
        return visibility switch
        {
            "none" => InferredEffectVisibility.None,
            "internal_only" => InferredEffectVisibility.InternalOnly,
            "caller_visible" => InferredEffectVisibility.CallerVisible,
            _ => InferredEffectVisibility.Unknown
        };
    }

    private static InferredSummaryUnknownReason GetUnknownReason(
        ImmutableArray<string> effects,
        ImmutableArray<string> categories)
    {
        if (categories.Contains("recursive_cycle", StringComparer.Ordinal))
            return InferredSummaryUnknownReason.RecursiveCycle;
        if (categories.Contains("dynamic_dispatch", StringComparer.Ordinal) ||
            effects.Contains("virtual_call", StringComparer.Ordinal) ||
            effects.Contains("indirect_call", StringComparer.Ordinal))
            return InferredSummaryUnknownReason.UnresolvedDispatch;
        if (effects.Contains("no_il_body", StringComparer.Ordinal) ||
            effects.Contains("abstract", StringComparer.Ordinal))
            return InferredSummaryUnknownReason.MissingBody;
        if (effects.Any(static effect => effect.StartsWith("unknown_opcode_at_", StringComparison.Ordinal)))
            return InferredSummaryUnknownReason.UnsupportedOperation;

        return InferredSummaryUnknownReason.MissingSummary;
    }

    private static ImmutableArray<string> Normalize(IEnumerable<string>? values)
    {
        return values == null
            ? ImmutableArray<string>.Empty
            : values.Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value.Trim())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static value => value, StringComparer.Ordinal)
                .ToImmutableArray();
    }
}

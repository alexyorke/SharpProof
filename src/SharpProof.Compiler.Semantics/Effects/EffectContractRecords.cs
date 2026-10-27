#nullable enable

namespace SharpProof.Effects;

internal readonly record struct EffectContractInvalidAttribute(
    AttributeData Attribute,
    string Reason
)
{
    public AttributeData Attribute { get; init; } = Attribute ?? throw new ArgumentNullException(nameof(Attribute));
    public string Reason { get; init; } = Reason ?? throw new ArgumentNullException(nameof(Reason));
}

internal readonly record struct EffectContractResolution(
    EffectContractResolutionKind Kind,
    EffectSummary Summary,
    ImmutableArray<EffectContractInvalidAttribute> InvalidAttributes = default
)
{
    public EffectSummary Summary { get; init; } = Summary ?? throw new ArgumentNullException(nameof(Summary));
    public ImmutableArray<EffectContractInvalidAttribute> InvalidAttributes { get; init; } = InvalidAttributes.IsDefault ? [] : InvalidAttributes;
}

internal readonly struct EffectEvidenceRule(
    Type type, long mask, bool flags, ImmutableArray<long> values)
{
    internal Type Type { get; } = type ?? throw new ArgumentNullException(nameof(type));
    internal long Mask { get; } = mask;
    internal bool Flags { get; } = flags;
    internal ImmutableArray<long> Values { get; } = values.IsDefault ? throw new ArgumentException("Array must be initialized.", nameof(values)) : values;
}

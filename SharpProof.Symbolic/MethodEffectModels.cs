using SharpProof.Attributes;
namespace SharpProof.Symbolic;
public enum SharpProofVerdict { Proven, Disproven, Unknown }
public enum MethodEffectOrigin {
    Ambient, Receiver, Argument, Captured, Static, FreshOwned, Allocation, Synchronization,
    Native, Nondeterminism, Exception, Call, Unknown
}
public enum MethodExceptionSource { ExplicitThrow, RuntimeHazard, Callee, Metadata, Contract, Unknown }
public sealed record MethodExceptionFact(
    string ExceptionType,
    SharpProofVerdict Escape,
    MethodExceptionSource Source,
    string Operation,
    string Symbol,
    int SpanStart,
    int SpanLength,
    bool IsTransitive,
    string Reason,
    string Kind = "") {
    public static MethodExceptionFact Boundary(
        string exceptionType,
        MethodExceptionSource source,
        string reason,
        SharpProofVerdict escape = SharpProofVerdict.Proven) => new(
        exceptionType, escape, source, string.Empty, string.Empty, 0, 0, true, reason);
}
public sealed record MethodEffectSite(
    SharpProofEffect Effect,
    SharpProofCapability Capabilities,
    string Operation,
    string Symbol,
    int SpanStart,
    int SpanLength,
    bool IsTransitive,
    string Reason,
    MethodEffectOrigin Origin = MethodEffectOrigin.Unknown,
    string? ExceptionType = null,
    string? TransitiveSource = null,
    SharpProofVerdict EscapeStatus = SharpProofVerdict.Unknown,
    SharpProofVerdict ProofStatus = SharpProofVerdict.Proven);
public sealed record MethodEffects(
    SharpProofEffect Effects,
    SharpProofCapability Capabilities,
    ImmutableArray<MethodExceptionFact> ExceptionFacts,
    ImmutableArray<MethodEffectSite> Sites,
    ImmutableArray<SharpProofUnknownReason> UnknownReasons) {
    private const SharpProofEffect ImpureEffects =
        SharpProofEffect.ReadsAmbientState | SharpProofEffect.WritesAmbientState |
        SharpProofEffect.ReadsStaticState | SharpProofEffect.WritesReceiverState |
        SharpProofEffect.WritesArgumentState | SharpProofEffect.WritesCapturedState |
        SharpProofEffect.WritesStaticState | SharpProofEffect.Synchronizes |
        SharpProofEffect.UsesNondeterminism | SharpProofEffect.UsesNativeCode |
        SharpProofEffect.UsesReflection;
    public SharpProofVerdict Purity => GetVerdict(ImpureEffects, Capabilities != SharpProofCapability.None);
    public SharpProofVerdict AllocationFree => GetVerdict(SharpProofEffect.Allocates, false);
    public ImmutableArray<string> ThrownExceptions => [.. ExceptionFacts
        .Where(static fact => fact.Escape == SharpProofVerdict.Proven)
        .Select(static fact => fact.ExceptionType).Distinct(StringComparer.Ordinal)];
    public SharpProofVerdict DoesNotThrow {
        get {
            if (ExceptionFacts.Any(static fact => fact.Escape == SharpProofVerdict.Proven)) return SharpProofVerdict.Disproven;
            if (ExceptionFacts.Any(static fact => fact.Escape == SharpProofVerdict.Unknown)) return SharpProofVerdict.Unknown;
            return (Effects & SharpProofEffect.Unknown) != 0 || !UnknownReasons.IsDefaultOrEmpty
                ? SharpProofVerdict.Unknown : SharpProofVerdict.Proven;
        }
    }
    private SharpProofVerdict GetVerdict(SharpProofEffect prohibited, bool hasProhibitedCapability) {
        if ((Effects & prohibited) != 0 || hasProhibitedCapability) return SharpProofVerdict.Disproven;
        return (Effects & SharpProofEffect.Unknown) != 0 || !UnknownReasons.IsDefaultOrEmpty
            ? SharpProofVerdict.Unknown : SharpProofVerdict.Proven;
    }
}

namespace SharpProof.Symbolic.Ir;

internal enum SymbolicLoweringSupport
{
    Exact,
    Approximate,
    Unsupported
}

internal sealed record SymbolicLoweringProvenance(
    string Stage,
    TextSpan SourceSpan,
    string Detail);

internal sealed class SymbolicLoweringResult<T>(
    SymbolicLoweringSupport support,
    T? value,
    ImmutableArray<SymbolicLoweringProvenance> provenance,
    SymbolicUnknownReason unknownReason)
    where T : class
{
    internal SymbolicLoweringSupport Support { get; } = support;
    internal T? Value { get; } = value;
    internal ImmutableArray<SymbolicLoweringProvenance> Provenance { get; } = provenance;
    internal SymbolicUnknownReason UnknownReason { get; } = unknownReason;
    internal bool IsExact => Support == SymbolicLoweringSupport.Exact;
    internal bool IsApproximate => Support == SymbolicLoweringSupport.Approximate;
    internal bool IsUnsupported => Support == SymbolicLoweringSupport.Unsupported;

    internal static SymbolicLoweringResult<T> Exact(
        T value,
        SymbolicLoweringProvenance provenance)
    {
        return new SymbolicLoweringResult<T>(
            SymbolicLoweringSupport.Exact,
            value ?? throw new ArgumentNullException(nameof(value)),
            ImmutableArray.Create(provenance),
            SymbolicUnknownReason.None);
    }


    internal static SymbolicLoweringResult<T> Unsupported(SymbolicLoweringProvenance provenance)
    {
        return new SymbolicLoweringResult<T>(
            SymbolicLoweringSupport.Unsupported,
            null,
            ImmutableArray.Create(provenance),
            SymbolicUnknownReason.UnsupportedIrEncoding);
    }
}

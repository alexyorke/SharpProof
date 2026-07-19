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

internal sealed class SymbolicLoweringResult<T>
    where T : class
{
    private SymbolicLoweringResult(
        SymbolicLoweringSupport support,
        T? value,
        ImmutableArray<SymbolicLoweringProvenance> provenance,
        SymbolicUnknownReason unknownReason)
    {
        Support = support;
        Value = value;
        Provenance = provenance;
        UnknownReason = unknownReason;
    }

    internal SymbolicLoweringSupport Support { get; }
    internal T? Value { get; }
    internal ImmutableArray<SymbolicLoweringProvenance> Provenance { get; }
    internal SymbolicUnknownReason UnknownReason { get; }
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

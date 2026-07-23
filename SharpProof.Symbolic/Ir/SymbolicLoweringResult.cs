namespace SharpProof.Symbolic.Ir;
internal sealed record SymbolicLoweringProvenance(string Stage, TextSpan SourceSpan, string Detail);
internal sealed class SymbolicLoweringResult<T>(
    bool isExact,
    T? value,
    ImmutableArray<SymbolicLoweringProvenance> provenance,
    SymbolicUnknownReason unknownReason)
    where T : class {
    internal T? Value { get; } = value;
    internal ImmutableArray<SymbolicLoweringProvenance> Provenance { get; } = provenance;
    internal SymbolicUnknownReason UnknownReason { get; } = unknownReason;
    internal bool IsExact { get; } = isExact;
    internal static SymbolicLoweringResult<T> Exact(T value, SymbolicLoweringProvenance provenance) => new(
            true,
            value ?? throw new ArgumentNullException(nameof(value)),
            [provenance],
            SymbolicUnknownReason.None);
    internal static SymbolicLoweringResult<T> Unsupported(SymbolicLoweringProvenance provenance) => new(
            false,
            null,
            [provenance],
            SymbolicUnknownReason.UnsupportedIrEncoding);
}

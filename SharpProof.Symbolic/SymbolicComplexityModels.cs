using System.Text.Json.Serialization;

namespace SharpProof.Symbolic;

internal enum SymbolicComplexityKind {
    Constant,
    Linear,
    Product,
    Quadratic,
    Max,
    Unknown,
    RecursiveUnknown
}

internal enum SymbolicComplexityUnknownReason {
    None,
    UnsupportedTarget,
    NoContainingMethodLikeBody,
    UnsupportedLoopShape,
    UnsupportedWhileLoop,
    UnknownCallee,
    ExternalCallee,
    DynamicDispatch,
    RecursiveCycle,
    UnsupportedOperation,
    CancellationRequested,
    Unknown
}

internal sealed record SymbolicComplexityInfo(
    string Text,
    SymbolicComplexityKind Kind,
    bool IsConservative,
    bool IsUnknown,
    bool IsRecursiveUnknown);

internal sealed record SymbolicComplexityDriverInfo(
    string Kind,
    string Description,
    int SourceSpanStart,
    int SourceSpanLength,
    int SourceLine,
    int SourceColumn);

internal sealed record SymbolicComplexityCalleeInfo(
    string MethodDisplayName,
    string ComplexityText,
    SymbolicComplexityKind Kind,
    bool IsConservative,
    SymbolicComplexityUnknownReason UnknownReason) {
    public SymbolicUnknownReasonInfo UnknownReasonInfo { get; } =
        SymbolicUnknownReasonTaxonomy.ForComplexity(UnknownReason);
}

internal sealed record SymbolicComplexityResult(
    string FilePath,
    string MethodName,
    string MethodDisplayName,
    string DeclarationKind,
    int SpanStart,
    int SpanEnd,
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn,
    [property: JsonPropertyOrder(-5)] SymbolicComplexityInfo Complexity,
    [property: JsonPropertyOrder(-4)] IReadOnlyList<SymbolicComplexityDriverInfo> Drivers,
    [property: JsonPropertyOrder(-3)] IReadOnlyList<SymbolicComplexityUnknownReason> UnknownReasons,
    [property: JsonPropertyOrder(-1)] IReadOnlyList<SymbolicComplexityCalleeInfo> CalleeSummaries)
    : SymbolicMethodResult(
        FilePath,
        MethodName,
        MethodDisplayName,
        DeclarationKind,
        SpanStart,
        SpanEnd,
        StartLine,
        StartColumn,
        EndLine,
        EndColumn) {
    [JsonPropertyOrder(-2)]
    public IReadOnlyList<SymbolicUnknownReasonInfo> UnknownReasonDetails { get; } =
        UnknownReasons
        .Select(SymbolicUnknownReasonTaxonomy.ForComplexity)
        .ToArray();
}

namespace SharpProof.Symbolic;

internal enum SymbolicCapabilityUnknownReason {
    None,
    UnsupportedTarget,
    NoContainingMethodLikeBody,
    DynamicDispatch,
    MetadataClassificationUnavailable,
    UnsupportedOperation,
    RecursiveSourceCycle,
    ExternalSourceBoundary,
    CancellationRequested,
    Unknown
}

internal sealed record SymbolicCapabilitySite(
    SymbolicCapability Capabilities,
    string CapabilityText,
    string SiteKind,
    string OperationKind,
    string OperationText,
    string SymbolDisplayName,
    bool IsTransitive,
    bool IsUnknown,
    SymbolicCapabilityUnknownReason UnknownReason,
    int SourceSpanStart,
    int SourceSpanLength,
    int SourceLine,
    int SourceColumn) {
    public SymbolicUnknownReasonInfo UnknownReasonInfo { get; } =
        SymbolicUnknownReasonTaxonomy.ForCapability(UnknownReason);
}

internal sealed record SymbolicCapabilityResult(
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
    SymbolicCapability Capabilities,
    string CapabilityText,
    IReadOnlyList<SymbolicCapabilitySite> Sites,
    IReadOnlyList<SymbolicCapabilityUnknownReason> UnknownReasons)
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
    public IReadOnlyList<SymbolicUnknownReasonInfo> UnknownReasonDetails { get; } =
        UnknownReasons
        .Select(SymbolicUnknownReasonTaxonomy.ForCapability)
        .ToArray();

    public bool HasUnknowns => UnknownReasons.Count != 0 || Sites.Any(static site => site.IsUnknown);

    public bool IsConservative => HasUnknowns;
}

namespace SharpProof.Symbolic;

internal enum SymbolicCapabilityUnknownReason
{
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

internal sealed class SymbolicCapabilitySite(
    SymbolicCapability capabilities,
    string capabilityText,
    string siteKind,
    string operationKind,
    string operationText,
    string symbolDisplayName,
    bool isTransitive,
    bool isUnknown,
    SymbolicCapabilityUnknownReason unknownReason,
    int sourceSpanStart,
    int sourceSpanLength,
    int sourceLine,
    int sourceColumn)
{
    public SymbolicCapability Capabilities { get; } = capabilities;
    public string CapabilityText { get; } = capabilityText ?? string.Empty;
    public string SiteKind { get; } = siteKind ?? string.Empty;
    public string OperationKind { get; } = operationKind ?? string.Empty;
    public string OperationText { get; } = operationText ?? string.Empty;
    public string SymbolDisplayName { get; } = symbolDisplayName ?? string.Empty;
    public bool IsTransitive { get; } = isTransitive;
    public bool IsUnknown { get; } = isUnknown;
    public SymbolicCapabilityUnknownReason UnknownReason { get; } = unknownReason;
    public SymbolicUnknownReasonInfo UnknownReasonInfo { get; } =
        SymbolicUnknownReasonTaxonomy.ForCapability(unknownReason);
    public int SourceSpanStart { get; } = sourceSpanStart;
    public int SourceSpanLength { get; } = sourceSpanLength;
    public int SourceLine { get; } = sourceLine;
    public int SourceColumn { get; } = sourceColumn;
}

internal sealed class SymbolicCapabilityResult : SymbolicMethodResult
{
    public SymbolicCapabilityResult(
        string filePath,
        string methodName,
        string methodDisplayName,
        string declarationKind,
        int spanStart,
        int spanEnd,
        int startLine,
        int startColumn,
        int endLine,
        int endColumn,
        SymbolicCapability capabilities,
        string capabilityText,
        IReadOnlyList<SymbolicCapabilitySite>? sites = null,
        IReadOnlyList<SymbolicCapabilityUnknownReason>? unknownReasons = null)
        : base(
            filePath,
            methodName,
            methodDisplayName,
            declarationKind,
            spanStart,
            spanEnd,
            startLine,
            startColumn,
            endLine,
            endColumn)
    {
        Capabilities = capabilities;
        CapabilityText = capabilityText ?? string.Empty;
        Sites = sites ?? Array.Empty<SymbolicCapabilitySite>();
        UnknownReasons = unknownReasons ?? Array.Empty<SymbolicCapabilityUnknownReason>();
        UnknownReasonDetails = UnknownReasons
            .Select(SymbolicUnknownReasonTaxonomy.ForCapability)
            .ToArray();
    }

    public SymbolicCapability Capabilities { get; }

    public string CapabilityText { get; }

    public IReadOnlyList<SymbolicCapabilitySite> Sites { get; }

    public IReadOnlyList<SymbolicCapabilityUnknownReason> UnknownReasons { get; }

    public IReadOnlyList<SymbolicUnknownReasonInfo> UnknownReasonDetails { get; }

    public bool HasUnknowns => UnknownReasons.Count != 0 || Sites.Any(static site => site.IsUnknown);

    public bool IsConservative => HasUnknowns;

}

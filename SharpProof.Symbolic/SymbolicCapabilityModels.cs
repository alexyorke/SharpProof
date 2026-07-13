namespace SharpProof.Symbolic;

[Flags]
public enum SymbolicCapability
{
    None = 0,
    IO = 1 << 0,
    FileRead = 1 << 1,
    FileWrite = 1 << 2,
    Network = 1 << 3,
    Console = 1 << 4,
    Process = 1 << 5,
    Environment = 1 << 6,
    Registry = 1 << 7,
    Clock = 1 << 8,
    Randomness = 1 << 9,
    Reflection = 1 << 10,
    Synchronization = 1 << 11,
    NativeInterop = 1 << 12
}

public enum SymbolicCapabilityUnknownReason
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

public sealed class SymbolicCapabilitySite
{
    public SymbolicCapabilitySite(
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
        Capabilities = capabilities;
        CapabilityText = capabilityText ?? string.Empty;
        SiteKind = siteKind ?? string.Empty;
        OperationKind = operationKind ?? string.Empty;
        OperationText = operationText ?? string.Empty;
        SymbolDisplayName = symbolDisplayName ?? string.Empty;
        IsTransitive = isTransitive;
        IsUnknown = isUnknown;
        UnknownReason = unknownReason;
        UnknownReasonInfo = SymbolicUnknownReasonTaxonomy.ForCapability(unknownReason);
        SourceSpanStart = sourceSpanStart;
        SourceSpanLength = sourceSpanLength;
        SourceLine = sourceLine;
        SourceColumn = sourceColumn;
    }

    public SymbolicCapability Capabilities { get; }

    public string CapabilityText { get; }

    public string SiteKind { get; }

    public string OperationKind { get; }

    public string OperationText { get; }

    public string SymbolDisplayName { get; }

    public bool IsTransitive { get; }

    public bool IsUnknown { get; }

    public SymbolicCapabilityUnknownReason UnknownReason { get; }

    public SymbolicUnknownReasonInfo UnknownReasonInfo { get; }

    public int SourceSpanStart { get; }

    public int SourceSpanLength { get; }

    public int SourceLine { get; }

    public int SourceColumn { get; }
}

public sealed class SymbolicCapabilityResult : SymbolicMethodResult
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

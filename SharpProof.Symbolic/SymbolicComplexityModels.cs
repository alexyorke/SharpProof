namespace SharpProof.Symbolic;

internal enum SymbolicComplexityKind
{
    Constant,
    Linear,
    Product,
    Quadratic,
    Max,
    Unknown,
    RecursiveUnknown
}

internal enum SymbolicComplexityUnknownReason
{
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

internal sealed class SymbolicComplexityInfo(
    string text,
    SymbolicComplexityKind kind,
    bool isConservative,
    bool isUnknown,
    bool isRecursiveUnknown)
{
    public string Text { get; } = text ?? string.Empty;
    public SymbolicComplexityKind Kind { get; } = kind;
    public bool IsConservative { get; } = isConservative;
    public bool IsUnknown { get; } = isUnknown;
    public bool IsRecursiveUnknown { get; } = isRecursiveUnknown;
}

internal sealed class SymbolicComplexityDriverInfo(
    string kind,
    string description,
    int sourceSpanStart,
    int sourceSpanLength,
    int sourceLine,
    int sourceColumn)
{
    public string Kind { get; } = kind ?? string.Empty;
    public string Description { get; } = description ?? string.Empty;
    public int SourceSpanStart { get; } = sourceSpanStart;
    public int SourceSpanLength { get; } = sourceSpanLength;
    public int SourceLine { get; } = sourceLine;
    public int SourceColumn { get; } = sourceColumn;
}

internal sealed class SymbolicComplexityCalleeInfo(
    string methodDisplayName,
    string complexityText,
    SymbolicComplexityKind kind,
    bool isConservative,
    SymbolicComplexityUnknownReason unknownReason)
{
    public string MethodDisplayName { get; } = methodDisplayName ?? string.Empty;
    public string ComplexityText { get; } = complexityText ?? string.Empty;
    public SymbolicComplexityKind Kind { get; } = kind;
    public bool IsConservative { get; } = isConservative;
    public SymbolicComplexityUnknownReason UnknownReason { get; } = unknownReason;
    public SymbolicUnknownReasonInfo UnknownReasonInfo { get; } =
        SymbolicUnknownReasonTaxonomy.ForComplexity(unknownReason);
}

internal sealed class SymbolicComplexityResult : SymbolicMethodResult
{
    public SymbolicComplexityResult(
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
        SymbolicComplexityInfo complexity,
        IReadOnlyList<SymbolicComplexityDriverInfo>? drivers = null,
        IReadOnlyList<SymbolicComplexityUnknownReason>? unknownReasons = null,
        IReadOnlyList<SymbolicComplexityCalleeInfo>? calleeSummaries = null)
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
        Complexity = complexity ?? throw new ArgumentNullException(nameof(complexity));
        Drivers = drivers ?? Array.Empty<SymbolicComplexityDriverInfo>();
        UnknownReasons = unknownReasons ?? Array.Empty<SymbolicComplexityUnknownReason>();
        UnknownReasonDetails = UnknownReasons
            .Select(SymbolicUnknownReasonTaxonomy.ForComplexity)
            .ToArray();
        CalleeSummaries = calleeSummaries ?? Array.Empty<SymbolicComplexityCalleeInfo>();
    }

    public SymbolicComplexityInfo Complexity { get; }

    public IReadOnlyList<SymbolicComplexityDriverInfo> Drivers { get; }

    public IReadOnlyList<SymbolicComplexityUnknownReason> UnknownReasons { get; }

    public IReadOnlyList<SymbolicUnknownReasonInfo> UnknownReasonDetails { get; }

    public IReadOnlyList<SymbolicComplexityCalleeInfo> CalleeSummaries { get; }

}

namespace SharpProof.Symbolic;

public enum SymbolicComplexityKind
{
    Constant,
    Linear,
    Product,
    Quadratic,
    Max,
    Unknown,
    RecursiveUnknown
}

public enum SymbolicComplexityUnknownReason
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

public sealed class SymbolicComplexityInfo
{
    public SymbolicComplexityInfo(
        string text,
        SymbolicComplexityKind kind,
        bool isConservative,
        bool isUnknown,
        bool isRecursiveUnknown)
    {
        Text = text ?? string.Empty;
        Kind = kind;
        IsConservative = isConservative;
        IsUnknown = isUnknown;
        IsRecursiveUnknown = isRecursiveUnknown;
    }

    public string Text { get; }

    public SymbolicComplexityKind Kind { get; }

    public bool IsConservative { get; }

    public bool IsUnknown { get; }

    public bool IsRecursiveUnknown { get; }
}

public sealed class SymbolicComplexityDriverInfo
{
    public SymbolicComplexityDriverInfo(
        string kind,
        string description,
        int sourceSpanStart,
        int sourceSpanLength,
        int sourceLine,
        int sourceColumn)
    {
        Kind = kind ?? string.Empty;
        Description = description ?? string.Empty;
        SourceSpanStart = sourceSpanStart;
        SourceSpanLength = sourceSpanLength;
        SourceLine = sourceLine;
        SourceColumn = sourceColumn;
    }

    public string Kind { get; }

    public string Description { get; }

    public int SourceSpanStart { get; }

    public int SourceSpanLength { get; }

    public int SourceLine { get; }

    public int SourceColumn { get; }
}

public sealed class SymbolicComplexityCalleeInfo
{
    public SymbolicComplexityCalleeInfo(
        string methodDisplayName,
        string complexityText,
        SymbolicComplexityKind kind,
        bool isConservative,
        SymbolicComplexityUnknownReason unknownReason)
    {
        MethodDisplayName = methodDisplayName ?? string.Empty;
        ComplexityText = complexityText ?? string.Empty;
        Kind = kind;
        IsConservative = isConservative;
        UnknownReason = unknownReason;
        UnknownReasonInfo = SymbolicUnknownReasonTaxonomy.ForComplexity(unknownReason);
    }

    public string MethodDisplayName { get; }

    public string ComplexityText { get; }

    public SymbolicComplexityKind Kind { get; }

    public bool IsConservative { get; }

    public SymbolicComplexityUnknownReason UnknownReason { get; }

    public SymbolicUnknownReasonInfo UnknownReasonInfo { get; }
}

public sealed class SymbolicComplexityResult
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
    {
        Target = new SymbolicMethodTarget(
            filePath,
            methodName,
            methodDisplayName,
            declarationKind,
            spanStart,
            spanEnd,
            startLine,
            startColumn,
            endLine,
            endColumn);
        Complexity = complexity ?? throw new ArgumentNullException(nameof(complexity));
        Drivers = drivers ?? Array.Empty<SymbolicComplexityDriverInfo>();
        UnknownReasons = unknownReasons ?? Array.Empty<SymbolicComplexityUnknownReason>();
        UnknownReasonDetails = UnknownReasons
            .Select(SymbolicUnknownReasonTaxonomy.ForComplexity)
            .ToArray();
        CalleeSummaries = calleeSummaries ?? Array.Empty<SymbolicComplexityCalleeInfo>();
    }

    internal SymbolicMethodTarget Target { get; }

    public string FilePath => Target.FilePath;

    public string MethodName => Target.MethodName;

    public string MethodDisplayName => Target.MethodDisplayName;

    public string DeclarationKind => Target.DeclarationKind;

    public int SpanStart => Target.SpanStart;

    public int SpanEnd => Target.SpanEnd;

    public int StartLine => Target.StartLine;

    public int StartColumn => Target.StartColumn;

    public int EndLine => Target.EndLine;

    public int EndColumn => Target.EndColumn;

    public SymbolicComplexityInfo Complexity { get; }

    public IReadOnlyList<SymbolicComplexityDriverInfo> Drivers { get; }

    public IReadOnlyList<SymbolicComplexityUnknownReason> UnknownReasons { get; }

    public IReadOnlyList<SymbolicUnknownReasonInfo> UnknownReasonDetails { get; }

    public IReadOnlyList<SymbolicComplexityCalleeInfo> CalleeSummaries { get; }

    public SymbolicCompactComplexityResult ToCompactResult()
    {
        return SymbolicCompactComplexityResult.FromResult(this);
    }
}

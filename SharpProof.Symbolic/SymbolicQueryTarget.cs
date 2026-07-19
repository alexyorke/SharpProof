namespace SharpProof.Symbolic;

internal enum SymbolicQueryScopeKind
{
    Point,
    Line,
    Span,
    File
}

internal sealed class SymbolicQueryScope(
    SymbolicQueryScopeKind kind,
    string filePath,
    int? line = null,
    int? column = null,
    int? position = null,
    int? spanStart = null,
    int? spanEnd = null,
    int? lineCount = null,
    int? startLine = null,
    int? startColumn = null,
    int? endLine = null,
    int? endColumn = null)
{
    public SymbolicQueryScopeKind Kind { get; } = kind;
    public string FilePath { get; } = filePath ?? string.Empty;
    public int? Line { get; } = line;
    public int? Column { get; } = column;
    public int? Position { get; } = position;
    public int? SpanStart { get; } = spanStart;
    public int? SpanEnd { get; } = spanEnd;
    public int? LineCount { get; } = lineCount;
    internal int? StartLine { get; } = startLine;
    internal int? StartColumn { get; } = startColumn;
    internal int? EndLine { get; } = endLine;
    internal int? EndColumn { get; } = endColumn;
}

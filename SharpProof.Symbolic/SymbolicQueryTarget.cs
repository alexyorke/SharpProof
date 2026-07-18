namespace SharpProof.Symbolic;

internal enum SymbolicQueryScopeKind
{
    Point,
    Line,
    Span,
    File
}

internal sealed class SymbolicQueryScope
{
    internal SymbolicQueryScope(
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
        Kind = kind;
        FilePath = filePath ?? string.Empty;
        Line = line;
        Column = column;
        Position = position;
        SpanStart = spanStart;
        SpanEnd = spanEnd;
        LineCount = lineCount;
        StartLine = startLine;
        StartColumn = startColumn;
        EndLine = endLine;
        EndColumn = endColumn;
    }

    public SymbolicQueryScopeKind Kind { get; }

    public string FilePath { get; }

    public int? Line { get; }

    public int? Column { get; }

    public int? Position { get; }

    public int? SpanStart { get; }

    public int? SpanEnd { get; }

    public int? LineCount { get; }

    internal int? StartLine { get; }

    internal int? StartColumn { get; }

    internal int? EndLine { get; }

    internal int? EndColumn { get; }
}

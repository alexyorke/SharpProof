namespace SharpProof.Symbolic;

public abstract class SymbolicMethodResult
{
    protected SymbolicMethodResult(
        string? filePath,
        string? methodName,
        string? methodDisplayName,
        string? declarationKind,
        int spanStart,
        int spanEnd,
        int startLine,
        int startColumn,
        int endLine,
        int endColumn)
    {
        FilePath = filePath ?? string.Empty;
        MethodName = methodName ?? string.Empty;
        MethodDisplayName = methodDisplayName ?? string.Empty;
        DeclarationKind = declarationKind ?? string.Empty;
        SpanStart = spanStart;
        SpanEnd = spanEnd;
        StartLine = startLine;
        StartColumn = startColumn;
        EndLine = endLine;
        EndColumn = endColumn;
    }

    public string FilePath { get; }

    public string MethodName { get; }

    public string MethodDisplayName { get; }

    public string DeclarationKind { get; }

    public int SpanStart { get; }

    public int SpanEnd { get; }

    public int StartLine { get; }

    public int StartColumn { get; }

    public int EndLine { get; }

    public int EndColumn { get; }
}

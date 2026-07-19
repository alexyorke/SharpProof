namespace SharpProof.Symbolic;

internal abstract class SymbolicMethodResult(
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
    public string FilePath { get; } = filePath ?? string.Empty;
    public string MethodName { get; } = methodName ?? string.Empty;
    public string MethodDisplayName { get; } = methodDisplayName ?? string.Empty;
    public string DeclarationKind { get; } = declarationKind ?? string.Empty;
    public int SpanStart { get; } = spanStart;
    public int SpanEnd { get; } = spanEnd;
    public int StartLine { get; } = startLine;
    public int StartColumn { get; } = startColumn;
    public int EndLine { get; } = endLine;
    public int EndColumn { get; } = endColumn;
}

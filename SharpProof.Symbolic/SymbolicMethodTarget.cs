namespace SharpProof.Symbolic;

internal sealed class SymbolicMethodTarget
{
    internal SymbolicMethodTarget(
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

    internal string FilePath { get; }

    internal string MethodName { get; }

    internal string MethodDisplayName { get; }

    internal string DeclarationKind { get; }

    internal int SpanStart { get; }

    internal int SpanEnd { get; }

    internal int StartLine { get; }

    internal int StartColumn { get; }

    internal int EndLine { get; }

    internal int EndColumn { get; }
}

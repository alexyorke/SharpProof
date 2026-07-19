namespace SharpProof.Symbolic;

internal abstract record SymbolicMethodResult(
    string FilePath,
    string MethodName,
    string MethodDisplayName,
    string DeclarationKind,
    int SpanStart,
    int SpanEnd,
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn);

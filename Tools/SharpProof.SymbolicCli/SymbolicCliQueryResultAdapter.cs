using SharpProof.Symbolic;

internal static class SymbolicCliQueryResultAdapter
{
    internal static object ToLegacyResult(SymbolicQueryResult result)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));

        return result.Scope.Kind switch
        {
            SymbolicQueryScopeKind.File => new SymbolicFileQueryResult(
                result.FilePath,
                result.LineCount ?? 0,
                result.CreateLineResults(),
                result.SmtDiagnostics),
            SymbolicQueryScopeKind.Line => new SymbolicLineQueryResult(
                result.FilePath,
                result.Line ?? 0,
                result.ProgramPoints,
                result.SmtDiagnostics),
            SymbolicQueryScopeKind.Span => new SymbolicSpanQueryResult(
                result.FilePath,
                result.SpanStart ?? 0,
                result.SpanEnd ?? 0,
                result.Scope.StartLine ?? 1,
                result.Scope.StartColumn ?? 1,
                result.Scope.EndLine ?? 1,
                result.Scope.EndColumn ?? 1,
                result.ProgramPoints,
                result.SmtDiagnostics),
            SymbolicQueryScopeKind.Point when result.ProgramPoints.Count != 0 => result.ProgramPoints[0],
            _ => throw new InvalidOperationException("Symbolic query result has no value for its scope.")
        };
    }
}

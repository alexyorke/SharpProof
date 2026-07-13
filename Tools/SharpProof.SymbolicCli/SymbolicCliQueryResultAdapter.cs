using SharpProof.Symbolic;

internal static class SymbolicCliQueryResultAdapter
{
    internal static object ToLegacyResult(SymbolicQueryResult result)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));

        return result.Scope.Kind switch
        {
            SymbolicQueryScopeKind.File => RequireScopeResult(result.FileResult, result),
            SymbolicQueryScopeKind.Line => RequireScopeResult(result.LineResult, result),
            SymbolicQueryScopeKind.Span => RequireScopeResult(result.SpanResult, result),
            SymbolicQueryScopeKind.Point => RequireScopeResult(result.PointResult, result),
            _ => throw new InvalidOperationException("Unexpected symbolic query scope.")
        };
    }

    private static T RequireScopeResult<T>(T? value, SymbolicQueryResult result)
        where T : class
    {
        return value ?? throw new InvalidOperationException(
            $"Symbolic query result has no {result.Scope.Kind} scope projection.");
    }
}

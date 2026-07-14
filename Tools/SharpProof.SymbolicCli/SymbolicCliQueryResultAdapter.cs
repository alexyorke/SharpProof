using SharpProof.Symbolic;

internal static class SymbolicCliQueryResultAdapter
{
    internal static object ToLegacyResult(SymbolicQueryResult result)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));

        return result.SelectScope<object>(
            static file => file,
            static line => line,
            static span => span,
            static point => point);
    }
}

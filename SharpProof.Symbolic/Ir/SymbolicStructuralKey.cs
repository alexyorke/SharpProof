namespace SharpProof.Symbolic.Ir;

internal static class SymbolicStructuralKey
{
    internal static string ForTerm(SymbolicTerm term)
    {
        return SymbolicState.CreateProofTermKey(term);
    }

    internal static string ForFact(SymbolicFact fact)
    {
        return SymbolicState.CreateProofFactKey(fact);
    }

    internal static string ForCondition(SymbolicCondition condition)
    {
        return SymbolicState.CreateProofConditionKey(condition);
    }

    internal static string ForState(SymbolicState state)
    {
        return state.NormalizedProofKey;
    }
}

namespace SharpProof.Worker;

internal static class EffectGoalBuilder
{
    internal static IrTerm NoReachableSites(IrFactory factory, IEnumerable<IrTerm> sites)
    {
        IrTerm goal = factory.Boolean(true);
        foreach (var reach in sites)
        { goal = factory.Binary(IrBinaryOperator.AndAlso, goal, factory.Unary(IrUnaryOperator.Not, reach)); }
        return goal;
    }
}

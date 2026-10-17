namespace SharpProof.Frontend;

internal sealed partial class RoslynTotalExpressionLowerer
{
    private GuardedExpression? IntegerComparisonClause(IInvocationOperation invocation, TotalParameterState state, int depth)
    {
        invocation = RepositoryParameterGuard.NotNull(invocation, nameof(invocation));
        var primitive = CSharpOperationSemantics.IsIntegerCompareTo(invocation.TargetMethod);
        if (!primitive && !CSharpOperationSemantics.IsDefaultIntegerComparison(invocation))
        { return null; }
        var left = LowerClause(primitive ? invocation.Instance! : invocation.Arguments.Single(argument => argument is null ? throw new System.ArgumentNullException(nameof(argument)) : (argument.Parameter!.Ordinal == 0)).Value, state, depth + 1);
        var right = LowerClause(invocation.Arguments.Single(argument => argument is null ? throw new System.ArgumentNullException(nameof(argument)) : (argument.Parameter!.Ordinal == (primitive ? 0 : 1))).Value, state, depth + 1);
        var classification = First(left.Classification, right.Classification);
        return classification.IsExact
            ? new(CSharpOperationSemantics.IntegerComparison(_factory, left.Value, right.Value),
                And(left.SafeCondition, right.SafeCondition), classification)
            : Failed(invocation, classification.Abstention);
    }

    private TotalBodyValue? IntegerComparisonBody(IInvocationOperation invocation, IrBlockId block, int depth)
    {
        invocation = RepositoryParameterGuard.NotNull(invocation, nameof(invocation));
        RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
        var primitive = CSharpOperationSemantics.IsIntegerCompareTo(invocation.TargetMethod);
        if (!primitive && !CSharpOperationSemantics.IsDefaultIntegerComparison(invocation))
        { return null; }
        // Evaluate Default first, retaining cache initialization effects and faults.
        var receiver = LowerBodyValue(invocation.Instance!, block, depth + 1);
        if (!receiver.Classification.IsExact)
        { return Approximate(invocation, receiver.Continuation, receiver.Classification.Abstention); }
        block = receiver.Continuation;
        var values = new IrTerm[primitive ? 1 : 2];
        foreach (var argument in invocation.Arguments)
        {
            var lowered = LowerBodyValue(argument.Value, block, depth + 1);
            if (!lowered.Classification.IsExact)
            { return Approximate(invocation, lowered.Continuation, lowered.Classification.Abstention); }
            values[argument.Parameter!.Ordinal] = lowered.Value;
            block = lowered.Continuation;
        }
        return new(CSharpOperationSemantics.IntegerComparison(_factory,
            primitive ? receiver.Value : values[0], primitive ? values[0] : values[1]),
            block, FrontendSubsetClassification.Exact);
    }
}

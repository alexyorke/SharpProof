namespace SharpProof.Frontend;

internal sealed partial class RoslynTotalExpressionLowerer
{
    private GuardedExpression? ReferenceEqualsClause(IInvocationOperation invocation, TotalParameterState state, int depth)
    {
        invocation = RepositoryParameterGuard.NotNull(invocation, nameof(invocation));
        if (!CSharpOperationSemantics.IsObjectReferenceEquals(invocation.TargetMethod))
        { return null; }
        var values = new GuardedExpression[2];
        foreach (var argument in invocation.Arguments)
        { values[argument.Parameter!.Ordinal] = LowerClause(argument.Value, state, depth + 1); }
        var classification = First(values[0].Classification, values[1].Classification);
        return classification.IsExact ? new(_factory.Binary(IrBinaryOperator.Equal, values[0].Value, values[1].Value),
            And(values[0].SafeCondition, values[1].SafeCondition), classification) : Failed(invocation, classification.Abstention);
    }

    private TotalBodyValue? ReferenceEqualsBody(IInvocationOperation invocation, IrBlockId block, int depth)
    {
        invocation = RepositoryParameterGuard.NotNull(invocation, nameof(invocation));
        RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
        if (!CSharpOperationSemantics.IsObjectReferenceEquals(invocation.TargetMethod))
        { return null; }
        var values = new IrTerm[2];
        // Roslyn preserves source argument order; ordinals retain named bindings.
        foreach (var argument in invocation.Arguments)
        {
            var lowered = LowerBodyValue(argument.Value, block, depth + 1);
            if (!lowered.Classification.IsExact)
            { return Approximate(invocation, lowered.Continuation, lowered.Classification.Abstention); }
            values[argument.Parameter!.Ordinal] = lowered.Value;
            block = lowered.Continuation;
        }
        return new(_factory.Binary(IrBinaryOperator.Equal, values[0], values[1]), block, FrontendSubsetClassification.Exact);
    }

    private GuardedExpression? IntegerComparisonClause(IInvocationOperation invocation, TotalParameterState state, int depth)
    {
        invocation = RepositoryParameterGuard.NotNull(invocation, nameof(invocation));
        var primitive = CSharpOperationSemantics.IsIntegerCompareTo(invocation.TargetMethod);
        if (!primitive && !_context.IsDefaultIntegerComparison(invocation))
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
        if (!primitive && !_context.IsDefaultIntegerComparison(invocation))
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

namespace SharpProof.Symbolic.Ir;

internal readonly record struct SymbolicDeconstructionTarget(IOperation Operation, ISymbol? Symbol) {
    internal bool IsDiscard => Symbol == null;
}

internal readonly record struct SymbolicDeconstructionElement(
    SymbolicDeconstructionTarget Target,
    IOperation Value);

internal static class SymbolicDeconstructionPlan {
    internal static bool TryCollectTargets(
        IOperation operation,
        Func<IOperation, ISymbol?> resolveTarget,
        out ImmutableArray<SymbolicDeconstructionTarget> targets) {
        var builder = ImmutableArray.CreateBuilder<SymbolicDeconstructionTarget>();
        var supported = TryWalk(operation, null, resolveTarget, builder, null);
        targets = supported ? builder.ToImmutable() : ImmutableArray<SymbolicDeconstructionTarget>.Empty;
        return supported;
    }

    internal static bool TryPair(
        IOperation target,
        IOperation value,
        Func<IOperation, ISymbol?> resolveTarget,
        out ImmutableArray<SymbolicDeconstructionElement> elements) {
        var builder = ImmutableArray.CreateBuilder<SymbolicDeconstructionElement>();
        var supported = TryWalk(target, value, resolveTarget, null, builder);
        elements = supported ? builder.ToImmutable() : ImmutableArray<SymbolicDeconstructionElement>.Empty;
        return supported;
    }

    private static bool TryWalk(
        IOperation target,
        IOperation? value,
        Func<IOperation, ISymbol?> resolveTarget,
        ImmutableArray<SymbolicDeconstructionTarget>.Builder? targets,
        ImmutableArray<SymbolicDeconstructionElement>.Builder? elements) {
        target = Unwrap(target);
        value = value == null ? null : Unwrap(value);
        if (target is ITupleOperation targetTuple) {
            if (value != null &&
                (value is not ITupleOperation valueTuple || valueTuple.Elements.Length != targetTuple.Elements.Length))
                return false;

            for (var index = 0; index < targetTuple.Elements.Length; index++)
                if (!TryWalk(
                        targetTuple.Elements[index],
                        value is ITupleOperation tuple ? tuple.Elements[index] : null,
                        resolveTarget,
                        targets,
                        elements))
                    return false;
            return true;
        }

        var symbol = target is IDiscardOperation ? null : resolveTarget(target)?.OriginalDefinition;
        if (symbol == null && target is not IDiscardOperation) return false;
        var item = new SymbolicDeconstructionTarget(target, symbol);
        targets?.Add(item);
        if (value != null) elements?.Add(new SymbolicDeconstructionElement(item, value));
        return true;
    }

    private static IOperation Unwrap(IOperation operation) {
        while (operation is IConversionOperation { IsImplicit: true } conversion ||
               operation is IDeclarationExpressionOperation)
            operation = operation is IConversionOperation current
                ? current.Operand
                : ((IDeclarationExpressionOperation)operation).Expression;
        return operation;
    }
}

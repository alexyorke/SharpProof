namespace SharpProof.Symbolic.Ir;
internal readonly record struct SymbolicDeconstructionTarget(ISymbol? Symbol);
internal static class SymbolicDeconstructionPlan {
    internal static bool TryCollectTargets(
        IOperation operation,
        Func<IOperation, ISymbol?> resolveTarget,
        out ImmutableArray<SymbolicDeconstructionTarget> targets) {
        var builder = ImmutableArray.CreateBuilder<SymbolicDeconstructionTarget>();
        var supported = TryWalk(operation, resolveTarget, builder);
        targets = supported ? builder.ToImmutable() : [];
        return supported;
    }
    private static bool TryWalk(
        IOperation target,
        Func<IOperation, ISymbol?> resolveTarget,
        ImmutableArray<SymbolicDeconstructionTarget>.Builder targets) {
        target = Unwrap(target);
        if (target is ITupleOperation targetTuple) {
            foreach (var element in targetTuple.Elements)
                if (!TryWalk(element, resolveTarget, targets))
                    return false;
            return true;
        }
        var symbol = target is IDiscardOperation ? null : resolveTarget(target)?.OriginalDefinition;
        if (symbol == null && target is not IDiscardOperation) return false;
        targets.Add(new SymbolicDeconstructionTarget(symbol));
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

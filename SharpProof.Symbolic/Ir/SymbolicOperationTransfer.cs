namespace SharpProof.Symbolic.Ir;
internal static class SymbolicOperationTransfer {
    internal static bool ApplyAssignment(
        ref SymbolicState state,
        ISymbol targetSymbol,
        SyntaxNode valueSyntax,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        Func<ISymbol, int>? getTargetVersion = null,
        Func<ISymbol, int>? getValueVersion = null,
        string provenance = "operation-lowering.assignment",
        string? bindingProvenance = null,
        string? evidenceKey = null,
        SymbolicAssignmentPostconditionProfile postconditionProfile = SymbolicAssignmentPostconditionProfile.Analyzer,
        SymbolicTerm? preInvalidationTargetValue = null) {
        var targetContext = new SymbolicLoweringContext(semanticModel, cancellationToken, getTargetVersion);
        var valueContext = new SymbolicLoweringContext(
            semanticModel,
            cancellationToken,
            getValueVersion,
            symbolSubstitutions: preInvalidationTargetValue == null
                ? null
                : new Dictionary<ISymbol, SymbolicTerm>(1,
                    SymbolEqualityComparer.Default) { [targetSymbol.OriginalDefinition] = preInvalidationTargetValue });
        var lowering = SymbolicOperationLowerer.LowerSimpleAssignment(
            targetSymbol,
            valueSyntax,
            targetContext,
            valueContext,
            provenance,
            bindingProvenance,
            evidenceKey,
            postconditionProfile);
        return ApplyLowering(ref state, lowering);
    }
    internal static bool ApplyComputedUpdate(
        ref SymbolicState state,
        ISymbol targetSymbol,
        SymbolicTerm sourceTerm,
        SyntaxNode source,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string provenance) {
        var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
        var lowering = SymbolicOperationLowerer.LowerComputedUpdate(targetSymbol, sourceTerm, source, context, provenance);
        return ApplyLowering(ref state, lowering);
    }
    internal static bool ApplyCoalesceAssignment(
        ref SymbolicState state,
        ISymbol targetSymbol,
        Microsoft.CodeAnalysis.CSharp.Syntax.ExpressionSyntax rightExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string provenance) {
        var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
        return ApplyLowering(
            ref state,
            SymbolicOperationLowerer.LowerCoalesceAssignment(targetSymbol, rightExpression, context, provenance));
    }
    internal static bool ApplyBindings(
        ref SymbolicState state,
        System.Collections.Immutable.ImmutableArray<SymbolicAssignmentBinding> bindings,
        SyntaxNode source,
        string provenance) {
        var operation = new SymbolicStateDelta(
            bindings,
            new SymbolicOperationOrigin(source.Span, provenance));
        return SymbolicOperationTransferKernel.TryApply(ref state, operation);
    }
    internal static bool ApplyLowering(
        ref SymbolicState state,
        SymbolicLoweringResult<SymbolicStateDelta> lowering) => lowering is { IsExact: true, Value: { } delta }
            && SymbolicOperationTransferKernel.TryApply(ref state, delta);
}

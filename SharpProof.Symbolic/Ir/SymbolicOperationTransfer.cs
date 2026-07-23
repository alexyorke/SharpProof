namespace SharpProof.Symbolic.Ir;
internal static class SymbolicOperationTransfer {
    internal static SymbolicOperationTransitionResult ApplyAssignment(
        SymbolicState state,
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
        return ApplyLowering(state, lowering);
    }
    internal static SymbolicOperationTransitionResult ApplyComputedUpdate(
        SymbolicState state,
        ISymbol targetSymbol,
        SymbolicTerm sourceTerm,
        SyntaxNode source,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string provenance) {
        var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
        var lowering = SymbolicOperationLowerer.LowerComputedUpdate(targetSymbol, sourceTerm, source, context, provenance);
        return ApplyLowering(state, lowering);
    }
    internal static SymbolicOperationTransitionResult ApplyCoalesceAssignment(
        SymbolicState state,
        ISymbol targetSymbol,
        Microsoft.CodeAnalysis.CSharp.Syntax.ExpressionSyntax rightExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string provenance) {
        var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
        return ApplyLowering(
            state,
            SymbolicOperationLowerer.LowerCoalesceAssignment(targetSymbol, rightExpression, context, provenance));
    }
    internal static SymbolicOperationTransitionResult ApplyBindings(
        SymbolicState state,
        System.Collections.Immutable.ImmutableArray<SymbolicAssignmentBinding> bindings,
        SyntaxNode source,
        string provenance) {
        var operation = new SymbolicAssignmentOperation(
            bindings,
            [],
            new SymbolicOperationOrigin(source.Span, provenance));
        return SymbolicOperationTransferKernel.Apply(state, operation);
    }
    internal static SymbolicOperationTransitionResult ApplyLowering(
        SymbolicState state,
        SymbolicLoweringResult<SymbolicOperationDescriptor> lowering) => lowering is { IsExact: true, Value: { } operation }
            ? SymbolicOperationTransferKernel.Apply(state, operation)
            : SymbolicOperationTransitionResult.Unsupported(state);
}

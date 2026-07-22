namespace SharpProof.Symbolic;
internal static class SymbolicBranchCompletionStateTransfer {
    internal static IReadOnlyList<ISymbol> GetSwitchExpressionConditionSymbols(
        SwitchExpressionSyntax switchExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        var symbols = new List<ISymbol>();
        AddReferencedSymbols(switchExpression.GoverningExpression, semanticModel, cancellationToken, symbols);
        foreach (var arm in switchExpression.Arms) {
            AddReferencedSymbols(arm.Pattern, semanticModel, cancellationToken, symbols);
            AddDeclaredPatternSymbols(arm.Pattern, semanticModel, cancellationToken, symbols);
            if (arm.WhenClause != null)
                AddReferencedSymbols(arm.WhenClause.Condition, semanticModel, cancellationToken, symbols);
        }
        return symbols;
    }
    internal static void AddReferencedSymbols(
        SyntaxNode root,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        ICollection<ISymbol> symbols) {
        foreach (var symbol in SymbolMutationFacts.GetReferencedLocalAndParameterSymbols(root, semanticModel, cancellationToken))
            AddSymbolIfAbsent(symbols, symbol);
    }
    internal static void AddDeclaredPatternSymbols(
        SyntaxNode root,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        ICollection<ISymbol> symbols) {
        foreach (var node in root.DescendantNodesAndSelf(candidate => !CSharpSyntaxFacts.IsNestedLocalCallableBoundary(candidate)))
            if (node is SingleVariableDesignationSyntax singleVariableDesignation &&
                singleVariableDesignation.Identifier.ValueText != "_" &&
                semanticModel.GetDeclaredSymbol(singleVariableDesignation, cancellationToken) is ILocalSymbol
                    localSymbol)
                AddSymbolIfAbsent(symbols, localSymbol.OriginalDefinition);
    }
    internal static void AddMemberNotNullWhenTargetSymbols(
        SyntaxNode root,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        ICollection<ISymbol> symbols) {
        foreach (var invocation in root
                     .DescendantNodesAndSelf(candidate => !CSharpSyntaxFacts.IsNestedLocalCallableBoundary(candidate))
                     .OfType<InvocationExpressionSyntax>()) {
            if (semanticModel.GetOperation(invocation, cancellationToken) is not IInvocationOperation
                    invocationOperation ||
                invocationOperation.TargetMethod.IsStatic ||
                !SymbolicFrameworkPostconditionLowerer.IsCurrentInstanceInvocation(invocation))
                continue;
            foreach (var target in NullableFlowFacts.GetMemberNotNullWhenTargets(invocationOperation.TargetMethod))
                if (NullableFlowFacts.TryResolveInstanceMemberTarget(
                        invocationOperation.TargetMethod.ContainingType,
                        target,
                        out var member))
                    AddSymbolIfAbsent(symbols, member);
        }
    }
    private static void AddSymbolIfAbsent(ICollection<ISymbol> symbols, ISymbol symbol) {
        if (symbols.All(existing => !SymbolEqualityComparer.Default.Equals(existing, symbol))) symbols.Add(symbol);
    }
}

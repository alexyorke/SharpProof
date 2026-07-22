namespace SharpProof.Symbolic;

internal static class SymbolicBranchCompletionStateTransfer {
    internal static IReadOnlyList<ISymbol> GetLocalsDeclaredInside(
        StatementSyntax statement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        var symbols = new List<ISymbol>();
        foreach (var node in statement.DescendantNodesAndSelf(candidate => !CSharpSyntaxFacts.IsNestedLocalCallableBoundary(candidate))) {
            var symbol = node switch {
                VariableDeclaratorSyntax declarator => semanticModel.GetDeclaredSymbol(declarator, cancellationToken),
                SingleVariableDesignationSyntax designation => semanticModel.GetDeclaredSymbol(designation, cancellationToken),
                ForEachStatementSyntax forEachStatement => semanticModel.GetDeclaredSymbol(forEachStatement, cancellationToken),
                CatchDeclarationSyntax catchDeclaration => semanticModel.GetDeclaredSymbol(catchDeclaration, cancellationToken),
                _ => null
            };

            if (symbol is ILocalSymbol &&
                symbols.All(existing => !SymbolEqualityComparer.Default.Equals(existing, symbol.OriginalDefinition)))
                symbols.Add(symbol.OriginalDefinition);
        }
        return symbols;
    }
    internal static IReadOnlyList<ISymbol> GetSwitchConditionSymbols(
        SwitchStatementSyntax switchStatement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        var symbols = new List<ISymbol>();
        AddReferencedSymbols(switchStatement.Expression, semanticModel, cancellationToken, symbols);
        foreach (var section in switchStatement.Sections)
            foreach (var label in section.Labels)
                switch (label) {
                    case CaseSwitchLabelSyntax caseLabel:
                        AddReferencedSymbols(caseLabel.Value, semanticModel, cancellationToken, symbols);
                        break;
                    case CasePatternSwitchLabelSyntax patternLabel:
                        AddReferencedSymbols(patternLabel.Pattern, semanticModel, cancellationToken, symbols);
                        AddDeclaredPatternSymbols(patternLabel.Pattern, semanticModel, cancellationToken, symbols);
                        if (patternLabel.WhenClause != null)
                            AddReferencedSymbols(patternLabel.WhenClause.Condition, semanticModel, cancellationToken, symbols);

                        break;
                }
        return symbols;
    }
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

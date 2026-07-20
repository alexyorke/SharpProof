namespace SharpProof.Analyzer;

internal static class RequiresEntryStateBuilder {
    internal static SymbolicState CreateForUse(
        SyntaxNode useNode,
        SemanticModel semanticModel,
        SharpProofAttributeIdentityPolicy attributePolicy,
        CancellationToken cancellationToken) {
        var methodNode = useNode.AncestorsAndSelf().FirstOrDefault(IsMethodLikeDeclaration);
        if (methodNode == null ||
            semanticModel.GetDeclaredSymbol(methodNode, cancellationToken) is not IMethodSymbol methodSymbol)
            return new SymbolicState();

        return Create(methodSymbol, methodNode, semanticModel, attributePolicy, cancellationToken);
    }

    internal static SymbolicState Create(
        IMethodSymbol methodSymbol,
        SyntaxNode methodNode,
        SemanticModel semanticModel,
        SharpProofAttributeIdentityPolicy attributePolicy,
        CancellationToken cancellationToken) {
        var state = new SymbolicState();
        var contracts = RequiresContractHelpers.ValidContracts(
            methodSymbol,
            attributePolicy,
            cancellationToken);
        if (contracts.IsDefaultOrEmpty) return state;

        var position = RequiresContractHelpers.GetMethodEntrySpeculativePosition(methodNode);
        foreach (var contract in contracts) {
            cancellationToken.ThrowIfCancellationRequested();
            if (!RequiresContractHelpers.TryCreateCondition(
                    semanticModel,
                    position,
                    contract.Condition,
                    cancellationToken,
                    out var conditionExpression,
                    out _,
                    out var condition,
                    out _) ||
                RequiresContractHelpers.ContainsResultReference(conditionExpression))
                continue;

            state = state.AddPathCondition(condition);
        }

        return state;
    }

    internal static SymbolicState? CreateStable(
        SyntaxNode methodNode,
        SemanticModel semanticModel,
        SharpProofAttributeIdentityPolicy attributePolicy,
        CancellationToken cancellationToken) {
        if (!TryGetAnalysisContext(
                methodNode,
                semanticModel,
                attributePolicy,
                cancellationToken,
                out var methodSymbol,
                out var contracts,
                out var position))
            return null;

        var conditions = new List<SymbolicCondition>();
        foreach (var contract in contracts) {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ContractConditionHelpers.TryParse(
                    contract.Condition,
                    out var conditionStatement,
                    out var conditionExpression) ||
                RequiresContractHelpers.ContainsResultReference(conditionExpression) ||
                !ContractConditionHelpers.TryCreateSpeculativeModel(
                    semanticModel,
                    position,
                    conditionStatement,
                    out var conditionSemanticModel) ||
                !IsStableParameterCondition(
                    conditionExpression,
                    conditionSemanticModel,
                    methodSymbol,
                    methodNode,
                    semanticModel,
                    cancellationToken))
                continue;

            var lowering = SymbolicSemanticPipeline.LowerCondition(
                conditionExpression,
                new SymbolicLoweringContext(conditionSemanticModel, cancellationToken));
            if (lowering is { IsExact: true, Value: { } condition }) conditions.Add(condition);
        }

        return conditions.Count == 0
            ? null
            : new SymbolicState(pathConditions: conditions).Normalize();
    }

    private static bool TryGetAnalysisContext(
        SyntaxNode methodNode,
        SemanticModel semanticModel,
        SharpProofAttributeIdentityPolicy attributePolicy,
        CancellationToken cancellationToken,
        out IMethodSymbol methodSymbol,
        out ImmutableArray<RequiresContract> contracts,
        out int speculativePosition) {
        if (semanticModel.GetDeclaredSymbol(methodNode, cancellationToken) is not IMethodSymbol declaredMethod) {
            methodSymbol = null!;
            contracts = ImmutableArray<RequiresContract>.Empty;
            speculativePosition = default;
            return false;
        }

        methodSymbol = declaredMethod;
        contracts = RequiresContractHelpers.ValidContracts(
            methodSymbol,
            attributePolicy,
            cancellationToken);
        speculativePosition = RequiresContractHelpers.GetMethodEntrySpeculativePosition(methodNode);
        return !contracts.IsDefaultOrEmpty;
    }

    private static bool IsStableParameterCondition(
        ExpressionSyntax conditionExpression,
        SemanticModel conditionSemanticModel,
        IMethodSymbol methodSymbol,
        SyntaxNode methodNode,
        SemanticModel methodSemanticModel,
        CancellationToken cancellationToken) {
        if (conditionExpression.DescendantNodesAndSelf().Any(static node =>
                node is InvocationExpressionSyntax or ElementAccessExpressionSyntax))
            return false;

        foreach (var memberAccess in conditionExpression.DescendantNodesAndSelf().OfType<MemberAccessExpressionSyntax>()) {
            var member = conditionSemanticModel.GetSymbolInfo(memberAccess, cancellationToken).Symbol;
            if (!IsStableConditionMember(member, memberAccess, conditionSemanticModel, cancellationToken))
                return false;
        }

        var referencedSymbols = SymbolMutationFacts.GetReferencedLocalAndParameterSymbols(
            conditionExpression,
            conditionSemanticModel,
            cancellationToken);
        if (referencedSymbols.Any(symbol =>
                symbol is not IParameterSymbol parameter ||
                !SymbolEq.AreEqual(parameter.ContainingSymbol, methodSymbol)))
            return false;

        var mutations = SymbolicMutationInventory.Create(methodNode, methodSemanticModel, cancellationToken);
        return !referencedSymbols.Any(mutations.MutatesSymbol);
    }

    private static bool IsStableConditionMember(
        ISymbol? member,
        MemberAccessExpressionSyntax memberAccess,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        if (member is not IPropertySymbol property) return false;

        var receiverType = semanticModel.GetTypeInfo(memberAccess.Expression, cancellationToken).Type;
        if (property.Name == "Length" &&
            (receiverType is IArrayTypeSymbol || receiverType?.SpecialType == SpecialType.System_String))
            return true;

        return property.Name == "HasValue" &&
               receiverType is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T };
    }

    private static bool IsMethodLikeDeclaration(SyntaxNode node) {
        return node is MethodDeclarationSyntax or
            AccessorDeclarationSyntax or
            ConstructorDeclarationSyntax or
            ConversionOperatorDeclarationSyntax or
            OperatorDeclarationSyntax or
            LocalFunctionStatementSyntax;
    }
}

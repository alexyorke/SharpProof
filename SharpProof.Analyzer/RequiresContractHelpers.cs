namespace SharpProof.Analyzer;
internal static class RequiresContractHelpers {
    internal const string AttributeTypeName = "RequiresAttribute";
    internal const string AttributeDisplayName = "[Requires]";
    internal static ImmutableArray<RequiresContract> CollectContracts(
        IMethodSymbol methodSymbol,
        SharpProofAttributeIdentityPolicy attributePolicy,
        CancellationToken cancellationToken) => ContractConditionHelpers.Collect(
            methodSymbol,
            attributePolicy,
            AttributeTypeName,
            static contract => new RequiresContract(
                contract.Condition,
                contract.Location,
                contract.Argument,
                contract.InvalidReason,
                contract.SourceMethod),
            cancellationToken);
    internal static ImmutableArray<RequiresContract> ValidContracts(
        IMethodSymbol methodSymbol,
        SharpProofAttributeIdentityPolicy attributePolicy,
        CancellationToken cancellationToken)
            => [.. CollectContracts(methodSymbol, attributePolicy, cancellationToken).Where(static contract
            => contract.InvalidReason == null)];
    internal static bool ContainsResultReference(ExpressionSyntax conditionExpression) => conditionExpression
            .DescendantNodesAndSelf()
            .OfType<IdentifierNameSyntax>()
            .Any(static identifier => string.Equals(identifier.Identifier.ValueText, "result", StringComparison.Ordinal));
    internal static string CombineAsImplication(ImmutableArray<RequiresContract> requiresContracts, string consequent) {
        if (requiresContracts.IsDefaultOrEmpty) return consequent;
        var validConditions = requiresContracts
            .Where(static contract => contract.InvalidReason == null)
            .Select(static contract => contract.Condition)
            .Where(static condition => !string.IsNullOrWhiteSpace(condition))
            .ToArray();
        if (validConditions.Length == 0) return consequent;
        var antecedent = string.Join(" && ", validConditions.Select(static condition => "(" + condition + ")"));
        return "!(" + antecedent + ") || (" + consequent + ")";
    }
    internal static bool TryRewriteForArguments(
        string conditionText,
        IMethodSymbol contractMethod,
        IMethodSymbol invokedMethod,
        IReadOnlyDictionary<string, ExpressionSyntax> arguments,
        out string rewrittenCondition) {
        rewrittenCondition = conditionText;
        if (!ContractConditionHelpers.TryParse(conditionText, out _, out var conditionExpression)) return false;
        var typeReplacements = CreateTypeParameterReplacements(contractMethod, invokedMethod);
        var rewriter = new ParameterPlaceholderRewriter(arguments, typeReplacements);
        var rewritten = (ExpressionSyntax)rewriter.Visit(conditionExpression)!;
        rewrittenCondition = rewritten.ToFullString();
        return true;
    }
    private static IReadOnlyDictionary<string, TypeSyntax> CreateTypeParameterReplacements(
        IMethodSymbol contractMethod,
        IMethodSymbol invokedMethod) {
        var replacements = new Dictionary<string, TypeSyntax>(StringComparer.Ordinal);
        AddTypeParameterReplacements(contractMethod.TypeParameters, invokedMethod.TypeArguments, replacements);
        if (contractMethod.ContainingType != null)
            AddTypeParameterReplacements(
                contractMethod.ContainingType.OriginalDefinition.TypeParameters,
                contractMethod.ContainingType.TypeArguments,
                replacements);
        return replacements;
    }
    private static void AddTypeParameterReplacements(
        ImmutableArray<ITypeParameterSymbol> parameters,
        ImmutableArray<ITypeSymbol> arguments,
        IDictionary<string, TypeSyntax> replacements) {
        for (var index = 0; index < parameters.Length && index < arguments.Length; index++) {
            var display = arguments[index].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            replacements[parameters[index].Name] = SyntaxFactory.ParseTypeName(display);
        }
    }
    sealed class ParameterPlaceholderRewriter(
        IReadOnlyDictionary<string, ExpressionSyntax> replacements,
        IReadOnlyDictionary<string, TypeSyntax> typeReplacements) : CSharpSyntaxRewriter {
        private readonly IReadOnlyDictionary<string, ExpressionSyntax> _replacements = replacements;
        private readonly IReadOnlyDictionary<string, TypeSyntax> _typeReplacements = typeReplacements;
        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node) {
            if (CSharpSyntaxFacts.IsMemberOrQualifiedNameRightSide(node))
                return base.VisitIdentifierName(node);
            if (_typeReplacements.TryGetValue(node.Identifier.ValueText, out var typeReplacement))
                return typeReplacement.WithTriviaFrom(node);
            if (!_replacements.TryGetValue(node.Identifier.ValueText, out var replacement))
                return base.VisitIdentifierName(node);
            if (IsShadowedByNestedCallableParameter(node, node.Identifier.ValueText))
                return base.VisitIdentifierName(node);
            return SyntaxFactory.ParenthesizedExpression(replacement).WithTriviaFrom(node);
        }
        private static bool IsShadowedByNestedCallableParameter(IdentifierNameSyntax node, string name) {
            foreach (var ancestor in node.Ancestors())
                switch (ancestor) {
                    case SimpleLambdaExpressionSyntax simpleLambda:
                        return simpleLambda.Parameter.Identifier.ValueText == name;
                    case ParenthesizedLambdaExpressionSyntax parenthesizedLambda:
                        return parenthesizedLambda.ParameterList.Parameters.Any(parameter => parameter.Identifier.ValueText == name);
                    case AnonymousMethodExpressionSyntax anonymousMethod:
                        return anonymousMethod.ParameterList?.Parameters.Any(parameter => parameter.Identifier.ValueText == name) == true;
                    case LocalFunctionStatementSyntax localFunction:
                        return localFunction.ParameterList.Parameters.Any(parameter => parameter.Identifier.ValueText == name);
                }
            return false;
        }
    }
}
internal readonly record struct RequiresContract(
    string Condition,
    Location? Location,
    string Argument,
    string? InvalidReason,
    IMethodSymbol SourceMethod);

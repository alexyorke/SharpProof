namespace SharpProof.Analyzer;
internal static class RequiresContractHelpers {
    internal const string AttributeTypeName = "RequiresAttribute";
    internal const string AttributeDisplayName = "[Requires]";
    internal static ImmutableArray<ContractAttributeCondition> CollectContracts(
        IMethodSymbol methodSymbol,
        CancellationToken cancellationToken) => ContractConditionHelpers.Collect(
            methodSymbol,
            AttributeTypeName,
            cancellationToken);
    internal static ImmutableArray<ContractAttributeCondition> ValidContracts(
        IMethodSymbol methodSymbol,
        CancellationToken cancellationToken)
            => [.. CollectContracts(methodSymbol, cancellationToken).Where(static contract
            => contract.InvalidReason == null)];
    internal static bool ContainsResultReference(ExpressionSyntax conditionExpression) => conditionExpression
            .DescendantNodesAndSelf()
            .OfType<IdentifierNameSyntax>()
            .Any(static identifier => string.Equals(identifier.Identifier.ValueText, "result", StringComparison.Ordinal));
    internal static string CombineAsImplication(
        ImmutableArray<ContractAttributeCondition> requiresContracts, string consequent) {
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
        out string rewrittenCondition) =>
        TryRewriteForArguments(
            conditionText,
            contractMethod,
            invokedMethod,
            arguments,
            null,
            out rewrittenCondition);
    internal static bool TryRewriteForArguments(
        string conditionText,
        IMethodSymbol contractMethod,
        IMethodSymbol invokedMethod,
        IReadOnlyDictionary<string, ExpressionSyntax> arguments,
        ExpressionSyntax? receiver,
        out string rewrittenCondition) {
        rewrittenCondition = conditionText;
        if (!ContractConditionHelpers.TryParse(conditionText, out _, out var conditionExpression)) return false;
        var typeReplacements = CreateTypeParameterReplacements(contractMethod, invokedMethod);
        var parameterReplacements = CreateParameterReplacements(contractMethod, invokedMethod, arguments);
        var rewriter = new ParameterPlaceholderRewriter(
            parameterReplacements,
            typeReplacements,
            receiver);
        var rewritten = (ExpressionSyntax)rewriter.Visit(conditionExpression)!;
        rewrittenCondition = rewritten.ToFullString();
        return true;
    }
    internal static bool TryRewriteForMethod(
        string conditionText,
        IMethodSymbol contractMethod,
        IMethodSymbol targetMethod,
        out string rewrittenCondition) {
        var arguments = ImmutableDictionary.CreateBuilder<string, ExpressionSyntax>(StringComparer.Ordinal);
        foreach (var parameter in targetMethod.Parameters)
            arguments[parameter.Name] = SyntaxFactory.ParseExpression("@" + parameter.Name);
        return TryRewriteForArguments(
            conditionText,
            contractMethod,
            targetMethod,
            arguments.ToImmutable(),
            out rewrittenCondition);
    }
    private static IReadOnlyDictionary<string, ExpressionSyntax> CreateParameterReplacements(
        IMethodSymbol contractMethod,
        IMethodSymbol invokedMethod,
        IReadOnlyDictionary<string, ExpressionSyntax> arguments) {
        var replacements = new Dictionary<string, ExpressionSyntax>(StringComparer.Ordinal);
        for (var index = 0;
             index < contractMethod.Parameters.Length && index < invokedMethod.Parameters.Length;
             index++)
            if (arguments.TryGetValue(invokedMethod.Parameters[index].Name, out var argument))
                replacements[contractMethod.Parameters[index].Name] = argument;
        return replacements;
    }
    private static IReadOnlyDictionary<string, TypeSyntax> CreateTypeParameterReplacements(
        IMethodSymbol contractMethod,
        IMethodSymbol invokedMethod) {
        var replacements = new Dictionary<string, TypeSyntax>(StringComparer.Ordinal);
        AddTypeParameterReplacements(contractMethod.TypeParameters, invokedMethod.TypeArguments, replacements);
        var contractType = contractMethod.ContainingType;
        var invokedType = invokedMethod.ContainingType;
        while (contractType != null && invokedType != null) {
            AddTypeParameterReplacements(
                contractType.OriginalDefinition.TypeParameters,
                invokedType.TypeArguments,
                replacements);
            contractType = contractType.ContainingType;
            invokedType = invokedType.ContainingType;
        }
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
        IReadOnlyDictionary<string, TypeSyntax> typeReplacements,
        ExpressionSyntax? receiver) : CSharpSyntaxRewriter {
        private readonly IReadOnlyDictionary<string, ExpressionSyntax> _replacements = replacements;
        private readonly IReadOnlyDictionary<string, TypeSyntax> _typeReplacements = typeReplacements;
        private readonly ExpressionSyntax? _receiver = receiver;
        public override SyntaxNode? VisitThisExpression(ThisExpressionSyntax node) =>
            _receiver == null
                ? base.VisitThisExpression(node)
                : SyntaxFactory.ParenthesizedExpression(_receiver).WithTriviaFrom(node);
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
                    case SimpleLambdaExpressionSyntax simpleLambda
                        when simpleLambda.Parameter.Identifier.ValueText == name:
                    case ParenthesizedLambdaExpressionSyntax parenthesizedLambda
                        when parenthesizedLambda.ParameterList.Parameters.Any(
                            parameter => parameter.Identifier.ValueText == name):
                    case AnonymousMethodExpressionSyntax anonymousMethod
                        when anonymousMethod.ParameterList?.Parameters.Any(
                            parameter => parameter.Identifier.ValueText == name) == true:
                    case LocalFunctionStatementSyntax localFunction
                        when localFunction.ParameterList.Parameters.Any(
                            parameter => parameter.Identifier.ValueText == name):
                        return true;
                }
            return false;
        }
    }
}

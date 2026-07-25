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
            => contract.InvalidReason == null &&
               !ContainsUnsupportedResultReference(
                   contract.Condition,
                   contract.SourceMethod))];
    internal static bool ContainsResultReference(ExpressionSyntax conditionExpression) => conditionExpression
            .DescendantNodesAndSelf()
            .OfType<IdentifierNameSyntax>()
            .Any(IsResultPlaceholder);
    internal static bool IsResultPlaceholder(IdentifierNameSyntax identifier) =>
        string.Equals(identifier.Identifier.Text, "result", StringComparison.Ordinal);
    internal static bool ContainsUnsupportedResultReference(
        string condition,
        IMethodSymbol contractMethod) {
        if (!ContractConditionHelpers.TryParse(
                condition,
                out _,
                out var expression))
            return false;
        if (contractMethod.Parameters.Any(static parameter =>
                string.Equals(
                    parameter.Name,
                    "result",
                    StringComparison.Ordinal)))
            return false;
        return expression.DescendantNodesAndSelf()
            .OfType<IdentifierNameSyntax>()
            .Any(static identifier =>
                IsResultPlaceholder(identifier) &&
                !CSharpSyntaxFacts.IsMemberOrQualifiedNameRightSide(identifier));
    }
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
        while (contractType != null) {
            var arguments = SymbolEqualityComparer.Default.Equals(
                    contractType,
                    contractType.OriginalDefinition)
                ? FindConstructedType(
                    invokedMethod.ContainingType,
                    contractType.OriginalDefinition)?.TypeArguments ??
                  contractType.TypeArguments
                : contractType.TypeArguments;
            AddTypeParameterReplacements(
                contractType.OriginalDefinition.TypeParameters,
                arguments,
                replacements);
            contractType = contractType.ContainingType;
        }
        return replacements;
    }
    private static INamedTypeSymbol? FindConstructedType(
        INamedTypeSymbol? invokedType,
        INamedTypeSymbol definition) {
        for (var containing = invokedType;
             containing != null;
             containing = containing.ContainingType) {
            for (var current = containing;
                 current != null;
                 current = current.BaseType)
                if (SymbolEqualityComparer.Default.Equals(
                        current.OriginalDefinition,
                        definition))
                    return current;
            foreach (var interfaceType in containing.AllInterfaces)
                if (SymbolEqualityComparer.Default.Equals(
                        interfaceType.OriginalDefinition,
                        definition))
                    return interfaceType;
        }
        return null;
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
            if (IsShadowedByNestedDeclaration(node, node.Identifier.ValueText))
                return base.VisitIdentifierName(node);
            return SyntaxFactory.ParenthesizedExpression(replacement).WithTriviaFrom(node);
        }
        private static bool IsShadowedByNestedDeclaration(IdentifierNameSyntax node, string name) {
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
                    case CatchClauseSyntax catchClause
                        when catchClause.Declaration?.Identifier.ValueText == name:
                    case BlockSyntax block
                        when HasEarlierBlockScopedDeclaration(block, node, name):
                    case ExpressionSyntax expression
                        when HasEarlierPatternDeclaration(expression, node, name):
                    case QueryExpressionSyntax query
                        when HasEarlierQueryDeclaration(query, node, name):
                        return true;
                }
            return false;
        }
        private static bool HasEarlierBlockScopedDeclaration(
            BlockSyntax block,
            IdentifierNameSyntax node,
            string name) => block
            .DescendantNodes()
            .Where(declaration => declaration.SpanStart < node.SpanStart)
            .Where(declaration => declaration switch {
                VariableDeclaratorSyntax variable =>
                    variable.Identifier.ValueText == name,
                SingleVariableDesignationSyntax designation =>
                    designation.Identifier.ValueText == name,
                _ => false
            })
            .Any(declaration => ReferenceEquals(
                declaration.Ancestors().OfType<BlockSyntax>().FirstOrDefault(),
                block));
        private static bool HasEarlierPatternDeclaration(
            ExpressionSyntax expression,
            IdentifierNameSyntax node,
            string name) => expression
            .DescendantNodes()
            .OfType<SingleVariableDesignationSyntax>()
            .Any(designation =>
                designation.SpanStart < node.SpanStart &&
                designation.Identifier.ValueText == name);
        private static bool HasEarlierQueryDeclaration(
            QueryExpressionSyntax query,
            IdentifierNameSyntax node,
            string name) => query
            .DescendantNodesAndSelf()
            .Where(declaration => declaration.SpanStart < node.SpanStart)
            .Any(declaration => declaration switch {
                FromClauseSyntax from => from.Identifier.ValueText == name,
                JoinClauseSyntax join => join.Identifier.ValueText == name,
                LetClauseSyntax let => let.Identifier.ValueText == name,
                QueryContinuationSyntax continuation =>
                    continuation.Identifier.ValueText == name,
                _ => false
            });
    }
}

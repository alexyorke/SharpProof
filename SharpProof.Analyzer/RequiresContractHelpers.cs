using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Ir;

namespace SharpProof.Analyzer;

internal static class RequiresContractHelpers
{
    internal const string AttributeTypeName = "RequiresAttribute";
    internal const string AttributeDisplayName = "[Requires]";

    internal static readonly SharpProofAttributeIdentityPolicy OfficialAttributePolicy =
        SharpProofAttributeIdentityPolicy.Create(ImmutableHashSet<string>.Empty);

    internal static ImmutableArray<RequiresContract> CollectContracts(
        IMethodSymbol methodSymbol,
        SharpProofAttributeIdentityPolicy attributePolicy,
        CancellationToken cancellationToken)
    {
        return ContractConditionHelpers.Collect(
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
    }

    internal static ImmutableArray<RequiresContract> ValidContracts(
        IMethodSymbol methodSymbol,
        SharpProofAttributeIdentityPolicy attributePolicy,
        CancellationToken cancellationToken)
    {
        return CollectContracts(methodSymbol, attributePolicy, cancellationToken)
            .Where(static contract => contract.InvalidReason == null)
            .ToImmutableArray();
    }

    internal static bool ContainsResultReference(ExpressionSyntax conditionExpression)
    {
        return conditionExpression
            .DescendantNodesAndSelf()
            .OfType<IdentifierNameSyntax>()
            .Any(static identifier =>
                string.Equals(identifier.Identifier.ValueText, "result", StringComparison.Ordinal));
    }

    internal static bool TryCreateCondition(
        SemanticModel semanticModel,
        int position,
        string conditionText,
        CancellationToken cancellationToken,
        out ExpressionSyntax conditionExpression,
        out SemanticModel conditionSemanticModel,
        out SymbolicCondition condition,
        out string failureReason)
    {
        condition = null!;
        if (!ContractConditionHelpers.TryParse(conditionText, out var conditionStatement, out conditionExpression))
        {
            conditionSemanticModel = semanticModel;
            failureReason = "condition parse failure";
            return false;
        }

        if (!ContractConditionHelpers.TryCreateSpeculativeModel(
                semanticModel,
                position,
                conditionStatement,
                out conditionSemanticModel))
        {
            failureReason = "condition binding failure";
            return false;
        }

        var lowering = SymbolicSemanticPipeline.LowerCondition(
            conditionExpression,
            new SymbolicLoweringContext(conditionSemanticModel, cancellationToken));
        if (lowering is not { IsExact: true, Value: { } loweredCondition })
        {
            failureReason = "condition is not supported by the current bounded proof engine";
            return false;
        }

        condition = loweredCondition;
        failureReason = string.Empty;
        return true;
    }

    internal static int GetMethodEntrySpeculativePosition(SyntaxNode methodNode)
    {
        return methodNode switch
        {
            MethodDeclarationSyntax { Body: { } body } => body.OpenBraceToken.Span.End,
            MethodDeclarationSyntax { ExpressionBody: { } expressionBody } => expressionBody.Expression.SpanStart,
            ConstructorDeclarationSyntax { Body: { } body } => body.OpenBraceToken.Span.End,
            ConstructorDeclarationSyntax { ExpressionBody: { } expressionBody } => expressionBody.Expression.SpanStart,
            OperatorDeclarationSyntax { Body: { } body } => body.OpenBraceToken.Span.End,
            OperatorDeclarationSyntax { ExpressionBody: { } expressionBody } => expressionBody.Expression.SpanStart,
            ConversionOperatorDeclarationSyntax { Body: { } body } => body.OpenBraceToken.Span.End,
            ConversionOperatorDeclarationSyntax { ExpressionBody: { } expressionBody } => expressionBody.Expression
                .SpanStart,
            AccessorDeclarationSyntax { Body: { } body } => body.OpenBraceToken.Span.End,
            AccessorDeclarationSyntax { ExpressionBody: { } expressionBody } => expressionBody.Expression.SpanStart,
            LocalFunctionStatementSyntax { Body: { } body } => body.OpenBraceToken.Span.End,
            LocalFunctionStatementSyntax { ExpressionBody: { } expressionBody } => expressionBody.Expression.SpanStart,
            _ => methodNode.SpanStart
        };
    }

    internal static string CombineAsImplication(
        ImmutableArray<RequiresContract> requiresContracts,
        string consequent)
    {
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
        IMethodSymbol methodSymbol,
        ImmutableArray<IArgumentOperation> arguments,
        out string rewrittenCondition)
    {
        var replacements = new Dictionary<string, ExpressionSyntax>(StringComparer.Ordinal);
        foreach (var argument in arguments)
        {
            if (argument.Parameter == null ||
                argument.Value.Syntax is not ExpressionSyntax argumentExpression)
                continue;

            replacements[argument.Parameter.Name] = (ExpressionSyntax)argumentExpression.WithoutTrivia();
        }

        return TryRewriteForArguments(
            conditionText,
            methodSymbol,
            methodSymbol,
            replacements,
            out rewrittenCondition);
    }

    internal static bool TryRewriteForArguments(
        string conditionText,
        IMethodSymbol contractMethod,
        IMethodSymbol invokedMethod,
        IReadOnlyDictionary<string, ExpressionSyntax> arguments,
        out string rewrittenCondition)
    {
        rewrittenCondition = conditionText;
        if (!ContractConditionHelpers.TryParse(conditionText, out _, out var conditionExpression)) return false;

        var typeReplacements = CreateTypeParameterReplacements(contractMethod, invokedMethod);
        var rewriter = new ParameterPlaceholderRewriter(arguments, typeReplacements);
        var rewritten = (ExpressionSyntax)rewriter.Visit(conditionExpression)!;
        rewrittenCondition = rewritten.ToFullString();
        return true;
    }

    internal static bool TryRewriteForArguments(
        string conditionText,
        IReadOnlyDictionary<string, ExpressionSyntax> arguments,
        out string rewrittenCondition)
    {
        rewrittenCondition = conditionText;
        if (!ContractConditionHelpers.TryParse(conditionText, out _, out var conditionExpression)) return false;

        var rewriter = new ParameterPlaceholderRewriter(
            arguments,
            new Dictionary<string, TypeSyntax>(StringComparer.Ordinal));
        var rewritten = (ExpressionSyntax)rewriter.Visit(conditionExpression)!;
        rewrittenCondition = rewritten.ToFullString();
        return true;
    }

    private static IReadOnlyDictionary<string, TypeSyntax> CreateTypeParameterReplacements(
        IMethodSymbol contractMethod,
        IMethodSymbol invokedMethod)
    {
        var replacements = new Dictionary<string, TypeSyntax>(StringComparer.Ordinal);
        AddTypeParameterReplacements(
            contractMethod.TypeParameters,
            invokedMethod.TypeArguments,
            replacements);

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
        IDictionary<string, TypeSyntax> replacements)
    {
        for (var index = 0; index < parameters.Length && index < arguments.Length; index++)
        {
            var display = arguments[index].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            replacements[parameters[index].Name] = SyntaxFactory.ParseTypeName(display);
        }
    }

    internal static string CreateEvidenceKey(string prefix, string condition, Location? location, string reason)
    {
        return prefix +
               ":" +
               condition +
               "@" +
               ContractDiagnosticSupport.FormatLocationKey(location) +
               "|" +
               reason;
    }

    private sealed class ParameterPlaceholderRewriter : CSharpSyntaxRewriter
    {
        private readonly IReadOnlyDictionary<string, ExpressionSyntax> _replacements;
        private readonly IReadOnlyDictionary<string, TypeSyntax> _typeReplacements;

        public ParameterPlaceholderRewriter(
            IReadOnlyDictionary<string, ExpressionSyntax> replacements,
            IReadOnlyDictionary<string, TypeSyntax> typeReplacements)
        {
            _replacements = replacements;
            _typeReplacements = typeReplacements;
        }

        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        {
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

        private static bool IsShadowedByNestedCallableParameter(IdentifierNameSyntax node, string name)
        {
            foreach (var ancestor in node.Ancestors())
                switch (ancestor)
                {
                    case SimpleLambdaExpressionSyntax simpleLambda:
                        return simpleLambda.Parameter.Identifier.ValueText == name;
                    case ParenthesizedLambdaExpressionSyntax parenthesizedLambda:
                        return parenthesizedLambda.ParameterList.Parameters.Any(parameter =>
                            parameter.Identifier.ValueText == name);
                    case AnonymousMethodExpressionSyntax anonymousMethod:
                        return anonymousMethod.ParameterList?.Parameters.Any(parameter =>
                            parameter.Identifier.ValueText == name) == true;
                    case LocalFunctionStatementSyntax localFunction:
                        return localFunction.ParameterList.Parameters.Any(parameter =>
                            parameter.Identifier.ValueText == name);
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

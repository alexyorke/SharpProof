using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Symbolic.Ir;

internal static class SymbolicRegexLowerer
{
    private const string RegexMetadataName = "System.Text.RegularExpressions.Regex";
    private const string GeneratedRegexAttributeMetadataName =
        "System.Text.RegularExpressions.GeneratedRegexAttribute";

    internal static bool TryLowerRegexMatchSuccessCondition(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out SymbolicCondition condition)
    {
        condition = null!;
        expression = SymbolicLoweringValueFacts.UnwrapExpression(expression);
        if (expression is not MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "Success",
                Expression: InvocationExpressionSyntax invocation
            } ||
            context.SemanticModel.GetOperation(expression, context.CancellationToken) is not
                IPropertyReferenceOperation
                {
                    Property:
                    {
                        Name: "Success",
                        Type.SpecialType: SpecialType.System_Boolean
                    }
                } ||
            context.SemanticModel.GetOperation(invocation, context.CancellationToken) is not
                IInvocationOperation { TargetMethod.Name: "Match" })
            return false;

        return TryLowerRegexInvocationPredicate(invocation, context, out condition);
    }

    internal static bool TryLowerRegexMatchesCountComparison(
        BinaryExpressionSyntax comparison,
        SymbolicLoweringContext context,
        out SymbolicCondition condition)
    {
        condition = null!;
        if (TryLowerRegexMatchesCountComparisonOperand(
                comparison.Left,
                comparison.Right,
                comparison.Kind(),
                context,
                out condition))
            return true;

        return TryLowerRegexMatchesCountComparisonOperand(
            comparison.Right,
            comparison.Left,
            SymbolicStringLowerer.ReverseStringComparisonKind(comparison.Kind()),
            context,
            out condition);
    }

    private static bool TryLowerRegexMatchesCountComparisonOperand(
        ExpressionSyntax countExpression,
        ExpressionSyntax constantExpression,
        SyntaxKind comparisonKind,
        SymbolicLoweringContext context,
        out SymbolicCondition condition)
    {
        condition = null!;
        countExpression = SymbolicLoweringValueFacts.UnwrapExpression(countExpression);
        var constant = context.SemanticModel.GetConstantValue(constantExpression, context.CancellationToken);
        if (countExpression is not MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "Count",
                Expression: InvocationExpressionSyntax invocation
            } ||
            context.SemanticModel.GetOperation(countExpression, context.CancellationToken) is not
                IPropertyReferenceOperation { Property.Type.SpecialType: SpecialType.System_Int32 } ||
            context.SemanticModel.GetOperation(invocation, context.CancellationToken) is not
                IInvocationOperation { TargetMethod.Name: "Matches" } ||
            !constant.HasValue ||
            constant.Value == null ||
            !SymbolicLoweringValueFacts.TryGetIntegralConstant(constant.Value, out var count) ||
            !TryClassifyRegexMatchCountComparison(comparisonKind, count, out var hasMatch) ||
            !TryLowerRegexInvocationParts(invocation, context, out var evaluation, out var match))
            return false;

        condition = CombineRegexEvaluationAndValue(
            evaluation,
            hasMatch ? match : new SymbolicNotCondition(match));
        return true;
    }

    private static bool TryClassifyRegexMatchCountComparison(
        SyntaxKind comparisonKind,
        long constant,
        out bool hasMatch)
    {
        hasMatch = false;
        switch (comparisonKind)
        {
            case SyntaxKind.EqualsExpression when constant == 0:
            case SyntaxKind.LessThanExpression when constant == 1:
            case SyntaxKind.LessThanOrEqualExpression when constant == 0:
                return true;
            case SyntaxKind.NotEqualsExpression when constant == 0:
            case SyntaxKind.GreaterThanExpression when constant == 0:
            case SyntaxKind.GreaterThanOrEqualExpression when constant == 1:
                hasMatch = true;
                return true;
            default:
                return false;
        }
    }

    internal static bool TryLowerRegexInvocationPredicate(
        InvocationExpressionSyntax invocation,
        SymbolicLoweringContext context,
        out SymbolicCondition condition)
    {
        condition = null!;
        if (!TryLowerRegexInvocationParts(invocation, context, out var evaluation, out var predicate))
            return false;

        condition = CombineRegexEvaluationAndValue(evaluation, predicate);
        return true;
    }

    internal static bool TryLowerNegatedRegexInvocationPredicate(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out SymbolicCondition condition)
    {
        condition = null!;
        expression = SymbolicLoweringValueFacts.UnwrapExpression(expression);
        if (expression is not InvocationExpressionSyntax invocation ||
            !TryLowerRegexInvocationParts(invocation, context, out var evaluation, out var predicate))
            return false;

        condition = CombineRegexEvaluationAndValue(evaluation, new SymbolicNotCondition(predicate));
        return true;
    }

    private static bool TryLowerRegexInvocationParts(
        InvocationExpressionSyntax invocation,
        SymbolicLoweringContext context,
        out SymbolicCondition? evaluation,
        out SymbolicCondition predicate)
    {
        evaluation = null;
        predicate = null!;
        if (context.SemanticModel.GetOperation(invocation, context.CancellationToken) is not
                IInvocationOperation operation ||
            operation.TargetMethod.Name is not ("IsMatch" or "Match" or "Matches") ||
            !IsRegexType(operation.TargetMethod.ContainingType) ||
            !TryResolveRegexInvocation(
                invocation,
                operation,
                context,
                out var inputExpression,
                out var pattern,
                out var options) ||
            !SymbolicStringLowerer.TryLowerStringTerm(inputExpression, context, out var input))
            return false;

        predicate = SymbolicIrLowerer.CreateFactCondition(
            new SymbolicStringPredicateAtom(
                SymbolicStringPredicateKind.RegexMatch,
                input,
                new SymbolicStringConstantTerm(pattern),
                options),
            invocation,
            "ir.regex." + operation.TargetMethod.Name.ToLowerInvariant());

        if (SymbolicReferenceLowerer.TryLowerReferenceTerm(inputExpression, context, out var inputReference))
            evaluation = SymbolicIrLowerer.CreateReferenceNullCondition(
                inputReference,
                false,
                inputExpression,
                "ir.regex.input-non-null");

        return true;
    }

    private static SymbolicCondition CombineRegexEvaluationAndValue(
        SymbolicCondition? evaluation,
        SymbolicCondition value)
    {
        return evaluation == null
            ? value
            : new SymbolicBinaryCondition(SymbolicConditionOperator.And, evaluation, value);
    }

    private static bool TryResolveRegexInvocation(
        InvocationExpressionSyntax invocation,
        IInvocationOperation operation,
        SymbolicLoweringContext context,
        out ExpressionSyntax inputExpression,
        out string pattern,
        out RegexOptions options)
    {
        inputExpression = null!;
        pattern = string.Empty;
        options = RegexOptions.None;
        if (operation.TargetMethod.IsStatic)
        {
            if (operation.Arguments.Length is not 2 and not 3 ||
                operation.Arguments[0].Value.Syntax is not ExpressionSyntax input ||
                operation.Arguments[1].Value.Syntax is not ExpressionSyntax patternExpression ||
                !TryGetConstantString(patternExpression, context, out pattern))
                return false;

            if (operation.Arguments.Length == 3 &&
                (operation.Arguments[2].Value.Syntax is not ExpressionSyntax optionsExpression ||
                 !SymbolicStringLowerer.TryGetRegexOptions(optionsExpression, context, out options)))
                return false;

            inputExpression = input;
            return true;
        }

        if (operation.Instance?.Syntax is not ExpressionSyntax receiver ||
            operation.Arguments.Length is not 1 and not 2 ||
            operation.Arguments[0].Value.Syntax is not ExpressionSyntax instanceInput ||
            operation.Arguments.Length == 2 && !IsConstantZero(operation.Arguments[1].Value.Syntax, context) ||
            !TryResolveRegexSource(receiver, invocation, context, out pattern, out options))
            return false;

        inputExpression = instanceInput;
        return true;
    }

    private static bool TryResolveRegexSource(
        ExpressionSyntax expression,
        SyntaxNode useSite,
        SymbolicLoweringContext context,
        out string pattern,
        out RegexOptions options)
    {
        pattern = string.Empty;
        options = RegexOptions.None;
        expression = SymbolicLoweringValueFacts.UnwrapExpression(expression);
        if (expression is ObjectCreationExpressionSyntax creation)
            return TryResolveRegexObjectCreation(creation, context, out pattern, out options);

        if (expression is InvocationExpressionSyntax factoryInvocation &&
            context.SemanticModel.GetOperation(factoryInvocation, context.CancellationToken) is
                IInvocationOperation factoryOperation &&
            TryResolveGeneratedRegexFactory(factoryOperation.TargetMethod, out pattern, out options))
            return true;

        if (context.SemanticModel.GetSymbolInfo(expression, context.CancellationToken).Symbol is ILocalSymbol local)
            return TryResolveLocalRegexSource(local, useSite, context, out pattern, out options);

        if (context.SemanticModel.GetSymbolInfo(expression, context.CancellationToken).Symbol is IFieldSymbol field)
            return TryResolveReadonlyRegexFieldSource(field, context, out pattern, out options);

        return false;
    }

    private static bool TryResolveRegexObjectCreation(
        ObjectCreationExpressionSyntax creation,
        SymbolicLoweringContext context,
        out string pattern,
        out RegexOptions options)
    {
        pattern = string.Empty;
        options = RegexOptions.None;
        if (context.SemanticModel.GetOperation(creation, context.CancellationToken) is not
                IObjectCreationOperation operation ||
            !IsRegexType(operation.Constructor?.ContainingType) ||
            operation.Arguments.Length is not 1 and not 2 ||
            operation.Arguments[0].Value.Syntax is not ExpressionSyntax patternExpression ||
            !TryGetConstantString(patternExpression, context, out pattern))
            return false;

        return operation.Arguments.Length == 1 ||
               operation.Arguments[1].Value.Syntax is ExpressionSyntax optionsExpression &&
               SymbolicStringLowerer.TryGetRegexOptions(optionsExpression, context, out options);
    }

    private static bool TryResolveGeneratedRegexFactory(
        IMethodSymbol method,
        out string pattern,
        out RegexOptions options)
    {
        pattern = string.Empty;
        options = RegexOptions.None;
        foreach (var attribute in method.GetAttributes())
        {
            if (!string.Equals(
                    SymbolicTypeFacts.GetFullMetadataName(attribute.AttributeClass),
                    GeneratedRegexAttributeMetadataName,
                    StringComparison.Ordinal) ||
                attribute.ConstructorArguments.Length == 0 ||
                attribute.ConstructorArguments[0].Value is not string generatedPattern)
                continue;

            pattern = generatedPattern;
            if (attribute.ConstructorArguments.Length > 1 &&
                attribute.ConstructorArguments[1].Value is int rawOptions)
                options = (RegexOptions)rawOptions;
            return SymbolicStringLowerer.CanRepresentRegexOptions(options);
        }

        return false;
    }

    private static bool TryResolveLocalRegexSource(
        ILocalSymbol local,
        SyntaxNode useSite,
        SymbolicLoweringContext context,
        out string pattern,
        out RegexOptions options)
    {
        pattern = string.Empty;
        options = RegexOptions.None;
        if (local.DeclaringSyntaxReferences.Length != 1 ||
            local.DeclaringSyntaxReferences[0].GetSyntax(context.CancellationToken) is not VariableDeclaratorSyntax
            {
                Initializer.Value: { } initializer
            } declarator ||
            declarator.Parent?.Parent is not LocalDeclarationStatementSyntax declaration ||
            useSite.FirstAncestorOrSelf<StatementSyntax>() is not { } useStatement ||
            declaration.Parent is not BlockSyntax block ||
            !ReferenceEquals(block, useStatement.Parent))
            return false;

        var declarationIndex = block.Statements.IndexOf(declaration);
        var useIndex = block.Statements.IndexOf(useStatement);
        if (declarationIndex < 0 || useIndex <= declarationIndex ||
            SymbolicSourcePredicateLowerer.CountLocalSymbolReferences(useStatement, local, context) != 1)
            return false;

        for (var index = declarationIndex + 1; index < useIndex; index++)
            if (SymbolicSourcePredicateLowerer.CountLocalSymbolReferences(block.Statements[index], local, context) != 0)
                return false;

        return TryResolveRegexSource(initializer, declarator, context, out pattern, out options);
    }

    private static bool TryResolveReadonlyRegexFieldSource(
        IFieldSymbol field,
        SymbolicLoweringContext callerContext,
        out string pattern,
        out RegexOptions options)
    {
        pattern = string.Empty;
        options = RegexOptions.None;
        if (!field.IsReadOnly ||
            !IsRegexType(field.Type) ||
            FieldHasAssignmentOutsideInitializer(field, callerContext))
            return false;

        var declarator = field.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax(callerContext.CancellationToken))
            .OfType<VariableDeclaratorSyntax>()
            .FirstOrDefault(static syntax => syntax.Initializer?.Value != null);
        if (declarator?.Initializer?.Value is not { } initializer) return false;

        var semanticModel = callerContext.Compilation.GetSemanticModel(initializer.SyntaxTree);
        var initializerContext = new SymbolicLoweringContext(
            semanticModel,
            callerContext.CancellationToken,
            callerContext.GetSymbolVersion,
            callerContext.SmtAnalysis,
            callerContext.InvocationTermLowerer,
            callerContext.ImplicitThis,
            callerContext.InlineDepth,
            callerContext.SymbolSubstitutions,
            callerContext.InvocationTermTypeResolver);
        return TryResolveRegexSource(initializer, declarator, initializerContext, out pattern, out options);
    }

    private static bool FieldHasAssignmentOutsideInitializer(
        IFieldSymbol field,
        SymbolicLoweringContext context)
    {
        foreach (var typeReference in field.ContainingType.DeclaringSyntaxReferences)
        {
            var typeSyntax = typeReference.GetSyntax(context.CancellationToken);
            var semanticModel = context.Compilation.GetSemanticModel(typeSyntax.SyntaxTree);
            foreach (var assignment in typeSyntax.DescendantNodes().OfType<AssignmentExpressionSyntax>())
                if (SymbolEqualityComparer.Default.Equals(
                        semanticModel.GetSymbolInfo(assignment.Left, context.CancellationToken).Symbol,
                        field))
                    return true;
        }

        return false;
    }

    private static bool TryGetConstantString(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out string value)
    {
        var constant = context.SemanticModel.GetConstantValue(expression, context.CancellationToken);
        if (constant is { HasValue: true, Value: string stringValue })
        {
            value = stringValue;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static bool IsConstantZero(SyntaxNode expression, SymbolicLoweringContext context)
    {
        if (expression is not ExpressionSyntax expressionSyntax) return false;
        var constant = context.SemanticModel.GetConstantValue(expressionSyntax, context.CancellationToken);
        return constant.HasValue &&
               constant.Value != null &&
               SymbolicLoweringValueFacts.TryGetIntegralConstant(constant.Value, out var value) &&
               value == 0;
    }

    private static bool IsRegexType(ITypeSymbol? type)
    {
        return string.Equals(type?.ToDisplayString(), RegexMetadataName, StringComparison.Ordinal);
    }
}

using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SearchLib.Smt;

namespace SharpProof.Symbolic.Ir;

internal static partial class SymbolicIrLowerer
{
    private static bool TryLowerStringPredicateInvocation(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        SymbolicLoweringContext context,
        out SymbolicCondition condition)
    {
        condition = null!;
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
            invocation.ArgumentList.Arguments.Count is not 1 and not 2 ||
            method.Parameters.Length != invocation.ArgumentList.Arguments.Count ||
            !TryLowerStringTerm(memberAccess.Expression, context, out var receiver) ||
            !TryLowerStringPredicateArgument(
                invocation.ArgumentList.Arguments[0].Expression,
                method.Parameters[0].Type,
                context,
                out var argument))
            return false;

        var predicate = method.Name switch
        {
            nameof(string.Contains) => SymbolicStringPredicateKind.Contains,
            nameof(string.StartsWith) => SymbolicStringPredicateKind.StartsWith,
            nameof(string.EndsWith) => SymbolicStringPredicateKind.EndsWith,
            _ => (SymbolicStringPredicateKind?)null
        };

        if (predicate == null) return false;

        if (invocation.ArgumentList.Arguments.Count == 2 &&
            !IsOrdinalStringComparisonArgument(invocation.ArgumentList.Arguments[1].Expression, context))
            return false;

        if (predicate != SymbolicStringPredicateKind.Contains &&
            method.Parameters[0].Type.SpecialType != SpecialType.System_Char &&
            argument is not SymbolicStringConstantTerm)
            return false;

        condition = CreateFactCondition(
            new SymbolicStringPredicateAtom(
                predicate.Value,
                receiver,
                argument),
            invocation,
            "ir.known-api.string." + method.Name);
        return true;
    }

    private static bool TryLowerRegexIsMatchInvocation(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        SymbolicLoweringContext context,
        out SymbolicCondition condition)
    {
        condition = null!;
        if (!method.IsStatic ||
            invocation.ArgumentList.Arguments.Count is not 2 and not 3 ||
            method.Parameters.Length < 2 ||
            method.Parameters[0].Type.SpecialType != SpecialType.System_String ||
            method.Parameters[1].Type.SpecialType != SpecialType.System_String ||
            !TryLowerStringTerm(invocation.ArgumentList.Arguments[0].Expression, context, out var input) ||
            !TryLowerStringTerm(invocation.ArgumentList.Arguments[1].Expression, context, out var patternTerm) ||
            patternTerm is not SymbolicStringConstantTerm pattern)
            return false;

        var options = RegexOptions.None;
        if (invocation.ArgumentList.Arguments.Count == 3)
            if (!TryGetRegexOptions(invocation.ArgumentList.Arguments[2].Expression, context, out options))
                return false;

        if (!CanEncodeRegexOptions(options)) return false;

        condition = CreateFactCondition(
            new SymbolicStringPredicateAtom(
                SymbolicStringPredicateKind.RegexMatch,
                input,
                pattern,
                options),
            invocation,
            "ir.known-api.regex.is-match");
        return true;
    }

    private static bool TryLowerStringEqualsInvocation(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        SymbolicLoweringContext context,
        out SymbolicCondition condition)
    {
        condition = null!;
        if (!method.IsStatic)
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
                invocation.ArgumentList.Arguments.Count is not 1 and not 2 ||
                method.Parameters.Length != invocation.ArgumentList.Arguments.Count ||
                method.Parameters[0].Type.SpecialType != SpecialType.System_String)
                return false;

            if (invocation.ArgumentList.Arguments.Count == 2 &&
                !IsOrdinalStringComparisonArgument(invocation.ArgumentList.Arguments[1].Expression, context))
                return false;

            return TryCreateStringEqualityCondition(
                memberAccess.Expression,
                invocation.ArgumentList.Arguments[0].Expression,
                invocation,
                context,
                "ir.known-api.string.instance-equals",
                out condition);
        }

        if (invocation.ArgumentList.Arguments.Count is not 2 and not 3 ||
            method.Parameters.Length != invocation.ArgumentList.Arguments.Count ||
            method.Parameters[0].Type.SpecialType != SpecialType.System_String ||
            method.Parameters[1].Type.SpecialType != SpecialType.System_String)
            return false;

        if (invocation.ArgumentList.Arguments.Count == 3 &&
            !IsOrdinalStringComparisonArgument(invocation.ArgumentList.Arguments[2].Expression, context))
            return false;

        return TryCreateStringEqualityCondition(
            invocation.ArgumentList.Arguments[0].Expression,
            invocation.ArgumentList.Arguments[1].Expression,
            invocation,
            context,
            "ir.known-api.string.equals",
            out condition);
    }

    private static bool TryLowerStringEqualityCondition(
        BinaryExpressionSyntax binaryExpression,
        SymbolicLoweringContext context,
        out SymbolicCondition condition)
    {
        if (!TryCreateStringEqualityCondition(
                binaryExpression.Left,
                binaryExpression.Right,
                binaryExpression,
                context,
                "ir.string.equality",
                out condition))
            return false;

        if (binaryExpression.IsKind(SyntaxKind.NotEqualsExpression)) condition = new SymbolicNotCondition(condition);

        return true;
    }

    private static bool TryCreateStringEqualityCondition(
        ExpressionSyntax leftExpression,
        ExpressionSyntax rightExpression,
        SyntaxNode sourceNode,
        SymbolicLoweringContext context,
        string provenancePrefix,
        out SymbolicCondition condition)
    {
        condition = null!;
        if (!IsStringExpression(leftExpression, context) ||
            !IsStringExpression(rightExpression, context) ||
            !TryLowerStringTerm(leftExpression, context, out var leftValue) ||
            !TryLowerStringTerm(rightExpression, context, out var rightValue))
            return false;

        var valuesEqual = CreateRelationCondition(
            SymbolicRelationOperator.Equal,
            leftValue,
            rightValue,
            sourceNode,
            provenancePrefix + ".value");
        if (TryLowerTerm(leftExpression, context, out var leftReference) &&
            leftReference.Kind == SmtValueKind.Reference &&
            TryLowerTerm(rightExpression, context, out var rightReference) &&
            rightReference.Kind == SmtValueKind.Reference)
        {
            var bothNull = new SymbolicBinaryCondition(
                SymbolicConditionOperator.And,
                CreateRelationCondition(
                    SymbolicRelationOperator.Equal,
                    leftReference,
                    new SymbolicNullTerm(),
                    leftExpression,
                    provenancePrefix + ".left-null"),
                CreateRelationCondition(
                    SymbolicRelationOperator.Equal,
                    rightReference,
                    new SymbolicNullTerm(),
                    rightExpression,
                    provenancePrefix + ".right-null"));
            var bothNonNull = new SymbolicBinaryCondition(
                SymbolicConditionOperator.And,
                CreateRelationCondition(
                    SymbolicRelationOperator.NotEqual,
                    leftReference,
                    new SymbolicNullTerm(),
                    leftExpression,
                    provenancePrefix + ".left-not-null"),
                CreateRelationCondition(
                    SymbolicRelationOperator.NotEqual,
                    rightReference,
                    new SymbolicNullTerm(),
                    rightExpression,
                    provenancePrefix + ".right-not-null"));
            condition = new SymbolicBinaryCondition(
                SymbolicConditionOperator.Or,
                bothNull,
                new SymbolicBinaryCondition(SymbolicConditionOperator.And, bothNonNull, valuesEqual));
            return true;
        }

        if (TryLowerTerm(leftExpression, context, out leftReference) &&
            leftReference.Kind == SmtValueKind.Reference &&
            rightValue is SymbolicStringConstantTerm)
        {
            condition = new SymbolicBinaryCondition(
                SymbolicConditionOperator.And,
                CreateRelationCondition(
                    SymbolicRelationOperator.NotEqual,
                    leftReference,
                    new SymbolicNullTerm(),
                    leftExpression,
                    provenancePrefix + ".left-not-null"),
                valuesEqual);
            return true;
        }

        if (TryLowerTerm(rightExpression, context, out rightReference) &&
            rightReference.Kind == SmtValueKind.Reference &&
            leftValue is SymbolicStringConstantTerm)
        {
            condition = new SymbolicBinaryCondition(
                SymbolicConditionOperator.And,
                CreateRelationCondition(
                    SymbolicRelationOperator.NotEqual,
                    rightReference,
                    new SymbolicNullTerm(),
                    rightExpression,
                    provenancePrefix + ".right-not-null"),
                valuesEqual);
            return true;
        }

        condition = valuesEqual;
        return true;
    }

    private static bool TryLowerStringNullOrPredicateInvocation(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        SymbolicLoweringContext context,
        out SymbolicCondition condition)
    {
        condition = null!;
        if (!method.IsStatic ||
            invocation.ArgumentList.Arguments.Count != 1 ||
            method.Parameters.Length != 1 ||
            method.Parameters[0].Type.SpecialType != SpecialType.System_String ||
            !TryLowerStringValueWithOptionalReference(
                invocation.ArgumentList.Arguments[0].Expression,
                context,
                out var stringValue,
                out var reference))
            return false;

        SymbolicCondition predicateCondition;
        if (string.Equals(method.Name, nameof(string.IsNullOrEmpty), StringComparison.Ordinal))
            predicateCondition = CreateFactCondition(
                new SymbolicRelationAtom(
                    SymbolicRelationOperator.Equal,
                    new SymbolicLengthTerm(stringValue),
                    new SymbolicIntegerConstantTerm(0)),
                invocation,
                "ir.known-api.string.is-null-or-empty.empty");
        else if (string.Equals(method.Name, nameof(string.IsNullOrWhiteSpace), StringComparison.Ordinal))
            predicateCondition = CreateFactCondition(
                new SymbolicStringPredicateAtom(
                    SymbolicStringPredicateKind.RegexMatch,
                    stringValue,
                    new SymbolicStringConstantTerm(@"\A\s*\z")),
                invocation,
                "ir.known-api.string.is-null-or-white-space.regex");
        else
            return false;

        condition = reference == null
            ? predicateCondition
            : new SymbolicBinaryCondition(
                SymbolicConditionOperator.Or,
                CreateReferenceIsNullCondition(reference, invocation.ArgumentList.Arguments[0].Expression),
                predicateCondition);
        return true;
    }

    private static bool TryLowerStringExpressionTerm(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out SymbolicTerm term)
    {
        expression = UnwrapExpression(expression);

        if (expression is BinaryExpressionSyntax binary &&
            binary.IsKind(SyntaxKind.AddExpression) &&
            IsStringExpression(binary, context) &&
            TryLowerStringConcatOperand(binary.Left, context, out var left) &&
            TryLowerStringConcatOperand(binary.Right, context, out var right))
        {
            term = new SymbolicStringConcatTerm(left, right);
            return true;
        }

        if (expression is InvocationExpressionSyntax invocation &&
            TryLowerStringConcatInvocationTerm(invocation, context, out term))
            return true;

        if (expression is InterpolatedStringExpressionSyntax interpolatedString &&
            TryLowerInterpolatedStringTerm(interpolatedString, context, out term))
            return true;

        term = null!;
        return false;
    }

    internal static bool TryCreateStringContentReferenceTerm(
        SymbolicTerm reference,
        out SymbolicTerm term)
    {
        if (reference.Kind != SmtValueKind.Reference)
        {
            term = null!;
            return false;
        }

        term = new SymbolicStringContentTerm(reference);
        return true;
    }

    internal static bool TryLowerStringNonNullCondition(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out SymbolicCondition condition)
    {
        expression = UnwrapExpression(expression);

        var constantValue = context.SemanticModel.GetConstantValue(expression, context.CancellationToken);
        if (constantValue.HasValue)
        {
            if (constantValue.Value is string)
            {
                condition = new SymbolicConstantCondition(true);
                return true;
            }

            if (constantValue.Value == null)
            {
                condition = new SymbolicConstantCondition(false);
                return true;
            }
        }

        if (expression is BinaryExpressionSyntax coalesceExpression &&
            coalesceExpression.IsKind(SyntaxKind.CoalesceExpression) &&
            IsStringExpression(expression, context) &&
            TryLowerStringNonNullCondition(coalesceExpression.Left, context, out var coalesceLeftNonNull) &&
            TryLowerStringNonNullCondition(coalesceExpression.Right, context, out var coalesceRightNonNull))
        {
            condition = new SymbolicBinaryCondition(
                SymbolicConditionOperator.Or,
                coalesceLeftNonNull,
                coalesceRightNonNull);
            return true;
        }

        if (expression is ConditionalExpressionSyntax conditionalExpression &&
            TryLowerCondition(conditionalExpression.Condition, context, out var branchCondition) &&
            TryLowerStringNonNullCondition(conditionalExpression.WhenTrue, context, out var whenTrueNonNull) &&
            TryLowerStringNonNullCondition(conditionalExpression.WhenFalse, context, out var whenFalseNonNull))
        {
            condition = new SymbolicBinaryCondition(
                SymbolicConditionOperator.Or,
                new SymbolicBinaryCondition(
                    SymbolicConditionOperator.And,
                    branchCondition,
                    whenTrueNonNull),
                new SymbolicBinaryCondition(
                    SymbolicConditionOperator.And,
                    new SymbolicNotCondition(branchCondition),
                    whenFalseNonNull));
            return true;
        }

        if (TryLowerStringTerm(expression, context, out var stringTerm))
            switch (stringTerm)
            {
                case SymbolicStringConstantTerm:
                case SymbolicStringConcatTerm:
                    condition = new SymbolicConstantCondition(true);
                    return true;
                case SymbolicStringContentTerm stringContent:
                    condition = CreateReferenceNullCondition(
                        stringContent.Reference,
                        false,
                        expression,
                        "ir.string.non-null.reference");
                    return true;
            }

        condition = null!;
        return false;
    }

    internal static bool TryLowerStringTerm(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out SymbolicTerm term)
    {
        expression = UnwrapExpression(expression);

        if (expression is CastExpressionSyntax castExpression &&
            context.SemanticModel.GetTypeInfo(castExpression.Type, context.CancellationToken).Type?.SpecialType ==
            SpecialType.System_String)
        {
            if (TryLowerStringTerm(castExpression.Expression, context, out term)) return true;

            if (TryLowerTerm(castExpression.Expression, context, out var castReference) &&
                castReference.Kind == SmtValueKind.Reference &&
                TryCreateStringContentReferenceTerm(castReference, out term))
                return true;
        }

        var constantValue = context.SemanticModel.GetConstantValue(expression, context.CancellationToken);
        if (constantValue.HasValue && constantValue.Value is string stringValue)
        {
            term = new SymbolicStringConstantTerm(stringValue);
            return true;
        }

        if (expression is MemberAccessExpressionSyntax stringEmptyMemberAccess &&
            context.SemanticModel.GetSymbolInfo(stringEmptyMemberAccess, context.CancellationToken).Symbol is
                IFieldSymbol
            {
                IsStatic: true,
                Name: nameof(string.Empty),
                Type.SpecialType: SpecialType.System_String
            } stringEmptyField &&
            IsSystemStringType(stringEmptyField.ContainingType))
        {
            term = new SymbolicStringConstantTerm(string.Empty);
            return true;
        }

        if (TryLowerStringExpressionTerm(expression, context, out term)) return true;

        if (!IsStringExpression(expression, context) ||
            !TryLowerTerm(expression, context, out var reference))
        {
            term = null!;
            return false;
        }

        return TryCreateStringContentReferenceTerm(reference, out term);
    }

    private static bool TryLowerStringConcatOperand(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out SymbolicTerm term)
    {
        expression = UnwrapExpression(expression);

        if (expression is LiteralExpressionSyntax literal &&
            literal.IsKind(SyntaxKind.NullLiteralExpression))
        {
            term = new SymbolicStringConstantTerm(string.Empty);
            return true;
        }

        var constantValue = context.SemanticModel.GetConstantValue(expression, context.CancellationToken);
        if (constantValue.HasValue && constantValue.Value == null)
        {
            term = new SymbolicStringConstantTerm(string.Empty);
            return true;
        }

        if (!TryLowerStringTerm(expression, context, out term)) return false;

        if (term is SymbolicStringContentTerm stringContent)
            term = new SymbolicConditionalTerm(
                CreateReferenceIsNullCondition(stringContent.Reference, expression),
                new SymbolicStringConstantTerm(string.Empty),
                stringContent);

        return true;
    }

    private static bool TryLowerStringPredicateArgument(
        ExpressionSyntax expression,
        ITypeSymbol parameterType,
        SymbolicLoweringContext context,
        out SymbolicTerm argument)
    {
        if (parameterType.SpecialType == SpecialType.System_String)
            return TryLowerStringTerm(expression, context, out argument);

        if (parameterType.SpecialType == SpecialType.System_Char)
        {
            var constantValue = context.SemanticModel.GetConstantValue(expression, context.CancellationToken);
            if (constantValue.HasValue && constantValue.Value is char charValue)
            {
                argument = new SymbolicStringConstantTerm(charValue.ToString());
                return true;
            }
        }

        argument = null!;
        return false;
    }

    private static bool TryLowerStringValueWithOptionalReference(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out SymbolicTerm stringValue,
        out SymbolicTerm? reference)
    {
        expression = UnwrapExpression(expression);
        reference = null;

        if (!IsStringExpression(expression, context))
        {
            stringValue = null!;
            return false;
        }

        if (TryLowerTerm(expression, context, out var direct))
        {
            if (direct.Kind == SmtValueKind.Reference)
            {
                reference = direct;
                stringValue = new SymbolicStringContentTerm(direct);
                return true;
            }

            if (direct.Kind == SmtValueKind.String)
            {
                stringValue = direct;
                return true;
            }
        }

        return TryLowerStringTerm(expression, context, out stringValue);
    }

    private static bool TryLowerStringConcatInvocationTerm(
        InvocationExpressionSyntax invocation,
        SymbolicLoweringContext context,
        out SymbolicTerm term)
    {
        term = null!;
        if (context.SemanticModel.GetOperation(invocation, context.CancellationToken) is not IInvocationOperation
                operation ||
            !string.Equals(operation.TargetMethod.Name, nameof(string.Concat), StringComparison.Ordinal) ||
            !string.Equals(operation.TargetMethod.ContainingType?.ToDisplayString(), "string",
                StringComparison.Ordinal))
            return false;

        var parts = ImmutableArray.CreateBuilder<SymbolicTerm>();
        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            if (!TryLowerStringConcatOperand(argument.Expression, context, out var part)) return false;

            parts.Add(part);
        }

        return TryCombineStringTerms(parts.ToImmutable(), out term);
    }

    private static bool TryLowerInterpolatedStringTerm(
        InterpolatedStringExpressionSyntax interpolatedString,
        SymbolicLoweringContext context,
        out SymbolicTerm term)
    {
        var parts = ImmutableArray.CreateBuilder<SymbolicTerm>();
        foreach (var content in interpolatedString.Contents)
            switch (content)
            {
                case InterpolatedStringTextSyntax text:
                    parts.Add(new SymbolicStringConstantTerm(text.TextToken.ValueText));
                    break;
                case InterpolationSyntax interpolation:
                    if (interpolation.AlignmentClause != null ||
                        interpolation.FormatClause != null ||
                        !TryLowerStringConcatOperand(interpolation.Expression, context, out var part))
                    {
                        term = null!;
                        return false;
                    }

                    parts.Add(part);
                    break;
                default:
                    term = null!;
                    return false;
            }

        return TryCombineStringTerms(parts.ToImmutable(), out term);
    }

    private static bool TryCombineStringTerms(ImmutableArray<SymbolicTerm> parts, out SymbolicTerm term)
    {
        if (parts.Length == 0)
        {
            term = new SymbolicStringConstantTerm(string.Empty);
            return true;
        }

        term = parts[0];
        for (var index = 1; index < parts.Length; index++) term = new SymbolicStringConcatTerm(term, parts[index]);

        return true;
    }

    private static bool TryGetRegexOptions(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out RegexOptions options)
    {
        var constantValue = context.SemanticModel.GetConstantValue(expression, context.CancellationToken);
        if (constantValue.HasValue &&
            constantValue.Value != null &&
            TryGetIntegralConstant(constantValue.Value, out var rawOptions))
        {
            options = (RegexOptions)rawOptions;
            return true;
        }

        options = RegexOptions.None;
        return false;
    }

    private static bool CanEncodeRegexOptions(RegexOptions options)
    {
        const RegexOptions supportedOptions =
            RegexOptions.ExplicitCapture |
            RegexOptions.Compiled |
            RegexOptions.CultureInvariant |
            RegexOptions.Singleline |
            RegexOptions.Multiline |
            RegexOptions.IgnorePatternWhitespace |
            RegexOptions.IgnoreCase;

        if ((options & ~supportedOptions) != 0) return false;

        return (options & RegexOptions.IgnoreCase) == 0 ||
               (options & RegexOptions.CultureInvariant) != 0;
    }

    private static bool IsOrdinalStringComparisonArgument(
        ExpressionSyntax expression,
        SymbolicLoweringContext context)
    {
        var type = context.SemanticModel.GetTypeInfo(expression, context.CancellationToken).Type;
        if (!string.Equals(type?.ToDisplayString(), "System.StringComparison", StringComparison.Ordinal)) return false;

        var constantValue = context.SemanticModel.GetConstantValue(expression, context.CancellationToken);
        return constantValue.HasValue &&
               constantValue.Value != null &&
               TryGetIntegralConstant(constantValue.Value, out var rawComparison) &&
               rawComparison == (int)StringComparison.Ordinal;
    }

    private static bool IsStringExpression(
        ExpressionSyntax expression,
        SymbolicLoweringContext context)
    {
        var type = context.SemanticModel.GetTypeInfo(expression, context.CancellationToken).Type;
        return type?.SpecialType == SpecialType.System_String;
    }

    private static bool TryLowerStringStaticValueMember(ISymbol? memberSymbol, out SymbolicTerm term)
    {
        if (memberSymbol is IFieldSymbol
            {
                IsStatic: true,
                Name: nameof(string.Empty),
                Type.SpecialType: SpecialType.System_String
            } stringField &&
            IsSystemStringType(stringField.ContainingType))
        {
            term = new SymbolicStringConstantTerm(string.Empty);
            return true;
        }

        term = null!;
        return false;
    }

    private static bool IsSystemStringType(ITypeSymbol? type)
    {
        return type?.SpecialType == SpecialType.System_String ||
               (type is INamedTypeSymbol namedType &&
                string.Equals(namedType.MetadataName, "String", StringComparison.Ordinal) &&
                string.Equals(namedType.ContainingNamespace?.ToDisplayString(), "System", StringComparison.Ordinal));
    }
}
using System.Text.RegularExpressions;

namespace SharpProof.Symbolic.Ir;

internal static class SymbolicStringLowerer
{
    internal static bool TryLowerStringPredicateInvocation(
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

        var ignoreCase = false;
        if (invocation.ArgumentList.Arguments.Count == 2 &&
            !TryGetOrdinalStringComparison(
                invocation.ArgumentList.Arguments[1].Expression,
                context,
                out ignoreCase))
            return false;

        if (predicate != SymbolicStringPredicateKind.Contains &&
            method.Parameters[0].Type.SpecialType != SpecialType.System_Char &&
            argument is not SymbolicStringConstantTerm)
            return false;

        if (ignoreCase)
        {
            if (argument is not SymbolicStringConstantTerm constantArgument) return false;
            var pattern = predicate.Value switch
            {
                SymbolicStringPredicateKind.StartsWith => @"\A" + Regex.Escape(constantArgument.Value),
                SymbolicStringPredicateKind.EndsWith => Regex.Escape(constantArgument.Value) + @"\z",
                _ => Regex.Escape(constantArgument.Value)
            };
            condition = SymbolicIrLowerer.CreateFactCondition(
                new SymbolicStringPredicateAtom(
                    SymbolicStringPredicateKind.RegexMatch,
                    receiver,
                    new SymbolicStringConstantTerm(pattern),
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                invocation,
                "ir.known-api.string." + method.Name + ".ordinal-ignore-case");
        }
        else
        {
            condition = SymbolicIrLowerer.CreateFactCondition(
                new SymbolicStringPredicateAtom(
                    predicate.Value,
                    receiver,
                    argument),
                invocation,
                "ir.known-api.string." + method.Name);
        }
        return true;
    }

    internal static bool TryLowerRegexIsMatchInvocation(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        SymbolicLoweringContext context,
        out SymbolicCondition condition)
    {
        condition = null!;
        return string.Equals(method.Name, nameof(Regex.IsMatch), StringComparison.Ordinal) &&
               SymbolicRegexLowerer.TryLowerRegexInvocationPredicate(invocation, context, out condition);
    }

    internal static bool TryLowerStringEqualsInvocation(
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

            var ignoreCase = false;
            if (invocation.ArgumentList.Arguments.Count == 2 &&
                !TryGetOrdinalStringComparison(
                    invocation.ArgumentList.Arguments[1].Expression,
                    context,
                    out ignoreCase))
                return false;

            if (ignoreCase)
                return TryCreateOrdinalIgnoreCaseStringEqualityCondition(
                    memberAccess.Expression,
                    invocation.ArgumentList.Arguments[0].Expression,
                    invocation,
                    context,
                    out condition);

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

        var staticIgnoreCase = false;
        if (invocation.ArgumentList.Arguments.Count == 3 &&
            !TryGetOrdinalStringComparison(
                invocation.ArgumentList.Arguments[2].Expression,
                context,
                out staticIgnoreCase))
            return false;

        if (staticIgnoreCase)
            return TryCreateOrdinalIgnoreCaseStringEqualityCondition(
                invocation.ArgumentList.Arguments[0].Expression,
                invocation.ArgumentList.Arguments[1].Expression,
                invocation,
                context,
                out condition);

        return TryCreateStringEqualityCondition(
            invocation.ArgumentList.Arguments[0].Expression,
            invocation.ArgumentList.Arguments[1].Expression,
            invocation,
            context,
            "ir.known-api.string.equals",
            out condition);
    }

    internal static bool TryLowerStringEqualityCondition(
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

    private static bool TryCreateOrdinalIgnoreCaseStringEqualityCondition(
        ExpressionSyntax leftExpression,
        ExpressionSyntax rightExpression,
        SyntaxNode sourceNode,
        SymbolicLoweringContext context,
        out SymbolicCondition condition)
    {
        condition = null!;
        ExpressionSyntax subjectExpression;
        string constant;
        var rightConstant = context.SemanticModel.GetConstantValue(rightExpression, context.CancellationToken);
        if (rightConstant is { HasValue: true, Value: string rightString })
        {
            subjectExpression = leftExpression;
            constant = rightString;
        }
        else
        {
            var leftConstant = context.SemanticModel.GetConstantValue(leftExpression, context.CancellationToken);
            if (leftConstant is not { HasValue: true, Value: string leftString }) return false;
            subjectExpression = rightExpression;
            constant = leftString;
        }

        if (!TryLowerStringValueWithOptionalReference(
                subjectExpression,
                context,
                out var subject,
                out var reference))
            return false;

        var matches = SymbolicIrLowerer.CreateFactCondition(
            new SymbolicStringPredicateAtom(
                SymbolicStringPredicateKind.RegexMatch,
                subject,
                new SymbolicStringConstantTerm(@"\A" + Regex.Escape(constant) + @"\z"),
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            sourceNode,
            "ir.string.equals.ordinal-ignore-case");
        condition = reference == null
            ? matches
            : new SymbolicBinaryCondition(
                SymbolicConditionOperator.And,
                SymbolicIrLowerer.CreateRelationCondition(
                    SymbolicRelationOperator.NotEqual,
                    reference,
                    new SymbolicNullTerm(),
                    subjectExpression,
                    "ir.string.equals.ordinal-ignore-case.non-null"),
                matches);
        return true;
    }

    internal static bool TryLowerStringSearchComparison(
        BinaryExpressionSyntax comparison,
        SymbolicLoweringContext context,
        out SymbolicCondition condition)
    {
        condition = null!;
        var comparisonKind = comparison.Kind();
        if (TryLowerStringSearchComparisonOperand(
                comparison.Left,
                comparison.Right,
                comparisonKind,
                context,
                out condition))
            return true;

        return TryLowerStringSearchComparisonOperand(
            comparison.Right,
            comparison.Left,
            ReverseStringComparisonKind(comparisonKind),
            context,
            out condition);
    }

    private static bool TryLowerStringSearchComparisonOperand(
        ExpressionSyntax searchResultExpression,
        ExpressionSyntax constantExpression,
        SyntaxKind comparisonKind,
        SymbolicLoweringContext context,
        out SymbolicCondition condition)
    {
        condition = null!;
        var constantValue = context.SemanticModel.GetConstantValue(constantExpression, context.CancellationToken);
        if (!constantValue.HasValue ||
            constantValue.Value == null ||
            !SymbolicLoweringValueFacts.TryGetIntegralConstant(constantValue.Value, out var comparisonConstant) ||
            !TryClassifyStringSearchComparison(comparisonKind, comparisonConstant, out var found) ||
            !TryLowerStringSearchPredicate(searchResultExpression, context, out var predicate))
            return false;

        condition = found ? predicate : new SymbolicNotCondition(predicate);
        return true;
    }

    private static bool TryLowerStringSearchPredicate(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out SymbolicCondition condition)
    {
        condition = null!;
        expression = SymbolicLoweringValueFacts.UnwrapExpression(expression);
        if (expression is not InvocationExpressionSyntax invocation ||
            context.SemanticModel.GetOperation(invocation, context.CancellationToken) is not
                IInvocationOperation
                {
                    Instance.Syntax: ExpressionSyntax receiverExpression
                } operation ||
            operation.TargetMethod is not
            {
                IsStatic: false,
                Name: "IndexOf" or "LastIndexOf",
                ReturnType.SpecialType: SpecialType.System_Int32,
                ContainingType.SpecialType: SpecialType.System_String
            } method ||
            operation.Arguments.Length == 0 ||
            operation.Arguments[0].Value.Syntax is not ExpressionSyntax searchExpression ||
            !TryLowerStringTerm(receiverExpression, context, out var receiver) ||
            !TryLowerStringPredicateArgument(searchExpression, method.Parameters[0].Type, context, out var search))
            return false;

        var isCharacterDefault = method.Parameters.Length == 1 &&
                                 method.Parameters[0].Type.SpecialType == SpecialType.System_Char;
        var isIgnoreCase = false;
        var hasOrdinalComparison = method.Parameters.Length == 2 &&
                                   TryGetOrdinalStringComparison(
                                       operation.Arguments[1].Value.Syntax as ExpressionSyntax ??
                                       invocation.ArgumentList.Arguments[1].Expression,
                                       context,
                                       out isIgnoreCase);
        if (!isCharacterDefault && !hasOrdinalComparison) return false;

        if (isIgnoreCase)
        {
            if (search is not SymbolicStringConstantTerm constantSearch) return false;
            condition = SymbolicIrLowerer.CreateFactCondition(
                new SymbolicStringPredicateAtom(
                    SymbolicStringPredicateKind.RegexMatch,
                    receiver,
                    new SymbolicStringConstantTerm(Regex.Escape(constantSearch.Value)),
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                invocation,
                "ir.string-search.ordinal-ignore-case");
            return true;
        }

        condition = SymbolicIrLowerer.CreateFactCondition(
            new SymbolicStringPredicateAtom(SymbolicStringPredicateKind.Contains, receiver, search),
            invocation,
            "ir.string-search.ordinal");
        return true;
    }

    private static bool TryClassifyStringSearchComparison(
        SyntaxKind comparisonKind,
        long constant,
        out bool found)
    {
        found = false;
        switch (comparisonKind)
        {
            case SyntaxKind.EqualsExpression when constant == -1:
            case SyntaxKind.LessThanExpression when constant == 0:
            case SyntaxKind.LessThanOrEqualExpression when constant == -1:
                return true;
            case SyntaxKind.NotEqualsExpression when constant == -1:
            case SyntaxKind.GreaterThanExpression when constant == -1:
            case SyntaxKind.GreaterThanOrEqualExpression when constant == 0:
                found = true;
                return true;
            default:
                return false;
        }
    }

    internal static SyntaxKind ReverseStringComparisonKind(SyntaxKind kind)
    {
        return kind switch
        {
            SyntaxKind.LessThanExpression => SyntaxKind.GreaterThanExpression,
            SyntaxKind.LessThanOrEqualExpression => SyntaxKind.GreaterThanOrEqualExpression,
            SyntaxKind.GreaterThanExpression => SyntaxKind.LessThanExpression,
            SyntaxKind.GreaterThanOrEqualExpression => SyntaxKind.LessThanOrEqualExpression,
            _ => kind
        };
    }

    internal static bool TryLowerPrefixSubstringComparison(
        BinaryExpressionSyntax comparison,
        SymbolicLoweringContext context,
        out SymbolicCondition condition)
    {
        condition = null!;
        if (!comparison.IsKind(SyntaxKind.EqualsExpression) &&
            !comparison.IsKind(SyntaxKind.NotEqualsExpression))
            return false;

        if (!TryGetPrefixSubstringParts(comparison.Left, comparison.Right, context, out var receiver, out var prefix) &&
            !TryGetPrefixSubstringParts(comparison.Right, comparison.Left, context, out receiver, out prefix))
            return false;

        if (!TryLowerStringTerm(receiver, context, out var receiverTerm)) return false;
        condition = SymbolicIrLowerer.CreateFactCondition(
            new SymbolicStringPredicateAtom(
                SymbolicStringPredicateKind.StartsWith,
                receiverTerm,
                new SymbolicStringConstantTerm(prefix)),
            comparison,
            "ir.string.substring-prefix");
        if (comparison.IsKind(SyntaxKind.NotEqualsExpression)) condition = new SymbolicNotCondition(condition);
        return true;
    }

    private static bool TryGetPrefixSubstringParts(
        ExpressionSyntax substringExpression,
        ExpressionSyntax prefixExpression,
        SymbolicLoweringContext context,
        out ExpressionSyntax receiver,
        out string prefix)
    {
        receiver = null!;
        prefix = string.Empty;
        var constantPrefix = context.SemanticModel.GetConstantValue(prefixExpression, context.CancellationToken);
        if (constantPrefix is not { HasValue: true, Value: string prefixValue }) return false;

        substringExpression = SymbolicLoweringValueFacts.UnwrapExpression(substringExpression);
        if (substringExpression is not InvocationExpressionSyntax invocation ||
            context.SemanticModel.GetOperation(invocation, context.CancellationToken) is not
                IInvocationOperation
                {
                    Instance.Syntax: ExpressionSyntax receiverExpression,
                    TargetMethod:
                    {
                        IsStatic: false,
                        Name: "Substring",
                        ContainingType.SpecialType: SpecialType.System_String
                    },
                    Arguments.Length: 2
                } operation ||
            operation.Arguments[0].Value.Syntax is not ExpressionSyntax startExpression ||
            operation.Arguments[1].Value.Syntax is not ExpressionSyntax lengthExpression)
            return false;

        var start = context.SemanticModel.GetConstantValue(startExpression, context.CancellationToken);
        var length = context.SemanticModel.GetConstantValue(lengthExpression, context.CancellationToken);
        if (!start.HasValue ||
            start.Value == null ||
            !SymbolicLoweringValueFacts.TryGetIntegralConstant(start.Value, out var startValue) ||
            startValue != 0 ||
            !length.HasValue ||
            length.Value == null ||
            !SymbolicLoweringValueFacts.TryGetIntegralConstant(length.Value, out var lengthValue) ||
            lengthValue != prefixValue.Length)
            return false;

        receiver = receiverExpression;
        prefix = prefixValue;
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

        var valuesEqual = SymbolicIrLowerer.CreateRelationCondition(
            SymbolicRelationOperator.Equal,
            leftValue,
            rightValue,
            sourceNode,
            provenancePrefix + ".value");
        if (SymbolicReferenceLowerer.TryLowerReferenceTerm(leftExpression, context, out var leftReference) &&
            SymbolicReferenceLowerer.TryLowerReferenceTerm(rightExpression, context, out var rightReference))
        {
            var bothNull = new SymbolicBinaryCondition(
                SymbolicConditionOperator.And,
                SymbolicIrLowerer.CreateRelationCondition(
                    SymbolicRelationOperator.Equal,
                    leftReference,
                    new SymbolicNullTerm(),
                    leftExpression,
                    provenancePrefix + ".left-null"),
                SymbolicIrLowerer.CreateRelationCondition(
                    SymbolicRelationOperator.Equal,
                    rightReference,
                    new SymbolicNullTerm(),
                    rightExpression,
                    provenancePrefix + ".right-null"));
            var bothNonNull = new SymbolicBinaryCondition(
                SymbolicConditionOperator.And,
                SymbolicIrLowerer.CreateRelationCondition(
                    SymbolicRelationOperator.NotEqual,
                    leftReference,
                    new SymbolicNullTerm(),
                    leftExpression,
                    provenancePrefix + ".left-not-null"),
                SymbolicIrLowerer.CreateRelationCondition(
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

        if (SymbolicReferenceLowerer.TryLowerReferenceTerm(leftExpression, context, out leftReference) &&
            rightValue is SymbolicStringConstantTerm)
        {
            condition = new SymbolicBinaryCondition(
                SymbolicConditionOperator.And,
                SymbolicIrLowerer.CreateRelationCondition(
                    SymbolicRelationOperator.NotEqual,
                    leftReference,
                    new SymbolicNullTerm(),
                    leftExpression,
                    provenancePrefix + ".left-not-null"),
                valuesEqual);
            return true;
        }

        if (SymbolicReferenceLowerer.TryLowerReferenceTerm(rightExpression, context, out rightReference) &&
            leftValue is SymbolicStringConstantTerm)
        {
            condition = new SymbolicBinaryCondition(
                SymbolicConditionOperator.And,
                SymbolicIrLowerer.CreateRelationCondition(
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

    internal static bool TryLowerStringNullOrPredicateInvocation(
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
            predicateCondition = SymbolicIrLowerer.CreateFactCondition(
                new SymbolicRelationAtom(
                    SymbolicRelationOperator.Equal,
                    new SymbolicLengthTerm(stringValue),
                    new SymbolicIntegerConstantTerm(0)),
                invocation,
                "ir.known-api.string.is-null-or-empty.empty");
        else if (string.Equals(method.Name, nameof(string.IsNullOrWhiteSpace), StringComparison.Ordinal))
            predicateCondition = SymbolicIrLowerer.CreateFactCondition(
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
                SymbolicIrLowerer.CreateReferenceIsNullCondition(reference, invocation.ArgumentList.Arguments[0].Expression),
                predicateCondition);
        return true;
    }

    internal static bool TryLowerStringExpressionTerm(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out SymbolicTerm term)
    {
        expression = SymbolicLoweringValueFacts.UnwrapExpression(expression);

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
        expression = SymbolicLoweringValueFacts.UnwrapExpression(expression);

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
            SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerCondition(conditionalExpression.Condition, context), out var branchCondition) &&
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
                    condition = SymbolicIrLowerer.CreateReferenceNullCondition(
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
        expression = SymbolicLoweringValueFacts.UnwrapExpression(expression);

        if (expression is CastExpressionSyntax castExpression &&
            context.SemanticModel.GetTypeInfo(castExpression.Type, context.CancellationToken).Type?.SpecialType ==
            SpecialType.System_String)
        {
            if (TryLowerStringTerm(castExpression.Expression, context, out term)) return true;

            if (SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerTerm(castExpression.Expression, context), out var castReference) &&
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
            !SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerTerm(expression, context), out var reference))
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
        expression = SymbolicLoweringValueFacts.UnwrapExpression(expression);

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
                SymbolicIrLowerer.CreateReferenceIsNullCondition(stringContent.Reference, expression),
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
        expression = SymbolicLoweringValueFacts.UnwrapExpression(expression);
        reference = null;

        if (!IsStringExpression(expression, context))
        {
            stringValue = null!;
            return false;
        }

        if (SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerTerm(expression, context), out var direct))
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

    internal static bool TryGetRegexOptions(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out RegexOptions options)
    {
        var constantValue = context.SemanticModel.GetConstantValue(expression, context.CancellationToken);
        if (constantValue.HasValue &&
            constantValue.Value != null &&
            SymbolicLoweringValueFacts.TryGetIntegralConstant(constantValue.Value, out var rawOptions))
        {
            options = (RegexOptions)rawOptions;
            return CanRepresentRegexOptions(options);
        }

        options = RegexOptions.None;
        return false;
    }

    internal static bool CanRepresentRegexOptions(RegexOptions options)
    {
        return SmtRegexSemantics.CanPreserveOptions(options);
    }

    private static bool TryGetOrdinalStringComparison(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out bool ignoreCase)
    {
        ignoreCase = false;
        var type = context.SemanticModel.GetTypeInfo(expression, context.CancellationToken).Type;
        if (type is not INamedTypeSymbol namedType ||
            !string.Equals(
                SymbolicTypeFacts.GetFullMetadataName(namedType),
                "System.StringComparison",
                StringComparison.Ordinal))
            return false;

        var constantValue = context.SemanticModel.GetConstantValue(expression, context.CancellationToken);
        if (!constantValue.HasValue ||
            constantValue.Value == null ||
            !SymbolicLoweringValueFacts.TryGetIntegralConstant(constantValue.Value, out var rawComparison))
            return false;

        ignoreCase = rawComparison == (int)StringComparison.OrdinalIgnoreCase;
        return ignoreCase || rawComparison == (int)StringComparison.Ordinal;
    }

    private static bool IsStringExpression(
        ExpressionSyntax expression,
        SymbolicLoweringContext context)
    {
        var type = context.SemanticModel.GetTypeInfo(expression, context.CancellationToken).Type;
        return type?.SpecialType == SpecialType.System_String;
    }

    internal static bool TryLowerStringStaticValueMember(ISymbol? memberSymbol, out SymbolicTerm term)
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

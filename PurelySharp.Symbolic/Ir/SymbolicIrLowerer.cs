using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SearchLib.Smt;

namespace PurelySharp.Symbolic.Ir
{
    internal static partial class SymbolicIrLowerer
    {
        private static readonly ImmutableArray<KnownApiLoweringDescriptor> KnownApiLowerings =
            ImmutableArray.Create(
                new KnownApiLoweringDescriptor("object", nameof(object.ReferenceEquals), TryLowerObjectReferenceEqualsInvocation),
                new KnownApiLoweringDescriptor("string", nameof(string.Contains), TryLowerStringPredicateInvocation),
                new KnownApiLoweringDescriptor("string", nameof(string.StartsWith), TryLowerStringPredicateInvocation),
                new KnownApiLoweringDescriptor("string", nameof(string.EndsWith), TryLowerStringPredicateInvocation),
                new KnownApiLoweringDescriptor("string", nameof(string.IsNullOrEmpty), TryLowerStringNullOrPredicateInvocation),
                new KnownApiLoweringDescriptor("string", nameof(string.IsNullOrWhiteSpace), TryLowerStringNullOrPredicateInvocation),
                new KnownApiLoweringDescriptor("string", nameof(string.Equals), TryLowerStringEqualsInvocation),
                new KnownApiLoweringDescriptor("System.Text.RegularExpressions.Regex", nameof(Regex.IsMatch), TryLowerRegexIsMatchInvocation));

        public static bool TryLowerCondition(
            ExpressionSyntax expression,
            SymbolicLoweringContext context,
            out SymbolicCondition condition)
        {
            expression = UnwrapExpression(expression);
            context.CancellationToken.ThrowIfCancellationRequested();

            var constantValue = context.SemanticModel.GetConstantValue(expression, context.CancellationToken);
            if (constantValue.HasValue && constantValue.Value is bool booleanValue)
            {
                condition = new SymbolicConstantCondition(booleanValue);
                return true;
            }

            if (expression is PrefixUnaryExpressionSyntax prefixUnary &&
                prefixUnary.IsKind(SyntaxKind.LogicalNotExpression) &&
                TryLowerCondition(prefixUnary.Operand, context, out var operand))
            {
                condition = new SymbolicNotCondition(operand);
                return true;
            }

            if (expression is BinaryExpressionSyntax binaryExpression)
            {
                if (binaryExpression.IsKind(SyntaxKind.IsExpression) &&
                    binaryExpression.Right is TypeSyntax typeSyntax &&
                    TryLowerTypeTestCondition(binaryExpression.Left, typeSyntax, binaryExpression, negate: false, context, out condition))
                {
                    return true;
                }

                if (binaryExpression.IsKind(SyntaxKind.LogicalAndExpression) &&
                    TryLowerCondition(binaryExpression.Left, context, out var leftAnd) &&
                    TryLowerCondition(binaryExpression.Right, context, out var rightAnd))
                {
                    condition = new SymbolicBinaryCondition(SymbolicConditionOperator.And, leftAnd, rightAnd);
                    return true;
                }

                if (binaryExpression.IsKind(SyntaxKind.LogicalOrExpression) &&
                    TryLowerCondition(binaryExpression.Left, context, out var leftOr) &&
                    TryLowerCondition(binaryExpression.Right, context, out var rightOr))
                {
                    condition = new SymbolicBinaryCondition(SymbolicConditionOperator.Or, leftOr, rightOr);
                    return true;
                }

                if (IsEqualityExpression(binaryExpression) &&
                    TryLowerStringEqualityCondition(binaryExpression, context, out condition))
                {
                    return true;
                }

                if (IsEqualityExpression(binaryExpression) &&
                    TryLowerTupleEqualityCondition(binaryExpression, context, out condition))
                {
                    return true;
                }

                if (TryGetRelationOperator(binaryExpression.Kind(), out var relationOperator) &&
                    TryLowerTerm(binaryExpression.Left, context, out var left) &&
                    TryLowerTerm(binaryExpression.Right, context, out var right) &&
                    CanCompareTerms(left, right, relationOperator))
                {
                    condition = CreateFactCondition(
                        new SymbolicRelationAtom(relationOperator, left, right),
                        binaryExpression,
                        "ir.relation");
                    return true;
                }
            }

            if (expression is IsPatternExpressionSyntax isPatternExpression &&
                (TryLowerBinaryPatternCondition(isPatternExpression, context, out condition) ||
                    TryLowerNullPatternCondition(isPatternExpression, context, out condition) ||
                    TryLowerConstantPatternCondition(isPatternExpression, context, out condition) ||
                    TryLowerRelationalPatternCondition(isPatternExpression, context, out condition) ||
                    TryLowerEmptyRecursivePatternCondition(isPatternExpression, context, out condition) ||
                    TryLowerTypePatternCondition(isPatternExpression, context, out condition) ||
                    TryLowerUnaryPatternCondition(isPatternExpression.Expression, isPatternExpression.Pattern, context, out condition)))
            {
                return true;
            }

            if (expression is InvocationExpressionSyntax invocation &&
                TryLowerKnownApiInvocation(invocation, context, out condition))
            {
                return true;
            }

            if (TryLowerTerm(expression, context, out var term) &&
                term.Kind == SmtValueKind.Bool)
            {
                condition = CreateFactCondition(new SymbolicTruthAtom(term), expression, "ir.truth");
                return true;
            }

            condition = null!;
            return false;
        }

        private static bool TryLowerBinaryPatternCondition(
            IsPatternExpressionSyntax expression,
            SymbolicLoweringContext context,
            out SymbolicCondition condition)
        {
            return TryLowerBinaryPatternCondition(expression.Expression, expression.Pattern, context, out condition);
        }

        private static bool TryLowerPatternCondition(
            ExpressionSyntax expression,
            PatternSyntax pattern,
            SyntaxNode sourceNode,
            SymbolicLoweringContext context,
            out SymbolicCondition condition)
        {
            return TryLowerBinaryPatternCondition(expression, pattern, context, out condition) ||
                TryLowerNullPatternCondition(expression, pattern, sourceNode, context, out condition) ||
                TryLowerConstantPatternCondition(expression, pattern, sourceNode, context, out condition) ||
                TryLowerRelationalPatternCondition(expression, pattern, sourceNode, context, out condition) ||
                TryLowerEmptyRecursivePatternCondition(expression, pattern, sourceNode, context, out condition) ||
                TryLowerTypePatternCondition(expression, pattern, sourceNode, context, out condition) ||
                TryLowerUnaryPatternCondition(expression, pattern, context, out condition);
        }

        private static bool TryLowerBinaryPatternCondition(
            ExpressionSyntax expression,
            PatternSyntax pattern,
            SymbolicLoweringContext context,
            out SymbolicCondition condition)
        {
            condition = null!;
            if (UnwrapPattern(pattern) is not BinaryPatternSyntax binaryPattern ||
                !TryLowerPatternCondition(expression, binaryPattern.Left, binaryPattern.Left, context, out var left) ||
                !TryLowerPatternCondition(expression, binaryPattern.Right, binaryPattern.Right, context, out var right))
            {
                return false;
            }

            if (binaryPattern.OperatorToken.IsKind(SyntaxKind.AndKeyword))
            {
                condition = new SymbolicBinaryCondition(SymbolicConditionOperator.And, left, right);
                return true;
            }

            if (binaryPattern.OperatorToken.IsKind(SyntaxKind.OrKeyword))
            {
                condition = new SymbolicBinaryCondition(SymbolicConditionOperator.Or, left, right);
                return true;
            }

            return false;
        }

        private static bool TryLowerUnaryPatternCondition(
            ExpressionSyntax expression,
            PatternSyntax pattern,
            SymbolicLoweringContext context,
            out SymbolicCondition condition)
        {
            condition = null!;
            if (UnwrapPattern(pattern) is not UnaryPatternSyntax unaryPattern ||
                !unaryPattern.IsKind(SyntaxKind.NotPattern) ||
                !TryLowerPatternCondition(expression, unaryPattern.Pattern, unaryPattern.Pattern, context, out var operand))
            {
                return false;
            }

            condition = new SymbolicNotCondition(operand);
            return true;
        }

        private static bool TryLowerNullPatternCondition(
            IsPatternExpressionSyntax expression,
            SymbolicLoweringContext context,
            out SymbolicCondition condition)
        {
            return TryLowerNullPatternCondition(expression.Expression, expression.Pattern, expression, context, out condition);
        }

        private static bool TryLowerNullPatternCondition(
            ExpressionSyntax expression,
            PatternSyntax pattern,
            SyntaxNode sourceNode,
            SymbolicLoweringContext context,
            out SymbolicCondition condition)
        {
            condition = null!;
            if (!TryLowerNullPattern(pattern, context, out var negate) ||
                !TryLowerTerm(expression, context, out var value) ||
                value.Kind != SmtValueKind.Reference)
            {
                return false;
            }

            condition = CreateRelationCondition(
                negate ? SymbolicRelationOperator.NotEqual : SymbolicRelationOperator.Equal,
                value,
                new SymbolicNullTerm(),
                sourceNode,
                "ir.pattern.null");
            return true;
        }

        private static bool TryLowerNullPattern(
            PatternSyntax pattern,
            SymbolicLoweringContext context,
            out bool negate)
        {
            pattern = UnwrapPattern(pattern);
            negate = false;

            if (pattern is ConstantPatternSyntax constantPattern &&
                context.SemanticModel.GetConstantValue(constantPattern.Expression, context.CancellationToken) is { HasValue: true, Value: null })
            {
                return true;
            }

            if (pattern is UnaryPatternSyntax unaryPattern &&
                unaryPattern.IsKind(SyntaxKind.NotPattern) &&
                TryLowerNullPattern(unaryPattern.Pattern, context, out var nestedNegate))
            {
                negate = !nestedNegate;
                return true;
            }

            return false;
        }

        private static bool TryLowerConstantPatternCondition(
            IsPatternExpressionSyntax expression,
            SymbolicLoweringContext context,
            out SymbolicCondition condition)
        {
            return TryLowerConstantPatternCondition(expression.Expression, expression.Pattern, expression, context, out condition);
        }

        private static bool TryLowerConstantPatternCondition(
            ExpressionSyntax expression,
            PatternSyntax pattern,
            SyntaxNode sourceNode,
            SymbolicLoweringContext context,
            out SymbolicCondition condition)
        {
            condition = null!;
            if (!TryLowerConstantPattern(pattern, out var constantExpression, out var negate) ||
                !TryLowerTerm(expression, context, out var value) ||
                !TryLowerTerm(constantExpression, context, out var constant) ||
                !CanCompareTerms(value, constant, SymbolicRelationOperator.Equal))
            {
                return false;
            }

            condition = CreateRelationCondition(
                negate ? SymbolicRelationOperator.NotEqual : SymbolicRelationOperator.Equal,
                value,
                constant,
                sourceNode,
                "ir.pattern.constant");
            return true;
        }

        private static bool TryLowerConstantPattern(
            PatternSyntax pattern,
            out ExpressionSyntax constantExpression,
            out bool negate)
        {
            pattern = UnwrapPattern(pattern);
            negate = false;

            if (pattern is ConstantPatternSyntax constantPattern &&
                !constantPattern.Expression.IsKind(SyntaxKind.NullLiteralExpression))
            {
                constantExpression = constantPattern.Expression;
                return true;
            }

            if (pattern is UnaryPatternSyntax unaryPattern &&
                unaryPattern.IsKind(SyntaxKind.NotPattern) &&
                TryLowerConstantPattern(unaryPattern.Pattern, out constantExpression, out var nestedNegate))
            {
                negate = !nestedNegate;
                return true;
            }

            constantExpression = null!;
            return false;
        }

        private static bool TryLowerRelationalPatternCondition(
            IsPatternExpressionSyntax expression,
            SymbolicLoweringContext context,
            out SymbolicCondition condition)
        {
            return TryLowerRelationalPatternCondition(expression.Expression, expression.Pattern, expression, context, out condition);
        }

        private static bool TryLowerRelationalPatternCondition(
            ExpressionSyntax expression,
            PatternSyntax pattern,
            SyntaxNode sourceNode,
            SymbolicLoweringContext context,
            out SymbolicCondition condition)
        {
            condition = null!;
            if (!TryLowerRelationalPattern(pattern, out var operatorKind, out var relationalExpression, out var negate) ||
                !TryGetRelationalPatternOperator(operatorKind, negate, out var relationOperator) ||
                !TryLowerTerm(expression, context, out var value) ||
                value.Kind != SmtValueKind.Int ||
                !TryLowerTerm(relationalExpression, context, out var relationalValue) ||
                relationalValue.Kind != SmtValueKind.Int)
            {
                return false;
            }

            condition = CreateRelationCondition(
                relationOperator,
                value,
                relationalValue,
                sourceNode,
                "ir.pattern.relational");
            return true;
        }

        private static bool TryLowerRelationalPattern(
            PatternSyntax pattern,
            out SyntaxKind operatorKind,
            out ExpressionSyntax relationalExpression,
            out bool negate)
        {
            pattern = UnwrapPattern(pattern);
            negate = false;

            if (pattern is RelationalPatternSyntax relationalPattern)
            {
                operatorKind = relationalPattern.OperatorToken.Kind();
                relationalExpression = relationalPattern.Expression;
                return true;
            }

            if (pattern is UnaryPatternSyntax unaryPattern &&
                unaryPattern.IsKind(SyntaxKind.NotPattern) &&
                TryLowerRelationalPattern(unaryPattern.Pattern, out operatorKind, out relationalExpression, out var nestedNegate))
            {
                negate = !nestedNegate;
                return true;
            }

            operatorKind = default;
            relationalExpression = null!;
            return false;
        }

        private static bool TryGetRelationalPatternOperator(
            SyntaxKind operatorKind,
            bool negate,
            out SymbolicRelationOperator relationOperator)
        {
            relationOperator = operatorKind switch
            {
                SyntaxKind.GreaterThanToken => negate ? SymbolicRelationOperator.LessThanOrEqual : SymbolicRelationOperator.GreaterThan,
                SyntaxKind.GreaterThanEqualsToken => negate ? SymbolicRelationOperator.LessThan : SymbolicRelationOperator.GreaterThanOrEqual,
                SyntaxKind.LessThanToken => negate ? SymbolicRelationOperator.GreaterThanOrEqual : SymbolicRelationOperator.LessThan,
                SyntaxKind.LessThanEqualsToken => negate ? SymbolicRelationOperator.GreaterThan : SymbolicRelationOperator.LessThanOrEqual,
                _ => default,
            };
            return operatorKind is
                SyntaxKind.GreaterThanToken or
                SyntaxKind.GreaterThanEqualsToken or
                SyntaxKind.LessThanToken or
                SyntaxKind.LessThanEqualsToken;
        }

        private static bool TryLowerEmptyRecursivePatternCondition(
            IsPatternExpressionSyntax expression,
            SymbolicLoweringContext context,
            out SymbolicCondition condition)
        {
            return TryLowerEmptyRecursivePatternCondition(expression.Expression, expression.Pattern, expression, context, out condition);
        }

        private static bool TryLowerEmptyRecursivePatternCondition(
            ExpressionSyntax expression,
            PatternSyntax pattern,
            SyntaxNode sourceNode,
            SymbolicLoweringContext context,
            out SymbolicCondition condition)
        {
            condition = null!;
            if (!TryLowerEmptyRecursivePattern(pattern, out var negate) ||
                !TryLowerTerm(expression, context, out var value) ||
                value.Kind != SmtValueKind.Reference)
            {
                return false;
            }

            condition = CreateRelationCondition(
                negate ? SymbolicRelationOperator.Equal : SymbolicRelationOperator.NotEqual,
                value,
                new SymbolicNullTerm(),
                sourceNode,
                "ir.pattern.recursive.empty");
            return true;
        }

        private static bool TryLowerEmptyRecursivePattern(PatternSyntax pattern, out bool negate)
        {
            pattern = UnwrapPattern(pattern);
            negate = false;

            if (pattern is RecursivePatternSyntax recursivePattern &&
                recursivePattern.PropertyPatternClause is not { Subpatterns.Count: > 0 } &&
                recursivePattern.PositionalPatternClause is not { Subpatterns.Count: > 0 })
            {
                return true;
            }

            if (pattern is UnaryPatternSyntax unaryPattern &&
                unaryPattern.IsKind(SyntaxKind.NotPattern) &&
                TryLowerEmptyRecursivePattern(unaryPattern.Pattern, out var nestedNegate))
            {
                negate = !nestedNegate;
                return true;
            }

            return false;
        }

        private static bool TryLowerTypePatternCondition(
            IsPatternExpressionSyntax expression,
            SymbolicLoweringContext context,
            out SymbolicCondition condition)
        {
            return TryLowerTypePatternCondition(expression.Expression, expression.Pattern, expression.Pattern, context, out condition);
        }

        private static bool TryLowerTypePatternCondition(
            ExpressionSyntax expression,
            PatternSyntax pattern,
            SyntaxNode sourceNode,
            SymbolicLoweringContext context,
            out SymbolicCondition condition)
        {
            condition = null!;
            if (!TryLowerTypePattern(pattern, out var typeSyntax, out var negate))
            {
                return false;
            }

            return TryLowerTypeTestCondition(expression, typeSyntax, sourceNode, negate, context, out condition);
        }

        private static bool TryLowerTypeTestCondition(
            ExpressionSyntax expression,
            TypeSyntax typeSyntax,
            SyntaxNode sourceNode,
            bool negate,
            SymbolicLoweringContext context,
            out SymbolicCondition condition)
        {
            condition = null!;
            var type = context.SemanticModel.GetTypeInfo(typeSyntax, context.CancellationToken).Type;
            if (!SymbolicRuntimeTypeFacts.TryGetRuntimeTypeTestKey(type, out var typeKey) ||
                !TryLowerTerm(expression, context, out var value) ||
                value.Kind != SmtValueKind.Reference)
            {
                return false;
            }

            var nonNull = CreateRelationCondition(
                SymbolicRelationOperator.NotEqual,
                value,
                new SymbolicNullTerm(),
                expression,
                "ir.pattern.type.non-null");
            var typeTest = CreateFactCondition(
                new SymbolicTypeTestAtom(value, typeKey),
                sourceNode,
                "ir.pattern.type.test");
            var positive = new SymbolicBinaryCondition(SymbolicConditionOperator.And, nonNull, typeTest);
            condition = negate ? new SymbolicNotCondition(positive) : positive;
            return true;
        }

        private static bool TryLowerTypePattern(
            PatternSyntax pattern,
            out TypeSyntax type,
            out bool negate)
        {
            pattern = UnwrapPattern(pattern);
            negate = false;

            if (pattern is TypePatternSyntax typePattern)
            {
                type = typePattern.Type;
                return true;
            }

            if (pattern is DeclarationPatternSyntax declarationPattern)
            {
                type = declarationPattern.Type;
                return true;
            }

            if (pattern is UnaryPatternSyntax unaryPattern &&
                unaryPattern.IsKind(SyntaxKind.NotPattern) &&
                TryLowerTypePattern(unaryPattern.Pattern, out type, out var nestedNegate))
            {
                negate = !nestedNegate;
                return true;
            }

            type = null!;
            return false;
        }

        private static PatternSyntax UnwrapPattern(PatternSyntax pattern)
        {
            while (pattern is ParenthesizedPatternSyntax parenthesizedPattern)
            {
                pattern = parenthesizedPattern.Pattern;
            }

            return pattern;
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
            {
                return false;
            }

            if (binaryExpression.IsKind(SyntaxKind.NotEqualsExpression))
            {
                condition = new SymbolicNotCondition(condition);
            }

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
            {
                return false;
            }

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

        private static bool TryLowerTupleEqualityCondition(
            BinaryExpressionSyntax binaryExpression,
            SymbolicLoweringContext context,
            out SymbolicCondition condition)
        {
            condition = null!;
            if (!TryLowerTupleElementTerms(binaryExpression.Left, context, out var leftElements) ||
                !TryLowerTupleElementTerms(binaryExpression.Right, context, out var rightElements) ||
                leftElements.Length == 0 ||
                leftElements.Length != rightElements.Length)
            {
                return false;
            }

            SymbolicCondition? equality = null;
            for (var index = 0; index < leftElements.Length; index++)
            {
                if (!CanCompareTerms(leftElements[index], rightElements[index], SymbolicRelationOperator.Equal))
                {
                    return false;
                }

                var elementEquality = CreateRelationCondition(
                    SymbolicRelationOperator.Equal,
                    leftElements[index],
                    rightElements[index],
                    binaryExpression,
                    "ir.tuple.equality.element");
                equality = equality == null
                    ? elementEquality
                    : new SymbolicBinaryCondition(SymbolicConditionOperator.And, equality, elementEquality);
            }

            condition = binaryExpression.IsKind(SyntaxKind.EqualsExpression)
                ? equality!
                : new SymbolicNotCondition(equality!);
            return true;
        }

        public static bool TryLowerTerm(
            ExpressionSyntax expression,
            SymbolicLoweringContext context,
            out SymbolicTerm term)
        {
            expression = UnwrapExpression(expression);
            context.CancellationToken.ThrowIfCancellationRequested();

            var constantValue = context.SemanticModel.GetConstantValue(expression, context.CancellationToken);
            if (constantValue.HasValue)
            {
                if (constantValue.Value is bool booleanValue)
                {
                    term = new SymbolicBooleanConstantTerm(booleanValue);
                    return true;
                }

                if (constantValue.Value == null)
                {
                    term = new SymbolicNullTerm();
                    return true;
                }

                if (constantValue.Value is string stringValue)
                {
                    term = new SymbolicStringConstantTerm(stringValue);
                    return true;
                }

                if (TryGetIntegralConstant(constantValue.Value, out var integralValue))
                {
                    term = new SymbolicIntegerConstantTerm(integralValue);
                    return true;
                }
            }

            if (expression is LiteralExpressionSyntax literal &&
                literal.IsKind(SyntaxKind.NullLiteralExpression))
            {
                term = new SymbolicNullTerm();
                return true;
            }

            if (TryLowerSupportedConversionTerm(expression, context, out term))
            {
                return true;
            }

            if (expression is ThisExpressionSyntax)
            {
                term = new SymbolicVariableTerm("this", SmtValueKind.Reference);
                return true;
            }

            if (TryLowerStringExpressionTerm(expression, context, out term))
            {
                return true;
            }

            if (expression is BinaryExpressionSyntax coalesceExpression &&
                coalesceExpression.IsKind(SyntaxKind.CoalesceExpression) &&
                TryLowerTerm(coalesceExpression.Left, context, out var coalesceLeft) &&
                TryLowerTerm(coalesceExpression.Right, context, out var coalesceRight) &&
                coalesceLeft.Kind == SmtValueKind.Reference &&
                coalesceRight.Kind == SmtValueKind.Reference)
            {
                term = new SymbolicConditionalTerm(
                    CreateRelationCondition(
                        SymbolicRelationOperator.NotEqual,
                        coalesceLeft,
                        new SymbolicNullTerm(),
                        coalesceExpression.Left,
                        "ir.coalesce.left-not-null"),
                    coalesceLeft,
                    coalesceRight);
                return true;
            }

            if (expression is ConditionalExpressionSyntax conditionalExpression &&
                TryLowerCondition(conditionalExpression.Condition, context, out var condition) &&
                TryLowerTerm(conditionalExpression.WhenTrue, context, out var whenTrue) &&
                TryLowerTerm(conditionalExpression.WhenFalse, context, out var whenFalse) &&
                whenTrue.Kind == whenFalse.Kind)
            {
                term = new SymbolicConditionalTerm(condition, whenTrue, whenFalse);
                return true;
            }

            if (expression is PrefixUnaryExpressionSyntax prefixUnary &&
                prefixUnary.IsKind(SyntaxKind.UnaryMinusExpression) &&
                TryLowerTerm(prefixUnary.Operand, context, out var unaryOperand) &&
                unaryOperand.Kind == SmtValueKind.Int)
            {
                term = new SymbolicBinaryTerm(
                    SymbolicBinaryTermOperator.Subtract,
                    new SymbolicIntegerConstantTerm(0),
                    unaryOperand);
                return true;
            }

            if (expression is BinaryExpressionSyntax asExpression &&
                asExpression.IsKind(SyntaxKind.AsExpression) &&
                TryLowerIdentityPreservingAsTerm(asExpression, context, out term))
            {
                return true;
            }

            if (expression is BinaryExpressionSyntax binary &&
                TryGetBinaryTermOperator(binary.Kind(), out var binaryOperator) &&
                TryLowerTerm(binary.Left, context, out var left) &&
                TryLowerTerm(binary.Right, context, out var right) &&
                left.Kind == SmtValueKind.Int &&
                right.Kind == SmtValueKind.Int)
            {
                term = new SymbolicBinaryTerm(binaryOperator, left, right);
                return true;
            }

            if (expression is MemberAccessExpressionSyntax memberAccess &&
                TryLowerMemberTerm(memberAccess, context, out term))
            {
                return true;
            }

            var symbol = context.SemanticModel.GetSymbolInfo(expression, context.CancellationToken).Symbol;
            if ((symbol is ILocalSymbol || symbol is IParameterSymbol) &&
                TryGetSymbolType(symbol, out var symbolType) &&
                TryGetValueKind(symbolType, out var kind))
            {
                term = new SymbolicVariableTerm(context.GetVariableName(symbol), kind);
                return true;
            }

            term = null!;
            return false;
        }

        private static bool TryLowerIdentityPreservingAsTerm(
            BinaryExpressionSyntax asExpression,
            SymbolicLoweringContext context,
            out SymbolicTerm term)
        {
            term = null!;
            if (asExpression.Right is not TypeSyntax targetTypeSyntax ||
                !IsIdentityPreservingReferenceConversion(asExpression.Left, targetTypeSyntax, context) ||
                !TryLowerTerm(asExpression.Left, context, out var operand) ||
                operand.Kind != SmtValueKind.Reference)
            {
                return false;
            }

            term = operand;
            return true;
        }

        private static bool IsIdentityPreservingReferenceConversion(
            ExpressionSyntax expression,
            TypeSyntax targetTypeSyntax,
            SymbolicLoweringContext context)
        {
            var sourceType = context.SemanticModel.GetTypeInfo(expression, context.CancellationToken).Type;
            var targetType = context.SemanticModel.GetTypeInfo(targetTypeSyntax, context.CancellationToken).Type;
            if (sourceType == null ||
                targetType == null ||
                !sourceType.IsReferenceType ||
                !targetType.IsReferenceType)
            {
                return false;
            }

            if (SymbolEqualityComparer.Default.Equals(sourceType, targetType) ||
                targetType.SpecialType == SpecialType.System_Object)
            {
                return true;
            }

            if (sourceType is INamedTypeSymbol sourceNamedType)
            {
                for (var current = sourceNamedType.BaseType; current != null; current = current.BaseType)
                {
                    if (SymbolEqualityComparer.Default.Equals(current, targetType))
                    {
                        return true;
                    }
                }
            }

            foreach (var candidate in sourceType.AllInterfaces)
            {
                if (SymbolEqualityComparer.Default.Equals(candidate, targetType))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryLowerMemberTerm(
            MemberAccessExpressionSyntax memberAccess,
            SymbolicLoweringContext context,
            out SymbolicTerm term)
        {
            term = null!;

            var memberName = memberAccess.Name.Identifier.ValueText;
            if (TryLowerKnownStaticValueMember(memberAccess, context, out term))
            {
                return true;
            }

            var receiverType = context.SemanticModel.GetTypeInfo(memberAccess.Expression, context.CancellationToken).Type;
            if (TryLowerTupleElementMemberTerm(memberAccess, context, out term))
            {
                return true;
            }

            if (string.Equals(memberName, "HasValue", StringComparison.Ordinal) &&
                TryLowerNullableHasValueTerm(memberAccess.Expression, context, out term))
            {
                return true;
            }

            if (string.Equals(memberName, "Value", StringComparison.Ordinal) &&
                TryLowerNullableValueTerm(memberAccess.Expression, context, out term))
            {
                return true;
            }

            if (string.Equals(memberName, nameof(string.Length), StringComparison.Ordinal))
            {
                if (receiverType?.SpecialType == SpecialType.System_String)
                {
                    if (!TryLowerStringTerm(memberAccess.Expression, context, out var stringValue))
                    {
                        return false;
                    }

                    term = new SymbolicLengthTerm(stringValue);
                    return true;
                }

                if (receiverType is IArrayTypeSymbol { Rank: 1 } ||
                    IsBuiltInSpanOrMemoryType(receiverType))
                {
                    if (!TryLowerTerm(memberAccess.Expression, context, out var lengthReceiver))
                    {
                        return false;
                    }

                    term = new SymbolicLengthTerm(lengthReceiver);
                    return true;
                }
            }

            if (!TryLowerTerm(memberAccess.Expression, context, out var receiver))
            {
                return false;
            }

            if (string.Equals(memberName, "Count", StringComparison.Ordinal) &&
                receiver.Kind == SmtValueKind.Reference)
            {
                term = new SymbolicCountTerm(receiver);
                return true;
            }

            if (TryGetInstanceMemberValueKind(memberAccess, context, out var memberKind) &&
                receiver.Kind == SmtValueKind.Reference)
            {
                term = new SymbolicMemberTerm(receiver, memberName, memberKind);
                return true;
            }

            return false;
        }

        private static bool TryGetInstanceMemberValueKind(
            MemberAccessExpressionSyntax memberAccess,
            SymbolicLoweringContext context,
            out SmtValueKind kind)
        {
            var symbol = context.SemanticModel.GetSymbolInfo(memberAccess, context.CancellationToken).Symbol;
            if (symbol is IPropertySymbol { IsStatic: false } property &&
                TryGetValueKind(property.Type, out kind))
            {
                return true;
            }

            if (symbol is IFieldSymbol { IsStatic: false } field &&
                TryGetValueKind(field.Type, out kind))
            {
                return true;
            }

            kind = default;
            return false;
        }

        private static bool TryLowerSupportedConversionTerm(
            ExpressionSyntax expression,
            SymbolicLoweringContext context,
            out SymbolicTerm term)
        {
            if (expression is CheckedExpressionSyntax checkedExpression &&
                checkedExpression.IsKind(SyntaxKind.UncheckedExpression))
            {
                if (checkedExpression.Expression is CastExpressionSyntax)
                {
                    return TryLowerSupportedConversionTerm(checkedExpression.Expression, context, out term);
                }

                term = null!;
                return false;
            }

            if (expression is CastExpressionSyntax castExpression)
            {
                if (IsIdentityPreservingReferenceConversion(castExpression.Expression, castExpression.Type, context) &&
                    TryLowerTerm(castExpression.Expression, context, out var referenceOperand) &&
                    referenceOperand.Kind == SmtValueKind.Reference)
                {
                    term = referenceOperand;
                    return true;
                }

                var sourceType = context.SemanticModel.GetTypeInfo(castExpression.Expression, context.CancellationToken).Type;
                var targetType = context.SemanticModel.GetTypeInfo(castExpression.Type, context.CancellationToken).Type;
                if (sourceType?.TypeKind == TypeKind.Enum &&
                    sourceType is INamedTypeSymbol { EnumUnderlyingType.SpecialType: SpecialType.System_Int32 } &&
                    targetType?.SpecialType == SpecialType.System_Int32 &&
                    TryLowerTerm(castExpression.Expression, context, out var operand) &&
                    operand.Kind == SmtValueKind.Int)
                {
                    term = operand;
                    return true;
                }
            }

            term = null!;
            return false;
        }

        private static bool TryLowerTupleElementMemberTerm(
            MemberAccessExpressionSyntax memberAccess,
            SymbolicLoweringContext context,
            out SymbolicTerm term)
        {
            term = null!;
            if (!TryGetStableVariableSymbol(memberAccess.Expression, context, out var tupleSymbol) ||
                context.SemanticModel.GetSymbolInfo(memberAccess, context.CancellationToken).Symbol is not IFieldSymbol field ||
                !TryGetTupleElementStorageName(field, out var storageName) ||
                !TryGetValueKind(field.Type, out var kind))
            {
                return false;
            }

            term = new SymbolicVariableTerm(context.GetVariableName(tupleSymbol) + "." + storageName, kind);
            return true;
        }

        public static bool TryLowerNullableHasValueTerm(
            ExpressionSyntax nullableExpression,
            SymbolicLoweringContext context,
            out SymbolicTerm term)
        {
            nullableExpression = UnwrapExpression(nullableExpression);
            if (!SymbolicTypeFacts.TryGetNullableUnderlyingType(
                    context.SemanticModel.GetTypeInfo(nullableExpression, context.CancellationToken).Type,
                    out _) ||
                !TryGetStableVariableSymbol(nullableExpression, context, out var symbol))
            {
                term = null!;
                return false;
            }

            term = new SymbolicNullableHasValueTerm(context.GetVariableName(symbol));
            return true;
        }

        public static bool TryLowerNullableValueTerm(
            ExpressionSyntax nullableExpression,
            SymbolicLoweringContext context,
            out SymbolicTerm term)
        {
            nullableExpression = UnwrapExpression(nullableExpression);
            if (!SymbolicTypeFacts.TryGetNullableUnderlyingType(
                    context.SemanticModel.GetTypeInfo(nullableExpression, context.CancellationToken).Type,
                    out var underlyingType) ||
                !TryGetValueKind(underlyingType, out var valueKind) ||
                !TryGetStableVariableSymbol(nullableExpression, context, out var symbol))
            {
                term = null!;
                return false;
            }

            term = new SymbolicNullableValueTerm(context.GetVariableName(symbol), valueKind);
            return true;
        }

        public static bool TryLowerArrayDimensionLengthTerm(
            ExpressionSyntax arrayExpression,
            int dimension,
            SymbolicLoweringContext context,
            out SymbolicTerm term)
        {
            arrayExpression = UnwrapExpression(arrayExpression);
            var type = context.SemanticModel.GetTypeInfo(arrayExpression, context.CancellationToken).ConvertedType ??
                context.SemanticModel.GetTypeInfo(arrayExpression, context.CancellationToken).Type;
            if (type is not IArrayTypeSymbol arrayType ||
                dimension < 0 ||
                dimension >= arrayType.Rank ||
                !TryLowerTerm(arrayExpression, context, out var arrayTerm) ||
                arrayTerm.Kind != SmtValueKind.Reference)
            {
                term = null!;
                return false;
            }

            term = new SymbolicArrayDimensionLengthTerm(arrayTerm, dimension);
            return true;
        }

        private static bool TryGetStableVariableSymbol(
            ExpressionSyntax expression,
            SymbolicLoweringContext context,
            out ISymbol symbol)
        {
            if (expression is IdentifierNameSyntax)
            {
                symbol = context.SemanticModel.GetSymbolInfo(expression, context.CancellationToken).Symbol!;
                return symbol is ILocalSymbol or IParameterSymbol;
            }

            symbol = null!;
            return false;
        }

        private static bool TryLowerKnownApiInvocation(
            InvocationExpressionSyntax invocation,
            SymbolicLoweringContext context,
            out SymbolicCondition condition)
        {
            condition = null!;
            if (context.SemanticModel.GetOperation(invocation, context.CancellationToken) is not Microsoft.CodeAnalysis.Operations.IInvocationOperation operation)
            {
                return false;
            }

            foreach (var descriptor in KnownApiLowerings)
            {
                if (descriptor.Matches(operation.TargetMethod) &&
                    descriptor.Handler(invocation, operation.TargetMethod, context, out condition))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryLowerObjectReferenceEqualsInvocation(
            InvocationExpressionSyntax invocation,
            IMethodSymbol method,
            SymbolicLoweringContext context,
            out SymbolicCondition condition)
        {
            condition = null!;
            if (!method.IsStatic ||
                invocation.ArgumentList.Arguments.Count != 2 ||
                method.Parameters.Length != 2 ||
                !TryLowerTerm(invocation.ArgumentList.Arguments[0].Expression, context, out var left) ||
                !TryLowerTerm(invocation.ArgumentList.Arguments[1].Expression, context, out var right) ||
                !CanCompareTerms(left, right, SymbolicRelationOperator.Equal) ||
                left.Kind != SmtValueKind.Reference && right.Kind != SmtValueKind.Reference)
            {
                return false;
            }

            condition = CreateFactCondition(
                new SymbolicRelationAtom(
                    SymbolicRelationOperator.Equal,
                    left,
                    right),
                invocation,
                "ir.known-api.object.reference-equals");
            return true;
        }

        private static bool TryLowerKnownStaticValueMember(
            MemberAccessExpressionSyntax memberAccess,
            SymbolicLoweringContext context,
            out SymbolicTerm term)
        {
            if (context.SemanticModel.GetSymbolInfo(memberAccess, context.CancellationToken).Symbol is IPropertySymbol property &&
                IsBigIntegerType(property.Type))
            {
                if (string.Equals(property.Name, "Zero", StringComparison.Ordinal))
                {
                    term = new SymbolicIntegerConstantTerm(0);
                    return true;
                }

                if (string.Equals(property.Name, "One", StringComparison.Ordinal))
                {
                    term = new SymbolicIntegerConstantTerm(1);
                    return true;
                }
            }

            term = null!;
            return false;
        }

        private static SymbolicCondition CreateFactCondition(SymbolicAtom atom, SyntaxNode node, string provenance)
        {
            return new SymbolicFactCondition(SymbolicFact.Exact(atom, node, provenance));
        }

        private static SymbolicCondition CreateRelationCondition(
            SymbolicRelationOperator op,
            SymbolicTerm left,
            SymbolicTerm right,
            SyntaxNode node,
            string provenance)
        {
            return CreateFactCondition(new SymbolicRelationAtom(op, left, right), node, provenance);
        }

        private static SymbolicCondition CreateReferenceIsNullCondition(SymbolicTerm reference, SyntaxNode node)
        {
            return CreateFactCondition(
                new SymbolicRelationAtom(
                    SymbolicRelationOperator.Equal,
                    reference,
                    new SymbolicNullTerm()),
                node,
                "ir.string.concat.null-empty");
        }

        private static bool CanCompareTerms(SymbolicTerm left, SymbolicTerm right, SymbolicRelationOperator op)
        {
            if (op is not SymbolicRelationOperator.Equal and not SymbolicRelationOperator.NotEqual &&
                left.Kind != SmtValueKind.Int)
            {
                return false;
            }

            return left.Kind == right.Kind ||
                left is SymbolicNullTerm && right.Kind == SmtValueKind.Reference ||
                right is SymbolicNullTerm && left.Kind == SmtValueKind.Reference;
        }

        private static bool IsEqualityExpression(BinaryExpressionSyntax binaryExpression)
        {
            return binaryExpression.IsKind(SyntaxKind.EqualsExpression) ||
                binaryExpression.IsKind(SyntaxKind.NotEqualsExpression);
        }

        private static bool TryLowerTupleElementTerms(
            ExpressionSyntax expression,
            SymbolicLoweringContext context,
            out ImmutableArray<SymbolicTerm> terms)
        {
            terms = ImmutableArray<SymbolicTerm>.Empty;
            if (!TryGetStableVariableSymbol(expression, context, out var symbol) ||
                context.SemanticModel.GetTypeInfo(expression, context.CancellationToken).Type is not INamedTypeSymbol { IsTupleType: true } tupleType ||
                tupleType.TupleElements.Length == 0)
            {
                return false;
            }

            var builder = ImmutableArray.CreateBuilder<SymbolicTerm>(tupleType.TupleElements.Length);
            foreach (var element in tupleType.TupleElements)
            {
                var field = element.CorrespondingTupleField ?? element;
                if (!TryGetTupleElementStorageName(field, out var storageName) ||
                    !TryGetValueKind(field.Type, out var kind))
                {
                    return false;
                }

                builder.Add(new SymbolicVariableTerm(context.GetVariableName(symbol) + "." + storageName, kind));
            }

            terms = builder.ToImmutable();
            return true;
        }

        private static bool TryGetTupleElementStorageName(IFieldSymbol field, out string storageName)
        {
            var storageField = field.CorrespondingTupleField ?? field;
            storageName = storageField.Name;
            return storageName.StartsWith("Item", StringComparison.Ordinal);
        }

        private static bool TryGetRelationOperator(SyntaxKind kind, out SymbolicRelationOperator op)
        {
            switch (kind)
            {
                case SyntaxKind.EqualsExpression:
                    op = SymbolicRelationOperator.Equal;
                    return true;
                case SyntaxKind.NotEqualsExpression:
                    op = SymbolicRelationOperator.NotEqual;
                    return true;
                case SyntaxKind.LessThanExpression:
                    op = SymbolicRelationOperator.LessThan;
                    return true;
                case SyntaxKind.LessThanOrEqualExpression:
                    op = SymbolicRelationOperator.LessThanOrEqual;
                    return true;
                case SyntaxKind.GreaterThanExpression:
                    op = SymbolicRelationOperator.GreaterThan;
                    return true;
                case SyntaxKind.GreaterThanOrEqualExpression:
                    op = SymbolicRelationOperator.GreaterThanOrEqual;
                    return true;
                default:
                    op = default;
                    return false;
            }
        }

        private static bool TryGetBinaryTermOperator(SyntaxKind kind, out SymbolicBinaryTermOperator op)
        {
            switch (kind)
            {
                case SyntaxKind.AddExpression:
                    op = SymbolicBinaryTermOperator.Add;
                    return true;
                case SyntaxKind.SubtractExpression:
                    op = SymbolicBinaryTermOperator.Subtract;
                    return true;
                case SyntaxKind.MultiplyExpression:
                    op = SymbolicBinaryTermOperator.Multiply;
                    return true;
                case SyntaxKind.DivideExpression:
                    op = SymbolicBinaryTermOperator.Divide;
                    return true;
                case SyntaxKind.ModuloExpression:
                    op = SymbolicBinaryTermOperator.Remainder;
                    return true;
                default:
                    op = default;
                    return false;
            }
        }

        private static bool TryGetSymbolType(ISymbol symbol, out ITypeSymbol type)
        {
            switch (symbol)
            {
                case ILocalSymbol local:
                    type = local.Type;
                    return true;
                case IParameterSymbol parameter:
                    type = parameter.Type;
                    return true;
                case IPropertySymbol property:
                    type = property.Type;
                    return true;
                case IFieldSymbol field:
                    type = field.Type;
                    return true;
                default:
                    type = null!;
                    return false;
            }
        }

        private static bool TryGetValueKind(ITypeSymbol type, out SmtValueKind kind)
        {
            if (type.SpecialType == SpecialType.System_Boolean)
            {
                kind = SmtValueKind.Bool;
                return true;
            }

            if (IsIntegerSmtType(type))
            {
                kind = SmtValueKind.Int;
                return true;
            }

            if (type.TypeKind == TypeKind.Dynamic ||
                type.IsReferenceType ||
                IsSupportedTupleCarrierType(type))
            {
                kind = SmtValueKind.Reference;
                return true;
            }

            kind = default;
            return false;
        }

        private static bool IsIntegerSmtType(ITypeSymbol type)
        {
            return type.SpecialType is
                SpecialType.System_Char or
                SpecialType.System_SByte or
                SpecialType.System_Byte or
                SpecialType.System_Int16 or
                SpecialType.System_UInt16 or
                SpecialType.System_Int32 or
                SpecialType.System_UInt32 or
                SpecialType.System_Int64 or
                SpecialType.System_UInt64 ||
                type.TypeKind == TypeKind.Enum ||
                IsBigIntegerType(type);
        }

        private static bool IsBigIntegerType(ITypeSymbol type)
        {
            return string.Equals(type.ContainingNamespace?.ToDisplayString(), "System.Numerics", StringComparison.Ordinal) &&
                string.Equals(type.Name, "BigInteger", StringComparison.Ordinal);
        }

        private static bool IsSupportedTupleCarrierType(ITypeSymbol type)
        {
            return type is INamedTypeSymbol { IsTupleType: true, TupleElements.Length: > 0 };
        }

        private static bool IsBuiltInSpanOrMemoryType(ITypeSymbol? type)
        {
            if (type is not INamedTypeSymbol namedType)
            {
                return false;
            }

            var metadataName = namedType.ConstructedFrom.ToDisplayString();
            return string.Equals(metadataName, "System.Span<T>", StringComparison.Ordinal) ||
                string.Equals(metadataName, "System.ReadOnlySpan<T>", StringComparison.Ordinal) ||
                string.Equals(metadataName, "System.Memory<T>", StringComparison.Ordinal) ||
                string.Equals(metadataName, "System.ReadOnlyMemory<T>", StringComparison.Ordinal);
        }

        private static bool TryGetIntegralConstant(object value, out long result)
        {
            try
            {
                switch (value)
                {
                    case char charValue:
                        result = charValue;
                        return true;
                    case sbyte sbyteValue:
                        result = sbyteValue;
                        return true;
                    case byte byteValue:
                        result = byteValue;
                        return true;
                    case short shortValue:
                        result = shortValue;
                        return true;
                    case ushort ushortValue:
                        result = ushortValue;
                        return true;
                    case int intValue:
                        result = intValue;
                        return true;
                    case uint uintValue:
                        result = uintValue;
                        return true;
                    case long longValue:
                        result = longValue;
                        return true;
                    case ulong ulongValue when ulongValue <= long.MaxValue:
                        result = (long)ulongValue;
                        return true;
                }
            }
            catch (OverflowException)
            {
            }

            result = 0;
            return false;
        }

        private static ExpressionSyntax UnwrapExpression(ExpressionSyntax expression)
        {
            while (true)
            {
                switch (expression)
                {
                    case ParenthesizedExpressionSyntax parenthesized:
                        expression = parenthesized.Expression;
                        continue;
                    case PostfixUnaryExpressionSyntax postfix when postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression):
                        expression = postfix.Operand;
                        continue;
                    case CastExpressionSyntax castExpression
                        when castExpression.Type is NullableTypeSyntax:
                        expression = castExpression.Expression;
                        continue;
                    default:
                        return expression;
                }
            }
        }
    }
}

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SearchLib.Smt;

namespace PurelySharp.Symbolic.Ir
{
    internal static partial class SymbolicIrLowerer
    {
        public static bool TryLowerPatternCondition(
            SymbolicTerm value,
            PatternSyntax pattern,
            SyntaxNode sourceNode,
            SymbolicLoweringContext context,
            out SymbolicCondition condition)
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            return TryLowerTrivialPatternCondition(pattern, out condition) ||
                TryLowerBinaryPatternCondition(value, pattern, context, out condition) ||
                TryLowerNullPatternCondition(value, pattern, sourceNode, context, out condition) ||
                TryLowerConstantPatternCondition(value, pattern, sourceNode, context, out condition) ||
                TryLowerRelationalPatternCondition(value, pattern, sourceNode, context, out condition) ||
                TryLowerEmptyRecursivePatternCondition(value, pattern, sourceNode, context, out condition) ||
                TryLowerTypePatternCondition(value, pattern, sourceNode, context, out condition) ||
                TryLowerUnaryPatternCondition(value, pattern, context, out condition);
        }

        private static bool TryLowerTrivialPatternCondition(
            PatternSyntax pattern,
            out SymbolicCondition condition)
        {
            pattern = UnwrapPattern(pattern);

            if (pattern is DiscardPatternSyntax or VarPatternSyntax)
            {
                condition = new SymbolicConstantCondition(true);
                return true;
            }

            if (pattern is DeclarationPatternSyntax declarationPattern &&
                declarationPattern.Type.IsVar)
            {
                condition = new SymbolicConstantCondition(true);
                return true;
            }

            if (pattern is UnaryPatternSyntax unaryPattern &&
                unaryPattern.IsKind(SyntaxKind.NotPattern) &&
                TryLowerTrivialPatternCondition(unaryPattern.Pattern, out var operand))
            {
                condition = new SymbolicNotCondition(operand);
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
            condition = null!;
            return TryLowerTerm(expression, context, out var value) &&
                TryLowerPatternCondition(value, pattern, sourceNode, context, out condition);
        }

        private static bool TryLowerBinaryPatternCondition(
            ExpressionSyntax expression,
            PatternSyntax pattern,
            SymbolicLoweringContext context,
            out SymbolicCondition condition)
        {
            condition = null!;
            return TryLowerTerm(expression, context, out var value) &&
                TryLowerBinaryPatternCondition(value, pattern, context, out condition);
        }

        private static bool TryLowerBinaryPatternCondition(
            SymbolicTerm value,
            PatternSyntax pattern,
            SymbolicLoweringContext context,
            out SymbolicCondition condition)
        {
            condition = null!;
            if (UnwrapPattern(pattern) is not BinaryPatternSyntax binaryPattern ||
                !TryLowerPatternCondition(value, binaryPattern.Left, binaryPattern.Left, context, out var left) ||
                !TryLowerPatternCondition(value, binaryPattern.Right, binaryPattern.Right, context, out var right))
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
            return TryLowerTerm(expression, context, out var value) &&
                TryLowerUnaryPatternCondition(value, pattern, context, out condition);
        }

        private static bool TryLowerUnaryPatternCondition(
            SymbolicTerm value,
            PatternSyntax pattern,
            SymbolicLoweringContext context,
            out SymbolicCondition condition)
        {
            condition = null!;
            if (UnwrapPattern(pattern) is not UnaryPatternSyntax unaryPattern ||
                !unaryPattern.IsKind(SyntaxKind.NotPattern) ||
                !TryLowerPatternCondition(value, unaryPattern.Pattern, unaryPattern.Pattern, context, out var operand))
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
            return TryLowerTerm(expression, context, out var value) &&
                TryLowerNullPatternCondition(value, pattern, sourceNode, context, out condition);
        }

        private static bool TryLowerNullPatternCondition(
            SymbolicTerm value,
            PatternSyntax pattern,
            SyntaxNode sourceNode,
            SymbolicLoweringContext context,
            out SymbolicCondition condition)
        {
            condition = null!;
            if (!TryLowerNullPattern(pattern, context, out var negate) ||
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
            return TryLowerTerm(expression, context, out var value) &&
                TryLowerConstantPatternCondition(value, pattern, sourceNode, context, out condition);
        }

        private static bool TryLowerConstantPatternCondition(
            SymbolicTerm value,
            PatternSyntax pattern,
            SyntaxNode sourceNode,
            SymbolicLoweringContext context,
            out SymbolicCondition condition)
        {
            condition = null!;
            if (!TryLowerConstantPattern(pattern, out var constantExpression, out var negate) ||
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
            return TryLowerTerm(expression, context, out var value) &&
                TryLowerRelationalPatternCondition(value, pattern, sourceNode, context, out condition);
        }

        private static bool TryLowerRelationalPatternCondition(
            SymbolicTerm value,
            PatternSyntax pattern,
            SyntaxNode sourceNode,
            SymbolicLoweringContext context,
            out SymbolicCondition condition)
        {
            condition = null!;
            if (!TryLowerRelationalPattern(pattern, out var operatorKind, out var relationalExpression, out var negate) ||
                !TryGetRelationalPatternOperator(operatorKind, negate, out var relationOperator) ||
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
            return TryLowerTerm(expression, context, out var value) &&
                TryLowerEmptyRecursivePatternCondition(value, pattern, sourceNode, context, out condition);
        }

        private static bool TryLowerEmptyRecursivePatternCondition(
            SymbolicTerm value,
            PatternSyntax pattern,
            SyntaxNode sourceNode,
            SymbolicLoweringContext context,
            out SymbolicCondition condition)
        {
            condition = null!;
            if (!TryLowerEmptyRecursivePattern(pattern, out var negate) ||
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
            return TryLowerTerm(expression, context, out var value) &&
                TryLowerTypePatternCondition(value, pattern, sourceNode, context, out condition);
        }

        private static bool TryLowerTypePatternCondition(
            SymbolicTerm value,
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

            return TryLowerTypeTestCondition(value, typeSyntax, sourceNode, negate, context, out condition);
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
            return TryLowerTerm(expression, context, out var value) &&
                TryLowerTypeTestCondition(value, typeSyntax, sourceNode, negate, context, out condition);
        }

        private static bool TryLowerTypeTestCondition(
            SymbolicTerm value,
            TypeSyntax typeSyntax,
            SyntaxNode sourceNode,
            bool negate,
            SymbolicLoweringContext context,
            out SymbolicCondition condition)
        {
            condition = null!;
            var type = context.SemanticModel.GetTypeInfo(typeSyntax, context.CancellationToken).Type;
            if (!SymbolicRuntimeTypeFacts.TryGetRuntimeTypeTestKey(type, out var typeKey) ||
                value.Kind != SmtValueKind.Reference)
            {
                return false;
            }

            var nonNull = CreateRelationCondition(
                SymbolicRelationOperator.NotEqual,
                value,
                new SymbolicNullTerm(),
                sourceNode,
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
    }
}

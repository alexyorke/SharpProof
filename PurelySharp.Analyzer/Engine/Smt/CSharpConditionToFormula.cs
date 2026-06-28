using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SearchLib.Smt;

namespace PurelySharp.Analyzer.Engine.Smt
{
    internal static class CSharpConditionToFormula
    {
        public static bool TryTranslate(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion = null)
        {
            expression = UnwrapExpression(expression);

            var constantValue = semanticModel.GetConstantValue(expression, cancellationToken);
            if (constantValue.HasValue && constantValue.Value is bool booleanValue)
            {
                formula = new SmtBooleanConstant(booleanValue);
                return true;
            }

            if (TryTranslateValue(expression, semanticModel, cancellationToken, out var directValue, getSymbolVersion) &&
                directValue is { Kind: SmtValueKind.Bool })
            {
                formula = directValue;
                return true;
            }

            if (expression is PrefixUnaryExpressionSyntax prefixUnary &&
                prefixUnary.IsKind(SyntaxKind.LogicalNotExpression) &&
                TryTranslate(prefixUnary.Operand, semanticModel, cancellationToken, out var operand, getSymbolVersion) &&
                operand != null)
            {
                formula = new SmtUnaryFormula(SmtUnaryOperator.Not, operand);
                return true;
            }

            if (expression is BinaryExpressionSyntax binaryExpression)
            {
                if (binaryExpression.IsKind(SyntaxKind.LogicalAndExpression) &&
                    TryTranslate(binaryExpression.Left, semanticModel, cancellationToken, out var leftAnd, getSymbolVersion) &&
                    TryTranslate(binaryExpression.Right, semanticModel, cancellationToken, out var rightAnd, getSymbolVersion) &&
                    leftAnd != null &&
                    rightAnd != null)
                {
                    formula = new SmtBinaryFormula(SmtBinaryOperator.And, leftAnd, rightAnd);
                    return true;
                }

                if (binaryExpression.IsKind(SyntaxKind.LogicalOrExpression) &&
                    TryTranslate(binaryExpression.Left, semanticModel, cancellationToken, out var leftOr, getSymbolVersion) &&
                    TryTranslate(binaryExpression.Right, semanticModel, cancellationToken, out var rightOr, getSymbolVersion) &&
                    leftOr != null &&
                    rightOr != null)
                {
                    formula = new SmtBinaryFormula(SmtBinaryOperator.Or, leftOr, rightOr);
                    return true;
                }

                if (TryTranslateValue(binaryExpression.Left, semanticModel, cancellationToken, out var leftValue, getSymbolVersion) &&
                    TryTranslateValue(binaryExpression.Right, semanticModel, cancellationToken, out var rightValue, getSymbolVersion) &&
                    leftValue != null &&
                    rightValue != null &&
                    TryTranslateComparison(binaryExpression.Kind(), leftValue, rightValue, out var comparison))
                {
                    formula = comparison;
                    return true;
                }
            }

            if (expression is IsPatternExpressionSyntax isPatternExpression &&
                TryTranslatePatternExpression(isPatternExpression, semanticModel, cancellationToken, out var patternFormula, getSymbolVersion))
            {
                formula = patternFormula;
                return true;
            }

            formula = null;
            return false;
        }

        public static bool TryCollectBranchAssumptions(
            ExpressionSyntax expression,
            bool branchWhenTrue,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas)
        {
            return TryCollectBranchAssumptions(expression, branchWhenTrue, semanticModel, cancellationToken, formulas, getSymbolVersion: null);
        }

        public static bool TryCollectBranchAssumptions(
            ExpressionSyntax expression,
            bool branchWhenTrue,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion)
        {
            var originalCount = formulas.Count;
            AddBranchAssumptions(expression, branchWhenTrue, semanticModel, cancellationToken, formulas, getSymbolVersion);
            return formulas.Count > originalCount;
        }

        private static void AddBranchAssumptions(
            ExpressionSyntax expression,
            bool branchWhenTrue,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion)
        {
            expression = UnwrapExpression(expression);

            if (expression is PrefixUnaryExpressionSyntax prefixUnary &&
                prefixUnary.IsKind(SyntaxKind.LogicalNotExpression))
            {
                AddBranchAssumptions(prefixUnary.Operand, !branchWhenTrue, semanticModel, cancellationToken, formulas, getSymbolVersion);
                return;
            }

            if (expression is BinaryExpressionSyntax binaryExpression)
            {
                if (branchWhenTrue && binaryExpression.IsKind(SyntaxKind.LogicalAndExpression))
                {
                    AddBranchAssumptions(binaryExpression.Left, branchWhenTrue: true, semanticModel, cancellationToken, formulas, getSymbolVersion);
                    AddBranchAssumptions(binaryExpression.Right, branchWhenTrue: true, semanticModel, cancellationToken, formulas, getSymbolVersion);
                    return;
                }

                if (!branchWhenTrue && binaryExpression.IsKind(SyntaxKind.LogicalOrExpression))
                {
                    AddBranchAssumptions(binaryExpression.Left, branchWhenTrue: false, semanticModel, cancellationToken, formulas, getSymbolVersion);
                    AddBranchAssumptions(binaryExpression.Right, branchWhenTrue: false, semanticModel, cancellationToken, formulas, getSymbolVersion);
                    return;
                }
            }

            if (!TryTranslate(expression, semanticModel, cancellationToken, out var formula, getSymbolVersion) ||
                formula == null)
            {
                return;
            }

            formulas.Add(branchWhenTrue
                ? formula
                : new SmtUnaryFormula(SmtUnaryOperator.Not, formula));
        }

        private static bool TryTranslatePatternExpression(
            IsPatternExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion)
        {
            formula = null;
            if (!TryTranslateValue(expression.Expression, semanticModel, cancellationToken, out var value, getSymbolVersion) ||
                value == null)
            {
                return false;
            }

            return TryTranslatePattern(value, expression.Pattern, semanticModel, cancellationToken, out formula, getSymbolVersion);
        }

        private static bool TryTranslatePattern(
            SmtFormula value,
            PatternSyntax pattern,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion)
        {
            formula = null;

            if (pattern is ParenthesizedPatternSyntax parenthesizedPattern)
            {
                return TryTranslatePattern(value, parenthesizedPattern.Pattern, semanticModel, cancellationToken, out formula, getSymbolVersion);
            }

            if (pattern is ConstantPatternSyntax constantPattern &&
                TryTranslateValue(constantPattern.Expression, semanticModel, cancellationToken, out var constantValue, getSymbolVersion) &&
                constantValue != null &&
                AreComparable(value, constantValue))
            {
                formula = new SmtBinaryFormula(SmtBinaryOperator.Equal, value, constantValue);
                return true;
            }

            if (pattern is UnaryPatternSyntax unaryPattern &&
                unaryPattern.OperatorToken.IsKind(SyntaxKind.NotKeyword) &&
                TryTranslatePattern(value, unaryPattern.Pattern, semanticModel, cancellationToken, out var negatedPattern, getSymbolVersion) &&
                negatedPattern != null)
            {
                formula = new SmtUnaryFormula(SmtUnaryOperator.Not, negatedPattern);
                return true;
            }

            if (pattern is BinaryPatternSyntax binaryPattern &&
                TryTranslatePattern(value, binaryPattern.Left, semanticModel, cancellationToken, out var leftPattern, getSymbolVersion) &&
                TryTranslatePattern(value, binaryPattern.Right, semanticModel, cancellationToken, out var rightPattern, getSymbolVersion) &&
                leftPattern != null &&
                rightPattern != null)
            {
                if (binaryPattern.OperatorToken.IsKind(SyntaxKind.AndKeyword))
                {
                    formula = new SmtBinaryFormula(SmtBinaryOperator.And, leftPattern, rightPattern);
                    return true;
                }

                if (binaryPattern.OperatorToken.IsKind(SyntaxKind.OrKeyword))
                {
                    formula = new SmtBinaryFormula(SmtBinaryOperator.Or, leftPattern, rightPattern);
                    return true;
                }
            }

            if (pattern is RelationalPatternSyntax relationalPattern &&
                value.Kind == SmtValueKind.Int &&
                TryTranslateValue(relationalPattern.Expression, semanticModel, cancellationToken, out var relationalValue, getSymbolVersion) &&
                relationalValue is { Kind: SmtValueKind.Int })
            {
                switch (relationalPattern.OperatorToken.Kind())
                {
                    case SyntaxKind.GreaterThanToken:
                        formula = new SmtBinaryFormula(SmtBinaryOperator.GreaterThan, value, relationalValue);
                        return true;
                    case SyntaxKind.GreaterThanEqualsToken:
                        formula = new SmtBinaryFormula(SmtBinaryOperator.GreaterThanOrEqual, value, relationalValue);
                        return true;
                    case SyntaxKind.LessThanToken:
                        formula = new SmtBinaryFormula(SmtBinaryOperator.LessThan, value, relationalValue);
                        return true;
                    case SyntaxKind.LessThanEqualsToken:
                        formula = new SmtBinaryFormula(SmtBinaryOperator.LessThanOrEqual, value, relationalValue);
                        return true;
                }
            }

            return false;
        }

        private static bool TryTranslateComparison(
            SyntaxKind kind,
            SmtFormula left,
            SmtFormula right,
            out SmtFormula? formula)
        {
            formula = null;
            switch (kind)
            {
                case SyntaxKind.EqualsExpression:
                    if (AreComparable(left, right))
                    {
                        formula = new SmtBinaryFormula(SmtBinaryOperator.Equal, left, right);
                        return true;
                    }

                    return false;
                case SyntaxKind.NotEqualsExpression:
                    if (AreComparable(left, right))
                    {
                        formula = new SmtBinaryFormula(SmtBinaryOperator.NotEqual, left, right);
                        return true;
                    }

                    return false;
                case SyntaxKind.LessThanExpression:
                    return TryCreateIntegralComparison(SmtBinaryOperator.LessThan, left, right, out formula);
                case SyntaxKind.LessThanOrEqualExpression:
                    return TryCreateIntegralComparison(SmtBinaryOperator.LessThanOrEqual, left, right, out formula);
                case SyntaxKind.GreaterThanExpression:
                    return TryCreateIntegralComparison(SmtBinaryOperator.GreaterThan, left, right, out formula);
                case SyntaxKind.GreaterThanOrEqualExpression:
                    return TryCreateIntegralComparison(SmtBinaryOperator.GreaterThanOrEqual, left, right, out formula);
                default:
                    return false;
            }
        }

        private static bool TryCreateIntegralComparison(
            SmtBinaryOperator comparison,
            SmtFormula left,
            SmtFormula right,
            out SmtFormula? formula)
        {
            formula = null;
            if (left.Kind != SmtValueKind.Int || right.Kind != SmtValueKind.Int)
            {
                return false;
            }

            formula = new SmtBinaryFormula(comparison, left, right);
            return true;
        }

        private static bool AreComparable(SmtFormula left, SmtFormula right)
        {
            if (left.Kind == right.Kind)
            {
                return true;
            }

            return (left is SmtNullConstant && right.Kind == SmtValueKind.Reference) ||
                (right is SmtNullConstant && left.Kind == SmtValueKind.Reference);
        }

        private static bool TryTranslateValue(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion)
        {
            expression = UnwrapExpression(expression);
            formula = null;

            var constantValue = semanticModel.GetConstantValue(expression, cancellationToken);
            if (constantValue.HasValue)
            {
                if (constantValue.Value is bool booleanValue)
                {
                    formula = new SmtBooleanConstant(booleanValue);
                    return true;
                }

                if (constantValue.Value == null)
                {
                    formula = new SmtNullConstant();
                    return true;
                }

                if (TryGetIntegralConstant(constantValue.Value, out var integralValue))
                {
                    formula = new SmtIntegerConstant(integralValue);
                    return true;
                }
            }

            if (TryTranslateIntegralTerm(expression, semanticModel, cancellationToken, out formula, getSymbolVersion))
            {
                return true;
            }

            var symbol = semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol;
            if (symbol is not ILocalSymbol && symbol is not IParameterSymbol)
            {
                return TryTranslateMemberValue(expression, semanticModel, cancellationToken, out formula, getSymbolVersion);
            }

            var type = semanticModel.GetTypeInfo(expression, cancellationToken).Type;
            if (type == null)
            {
                return false;
            }

            if (type.SpecialType == SpecialType.System_Boolean)
            {
                formula = new SmtVariable(GetVariableName(symbol, getSymbolVersion), SmtValueKind.Bool);
                return true;
            }

            if (IsIntegralType(type))
            {
                formula = new SmtVariable(GetVariableName(symbol, getSymbolVersion), SmtValueKind.Int);
                return true;
            }

            if (type.IsReferenceType)
            {
                formula = new SmtVariable(GetVariableName(symbol, getSymbolVersion), SmtValueKind.Reference);
                return true;
            }

            return false;
        }

        private static bool TryTranslateIntegralTerm(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion)
        {
            formula = null;
            if (!HasSupportedIntegralType(expression, semanticModel, cancellationToken))
            {
                return false;
            }

            if (expression is PrefixUnaryExpressionSyntax prefixUnary)
            {
                if (prefixUnary.IsKind(SyntaxKind.UnaryPlusExpression))
                {
                    return TryTranslateValue(prefixUnary.Operand, semanticModel, cancellationToken, out formula, getSymbolVersion) &&
                        formula is { Kind: SmtValueKind.Int };
                }

                if (prefixUnary.IsKind(SyntaxKind.UnaryMinusExpression) &&
                    TryTranslateValue(prefixUnary.Operand, semanticModel, cancellationToken, out var operand, getSymbolVersion) &&
                    operand is { Kind: SmtValueKind.Int })
                {
                    formula = new SmtIntegerUnaryTerm(SmtIntegerUnaryOperator.Negate, operand);
                    return true;
                }
            }

            if (expression is BinaryExpressionSyntax binaryExpression)
            {
                if (binaryExpression.IsKind(SyntaxKind.AddExpression) &&
                    TryTranslateValue(binaryExpression.Left, semanticModel, cancellationToken, out var addLeft, getSymbolVersion) &&
                    TryTranslateValue(binaryExpression.Right, semanticModel, cancellationToken, out var addRight, getSymbolVersion) &&
                    addLeft is { Kind: SmtValueKind.Int } &&
                    addRight is { Kind: SmtValueKind.Int })
                {
                    formula = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Add, addLeft, addRight);
                    return true;
                }

                if (binaryExpression.IsKind(SyntaxKind.SubtractExpression) &&
                    TryTranslateValue(binaryExpression.Left, semanticModel, cancellationToken, out var subtractLeft, getSymbolVersion) &&
                    TryTranslateValue(binaryExpression.Right, semanticModel, cancellationToken, out var subtractRight, getSymbolVersion) &&
                    subtractLeft is { Kind: SmtValueKind.Int } &&
                    subtractRight is { Kind: SmtValueKind.Int })
                {
                    formula = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Subtract, subtractLeft, subtractRight);
                    return true;
                }

                if (binaryExpression.IsKind(SyntaxKind.MultiplyExpression) &&
                    TryTranslateValue(binaryExpression.Left, semanticModel, cancellationToken, out var multiplyLeft, getSymbolVersion) &&
                    TryTranslateValue(binaryExpression.Right, semanticModel, cancellationToken, out var multiplyRight, getSymbolVersion) &&
                    multiplyLeft is { Kind: SmtValueKind.Int } &&
                    multiplyRight is { Kind: SmtValueKind.Int } &&
                    (multiplyLeft is SmtIntegerConstant || multiplyRight is SmtIntegerConstant))
                {
                    formula = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Multiply, multiplyLeft, multiplyRight);
                    return true;
                }
            }

            return false;
        }

        private static bool TryTranslateMemberValue(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion)
        {
            formula = null;
            if (expression is not MemberAccessExpressionSyntax memberAccess)
            {
                return false;
            }

            var memberSymbol = semanticModel.GetSymbolInfo(memberAccess.Name, cancellationToken).Symbol;
            if (memberSymbol is not IPropertySymbol and not IFieldSymbol)
            {
                return false;
            }

            if (!TryTranslateValue(memberAccess.Expression, semanticModel, cancellationToken, out var receiver, getSymbolVersion) ||
                receiver == null)
            {
                return false;
            }

            var type = semanticModel.GetTypeInfo(expression, cancellationToken).Type;
            if (type == null)
            {
                return false;
            }

            var memberName = receiver + "." + memberSymbol.Name;
            if (type.SpecialType == SpecialType.System_Boolean)
            {
                formula = new SmtVariable(memberName, SmtValueKind.Bool);
                return true;
            }

            if (IsIntegralType(type))
            {
                formula = new SmtVariable(memberName, SmtValueKind.Int);
                return true;
            }

            if (type.IsReferenceType)
            {
                formula = new SmtVariable(memberName, SmtValueKind.Reference);
                return true;
            }

            return false;
        }

        private static string GetVariableName(ISymbol symbol, Func<ISymbol, int>? getSymbolVersion)
        {
            var start = symbol.Locations.FirstOrDefault()?.SourceSpan.Start ?? 0;
            var name = symbol.Name + "#" + start.ToString(CultureInfo.InvariantCulture);
            var version = getSymbolVersion?.Invoke(symbol.OriginalDefinition) ?? 0;
            return version > 0
                ? name + "@v" + version.ToString(CultureInfo.InvariantCulture)
                : name;
        }

        private static bool IsIntegralType(ITypeSymbol typeSymbol)
        {
            return typeSymbol.SpecialType is
                SpecialType.System_SByte or
                SpecialType.System_Byte or
                SpecialType.System_Int16 or
                SpecialType.System_UInt16 or
                SpecialType.System_Int32 or
                SpecialType.System_UInt32 or
                SpecialType.System_Int64;
        }

        private static bool TryGetIntegralConstant(object value, out long integralValue)
        {
            switch (value)
            {
                case sbyte signedByte:
                    integralValue = signedByte;
                    return true;
                case byte unsignedByte:
                    integralValue = unsignedByte;
                    return true;
                case short signedShort:
                    integralValue = signedShort;
                    return true;
                case ushort unsignedShort:
                    integralValue = unsignedShort;
                    return true;
                case int signedInt:
                    integralValue = signedInt;
                    return true;
                case uint unsignedInt:
                    integralValue = unsignedInt;
                    return true;
                case long signedLong:
                    integralValue = signedLong;
                    return true;
                default:
                    integralValue = default;
                    return false;
            }
        }

        private static bool HasSupportedIntegralType(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var type = semanticModel.GetTypeInfo(expression, cancellationToken).Type;
            return type != null && IsIntegralType(type);
        }

        private static ExpressionSyntax UnwrapExpression(ExpressionSyntax expression)
        {
            while (true)
            {
                if (expression is ParenthesizedExpressionSyntax parenthesizedExpression)
                {
                    expression = parenthesizedExpression.Expression;
                    continue;
                }

                if (expression is PostfixUnaryExpressionSyntax postfixUnary &&
                    postfixUnary.IsKind(SyntaxKind.SuppressNullableWarningExpression))
                {
                    expression = postfixUnary.Operand;
                    continue;
                }

                return expression;
            }
        }
    }
}

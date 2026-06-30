using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using PurelySharp.Analyzer.Engine;
using PurelySharp.Symbolic.Smt;
using SearchLib.Smt;

namespace PurelySharp.Analyzer
{
    internal static partial class ExceptionFlowAnalyzer
    {
        internal static IEnumerable<SyntaxNode> GetThrowNodes(SyntaxNode methodNode)
        {
            return GetRelevantDescendants<SyntaxNode>(methodNode)
                .Where(node => node is ThrowStatementSyntax || node is ThrowExpressionSyntax);
        }

        internal static IEnumerable<BinaryExpressionSyntax> GetDefiniteDivideByZeroNodes(
            SyntaxNode methodNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            SmtAnalysisService smtAnalysis)
        {
            foreach (var binaryExpression in GetRelevantDescendants<BinaryExpressionSyntax>(methodNode))
            {
                if (!binaryExpression.IsKind(SyntaxKind.DivideExpression) &&
                    !binaryExpression.IsKind(SyntaxKind.ModuloExpression))
                {
                    continue;
                }

                var rightType = semanticModel.GetTypeInfo(binaryExpression.Right, cancellationToken).ConvertedType;
                if (!IsThrowingDivideByZeroType(rightType))
                {
                    continue;
                }

                if (IsDefinitelyZeroExpression(binaryExpression.Right, binaryExpression, semanticModel, cancellationToken, smtAnalysis) &&
                    IsExceptionPathReachable(binaryExpression, semanticModel, cancellationToken, smtAnalysis))
                {
                    yield return binaryExpression;
                }
            }
        }

        internal static IEnumerable<SyntaxNode> GetDefiniteCheckedIntegralOverflowNodes(
            SyntaxNode methodNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            SmtAnalysisService smtAnalysis)
        {
            foreach (var binaryExpression in GetRelevantDescendants<BinaryExpressionSyntax>(methodNode))
            {
                if (IsDefinitelyCheckedIntegralOverflow(binaryExpression, semanticModel, cancellationToken, smtAnalysis) &&
                    IsExceptionPathReachable(binaryExpression, semanticModel, cancellationToken, smtAnalysis))
                {
                    yield return binaryExpression;
                }
            }

            foreach (var unaryExpression in GetRelevantDescendants<PrefixUnaryExpressionSyntax>(methodNode))
            {
                if (IsDefinitelyCheckedIntegralOverflow(unaryExpression, semanticModel, cancellationToken, smtAnalysis) &&
                    IsExceptionPathReachable(unaryExpression, semanticModel, cancellationToken, smtAnalysis))
                {
                    yield return unaryExpression;
                }
            }

            foreach (var unaryExpression in GetRelevantDescendants<PostfixUnaryExpressionSyntax>(methodNode))
            {
                if (IsDefinitelyCheckedIntegralOverflow(unaryExpression, semanticModel, cancellationToken, smtAnalysis) &&
                    IsExceptionPathReachable(unaryExpression, semanticModel, cancellationToken, smtAnalysis))
                {
                    yield return unaryExpression;
                }
            }

            foreach (var castExpression in GetRelevantDescendants<CastExpressionSyntax>(methodNode))
            {
                if (IsDefinitelyCheckedIntegralOverflow(castExpression, semanticModel, cancellationToken, smtAnalysis) &&
                    IsExceptionPathReachable(castExpression, semanticModel, cancellationToken, smtAnalysis))
                {
                    yield return castExpression;
                }
            }
        }

        internal static IEnumerable<SyntaxNode> GetDefiniteNullDereferenceNodes(
            SyntaxNode methodNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            SmtAnalysisService smtAnalysis)
        {
            foreach (var node in GetRelevantDescendants<SyntaxNode>(methodNode))
            {
                if (node is MemberAccessExpressionSyntax memberAccess &&
                    IsReferenceDereferenceReceiver(memberAccess.Expression, semanticModel, cancellationToken) &&
                    IsDefinitelyNullExpression(memberAccess.Expression, memberAccess, semanticModel, cancellationToken, smtAnalysis) &&
                    IsExceptionPathReachable(memberAccess, semanticModel, cancellationToken, smtAnalysis))
                {
                    yield return memberAccess;
                }
                else if (node is ElementAccessExpressionSyntax elementAccess &&
                    IsReferenceDereferenceReceiver(elementAccess.Expression, semanticModel, cancellationToken) &&
                    IsDefinitelyNullExpression(elementAccess.Expression, elementAccess, semanticModel, cancellationToken, smtAnalysis) &&
                    IsExceptionPathReachable(elementAccess, semanticModel, cancellationToken, smtAnalysis))
                {
                    yield return elementAccess;
                }
                else if (node is InvocationExpressionSyntax invocation &&
                    IsDefinitelyNullExpression(invocation.Expression, invocation, semanticModel, cancellationToken, smtAnalysis) &&
                    IsExceptionPathReachable(invocation, semanticModel, cancellationToken, smtAnalysis))
                {
                    yield return invocation;
                }
            }
        }

        private static bool IsReferenceDereferenceReceiver(
            ExpressionSyntax receiver,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            return IsReferenceType(GetExpressionType(receiver, semanticModel, cancellationToken));
        }

        internal static IEnumerable<MemberAccessExpressionSyntax> GetDefiniteNullableValueAccessNodes(
            SyntaxNode methodNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            SmtAnalysisService smtAnalysis)
        {
            foreach (var memberAccess in GetRelevantDescendants<MemberAccessExpressionSyntax>(methodNode))
            {
                if (IsNullableValueAccess(memberAccess, semanticModel, cancellationToken) &&
                    IsDefinitelyMissingNullableValue(memberAccess, semanticModel, cancellationToken, smtAnalysis))
                {
                    yield return memberAccess;
                }
            }
        }

        internal static IEnumerable<CastExpressionSyntax> GetDefiniteUnboxNullCastNodes(
            SyntaxNode methodNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            SmtAnalysisService smtAnalysis)
        {
            foreach (var castExpression in GetRelevantDescendants<CastExpressionSyntax>(methodNode))
            {
                if (IsDefinitelyUnboxNullCast(castExpression, semanticModel, cancellationToken, smtAnalysis) &&
                    IsExceptionPathReachable(castExpression, semanticModel, cancellationToken, smtAnalysis))
                {
                    yield return castExpression;
                }
            }
        }

        internal static IEnumerable<CastExpressionSyntax> GetDefiniteInvalidCastNodes(
            SyntaxNode methodNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            SmtAnalysisService smtAnalysis)
        {
            foreach (var castExpression in GetRelevantDescendants<CastExpressionSyntax>(methodNode))
            {
                if (IsDefinitelyInvalidCast(castExpression, semanticModel, cancellationToken, smtAnalysis) &&
                    IsExceptionPathReachable(castExpression, semanticModel, cancellationToken, smtAnalysis))
                {
                    yield return castExpression;
                }
            }
        }

        internal static IEnumerable<AssignmentExpressionSyntax> GetDefiniteArrayTypeMismatchStoreNodes(
            SyntaxNode methodNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            SmtAnalysisService smtAnalysis)
        {
            foreach (var assignment in GetRelevantDescendants<AssignmentExpressionSyntax>(methodNode))
            {
                if (IsDefinitelyArrayTypeMismatchStore(assignment, semanticModel, cancellationToken, smtAnalysis) &&
                    IsExceptionPathReachable(assignment, semanticModel, cancellationToken, smtAnalysis))
                {
                    yield return assignment;
                }
            }
        }

        internal static IEnumerable<ElementAccessExpressionSyntax> GetDefiniteIndexOutOfRangeNodes(
            SyntaxNode methodNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            SmtAnalysisService smtAnalysis)
        {
            foreach (var elementAccess in GetRelevantDescendants<ElementAccessExpressionSyntax>(methodNode))
            {
                if (IsDefinitelyOutOfRangeBuiltInIndexAccess(elementAccess, semanticModel, cancellationToken, smtAnalysis))
                {
                    yield return elementAccess;
                }
            }
        }

        internal static IEnumerable<SyntaxNode> GetDefiniteArgumentOutOfRangeNodes(
            SyntaxNode methodNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            SmtAnalysisService smtAnalysis)
        {
            foreach (var elementAccess in GetRelevantDescendants<ElementAccessExpressionSyntax>(methodNode))
            {
                if (IsDefinitelyOutOfRangeBuiltInRangeAccess(elementAccess, semanticModel, cancellationToken, smtAnalysis))
                {
                    yield return elementAccess;
                }
            }

            foreach (var invocation in GetRelevantDescendants<InvocationExpressionSyntax>(methodNode))
            {
                if (IsDefinitelyOutOfRangeBuiltInSliceCall(invocation, semanticModel, cancellationToken, smtAnalysis))
                {
                    yield return invocation;
                }
            }
        }

        internal static ITypeSymbol? GetThrownExceptionType(
            SyntaxNode throwNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            ExpressionSyntax? exceptionExpression = throwNode switch
            {
                ThrowStatementSyntax statement => statement.Expression,
                ThrowExpressionSyntax expression => expression.Expression,
                _ => null
            };

            if (exceptionExpression == null)
            {
                return throwNode is ThrowStatementSyntax statement
                    ? GetRethrownExceptionType(statement, semanticModel, cancellationToken)
                    : null;
            }

            var typeInfo = semanticModel.GetTypeInfo(exceptionExpression, cancellationToken);
            return typeInfo.Type ?? typeInfo.ConvertedType;
        }

        internal static bool IsShadowedByDefinitelyThrowingFinally(SyntaxNode site)
        {
            foreach (var tryStatement in site.Ancestors().OfType<TryStatementSyntax>())
            {
                if (!tryStatement.Span.Contains(site.SpanStart))
                {
                    continue;
                }

                if (tryStatement.Finally == null ||
                    !StatementDefinitelyExits(tryStatement.Finally.Block))
                {
                    continue;
                }

                if (tryStatement.Finally.Block.Span.Contains(site.SpanStart))
                {
                    continue;
                }

                if (tryStatement.Block.Span.Contains(site.SpanStart) ||
                    tryStatement.Catches.Any(catchClause => catchClause.Block.Span.Contains(site.SpanStart)))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsIntegralOrDecimalZero(object? value)
        {
            switch (value)
            {
                case byte byteValue:
                    return byteValue == 0;
                case sbyte sbyteValue:
                    return sbyteValue == 0;
                case short shortValue:
                    return shortValue == 0;
                case ushort ushortValue:
                    return ushortValue == 0;
                case int intValue:
                    return intValue == 0;
                case uint uintValue:
                    return uintValue == 0;
                case long longValue:
                    return longValue == 0L;
                case ulong ulongValue:
                    return ulongValue == 0UL;
                case decimal decimalValue:
                    return decimalValue == 0m;
                default:
                    return false;
            }
        }

        private static bool IsThrowingDivideByZeroType(ITypeSymbol? typeSymbol)
        {
            if (typeSymbol == null)
            {
                return false;
            }

            switch (typeSymbol.SpecialType)
            {
                case SpecialType.System_Byte:
                case SpecialType.System_SByte:
                case SpecialType.System_Int16:
                case SpecialType.System_UInt16:
                case SpecialType.System_Int32:
                case SpecialType.System_UInt32:
                case SpecialType.System_Int64:
                case SpecialType.System_UInt64:
                case SpecialType.System_Decimal:
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsDefinitelyZeroExpression(
            ExpressionSyntax expression,
            SyntaxNode useNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            SmtAnalysisService smtAnalysis)
        {
            var constantValue = semanticModel.GetConstantValue(expression, cancellationToken);
            return (constantValue.HasValue && IsIntegralOrDecimalZero(constantValue.Value)) ||
                IsKnownByPriorAssignment(expression, useNode, semanticModel, cancellationToken, PathFactKind.Zero) ||
                IsKnownByDominatingIf(expression, useNode, semanticModel, cancellationToken, PathFactKind.Zero, smtAnalysis);
        }

        private static bool IsDefinitelyNullExpression(
            ExpressionSyntax expression,
            SyntaxNode useNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            SmtAnalysisService smtAnalysis)
        {
            while (true)
            {
                if (expression is ParenthesizedExpressionSyntax parenthesized)
                {
                    expression = parenthesized.Expression;
                    continue;
                }

                if (expression is CastExpressionSyntax castExpression)
                {
                    if (IsDefinitelyNullExpression(castExpression.Expression, useNode, semanticModel, cancellationToken, smtAnalysis))
                    {
                        var castType = semanticModel.GetTypeInfo(castExpression, cancellationToken).Type;
                        return IsReferenceType(castType);
                    }

                    return false;
                }

                if (expression is PostfixUnaryExpressionSyntax postfixUnary &&
                    postfixUnary.IsKind(SyntaxKind.SuppressNullableWarningExpression))
                {
                    expression = postfixUnary.Operand;
                    continue;
                }

                break;
            }

            if (expression.IsKind(SyntaxKind.NullLiteralExpression))
            {
                return true;
            }

            if (expression is DefaultExpressionSyntax defaultExpression)
            {
                var defaultType = semanticModel.GetTypeInfo(defaultExpression, cancellationToken).Type;
                return IsReferenceType(defaultType);
            }

            return IsKnownByPriorAssignment(expression, useNode, semanticModel, cancellationToken, PathFactKind.Null) ||
                IsKnownByDominatingIf(expression, useNode, semanticModel, cancellationToken, PathFactKind.Null, smtAnalysis);
        }

        private static bool IsDefinitelyMissingNullableValue(
            MemberAccessExpressionSyntax memberAccess,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            SmtAnalysisService smtAnalysis)
        {
            if (IsKnownMissingNullableValueByPriorAssignment(
                    memberAccess.Expression,
                    memberAccess,
                    semanticModel,
                    cancellationToken))
            {
                return true;
            }

            if (!CSharpConditionToFormula.TryTranslateNullableHasValue(
                    memberAccess.Expression,
                    semanticModel,
                    cancellationToken,
                    out var hasValueFormula))
            {
                return false;
            }

            return IsDefinitelyFalseAtUse(memberAccess, hasValueFormula, semanticModel, cancellationToken, smtAnalysis);
        }

        private static bool IsDefinitelyCheckedIntegralOverflow(
            BinaryExpressionSyntax binaryExpression,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            SmtAnalysisService smtAnalysis)
        {
            if (!TryGetCheckedIntegralBinaryOperator(binaryExpression, semanticModel, cancellationToken, out var smtOperator, out var minValue, out var maxValue) ||
                !CSharpConditionToFormula.TryTranslateValue(binaryExpression.Left, semanticModel, cancellationToken, out var leftFormula, getSymbolVersion: null) ||
                leftFormula is not { Kind: SmtValueKind.Int } ||
                !CSharpConditionToFormula.TryTranslateValue(binaryExpression.Right, semanticModel, cancellationToken, out var rightFormula, getSymbolVersion: null) ||
                rightFormula is not { Kind: SmtValueKind.Int })
            {
                return false;
            }

            var resultFormula = new SmtIntegerBinaryTerm(smtOperator, leftFormula, rightFormula);
            return IsDefinitelyFalseAtUse(
                binaryExpression,
                CreateIntegralInRangeFormula(resultFormula, minValue, maxValue),
                semanticModel,
                cancellationToken,
                smtAnalysis);
        }

        private static bool IsDefinitelyCheckedIntegralOverflow(
            PrefixUnaryExpressionSyntax unaryExpression,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            SmtAnalysisService smtAnalysis)
        {
            if (TryGetCheckedIntegralUnaryOperator(unaryExpression, semanticModel, cancellationToken, out var minValue, out var maxValue) &&
                CSharpConditionToFormula.TryTranslateValue(unaryExpression.Operand, semanticModel, cancellationToken, out var operandFormula, getSymbolVersion: null) &&
                operandFormula is { Kind: SmtValueKind.Int })
            {
                var resultFormula = new SmtIntegerUnaryTerm(SmtIntegerUnaryOperator.Negate, operandFormula);
                return IsDefinitelyFalseAtUse(
                    unaryExpression,
                    CreateIntegralInRangeFormula(resultFormula, minValue, maxValue),
                    semanticModel,
                    cancellationToken,
                    smtAnalysis);
            }

            return IsDefinitelyCheckedIncrementOrDecrementOverflow(
                unaryExpression,
                unaryExpression.Operand,
                semanticModel,
                cancellationToken,
                smtAnalysis);
        }

        private static bool IsDefinitelyCheckedIntegralOverflow(
            PostfixUnaryExpressionSyntax unaryExpression,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            SmtAnalysisService smtAnalysis)
        {
            return IsDefinitelyCheckedIncrementOrDecrementOverflow(
                unaryExpression,
                unaryExpression.Operand,
                semanticModel,
                cancellationToken,
                smtAnalysis);
        }

        private static bool IsDefinitelyCheckedIncrementOrDecrementOverflow(
            ExpressionSyntax updateExpression,
            ExpressionSyntax operand,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            SmtAnalysisService smtAnalysis)
        {
            if (!TryGetCheckedIntegralIncrementOrDecrementOperator(
                    updateExpression,
                    operand,
                    semanticModel,
                    cancellationToken,
                    out var smtOperator,
                    out var minValue,
                    out var maxValue) ||
                !CSharpConditionToFormula.TryTranslateValue(operand, semanticModel, cancellationToken, out var operandFormula, getSymbolVersion: null) ||
                operandFormula is not { Kind: SmtValueKind.Int })
            {
                return false;
            }

            var resultFormula = new SmtIntegerBinaryTerm(smtOperator, operandFormula, new SmtIntegerConstant(1));
            return IsDefinitelyFalseAtUse(
                updateExpression,
                CreateIntegralInRangeFormula(resultFormula, minValue, maxValue),
                semanticModel,
                cancellationToken,
                smtAnalysis);
        }

        private static bool IsDefinitelyCheckedIntegralOverflow(
            CastExpressionSyntax castExpression,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            SmtAnalysisService smtAnalysis)
        {
            if (!TryGetCheckedIntegralConversionRange(castExpression, semanticModel, cancellationToken, out var minValue, out var maxValue) ||
                !CSharpConditionToFormula.TryTranslateValue(castExpression.Expression, semanticModel, cancellationToken, out var operandFormula, getSymbolVersion: null) ||
                operandFormula is not { Kind: SmtValueKind.Int })
            {
                return false;
            }

            return IsDefinitelyFalseAtUse(
                castExpression,
                CreateIntegralInRangeFormula(operandFormula, minValue, maxValue),
                semanticModel,
                cancellationToken,
                smtAnalysis);
        }

        private static bool TryGetCheckedIntegralBinaryOperator(
            BinaryExpressionSyntax binaryExpression,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            out SmtIntegerBinaryOperator smtOperator,
            out long minValue,
            out long maxValue)
        {
            smtOperator = default;
            minValue = default;
            maxValue = default;

            if (!TryGetCheckedIntegralRange(binaryExpression, semanticModel, cancellationToken, out minValue, out maxValue) ||
                semanticModel.GetOperation(binaryExpression, cancellationToken) is not IBinaryOperation
                {
                    IsChecked: true,
                    OperatorMethod: null
                })
            {
                return false;
            }

            switch (binaryExpression.Kind())
            {
                case SyntaxKind.AddExpression:
                    smtOperator = SmtIntegerBinaryOperator.Add;
                    return true;
                case SyntaxKind.SubtractExpression:
                    smtOperator = SmtIntegerBinaryOperator.Subtract;
                    return true;
                case SyntaxKind.MultiplyExpression:
                    smtOperator = SmtIntegerBinaryOperator.Multiply;
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryGetCheckedIntegralUnaryOperator(
            PrefixUnaryExpressionSyntax unaryExpression,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            out long minValue,
            out long maxValue)
        {
            minValue = default;
            maxValue = default;
            return unaryExpression.IsKind(SyntaxKind.UnaryMinusExpression) &&
                TryGetCheckedIntegralRange(unaryExpression, semanticModel, cancellationToken, out minValue, out maxValue) &&
                semanticModel.GetOperation(unaryExpression, cancellationToken) is IUnaryOperation
                {
                    IsChecked: true,
                    OperatorMethod: null
                };
        }

        private static bool TryGetCheckedIntegralIncrementOrDecrementOperator(
            ExpressionSyntax updateExpression,
            ExpressionSyntax operand,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            out SmtIntegerBinaryOperator smtOperator,
            out long minValue,
            out long maxValue)
        {
            smtOperator = default;
            minValue = default;
            maxValue = default;

            var operandType = semanticModel.GetTypeInfo(operand, cancellationToken).Type;
            if (!TryGetBoundedIntegralRange(operandType, out minValue, out maxValue) ||
                semanticModel.GetOperation(updateExpression, cancellationToken) is not IIncrementOrDecrementOperation
                {
                    IsChecked: true,
                    OperatorMethod: null
                })
            {
                return false;
            }

            switch (updateExpression.Kind())
            {
                case SyntaxKind.PreIncrementExpression:
                case SyntaxKind.PostIncrementExpression:
                    smtOperator = SmtIntegerBinaryOperator.Add;
                    return true;
                case SyntaxKind.PreDecrementExpression:
                case SyntaxKind.PostDecrementExpression:
                    smtOperator = SmtIntegerBinaryOperator.Subtract;
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryGetCheckedIntegralConversionRange(
            CastExpressionSyntax castExpression,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            out long minValue,
            out long maxValue)
        {
            minValue = default;
            maxValue = default;

            if (!TryGetConversionOperation(castExpression, semanticModel, cancellationToken, out var conversionOperation) ||
                !conversionOperation.IsChecked ||
                conversionOperation.Conversion.IsUserDefined ||
                conversionOperation.Type is not { } targetType ||
                !TryGetBoundedIntegralRange(targetType, out minValue, out maxValue))
            {
                return false;
            }

            var sourceType = semanticModel.GetTypeInfo(castExpression.Expression, cancellationToken).Type;
            if (!TryGetBoundedIntegralRange(sourceType, out var sourceMinValue, out var sourceMaxValue))
            {
                return false;
            }

            return sourceMinValue < minValue || sourceMaxValue > maxValue;
        }

        private static bool TryGetCheckedIntegralRange(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            out long minValue,
            out long maxValue)
        {
            var typeInfo = semanticModel.GetTypeInfo(expression, cancellationToken);
            return TryGetCheckedIntegralRange(typeInfo.ConvertedType ?? typeInfo.Type, out minValue, out maxValue);
        }

        private static bool TryGetCheckedIntegralRange(
            ITypeSymbol? typeSymbol,
            out long minValue,
            out long maxValue)
        {
            switch (typeSymbol?.SpecialType)
            {
                case SpecialType.System_Int32:
                    minValue = int.MinValue;
                    maxValue = int.MaxValue;
                    return true;
                case SpecialType.System_Int64:
                    minValue = long.MinValue;
                    maxValue = long.MaxValue;
                    return true;
                default:
                    minValue = default;
                    maxValue = default;
                    return false;
            }
        }

        private static bool TryGetBoundedIntegralRange(
            ITypeSymbol? typeSymbol,
            out long minValue,
            out long maxValue)
        {
            switch (typeSymbol?.SpecialType)
            {
                case SpecialType.System_Char:
                    minValue = char.MinValue;
                    maxValue = char.MaxValue;
                    return true;
                case SpecialType.System_SByte:
                    minValue = sbyte.MinValue;
                    maxValue = sbyte.MaxValue;
                    return true;
                case SpecialType.System_Byte:
                    minValue = byte.MinValue;
                    maxValue = byte.MaxValue;
                    return true;
                case SpecialType.System_Int16:
                    minValue = short.MinValue;
                    maxValue = short.MaxValue;
                    return true;
                case SpecialType.System_UInt16:
                    minValue = ushort.MinValue;
                    maxValue = ushort.MaxValue;
                    return true;
                case SpecialType.System_Int32:
                    minValue = int.MinValue;
                    maxValue = int.MaxValue;
                    return true;
                case SpecialType.System_UInt32:
                    minValue = uint.MinValue;
                    maxValue = uint.MaxValue;
                    return true;
                case SpecialType.System_Int64:
                    minValue = long.MinValue;
                    maxValue = long.MaxValue;
                    return true;
                default:
                    minValue = default;
                    maxValue = default;
                    return false;
            }
        }

        private static SmtFormula CreateIntegralInRangeFormula(SmtFormula resultFormula, long minValue, long maxValue)
        {
            var lowerBound = new SmtBinaryFormula(
                SmtBinaryOperator.GreaterThanOrEqual,
                resultFormula,
                new SmtIntegerConstant(minValue));
            var upperBound = new SmtBinaryFormula(
                SmtBinaryOperator.LessThanOrEqual,
                resultFormula,
                new SmtIntegerConstant(maxValue));
            return new SmtBinaryFormula(SmtBinaryOperator.And, lowerBound, upperBound);
        }

        private static bool IsDefinitelyUnboxNullCast(
            CastExpressionSyntax castExpression,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            SmtAnalysisService smtAnalysis)
        {
            if (!TryGetConversionOperation(castExpression, semanticModel, cancellationToken, out var conversionOperation) ||
                conversionOperation.Conversion.IsUserDefined ||
                !IsUnboxingCastShape(castExpression, conversionOperation.Type, semanticModel, cancellationToken))
            {
                return false;
            }

            return IsDefinitelyNullExpression(castExpression.Expression, castExpression, semanticModel, cancellationToken, smtAnalysis);
        }

        private static bool IsDefinitelyInvalidCast(
            CastExpressionSyntax castExpression,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            SmtAnalysisService smtAnalysis)
        {
            if (!TryGetConversionOperation(castExpression, semanticModel, cancellationToken, out var conversionOperation) ||
                conversionOperation.Conversion.IsUserDefined ||
                conversionOperation.Conversion.IsIdentity ||
                conversionOperation.Type is not { } targetType ||
                targetType.TypeKind == TypeKind.Dynamic)
            {
                return false;
            }

            if (IsUnboxingCastShape(castExpression, targetType, semanticModel, cancellationToken))
            {
                if (IsDefinitelyNullExpression(castExpression.Expression, castExpression, semanticModel, cancellationToken, smtAnalysis) ||
                    !TryGetExactRuntimeType(
                        castExpression.Expression,
                        castExpression,
                        semanticModel,
                        cancellationToken,
                        out var exactRuntimeType))
                {
                    return false;
                }

                return !CanUnboxExactRuntimeTypeToValueType(exactRuntimeType, targetType);
            }

            var operandType = GetExpressionType(castExpression.Expression, semanticModel, cancellationToken);
            if (!IsReferenceType(targetType) ||
                !IsReferenceType(operandType) ||
                !TryGetExactRuntimeType(
                    castExpression.Expression,
                    castExpression,
                    semanticModel,
                    cancellationToken,
                    out var exactReferenceRuntimeType))
            {
                return false;
            }

            return !CanCastExactRuntimeTypeToReferenceType(
                exactReferenceRuntimeType,
                targetType,
                semanticModel.Compilation);
        }

        private static bool IsDefinitelyArrayTypeMismatchStore(
            AssignmentExpressionSyntax assignment,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            SmtAnalysisService smtAnalysis)
        {
            if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
                UnwrapFactExpression(assignment.Left) is not ElementAccessExpressionSyntax elementAccess ||
                !IsObjectArrayElementStore(elementAccess, semanticModel, cancellationToken) ||
                IsDefinitelyNullExpression(assignment.Right, assignment, semanticModel, cancellationToken, smtAnalysis) ||
                !TryGetExactRuntimeType(
                    elementAccess.Expression,
                    assignment,
                    semanticModel,
                    cancellationToken,
                    out var exactRuntimeArrayType) ||
                exactRuntimeArrayType is not IArrayTypeSymbol exactArrayType ||
                exactArrayType.Rank != 1 ||
                !IsReferenceType(exactArrayType.ElementType) ||
                exactArrayType.ElementType.TypeKind == TypeKind.Dynamic ||
                !IsDefinitelyInRangeElementStore(elementAccess, semanticModel, cancellationToken, smtAnalysis) ||
                !TryGetExactRuntimeType(
                    assignment.Right,
                    assignment,
                    semanticModel,
                    cancellationToken,
                    out var exactAssignedType))
            {
                return false;
            }

            return !CanStoreExactRuntimeTypeInArrayElement(
                exactAssignedType,
                exactArrayType.ElementType,
                semanticModel.Compilation);
        }

        private static bool IsObjectArrayElementStore(
            ElementAccessExpressionSyntax elementAccess,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            if (elementAccess.ArgumentList.Arguments.Count != 1)
            {
                return false;
            }

            return GetExpressionType(elementAccess.Expression, semanticModel, cancellationToken) is IArrayTypeSymbol
            {
                Rank: 1,
                ElementType.SpecialType: SpecialType.System_Object
            };
        }

        private static bool IsDefinitelyInRangeElementStore(
            ElementAccessExpressionSyntax elementAccess,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            SmtAnalysisService smtAnalysis)
        {
            if (!TryTranslateBuiltInElementAccessInRangeForExceptionFlow(
                    elementAccess,
                    semanticModel,
                    cancellationToken,
                    out var inRangeFormula))
            {
                return false;
            }

            return IsDefinitelyTrueAtUse(elementAccess, inRangeFormula, semanticModel, cancellationToken, smtAnalysis);
        }

        private static bool CanStoreExactRuntimeTypeInArrayElement(
            ITypeSymbol exactRuntimeType,
            ITypeSymbol elementType,
            Compilation compilation)
        {
            if (exactRuntimeType.TypeKind == TypeKind.Dynamic ||
                elementType.TypeKind == TypeKind.Dynamic)
            {
                return true;
            }

            return CanCastExactRuntimeTypeToReferenceType(exactRuntimeType, elementType, compilation);
        }

        private static bool IsUnboxingCastShape(
            CastExpressionSyntax castExpression,
            ITypeSymbol? targetType,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            var operandType = GetExpressionType(castExpression.Expression, semanticModel, cancellationToken);
            return IsNonNullableValueType(targetType) &&
                IsReferenceType(operandType);
        }

        private static bool TryGetConversionOperation(
            CastExpressionSyntax castExpression,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            out IConversionOperation conversionOperation)
        {
            if (semanticModel.GetOperation(castExpression, cancellationToken) is IConversionOperation operation)
            {
                conversionOperation = operation;
                return true;
            }

            conversionOperation = null!;
            return false;
        }

        private static bool TryGetExactRuntimeType(
            ExpressionSyntax expression,
            SyntaxNode useNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            out ITypeSymbol exactType,
            int inlineDepth = 0)
        {
            exactType = null!;
            if (inlineDepth > 8)
            {
                return false;
            }

            expression = UnwrapFactExpression(expression);
            if (TryResolveCurrentSimpleValueExpression(
                    expression,
                    useNode,
                    semanticModel,
                    cancellationToken,
                    out var currentValueExpression))
            {
                return TryGetExactRuntimeType(
                    currentValueExpression,
                    useNode,
                    semanticModel,
                    cancellationToken,
                    out exactType,
                    inlineDepth + 1);
            }

            var expressionType = GetNaturalExpressionType(expression, semanticModel, cancellationToken);
            if (expressionType != null && IsNonNullableValueType(expressionType))
            {
                exactType = expressionType;
                return true;
            }

            if (expressionType?.TypeKind == TypeKind.Dynamic)
            {
                return false;
            }

            if (expression is CastExpressionSyntax castExpression)
            {
                var targetType = GetExpressionType(castExpression, semanticModel, cancellationToken);
                if (targetType == null ||
                    targetType.TypeKind == TypeKind.Dynamic)
                {
                    return false;
                }

                if (IsReferenceType(targetType))
                {
                    var operandType = GetExpressionType(castExpression.Expression, semanticModel, cancellationToken);
                    if (IsNonNullableValueType(operandType) &&
                        TryGetExactRuntimeType(
                            castExpression.Expression,
                            useNode,
                            semanticModel,
                            cancellationToken,
                            out var boxedValueType,
                            inlineDepth + 1))
                    {
                        exactType = boxedValueType;
                        return true;
                    }

                    if (TryGetExactRuntimeType(
                            castExpression.Expression,
                            useNode,
                            semanticModel,
                            cancellationToken,
                            out var operandExactType,
                            inlineDepth + 1) &&
                        CanCastExactRuntimeTypeToReferenceType(
                            operandExactType,
                            targetType,
                            semanticModel.Compilation))
                    {
                        exactType = operandExactType;
                        return true;
                    }
                }

                if (IsNonNullableValueType(targetType))
                {
                    exactType = targetType;
                    return true;
                }

                return false;
            }

            if (expression is ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax or
                ArrayCreationExpressionSyntax or ImplicitArrayCreationExpressionSyntax or AnonymousObjectCreationExpressionSyntax)
            {
                if (expressionType != null && !expressionType.IsAbstract)
                {
                    exactType = expressionType;
                    return true;
                }

                return false;
            }

            if (expression.IsKind(SyntaxKind.StringLiteralExpression) &&
                expressionType?.SpecialType == SpecialType.System_String)
            {
                exactType = expressionType;
                return true;
            }

            return false;
        }

        private static bool TryResolveCurrentSimpleValueExpression(
            ExpressionSyntax expression,
            SyntaxNode useNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            out ExpressionSyntax valueExpression)
        {
            valueExpression = null!;
            var symbol = GetLocalOrParameterSymbol(expression, semanticModel, cancellationToken);
            if (symbol == null)
            {
                return false;
            }

            ExpressionSyntax? currentValue = null;
            foreach (var (block, containingStatement) in EnumerateContainingBlocks(useNode).Reverse())
            {
                foreach (var statement in block.Statements)
                {
                    if (ReferenceEquals(statement, containingStatement))
                    {
                        break;
                    }

                    if (statement is LocalDeclarationStatementSyntax localDeclaration)
                    {
                        foreach (var declarator in localDeclaration.Declaration.Variables)
                        {
                            if (semanticModel.GetDeclaredSymbol(declarator, cancellationToken) is ILocalSymbol localSymbol &&
                                SymbolEqualityComparer.Default.Equals(localSymbol.OriginalDefinition, symbol))
                            {
                                currentValue = declarator.Initializer?.Value;
                            }
                        }

                        if (StatementMutatesSymbolExceptLinearAssignment(statement, symbol, semanticModel, cancellationToken))
                        {
                            currentValue = null;
                        }

                        continue;
                    }

                    if (statement is ExpressionStatementSyntax
                        {
                            Expression: AssignmentExpressionSyntax assignment
                        } &&
                        ExpressionMatchesSymbol(assignment.Left, symbol, semanticModel, cancellationToken))
                    {
                        if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
                            ExpressionReferencesSymbol(assignment.Right, symbol, semanticModel, cancellationToken))
                        {
                            currentValue = null;
                            continue;
                        }

                        currentValue = assignment.Right;
                        continue;
                    }

                    if (StatementMutatesSymbolExceptLinearAssignment(statement, symbol, semanticModel, cancellationToken))
                    {
                        currentValue = null;
                    }
                }
            }

            if (currentValue == null)
            {
                return false;
            }

            valueExpression = currentValue;
            return true;
        }

        private static ITypeSymbol? GetNaturalExpressionType(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            var typeInfo = semanticModel.GetTypeInfo(expression, cancellationToken);
            return typeInfo.Type ?? typeInfo.ConvertedType;
        }

        private static bool CanUnboxExactRuntimeTypeToValueType(ITypeSymbol exactRuntimeType, ITypeSymbol targetType)
        {
            if (!IsNonNullableValueType(targetType))
            {
                return false;
            }

            return SymbolEqualityComparer.Default.Equals(exactRuntimeType, targetType);
        }

        private static bool CanCastExactRuntimeTypeToReferenceType(
            ITypeSymbol exactRuntimeType,
            ITypeSymbol targetType,
            Compilation compilation)
        {
            if (targetType.TypeKind == TypeKind.Dynamic ||
                exactRuntimeType.TypeKind == TypeKind.Dynamic)
            {
                return true;
            }

            if (IsReferenceType(targetType) &&
                targetType.SpecialType == SpecialType.System_Object)
            {
                return true;
            }

            var conversion = compilation.ClassifyCommonConversion(exactRuntimeType, targetType);
            return conversion.Exists &&
                (conversion.IsIdentity || conversion.IsImplicit);
        }

        private static bool IsKnownMissingNullableValueByPriorAssignment(
            ExpressionSyntax nullableExpression,
            SyntaxNode useNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            nullableExpression = UnwrapFactExpression(nullableExpression);
            if (IsMissingNullableValueExpression(nullableExpression, semanticModel, cancellationToken))
            {
                return true;
            }

            var symbol = GetLocalOrParameterSymbol(nullableExpression, semanticModel, cancellationToken);
            if (symbol == null ||
                !IsNullableType(GetTrackedSymbolType(symbol)) ||
                !TryResolveCurrentNullableValueExpression(
                    symbol,
                    useNode,
                    semanticModel,
                    cancellationToken,
                    out var currentValueExpression))
            {
                return false;
            }

            return IsMissingNullableValueExpression(currentValueExpression, semanticModel, cancellationToken);
        }

        private static bool TryResolveCurrentNullableValueExpression(
            ISymbol symbol,
            SyntaxNode useNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            out ExpressionSyntax valueExpression)
        {
            valueExpression = null!;
            ExpressionSyntax? currentValue = null;
            foreach (var (block, containingStatement) in EnumerateContainingBlocks(useNode).Reverse())
            {
                foreach (var statement in block.Statements)
                {
                    if (ReferenceEquals(statement, containingStatement))
                    {
                        break;
                    }

                    if (statement is LocalDeclarationStatementSyntax localDeclaration)
                    {
                        foreach (var declarator in localDeclaration.Declaration.Variables)
                        {
                            if (semanticModel.GetDeclaredSymbol(declarator, cancellationToken) is ILocalSymbol localSymbol &&
                                SymbolEqualityComparer.Default.Equals(localSymbol.OriginalDefinition, symbol))
                            {
                                currentValue = declarator.Initializer?.Value;
                            }
                        }

                        if (StatementMutatesSymbolExceptLinearAssignment(statement, symbol, semanticModel, cancellationToken))
                        {
                            currentValue = null;
                        }

                        continue;
                    }

                    if (statement is ExpressionStatementSyntax
                        {
                            Expression: AssignmentExpressionSyntax assignment
                        } &&
                        ExpressionMatchesSymbol(assignment.Left, symbol, semanticModel, cancellationToken))
                    {
                        if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
                            ExpressionReferencesSymbol(assignment.Right, symbol, semanticModel, cancellationToken))
                        {
                            currentValue = null;
                            continue;
                        }

                        currentValue = assignment.Right;
                        continue;
                    }

                    if (StatementMutatesSymbolExceptLinearAssignment(statement, symbol, semanticModel, cancellationToken))
                    {
                        currentValue = null;
                    }
                }
            }

            if (currentValue == null)
            {
                return false;
            }

            valueExpression = currentValue;
            return true;
        }

        private static bool IsMissingNullableValueExpression(
            ExpressionSyntax valueExpression,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            valueExpression = UnwrapFactExpression(valueExpression);
            var expressionType = GetExpressionType(valueExpression, semanticModel, cancellationToken);
            if (!IsNullableType(expressionType))
            {
                return false;
            }

            if (semanticModel.GetConstantValue(valueExpression, cancellationToken) is { HasValue: true, Value: null })
            {
                return true;
            }

            if (IsDefaultExpressionSyntax(valueExpression))
            {
                return true;
            }

            return valueExpression is ObjectCreationExpressionSyntax { ArgumentList.Arguments.Count: 0 } ||
                valueExpression is ImplicitObjectCreationExpressionSyntax { ArgumentList.Arguments.Count: 0 };
        }

        private static bool IsDefinitelyOutOfRangeBuiltInIndexAccess(
            ElementAccessExpressionSyntax elementAccess,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            SmtAnalysisService smtAnalysis)
        {
            if (!IsBuiltInSequenceElementAccess(elementAccess, semanticModel, cancellationToken) ||
                IsBuiltInRangeAccessArgument(
                    elementAccess.ArgumentList.Arguments[0].Expression,
                    semanticModel,
                    cancellationToken))
            {
                return false;
            }

            if (!TryTranslateBuiltInElementAccessInRangeForExceptionFlow(
                    elementAccess,
                    semanticModel,
                    cancellationToken,
                    out var inRangeFormula))
            {
                return false;
            }

            return IsDefinitelyFalseAtUse(elementAccess, inRangeFormula, semanticModel, cancellationToken, smtAnalysis);
        }

        private static bool IsDefinitelyOutOfRangeBuiltInRangeAccess(
            ElementAccessExpressionSyntax elementAccess,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            SmtAnalysisService smtAnalysis)
        {
            if (!IsBuiltInSequenceElementAccess(elementAccess, semanticModel, cancellationToken) ||
                !IsBuiltInRangeAccessArgument(
                    elementAccess.ArgumentList.Arguments[0].Expression,
                    semanticModel,
                    cancellationToken))
            {
                return false;
            }

            if (!TryTranslateBuiltInRangeAccessInRangeForExceptionFlow(
                    elementAccess,
                    semanticModel,
                    cancellationToken,
                    out var inRangeFormula))
            {
                return false;
            }

            return IsDefinitelyFalseAtUse(elementAccess, inRangeFormula, semanticModel, cancellationToken, smtAnalysis);
        }

        private static bool IsDefinitelyOutOfRangeBuiltInSliceCall(
            InvocationExpressionSyntax invocation,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            SmtAnalysisService smtAnalysis)
        {
            if (!TryTranslateBuiltInSliceCallInRangeForExceptionFlow(
                    invocation,
                    semanticModel,
                    cancellationToken,
                    out var inRangeFormula))
            {
                return false;
            }

            return IsDefinitelyFalseAtUse(invocation, inRangeFormula, semanticModel, cancellationToken, smtAnalysis);
        }

        private static bool IsDefinitelyFalseAtUse(
            SyntaxNode useNode,
            SmtFormula formula,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            SmtAnalysisService smtAnalysis)
        {
            var outOfRangeFormula = new SmtUnaryFormula(SmtUnaryOperator.Not, formula);

            var pathConditions = CollectPathConditionsForUse(
                useNode,
                CollectLocalAndParameterSymbols(useNode, semanticModel, cancellationToken),
                semanticModel,
                cancellationToken);

            return PathConditionsAreSatisfiable(pathConditions, smtAnalysis) &&
                PathConditionsImplyFact(pathConditions, outOfRangeFormula, smtAnalysis);
        }

        private static bool IsDefinitelyTrueAtUse(
            SyntaxNode useNode,
            SmtFormula formula,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            SmtAnalysisService smtAnalysis)
        {
            var pathConditions = CollectPathConditionsForUse(
                useNode,
                CollectLocalAndParameterSymbols(useNode, semanticModel, cancellationToken),
                semanticModel,
                cancellationToken);

            return PathConditionsAreSatisfiable(pathConditions, smtAnalysis) &&
                PathConditionsImplyFact(pathConditions, formula, smtAnalysis);
        }

        private static bool IsBuiltInSequenceElementAccess(
            ElementAccessExpressionSyntax elementAccess,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            var argumentCount = elementAccess.ArgumentList.Arguments.Count;
            if (argumentCount == 0)
            {
                return false;
            }

            var receiverTypeInfo = semanticModel.GetTypeInfo(elementAccess.Expression, cancellationToken);
            var receiverType = receiverTypeInfo.ConvertedType ?? receiverTypeInfo.Type;
            if (receiverType is IArrayTypeSymbol arrayType)
            {
                return arrayType.Rank == argumentCount;
            }

            return argumentCount == 1 &&
                (receiverType?.SpecialType == SpecialType.System_String ||
                 IsBuiltInSpanType(receiverType));
        }

        private static bool IsBuiltInSpanType(ITypeSymbol? typeSymbol)
        {
            return typeSymbol is INamedTypeSymbol namedType &&
                namedType.OriginalDefinition.ToDisplayString() is "System.Span<T>" or "System.ReadOnlySpan<T>";
        }

        private static bool TryTranslateBuiltInSliceCallInRangeForExceptionFlow(
            InvocationExpressionSyntax invocation,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            out SmtFormula inRangeFormula)
        {
            inRangeFormula = null!;
            if (!TryGetBuiltInSliceCallParts(
                    invocation,
                    semanticModel,
                    cancellationToken,
                    out var receiverExpression,
                    out var startExpression,
                    out var lengthExpression) ||
                !CSharpConditionToFormula.TryTranslateBuiltInLengthValue(
                    receiverExpression,
                    semanticModel,
                    cancellationToken,
                    out var receiverLengthFormula) ||
                receiverLengthFormula is not { Kind: SmtValueKind.Int } ||
                !TryTranslateIntExpression(startExpression, semanticModel, cancellationToken, out var startFormula))
            {
                return false;
            }

            var nonNegativeStart = new SmtBinaryFormula(
                SmtBinaryOperator.GreaterThanOrEqual,
                startFormula,
                new SmtIntegerConstant(0));

            if (lengthExpression == null)
            {
                var startWithinLength = new SmtBinaryFormula(
                    SmtBinaryOperator.LessThanOrEqual,
                    startFormula,
                    receiverLengthFormula);
                inRangeFormula = new SmtBinaryFormula(SmtBinaryOperator.And, nonNegativeStart, startWithinLength);
                return true;
            }

            if (!TryTranslateIntExpression(lengthExpression, semanticModel, cancellationToken, out var sliceLengthFormula))
            {
                return false;
            }

            var nonNegativeLength = new SmtBinaryFormula(
                SmtBinaryOperator.GreaterThanOrEqual,
                sliceLengthFormula,
                new SmtIntegerConstant(0));
            var end = new SmtIntegerBinaryTerm(
                SmtIntegerBinaryOperator.Add,
                startFormula,
                sliceLengthFormula);
            var endWithinLength = new SmtBinaryFormula(
                SmtBinaryOperator.LessThanOrEqual,
                end,
                receiverLengthFormula);
            inRangeFormula = new SmtBinaryFormula(
                SmtBinaryOperator.And,
                nonNegativeStart,
                new SmtBinaryFormula(SmtBinaryOperator.And, nonNegativeLength, endWithinLength));
            return true;
        }

        private static bool TryGetBuiltInSliceCallParts(
            InvocationExpressionSyntax invocation,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            out ExpressionSyntax receiverExpression,
            out ExpressionSyntax startExpression,
            out ExpressionSyntax? lengthExpression)
        {
            receiverExpression = null!;
            startExpression = null!;
            lengthExpression = null;

            if (semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol
                {
                    Name: "Slice",
                    IsStatic: false,
                    Parameters.Length: >= 1 and <= 2
                } method ||
                !IsBuiltInSpanType(method.ContainingType) ||
                !method.Parameters.All(static parameter => parameter.Type.SpecialType == SpecialType.System_Int32) ||
                invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
                !TryMapInvocationArguments(invocation, method, out var arguments) ||
                arguments[0] == null)
            {
                return false;
            }

            receiverExpression = memberAccess.Expression;
            startExpression = arguments[0]!;
            lengthExpression = arguments.Length == 2
                ? arguments[1]
                : null;
            return lengthExpression != null || method.Parameters.Length == 1;
        }

        private static bool TryMapInvocationArguments(
            InvocationExpressionSyntax invocation,
            IMethodSymbol method,
            out ExpressionSyntax?[] arguments)
        {
            arguments = new ExpressionSyntax?[method.Parameters.Length];
            var nextOrdinal = 0;
            foreach (var argument in invocation.ArgumentList.Arguments)
            {
                var targetOrdinal = -1;
                if (argument.NameColon != null)
                {
                    var name = argument.NameColon.Name.Identifier.ValueText;
                    for (var parameterIndex = 0; parameterIndex < method.Parameters.Length; parameterIndex++)
                    {
                        if (string.Equals(method.Parameters[parameterIndex].Name, name, StringComparison.Ordinal))
                        {
                            targetOrdinal = parameterIndex;
                            break;
                        }
                    }
                }
                else
                {
                    while (nextOrdinal < arguments.Length && arguments[nextOrdinal] != null)
                    {
                        nextOrdinal++;
                    }

                    targetOrdinal = nextOrdinal++;
                }

                if (targetOrdinal < 0 || targetOrdinal >= arguments.Length || arguments[targetOrdinal] != null)
                {
                    arguments = Array.Empty<ExpressionSyntax?>();
                    return false;
                }

                arguments[targetOrdinal] = argument.Expression;
            }

            return arguments.All(static argument => argument != null);
        }

        private static bool TryTranslateIntExpression(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            out SmtFormula formula)
        {
            if (CSharpConditionToFormula.TryTranslateValue(
                    expression,
                    semanticModel,
                    cancellationToken,
                    out var translatedFormula,
                    getSymbolVersion: null) &&
                translatedFormula is { Kind: SmtValueKind.Int })
            {
                formula = translatedFormula;
                return true;
            }

            formula = null!;
            return false;
        }

        private static bool IsBuiltInRangeAccessArgument(
            ExpressionSyntax argumentExpression,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            argumentExpression = UnwrapFactExpression(argumentExpression);
            if (argumentExpression is RangeExpressionSyntax)
            {
                return true;
            }

            var typeInfo = semanticModel.GetTypeInfo(argumentExpression, cancellationToken);
            return IsSystemRangeType(typeInfo.ConvertedType ?? typeInfo.Type);
        }

        private static bool TryTranslateBuiltInElementAccessInRangeForExceptionFlow(
            ElementAccessExpressionSyntax elementAccess,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            out SmtFormula inRangeFormula)
        {
            if (CSharpConditionToFormula.TryTranslateBuiltInElementAccessInRange(
                    elementAccess,
                    semanticModel,
                    cancellationToken,
                    out inRangeFormula))
            {
                return true;
            }

            if (!CSharpConditionToFormula.TryTranslateBuiltInLengthValue(
                    elementAccess.Expression,
                    semanticModel,
                    cancellationToken,
                    out var lengthFormula) ||
                lengthFormula is not { Kind: SmtValueKind.Int } ||
                !TryCreateEffectiveSystemIndexVariableFormula(
                    elementAccess.ArgumentList.Arguments[0].Expression,
                    elementAccess,
                    lengthFormula,
                    semanticModel,
                    cancellationToken,
                    out var indexFormula))
            {
                inRangeFormula = null!;
                return false;
            }

            var lowerBound = new SmtBinaryFormula(
                SmtBinaryOperator.GreaterThanOrEqual,
                indexFormula,
                new SmtIntegerConstant(0));
            var upperBound = new SmtBinaryFormula(
                SmtBinaryOperator.LessThan,
                indexFormula,
                lengthFormula);
            inRangeFormula = new SmtBinaryFormula(SmtBinaryOperator.And, lowerBound, upperBound);
            return true;
        }

        private static bool TryTranslateBuiltInRangeAccessInRangeForExceptionFlow(
            ElementAccessExpressionSyntax elementAccess,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            out SmtFormula inRangeFormula)
        {
            if (CSharpConditionToFormula.TryTranslateBuiltInElementAccessInRange(
                    elementAccess,
                    semanticModel,
                    cancellationToken,
                    out inRangeFormula))
            {
                return true;
            }

            if (!CSharpConditionToFormula.TryTranslateBuiltInLengthValue(
                    elementAccess.Expression,
                    semanticModel,
                    cancellationToken,
                    out var lengthFormula) ||
                lengthFormula is not { Kind: SmtValueKind.Int } ||
                !TryCreateSystemRangeVariableInRangeFormula(
                    elementAccess.ArgumentList.Arguments[0].Expression,
                    elementAccess,
                    lengthFormula,
                    semanticModel,
                    cancellationToken,
                    out inRangeFormula))
            {
                inRangeFormula = null!;
                return false;
            }

            return true;
        }

        private static bool TryCreateSystemRangeVariableInRangeFormula(
            ExpressionSyntax rangeExpression,
            SyntaxNode useNode,
            SmtFormula lengthFormula,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            out SmtFormula inRangeFormula)
        {
            rangeExpression = UnwrapFactExpression(rangeExpression);
            if (!TryResolveCurrentSystemRangeValueExpression(
                    rangeExpression,
                    useNode,
                    semanticModel,
                    cancellationToken,
                    out var valueExpression))
            {
                inRangeFormula = null!;
                return false;
            }

            valueExpression = UnwrapFactExpression(valueExpression);
            if (valueExpression is not RangeExpressionSyntax resolvedRange ||
                !TryCreateEffectiveRangeEndpointFormula(
                    resolvedRange.LeftOperand,
                    lengthFormula,
                    defaultWhenOmitted: new SmtIntegerConstant(0),
                    semanticModel,
                    cancellationToken,
                    out var startFormula) ||
                !TryCreateEffectiveRangeEndpointFormula(
                    resolvedRange.RightOperand,
                    lengthFormula,
                    defaultWhenOmitted: lengthFormula,
                    semanticModel,
                    cancellationToken,
                    out var endFormula))
            {
                inRangeFormula = null!;
                return false;
            }

            var nonNegativeStart = new SmtBinaryFormula(
                SmtBinaryOperator.GreaterThanOrEqual,
                startFormula,
                new SmtIntegerConstant(0));
            var orderedEndpoints = new SmtBinaryFormula(
                SmtBinaryOperator.LessThanOrEqual,
                startFormula,
                endFormula);
            var endWithinLength = new SmtBinaryFormula(
                SmtBinaryOperator.LessThanOrEqual,
                endFormula,
                lengthFormula);
            inRangeFormula = new SmtBinaryFormula(
                SmtBinaryOperator.And,
                nonNegativeStart,
                new SmtBinaryFormula(SmtBinaryOperator.And, orderedEndpoints, endWithinLength));
            return true;
        }

        private static bool TryCreateEffectiveRangeEndpointFormula(
            ExpressionSyntax? endpointExpression,
            SmtFormula lengthFormula,
            SmtFormula defaultWhenOmitted,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            out SmtFormula endpointFormula)
        {
            if (endpointExpression == null)
            {
                endpointFormula = defaultWhenOmitted;
                return true;
            }

            return TryCreateEffectiveIndexExpressionFormula(
                endpointExpression,
                lengthFormula,
                semanticModel,
                cancellationToken,
                out endpointFormula);
        }

        private static bool TryCreateEffectiveSystemIndexVariableFormula(
            ExpressionSyntax indexExpression,
            SyntaxNode useNode,
            SmtFormula lengthFormula,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            out SmtFormula indexFormula)
        {
            indexExpression = UnwrapFactExpression(indexExpression);
            if (!TryResolveCurrentSystemIndexValueExpression(
                    indexExpression,
                    useNode,
                    semanticModel,
                    cancellationToken,
                    out var valueExpression))
            {
                indexFormula = null!;
                return false;
            }

            return TryCreateEffectiveIndexExpressionFormula(
                valueExpression,
                lengthFormula,
                semanticModel,
                cancellationToken,
                out indexFormula);
        }

        private static bool TryResolveCurrentSystemIndexValueExpression(
            ExpressionSyntax indexExpression,
            SyntaxNode useNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            out ExpressionSyntax valueExpression)
        {
            valueExpression = null!;
            var symbol = GetLocalOrParameterSymbol(indexExpression, semanticModel, cancellationToken);
            if (symbol == null ||
                !IsSystemIndexType(GetTrackedSymbolType(symbol)))
            {
                return false;
            }

            ExpressionSyntax? currentValue = null;
            foreach (var (block, containingStatement) in EnumerateContainingBlocks(useNode).Reverse())
            {
                foreach (var statement in block.Statements)
                {
                    if (ReferenceEquals(statement, containingStatement))
                    {
                        break;
                    }

                    if (statement is LocalDeclarationStatementSyntax localDeclaration)
                    {
                        foreach (var declarator in localDeclaration.Declaration.Variables)
                        {
                            if (semanticModel.GetDeclaredSymbol(declarator, cancellationToken) is ILocalSymbol localSymbol &&
                                SymbolEqualityComparer.Default.Equals(localSymbol.OriginalDefinition, symbol))
                            {
                                currentValue = declarator.Initializer?.Value;
                            }
                        }

                        if (StatementMutatesSymbolExceptLinearAssignment(statement, symbol, semanticModel, cancellationToken))
                        {
                            currentValue = null;
                        }

                        continue;
                    }

                    if (statement is ExpressionStatementSyntax
                        {
                            Expression: AssignmentExpressionSyntax assignment
                        } &&
                        ExpressionMatchesSymbol(assignment.Left, symbol, semanticModel, cancellationToken))
                    {
                        if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
                            ExpressionReferencesSymbol(assignment.Right, symbol, semanticModel, cancellationToken))
                        {
                            currentValue = null;
                            continue;
                        }

                        currentValue = assignment.Right;
                        continue;
                    }

                    if (StatementMutatesSymbolExceptLinearAssignment(statement, symbol, semanticModel, cancellationToken))
                    {
                        currentValue = null;
                    }
                }
            }

            if (currentValue == null)
            {
                return false;
            }

            valueExpression = currentValue;
            return true;
        }

        private static bool TryResolveCurrentSystemRangeValueExpression(
            ExpressionSyntax rangeExpression,
            SyntaxNode useNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            out ExpressionSyntax valueExpression)
        {
            valueExpression = null!;
            var symbol = GetLocalOrParameterSymbol(rangeExpression, semanticModel, cancellationToken);
            if (symbol == null ||
                !IsSystemRangeType(GetTrackedSymbolType(symbol)))
            {
                return false;
            }

            ExpressionSyntax? currentValue = null;
            foreach (var (block, containingStatement) in EnumerateContainingBlocks(useNode).Reverse())
            {
                foreach (var statement in block.Statements)
                {
                    if (ReferenceEquals(statement, containingStatement))
                    {
                        break;
                    }

                    if (statement is LocalDeclarationStatementSyntax localDeclaration)
                    {
                        foreach (var declarator in localDeclaration.Declaration.Variables)
                        {
                            if (semanticModel.GetDeclaredSymbol(declarator, cancellationToken) is ILocalSymbol localSymbol &&
                                SymbolEqualityComparer.Default.Equals(localSymbol.OriginalDefinition, symbol))
                            {
                                currentValue = declarator.Initializer?.Value;
                            }
                        }

                        if (StatementMutatesSymbolExceptLinearAssignment(statement, symbol, semanticModel, cancellationToken))
                        {
                            currentValue = null;
                        }

                        continue;
                    }

                    if (statement is ExpressionStatementSyntax
                        {
                            Expression: AssignmentExpressionSyntax assignment
                        } &&
                        ExpressionMatchesSymbol(assignment.Left, symbol, semanticModel, cancellationToken))
                    {
                        if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
                            ExpressionReferencesSymbol(assignment.Right, symbol, semanticModel, cancellationToken))
                        {
                            currentValue = null;
                            continue;
                        }

                        currentValue = assignment.Right;
                        continue;
                    }

                    if (StatementMutatesSymbolExceptLinearAssignment(statement, symbol, semanticModel, cancellationToken))
                    {
                        currentValue = null;
                    }
                }
            }

            if (currentValue == null)
            {
                return false;
            }

            valueExpression = currentValue;
            return true;
        }

        private static bool StatementMutatesSymbolExceptLinearAssignment(
            StatementSyntax statement,
            ISymbol symbol,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            foreach (var node in statement.DescendantNodesAndSelf(
                         descendIntoChildren: candidate => !ExecutionVisibility.IsNestedCallableBoundary(candidate)))
            {
                if (MutatesSymbol(node, symbol, semanticModel, cancellationToken))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryCreateEffectiveIndexExpressionFormula(
            ExpressionSyntax expression,
            SmtFormula lengthFormula,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            out SmtFormula indexFormula)
        {
            expression = UnwrapFactExpression(expression);
            if (expression is PrefixUnaryExpressionSyntax fromEndIndex &&
                fromEndIndex.OperatorToken.IsKind(SyntaxKind.CaretToken))
            {
                if (!CSharpConditionToFormula.TryTranslateValue(
                        fromEndIndex.Operand,
                        semanticModel,
                        cancellationToken,
                        out var fromEndOffset,
                        getSymbolVersion: null) ||
                    fromEndOffset is not { Kind: SmtValueKind.Int })
                {
                    indexFormula = null!;
                    return false;
                }

                indexFormula = new SmtIntegerBinaryTerm(
                    SmtIntegerBinaryOperator.Subtract,
                    lengthFormula,
                    fromEndOffset);
                return true;
            }

            if (!CSharpConditionToFormula.TryTranslateValue(
                    expression,
                    semanticModel,
                    cancellationToken,
                    out var ordinaryIndex,
                    getSymbolVersion: null) ||
                ordinaryIndex is not { Kind: SmtValueKind.Int })
            {
                indexFormula = null!;
                return false;
            }

            indexFormula = ordinaryIndex;
            return true;
        }

        private static bool IsSystemIndexType(ITypeSymbol? typeSymbol)
        {
            return typeSymbol is INamedTypeSymbol
            {
                Name: "Index",
                ContainingNamespace: { } containingNamespace
            } &&
            containingNamespace.ToDisplayString() == "System";
        }

        private static bool IsSystemRangeType(ITypeSymbol? typeSymbol)
        {
            return typeSymbol is INamedTypeSymbol
            {
                Name: "Range",
                ContainingNamespace: { } containingNamespace
            } &&
            containingNamespace.ToDisplayString() == "System";
        }

        private static bool IsNullableValueAccess(
            MemberAccessExpressionSyntax memberAccess,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            if (memberAccess.Name.Identifier.ValueText != "Value" ||
                semanticModel.GetSymbolInfo(memberAccess, cancellationToken).Symbol is not IPropertySymbol
                {
                    Name: "Value",
                    ContainingType.OriginalDefinition.SpecialType: SpecialType.System_Nullable_T
                })
            {
                return false;
            }

            return true;
        }

        private static bool IsNullableType(ITypeSymbol? typeSymbol)
        {
            return typeSymbol is INamedTypeSymbol
            {
                OriginalDefinition.SpecialType: SpecialType.System_Nullable_T
            };
        }

        private static bool IsNonNullableValueType(ITypeSymbol? typeSymbol)
        {
            return typeSymbol?.IsValueType == true &&
                !IsNullableType(typeSymbol);
        }

        private static bool IsReferenceType(ITypeSymbol? typeSymbol)
        {
            if (typeSymbol == null)
            {
                return false;
            }

            if (typeSymbol is ITypeParameterSymbol typeParameter)
            {
                return IsKnownReferenceTypeParameter(
                    typeParameter,
                    new HashSet<ITypeParameterSymbol>(SymbolEqualityComparer.Default));
            }

            return typeSymbol.IsReferenceType;
        }

        private static bool IsKnownReferenceTypeParameter(
            ITypeParameterSymbol typeParameter,
            HashSet<ITypeParameterSymbol> visited)
        {
            if (!visited.Add(typeParameter))
            {
                return false;
            }

            if (typeParameter.HasReferenceTypeConstraint)
            {
                return true;
            }

            return typeParameter.ConstraintTypes.Any(constraint =>
                constraint.IsReferenceType ||
                constraint is ITypeParameterSymbol nestedTypeParameter &&
                IsKnownReferenceTypeParameter(nestedTypeParameter, visited));
        }

        private static ITypeSymbol? GetRethrownExceptionType(
            ThrowStatementSyntax throwStatement,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            foreach (var catchClause in throwStatement.Ancestors().OfType<CatchClauseSyntax>())
            {
                if (!catchClause.Block.Span.Contains(throwStatement.SpanStart))
                {
                    continue;
                }

                if (catchClause.Declaration == null)
                {
                    return null;
                }

                return semanticModel.GetTypeInfo(catchClause.Declaration.Type, cancellationToken).Type;
            }

            return null;
        }
    }
}

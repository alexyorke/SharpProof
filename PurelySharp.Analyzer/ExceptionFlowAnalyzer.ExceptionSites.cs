using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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

                if (IsDefinitelyZeroExpression(binaryExpression.Right, binaryExpression, semanticModel, cancellationToken, smtAnalysis))
                {
                    yield return binaryExpression;
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
                    IsDefinitelyNullExpression(memberAccess.Expression, memberAccess, semanticModel, cancellationToken, smtAnalysis))
                {
                    yield return memberAccess;
                }
                else if (node is ElementAccessExpressionSyntax elementAccess &&
                    IsDefinitelyNullExpression(elementAccess.Expression, elementAccess, semanticModel, cancellationToken, smtAnalysis))
                {
                    yield return elementAccess;
                }
                else if (node is InvocationExpressionSyntax invocation &&
                    IsDefinitelyNullExpression(invocation.Expression, invocation, semanticModel, cancellationToken, smtAnalysis))
                {
                    yield return invocation;
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

        private static bool IsDefinitelyOutOfRangeBuiltInIndexAccess(
            ElementAccessExpressionSyntax elementAccess,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            SmtAnalysisService smtAnalysis)
        {
            if (elementAccess.ArgumentList.Arguments.Count != 1)
            {
                return false;
            }

            var receiverTypeInfo = semanticModel.GetTypeInfo(elementAccess.Expression, cancellationToken);
            var receiverType = receiverTypeInfo.ConvertedType ?? receiverTypeInfo.Type;
            if (receiverType is not IArrayTypeSymbol { Rank: 1 } &&
                receiverType?.SpecialType != SpecialType.System_String)
            {
                return false;
            }

            if (!TryCreateBuiltInLengthValueFormula(
                    elementAccess.Expression,
                    semanticModel,
                    cancellationToken,
                    out var lengthFormula))
            {
                return false;
            }

            var indexExpression = elementAccess.ArgumentList.Arguments[0].Expression;
            if (!TryCreateEffectiveBuiltInIndexFormula(
                    indexExpression,
                    lengthFormula,
                    semanticModel,
                    cancellationToken,
                    out var indexFormula))
            {
                return false;
            }

            var lowerBoundViolation = new SmtBinaryFormula(
                SmtBinaryOperator.LessThan,
                indexFormula,
                new SmtIntegerConstant(0));
            var upperBoundViolation = new SmtBinaryFormula(
                SmtBinaryOperator.GreaterThanOrEqual,
                indexFormula,
                lengthFormula);
            var outOfRangeFormula = new SmtBinaryFormula(
                SmtBinaryOperator.Or,
                lowerBoundViolation,
                upperBoundViolation);

            var pathConditions = CollectPathConditionsForUse(
                elementAccess,
                CollectLocalAndParameterSymbols(elementAccess, semanticModel, cancellationToken),
                semanticModel,
                cancellationToken);

            return PathConditionsImplyFact(pathConditions, outOfRangeFormula, smtAnalysis);
        }

        private static bool TryCreateEffectiveBuiltInIndexFormula(
            ExpressionSyntax indexExpression,
            SmtFormula lengthFormula,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            out SmtFormula indexFormula)
        {
            indexFormula = null!;
            indexExpression = UnwrapIndexExpression(indexExpression);
            if (indexExpression is PrefixUnaryExpressionSyntax fromEndIndex &&
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
                    return false;
                }

                indexFormula = new SmtIntegerBinaryTerm(
                    SmtIntegerBinaryOperator.Subtract,
                    lengthFormula,
                    fromEndOffset);
                return true;
            }

            if (!CSharpConditionToFormula.TryTranslateValue(
                    indexExpression,
                    semanticModel,
                    cancellationToken,
                    out var ordinaryIndex,
                    getSymbolVersion: null) ||
                ordinaryIndex is not { Kind: SmtValueKind.Int })
            {
                return false;
            }

            indexFormula = ordinaryIndex;
            return true;
        }

        private static ExpressionSyntax UnwrapIndexExpression(ExpressionSyntax expression)
        {
            while (expression is ParenthesizedExpressionSyntax parenthesized)
            {
                expression = parenthesized.Expression;
            }

            return expression;
        }

        private static bool IsReferenceType(ITypeSymbol? typeSymbol)
        {
            return typeSymbol != null &&
                typeSymbol.TypeKind != TypeKind.TypeParameter &&
                typeSymbol.IsReferenceType;
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

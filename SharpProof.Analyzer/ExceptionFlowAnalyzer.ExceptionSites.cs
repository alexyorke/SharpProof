using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Analyzer.Engine;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Smt;
using SearchLib.Smt;

namespace SharpProof.Analyzer
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

                var rightTypeInfo = semanticModel.GetTypeInfo(binaryExpression.Right, cancellationToken);
                var rightType = rightTypeInfo.ConvertedType ?? rightTypeInfo.Type;
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

        internal static IEnumerable<ArrayCreationExpressionSyntax> GetDefiniteNegativeArrayLengthNodes(
            SyntaxNode methodNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            SmtAnalysisService smtAnalysis)
        {
            foreach (var arrayCreation in GetRelevantDescendants<ArrayCreationExpressionSyntax>(methodNode))
            {
                if (IsDefinitelyNegativeArrayLength(arrayCreation, semanticModel, cancellationToken, smtAnalysis) &&
                    IsExceptionPathReachable(arrayCreation, semanticModel, cancellationToken, smtAnalysis))
                {
                    yield return arrayCreation;
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
                         !IsDynamicExpression(invocation.Expression, semanticModel, cancellationToken) &&
                         IsDefinitelyNullExpression(invocation.Expression, invocation, semanticModel, cancellationToken, smtAnalysis) &&
                         IsExceptionPathReachable(invocation, semanticModel, cancellationToken, smtAnalysis))
                {
                    yield return invocation;
                }
                else if (node is AwaitExpressionSyntax awaitExpression &&
                         IsReferenceDereferenceReceiver(awaitExpression.Expression, semanticModel, cancellationToken) &&
                         IsDefinitelyNullExpression(awaitExpression.Expression, awaitExpression, semanticModel, cancellationToken, smtAnalysis) &&
                         IsExceptionPathReachable(awaitExpression, semanticModel, cancellationToken, smtAnalysis))
                {
                    yield return awaitExpression;
                }
            }
        }

        internal static IEnumerable<LockStatementSyntax> GetDefiniteLockNullNodes(
            SyntaxNode methodNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            SmtAnalysisService smtAnalysis)
        {
            foreach (var lockStatement in GetRelevantDescendants<LockStatementSyntax>(methodNode))
            {
                if (IsReferenceDereferenceReceiver(lockStatement.Expression, semanticModel, cancellationToken) &&
                    IsDefinitelyNullExpression(lockStatement.Expression, lockStatement, semanticModel, cancellationToken, smtAnalysis) &&
                    IsExceptionPathReachable(lockStatement, semanticModel, cancellationToken, smtAnalysis))
                {
                    yield return lockStatement;
                }
            }
        }

        internal static IEnumerable<DynamicNullBindingSite> GetDefiniteDynamicNullBindingSites(
            SyntaxNode methodNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            SmtAnalysisService smtAnalysis)
        {
            foreach (var node in GetRelevantDescendants<SyntaxNode>(methodNode))
            {
                if (SymbolicDynamicNullBindingFacts.TryGetDynamicNullBindingShape(
                        node,
                        UnwrapFactExpression,
                        out var site,
                        out var receiver,
                        out var category,
                        out var source) &&
                    IsDefiniteDynamicNullReceiver(receiver, site, semanticModel, cancellationToken, smtAnalysis))
                {
                    yield return new DynamicNullBindingSite(
                        site,
                        category,
                        source);
                }
            }
        }

        private static bool IsDefiniteDynamicNullReceiver(
            ExpressionSyntax receiver,
            SyntaxNode site,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            SmtAnalysisService smtAnalysis)
        {
            return IsDynamicExpression(receiver, semanticModel, cancellationToken) &&
                IsDefinitelyNullExpression(receiver, site, semanticModel, cancellationToken, smtAnalysis) &&
                IsExceptionPathReachable(site, semanticModel, cancellationToken, smtAnalysis);
        }

        private static bool IsReferenceDereferenceReceiver(
            ExpressionSyntax receiver,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            return !IsDynamicExpression(receiver, semanticModel, cancellationToken) &&
                IsReferenceType(GetExpressionType(receiver, semanticModel, cancellationToken));
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

        internal static IEnumerable<InvocationExpressionSyntax> GetDefiniteArrayGetValueIndexOutOfRangeNodes(
            SyntaxNode methodNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            SmtAnalysisService smtAnalysis)
        {
            foreach (var invocation in GetRelevantDescendants<InvocationExpressionSyntax>(methodNode))
            {
                if (IsDefinitelyOutOfRangeArrayGetValueCall(invocation, semanticModel, cancellationToken, smtAnalysis))
                {
                    yield return invocation;
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
            if (throwNode is not ThrowStatementSyntax and not ThrowExpressionSyntax)
            {
                return null;
            }

            return SymbolicRuntimeExceptionFacts.GetThrownExceptionType(
                throwNode,
                semanticModel,
                cancellationToken,
                stopAtUntypedCatch: true);
        }

        internal static bool IsDefinitelyThrowNull(
            SyntaxNode throwNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            SmtAnalysisService smtAnalysis)
        {
            return SymbolicRuntimeExceptionFacts.TryGetThrowExpression(throwNode, out var expression) &&
                IsDefinitelyNullExpression(expression, throwNode, semanticModel, cancellationToken, smtAnalysis);
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
            return SymbolicValueFacts.IsIntegralOrDecimalZero(value);
        }

        private static bool IsThrowingDivideByZeroType(ITypeSymbol? typeSymbol)
        {
            return SymbolicTypeFacts.IsThrowingDivideByZeroType(typeSymbol);
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
                        return IsReferenceLikeType(castType);
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
                return IsReferenceLikeType(defaultType);
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

            if (!SymbolicReachabilityService.TryCreateNullableHasValueCondition(
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
                !SymbolicReachabilityService.TryCreateIntegerBinaryInRangeCondition(
                    binaryExpression.Left,
                    binaryExpression.Right,
                    smtOperator,
                    semanticModel,
                    cancellationToken,
                    minValue,
                    maxValue,
                    out var inRangeFormula))
            {
                return false;
            }

            return IsDefinitelyFalseAtUse(
                binaryExpression,
                inRangeFormula,
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
                SymbolicReachabilityService.TryCreateIntegerUnaryInRangeCondition(
                    unaryExpression.Operand,
                    SmtIntegerUnaryOperator.Negate,
                    semanticModel,
                    cancellationToken,
                    minValue,
                    maxValue,
                    out var inRangeFormula))
            {
                return IsDefinitelyFalseAtUse(
                    unaryExpression,
                    inRangeFormula,
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
                !SymbolicReachabilityService.TryCreateIntegerIncrementOrDecrementInRangeCondition(
                    operand,
                    smtOperator,
                    semanticModel,
                    cancellationToken,
                    minValue,
                    maxValue,
                    out var inRangeFormula))
            {
                return false;
            }

            return IsDefinitelyFalseAtUse(
                updateExpression,
                inRangeFormula,
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
            if (!TryGetCheckedExplicitNumericConversionRange(castExpression, semanticModel, cancellationToken, out var minValue, out var maxValue) ||
                !SymbolicReachabilityService.TryCreateIntegerInRangeCondition(
                    castExpression.Expression,
                    semanticModel,
                    cancellationToken,
                    minValue,
                    maxValue,
                    out var inRangeFormula))
            {
                return false;
            }

            return IsDefinitelyFalseAtUse(
                castExpression,
                inRangeFormula,
                semanticModel,
                cancellationToken,
                smtAnalysis);
        }

        private static bool IsDefinitelyNegativeArrayLength(
            ArrayCreationExpressionSyntax arrayCreation,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            SmtAnalysisService smtAnalysis)
        {
            foreach (var lengthExpression in GetArrayLengthExpressions(arrayCreation))
            {
                if (!SymbolicReachabilityService.TryCreateNegativeLengthTrigger(
                        lengthExpression,
                        semanticModel,
                        cancellationToken,
                        out var negativeLength))
                {
                    continue;
                }

                if (IsDefinitelyTrueAtUse(arrayCreation, negativeLength, semanticModel, cancellationToken, smtAnalysis))
                {
                    return true;
                }
            }

            return false;
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

        private static bool TryGetCheckedExplicitNumericConversionRange(
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
                !conversionOperation.Conversion.Exists ||
                conversionOperation.Conversion.IsIdentity ||
                conversionOperation.Conversion.IsImplicit ||
                !conversionOperation.Conversion.IsNumeric ||
                conversionOperation.Conversion.IsUserDefined ||
                conversionOperation.Conversion.MethodSymbol != null ||
                !TryGetCheckedNumericConversionRange(
                    SymbolicRuntimeTypeFacts.GetNaturalExpressionType(castExpression, semanticModel, cancellationToken),
                    out minValue,
                    out maxValue))
            {
                return false;
            }

            if (TryGetCheckedNumericConversionRange(
                    SymbolicRuntimeTypeFacts.GetNaturalExpressionType(castExpression.Expression, semanticModel, cancellationToken),
                    out var sourceMinValue,
                    out var sourceMaxValue) &&
                sourceMinValue >= minValue &&
                sourceMaxValue <= maxValue)
            {
                return false;
            }

            return true;
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
            return SymbolicTypeFacts.TryGetCheckedIntegralRange(typeSymbol, out minValue, out maxValue);
        }

        private static bool TryGetBoundedIntegralRange(
            ITypeSymbol? typeSymbol,
            out long minValue,
            out long maxValue)
        {
            return SymbolicTypeFacts.TryGetBoundedIntegralRange(typeSymbol, out minValue, out maxValue);
        }

        private static bool TryGetCheckedNumericConversionRange(
            ITypeSymbol? typeSymbol,
            out long minValue,
            out long maxValue)
        {
            return SymbolicTypeFacts.TryGetCheckedNumericConversionRange(typeSymbol, out minValue, out maxValue);
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
                    !SymbolicRuntimeTypeFacts.TryGetExactRuntimeType(
                        castExpression.Expression,
                        castExpression,
                        semanticModel,
                        cancellationToken,
                        out var exactRuntimeType))
                {
                    return false;
                }

                return !SymbolicRuntimeTypeFacts.CanUnboxExactRuntimeTypeToValueType(exactRuntimeType, targetType);
            }

            var operandType = GetExpressionType(castExpression.Expression, semanticModel, cancellationToken);
            if (!IsReferenceType(targetType) ||
                !IsReferenceType(operandType) ||
                !SymbolicRuntimeTypeFacts.TryGetExactRuntimeType(
                    castExpression.Expression,
                    castExpression,
                    semanticModel,
                    cancellationToken,
                    out var exactReferenceRuntimeType))
            {
                return false;
            }

            return !SymbolicRuntimeTypeFacts.CanCastExactRuntimeTypeToReferenceType(
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
                !SymbolicRuntimeTypeFacts.TryGetExactRuntimeType(
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
                !SymbolicRuntimeTypeFacts.TryGetExactRuntimeType(
                    assignment.Right,
                    assignment,
                    semanticModel,
                    cancellationToken,
                    out var exactAssignedType))
            {
                return false;
            }

            return !SymbolicRuntimeTypeFacts.CanStoreExactRuntimeTypeInArrayElement(
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
            if (!SymbolicReachabilityService.TryCreateBuiltInElementAccessInRangeCondition(
                    elementAccess,
                    semanticModel,
                    cancellationToken,
                    out var inRangeFormula))
            {
                return false;
            }

            return IsDefinitelyTrueAtUse(elementAccess, inRangeFormula, semanticModel, cancellationToken, smtAnalysis);
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

        private static IEnumerable<ExpressionSyntax> GetArrayLengthExpressions(ArrayCreationExpressionSyntax arrayCreation)
        {
            foreach (var rankSpecifier in arrayCreation.Type.RankSpecifiers)
            {
                foreach (var size in rankSpecifier.Sizes)
                {
                    if (!size.IsKind(SyntaxKind.OmittedArraySizeExpression))
                    {
                        yield return size;
                    }
                }
            }
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
                !IsNullableType(SymbolicFactFactory.GetTrackedSymbolType(symbol)) ||
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

            if (!SymbolicReachabilityService.TryCreateBuiltInElementAccessInRangeCondition(
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

            if (!SymbolicReachabilityService.TryCreateBuiltInElementAccessInRangeCondition(
                    elementAccess,
                    semanticModel,
                    cancellationToken,
                    out var inRangeFormula))
            {
                return false;
            }

            return IsDefinitelyFalseAtUse(elementAccess, inRangeFormula, semanticModel, cancellationToken, smtAnalysis);
        }

        private static bool IsDefinitelyOutOfRangeArrayGetValueCall(
            InvocationExpressionSyntax invocation,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            SmtAnalysisService smtAnalysis)
        {
            if (semanticModel.GetOperation(invocation, cancellationToken) is not IInvocationOperation invocationOperation ||
                !IsArrayGetValueInvocation(invocationOperation.TargetMethod) ||
                invocationOperation.Instance?.Syntax is not ExpressionSyntax receiverExpression ||
                !TryGetArrayGetValueRuntimeArrayType(
                    receiverExpression,
                    invocation,
                    semanticModel,
                    cancellationToken,
                    out var arrayType) ||
                invocationOperation.Arguments.Length != arrayType.Rank)
            {
                return false;
            }

            var indexExpressions = new List<ExpressionSyntax>(arrayType.Rank);
            for (var dimension = 0; dimension < arrayType.Rank; dimension++)
            {
                if (!TryGetInvocationArgumentExpression(invocationOperation, dimension, out var indexExpression))
                {
                    return false;
                }

                indexExpressions.Add(indexExpression);
            }

            return SymbolicReachabilityService.TryCreateArrayGetValueIndexesInRangeFormula(
                    receiverExpression,
                    arrayType,
                    indexExpressions,
                    semanticModel,
                    cancellationToken,
                    out var inRangeFormula) &&
                IsDefinitelyFalseAtUse(invocation, inRangeFormula, semanticModel, cancellationToken, smtAnalysis);
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
            var outOfRangeFormula = SmtFormulaFactory.CreateNot(formula);

            var pathConditions = CollectPathConditionsForUse(useNode, semanticModel, cancellationToken);

            return SymbolicReachabilityService.PathConditionsAllowAndImplyWithIrFirst(
                pathConditions,
                outOfRangeFormula,
                useNode,
                smtAnalysis,
                "exception.path.query",
                "exception.path.query");
        }

        private static bool IsDefinitelyTrueAtUse(
            SyntaxNode useNode,
            SmtFormula formula,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            SmtAnalysisService smtAnalysis)
        {
            var pathConditions = CollectPathConditionsForUse(useNode, semanticModel, cancellationToken);

            return SymbolicReachabilityService.PathConditionsAllowAndImplyWithIrFirst(
                pathConditions,
                formula,
                useNode,
                smtAnalysis,
                "exception.path.query",
                "exception.path.query");
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
            return SymbolicTypeFacts.IsBuiltInSpanType(typeSymbol);
        }

        private static bool IsBuiltInSpanOrMemoryType(ITypeSymbol? typeSymbol)
        {
            return SymbolicTypeFacts.IsBuiltInSpanOrMemoryType(typeSymbol);
        }

        private static bool IsArrayGetValueInvocation(IMethodSymbol method)
        {
            return method.Name == "GetValue" &&
                !method.IsStatic &&
                method.ContainingType?.SpecialType == SpecialType.System_Array &&
                method.ReturnType.SpecialType == SpecialType.System_Object &&
                method.Parameters.Length > 0 &&
                method.Parameters.All(static parameter => parameter.Type.SpecialType == SpecialType.System_Int32);
        }

        private static bool TryGetArrayGetValueRuntimeArrayType(
            ExpressionSyntax receiverExpression,
            SyntaxNode useNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            out IArrayTypeSymbol arrayType)
        {
            var receiverType = GetExpressionType(receiverExpression, semanticModel, cancellationToken);
            if (receiverType is IArrayTypeSymbol staticArrayType)
            {
                arrayType = staticArrayType;
                return true;
            }

            if (SymbolicRuntimeTypeFacts.TryGetExactRuntimeType(
                    receiverExpression,
                    useNode,
                    semanticModel,
                    cancellationToken,
                    out var exactRuntimeType) &&
                exactRuntimeType is IArrayTypeSymbol exactArrayType)
            {
                arrayType = exactArrayType;
                return true;
            }

            arrayType = null!;
            return false;
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
                    out var lengthExpression))
            {
                return false;
            }

            return SymbolicReachabilityService.TryCreateSubsequenceInRangeCondition(
                receiverExpression,
                startExpression,
                lengthExpression,
                semanticModel,
                cancellationToken,
                out inRangeFormula,
                oneArgumentUpperBoundIsInclusive: true);
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
                !IsBuiltInSpanOrMemoryType(method.ContainingType) ||
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

        private static bool TryGetInvocationArgumentExpression(
            IInvocationOperation invocationOperation,
            int parameterIndex,
            out ExpressionSyntax expression)
        {
            foreach (var argument in invocationOperation.Arguments)
            {
                if (argument.Parameter?.Ordinal == parameterIndex &&
                    argument.Value.Syntax is ExpressionSyntax argumentExpression)
                {
                    expression = argumentExpression;
                    return true;
                }
            }

            expression = null!;
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

        private static bool IsSystemRangeType(ITypeSymbol? typeSymbol)
        {
            return SymbolicTypeFacts.IsSystemRangeType(typeSymbol);
        }

        private static bool IsNullableValueAccess(
            MemberAccessExpressionSyntax memberAccess,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            return SymbolicTypeFacts.IsNullableValueAccess(memberAccess, semanticModel, cancellationToken);
        }

        private static bool IsNullableType(ITypeSymbol? typeSymbol)
        {
            return SymbolicTypeFacts.IsNullableType(typeSymbol);
        }

        private static bool IsNonNullableValueType(ITypeSymbol? typeSymbol)
        {
            return typeSymbol?.IsValueType == true &&
                !IsNullableType(typeSymbol);
        }

        private static bool IsReferenceType(ITypeSymbol? typeSymbol)
        {
            return SymbolicTypeFacts.IsReferenceType(typeSymbol);
        }

        private static bool IsReferenceLikeType(ITypeSymbol? typeSymbol)
        {
            return SymbolicTypeFacts.IsReferenceLikeType(typeSymbol);
        }

        private static bool IsDynamicExpression(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            return SymbolicTypeFacts.IsDynamicExpression(
                expression,
                semanticModel,
                cancellationToken,
                UnwrapFactExpression);
        }

        internal readonly struct DynamicNullBindingSite
        {
            public DynamicNullBindingSite(SyntaxNode site, string category, string source)
            {
                Site = site;
                Category = category;
                Source = source;
            }

            public SyntaxNode Site { get; }

            public string Category { get; }

            public string Source { get; }
        }

    }
}

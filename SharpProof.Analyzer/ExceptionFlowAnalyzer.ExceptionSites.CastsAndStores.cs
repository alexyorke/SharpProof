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

    }
}

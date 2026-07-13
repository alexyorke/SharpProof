using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;

using static SharpProof.Analyzer.ExceptionFlowAnalyzer;

namespace SharpProof.Analyzer;

internal static partial class ExceptionSiteClassifier
{
    private static bool IsDefinitelyUnboxNullCast(
        CastExpressionSyntax castExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        if (!TryGetConversionOperation(castExpression, semanticModel, cancellationToken, out var conversionOperation) ||
            conversionOperation.Conversion.IsUserDefined ||
            !IsUnboxingCastShape(castExpression, conversionOperation.Type, semanticModel, cancellationToken))
            return false;

        return IsDefinitelyNullExpression(castExpression.Expression, castExpression, semanticModel, cancellationToken,
            smtAnalysis);
    }

    private static bool IsDefinitelyInvalidCast(
        CastExpressionSyntax castExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        if (!TryGetConversionOperation(castExpression, semanticModel, cancellationToken, out var conversionOperation) ||
            conversionOperation.Conversion.IsUserDefined ||
            conversionOperation.Conversion.IsIdentity ||
            conversionOperation.Type is not { } targetType ||
            targetType.TypeKind == TypeKind.Dynamic)
            return false;

        if (IsUnboxingCastShape(castExpression, targetType, semanticModel, cancellationToken))
        {
            if (IsDefinitelyNullExpression(castExpression.Expression, castExpression, semanticModel, cancellationToken,
                    smtAnalysis) ||
                !SymbolicRuntimeTypeFacts.TryGetExactRuntimeType(
                    castExpression.Expression,
                    castExpression,
                    semanticModel,
                    cancellationToken,
                    out var exactRuntimeType))
                return false;

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
            return false;

        return !SymbolicRuntimeTypeFacts.CanCastExactRuntimeTypeToReferenceType(
            exactReferenceRuntimeType,
            targetType,
            semanticModel.Compilation);
    }

    private static bool IsDefinitelyArrayTypeMismatchStore(
        AssignmentExpressionSyntax assignment,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
            UnwrapFactExpression(assignment.Left) is not ElementAccessExpressionSyntax elementAccess ||
            !IsReferenceArrayElementStore(elementAccess, semanticModel, cancellationToken) ||
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
            return false;

        return !SymbolicRuntimeTypeFacts.CanStoreExactRuntimeTypeInArrayElement(
            exactAssignedType,
            exactArrayType.ElementType,
            semanticModel.Compilation);
    }

    private static bool IsReferenceArrayElementStore(
        ElementAccessExpressionSyntax elementAccess,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (elementAccess.ArgumentList.Arguments.Count != 1) return false;

        return GetExpressionType(elementAccess.Expression, semanticModel, cancellationToken) is IArrayTypeSymbol
        {
            Rank: 1,
            ElementType: { IsReferenceType: true, TypeKind: not TypeKind.Dynamic }
        };
    }

    private static bool IsDefinitelyInRangeElementStore(
        ElementAccessExpressionSyntax elementAccess,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        var lowering = SymbolicSemanticPipeline.LowerBuiltInElementAccessInRangeCondition(
            elementAccess,
            new SymbolicLoweringContext(semanticModel, cancellationToken));
        if (lowering is not { IsExact: true, Value: { } inRangeCondition })
            return false;

        return IsDefinitelyTrueAtUse(elementAccess, inRangeCondition, semanticModel, cancellationToken, smtAnalysis);
    }

    private static bool IsUnboxingCastShape(
        CastExpressionSyntax castExpression,
        ITypeSymbol? targetType,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var operandType = GetExpressionType(castExpression.Expression, semanticModel, cancellationToken);
        return IsNonNullableValueType(targetType) &&
               IsReferenceType(operandType);
    }

    private static bool TryGetConversionOperation(
        CastExpressionSyntax castExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
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

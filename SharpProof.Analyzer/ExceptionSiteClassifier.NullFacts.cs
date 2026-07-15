using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;

using static SharpProof.Analyzer.ExceptionFlowAnalyzer;

namespace SharpProof.Analyzer;

internal static partial class ExceptionSiteClassifier
{
    private static bool IsDefinitelyNullExpression(
        ExpressionSyntax expression,
        SyntaxNode useNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        expression = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(expression);
        if (expression is CastExpressionSyntax castExpression)
        {
            if (IsDefinitelyNullExpression(castExpression.Expression, useNode, semanticModel, cancellationToken,
                    smtAnalysis))
            {
                var castType = semanticModel.GetTypeInfo(castExpression, cancellationToken).Type;
                return IsReferenceLikeType(castType);
            }

            return false;
        }

        if (expression.IsKind(SyntaxKind.NullLiteralExpression)) return true;

        if (expression is DefaultExpressionSyntax defaultExpression)
        {
            var defaultType = semanticModel.GetTypeInfo(defaultExpression, cancellationToken).Type;
            return IsReferenceLikeType(defaultType);
        }

        if (expression.IsKind(SyntaxKind.DefaultLiteralExpression))
        {
            var defaultType = semanticModel.GetTypeInfo(expression, cancellationToken).ConvertedType ??
                              semanticModel.GetTypeInfo(expression, cancellationToken).Type;
            return IsReferenceLikeType(defaultType);
        }

        return ExceptionPathStateService.IsKnownByDominatingIf(
            expression,
            useNode,
            semanticModel,
            cancellationToken,
            PathFactKind.Null,
            smtAnalysis);
    }

    private static bool IsDefinitelyMissingNullableValue(
        MemberAccessExpressionSyntax memberAccess,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        if (IsKnownMissingNullableValueByPriorAssignment(
                memberAccess.Expression,
                memberAccess,
                semanticModel,
                cancellationToken))
            return true;

        var lowering = SymbolicSemanticPipeline.LowerNullableHasValueCondition(
            memberAccess.Expression,
            new SymbolicLoweringContext(semanticModel, cancellationToken));
        if (lowering is not { IsExact: true, Value: { } hasValueCondition })
            return false;

        return IsConditionStatusAtUse(memberAccess, hasValueCondition, semanticModel, cancellationToken, smtAnalysis,
            SymbolicProofStatus.ProvenFalse);
    }
}

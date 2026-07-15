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

}

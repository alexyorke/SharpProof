using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Analyzer;

internal static partial class ExceptionFlowAnalyzer
{
    private static bool IsIntegralOrDecimalZero(object? value)
    {
        return SymbolicValueFacts.IsIntegralOrDecimalZero(value);
    }

    private static bool IsThrowingDivideByZeroExpression(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var typeInfo = semanticModel.GetTypeInfo(expression, cancellationToken);
        return SymbolicTypeFacts.IsThrowingDivideByZeroType(typeInfo.ConvertedType ?? typeInfo.Type);
    }

    private static bool IsDefinitelyZeroExpression(
        ExpressionSyntax expression,
        SyntaxNode useNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        var constantValue = semanticModel.GetConstantValue(expression, cancellationToken);
        return (constantValue.HasValue && IsIntegralOrDecimalZero(constantValue.Value)) ||
               IsKnownByPriorAssignment(expression, useNode, semanticModel, cancellationToken, PathFactKind.Zero) ||
               IsProvenZeroByCanonicalState(
                   expression,
                   useNode,
                   semanticModel,
                   cancellationToken,
                   smtAnalysis) ||
               IsKnownByDominatingIf(expression, useNode, semanticModel, cancellationToken, PathFactKind.Zero,
                   smtAnalysis);
    }

    private static bool IsProvenZeroByCanonicalState(
        ExpressionSyntax expression,
        SyntaxNode useNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
        var lowering = SymbolicSemanticPipeline.LowerTerm(expression, context);
        if (lowering is not { IsExact: true, Value: { Kind: SearchLib.Smt.SmtValueKind.Int } term }) return false;

        var analysis = new SymbolicInvariantService().AnalyzeAt(
            useNode,
            semanticModel,
            smtAnalysis,
            cancellationToken);
        var zeroCondition = SymbolicIrLowerer.CreateIntegerZeroCondition(
            term,
            expression,
            "ir.exception-flow.divide-by-zero");
        var proof = SymbolicReachabilityService.ClassifyStateConditionTruth(
            analysis.PathState,
            zeroCondition,
            smtAnalysis);
        return proof.Info.Status == SymbolicProofStatus.ProvenTrue;
    }

    private static bool IsDefinitelyNullExpression(
        ExpressionSyntax expression,
        SyntaxNode useNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
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
                if (IsDefinitelyNullExpression(castExpression.Expression, useNode, semanticModel, cancellationToken,
                        smtAnalysis))
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

        if (expression.IsKind(SyntaxKind.NullLiteralExpression)) return true;

        if (expression is DefaultExpressionSyntax defaultExpression)
        {
            var defaultType = semanticModel.GetTypeInfo(defaultExpression, cancellationToken).Type;
            return IsReferenceLikeType(defaultType);
        }

        return IsKnownByPriorAssignment(expression, useNode, semanticModel, cancellationToken, PathFactKind.Null) ||
               IsKnownByDominatingIf(expression, useNode, semanticModel, cancellationToken, PathFactKind.Null,
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

        return IsDefinitelyFalseAtUse(memberAccess, hasValueCondition, semanticModel, cancellationToken, smtAnalysis);
    }
}

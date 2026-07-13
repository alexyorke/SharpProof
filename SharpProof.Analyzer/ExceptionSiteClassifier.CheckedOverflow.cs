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
    private static bool IsDefinitelyCheckedIntegralOverflow(
        BinaryExpressionSyntax binaryExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        return SymbolicRuntimeHazardSyntaxCandidateFactory.TryCreateCheckedIntegralOverflowCandidate(
                   binaryExpression,
                   semanticModel,
                   cancellationToken,
                   out var candidate) &&
               candidate.TryGetExactTriggerCondition(out var trigger) &&
               IsDefinitelyTrueAtUse(
                   binaryExpression,
                   trigger,
                   semanticModel,
                   cancellationToken,
                   smtAnalysis);
    }

    private static bool IsDefinitelyCheckedIntegralOverflow(
        PrefixUnaryExpressionSyntax unaryExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        return SymbolicRuntimeHazardSyntaxCandidateFactory.TryCreateCheckedIntegralOverflowCandidate(
                   unaryExpression,
                   semanticModel,
                   cancellationToken,
                   out var candidate) &&
               candidate.TryGetExactTriggerCondition(out var trigger) &&
               IsDefinitelyTrueAtUse(
                   unaryExpression,
                   trigger,
                   semanticModel,
                   cancellationToken,
                   smtAnalysis);
    }

    private static bool IsDefinitelyCheckedIntegralOverflow(
        PostfixUnaryExpressionSyntax unaryExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        return SymbolicRuntimeHazardSyntaxCandidateFactory.TryCreateCheckedIntegralOverflowCandidate(
                   unaryExpression,
                   semanticModel,
                   cancellationToken,
                   out var candidate) &&
               candidate.TryGetExactTriggerCondition(out var trigger) &&
               IsDefinitelyTrueAtUse(
                   unaryExpression,
                   trigger,
                   semanticModel,
                   cancellationToken,
                   smtAnalysis);
    }

    private static bool IsDefinitelyCheckedIntegralOverflow(
        CastExpressionSyntax castExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        return SymbolicRuntimeHazardSyntaxCandidateFactory.TryCreateCheckedExplicitNumericConversionOverflowCandidate(
                   castExpression,
                   semanticModel,
                   cancellationToken,
                   out var candidate) &&
               candidate.TryGetExactTriggerCondition(out var trigger) &&
               IsDefinitelyTrueAtUse(
                   castExpression,
                   trigger,
                   semanticModel,
                   cancellationToken,
                   smtAnalysis);
    }

    private static bool IsDefinitelyNegativeArrayLength(
        ArrayCreationExpressionSyntax arrayCreation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        foreach (var lengthExpression in CSharpSyntaxFacts.GetExplicitArraySizeExpressions(arrayCreation))
        {
            var lowering = SymbolicSemanticPipeline.LowerNegativeIntegerCondition(
                    lengthExpression,
                    new SymbolicLoweringContext(semanticModel, cancellationToken));
            if (lowering is not { IsExact: true, Value: { } negativeLength })
                continue;

            if (IsDefinitelyTrueAtUse(arrayCreation, negativeLength, semanticModel, cancellationToken, smtAnalysis))
                return true;
        }

        return false;
    }

}

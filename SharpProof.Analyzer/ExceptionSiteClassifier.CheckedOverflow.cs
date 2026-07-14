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
    private static bool IsDefinitelyCheckedOverflow(
        SyntaxNode expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        return SymbolicRuntimeHazardSyntaxCandidateFactory.TryCreateCheckedOverflowCandidate(
                   expression,
                   semanticModel,
                   cancellationToken,
                   out var candidate) &&
               candidate.TryGetExactTriggerCondition(out var trigger) &&
               IsDefinitelyTrueAtUse(
                   expression,
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

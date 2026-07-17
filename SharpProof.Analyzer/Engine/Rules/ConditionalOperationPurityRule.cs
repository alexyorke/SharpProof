using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Analyzer.Engine.Rules;

internal sealed class ConditionalOperationPurityRule : PurityRuleBase<IConditionalOperation>
{
    protected override OperationKind Kind => OperationKind.Conditional;

    protected override PurityAnalysisEngine.PurityAnalysisResult CheckTyped(IConditionalOperation conditionalOperation,
        PurityAnalysisContext context, PurityAnalysisEngine.PurityAnalysisState currentState)
    {


        var conditionResult =
            PurityAnalysisEngine.CheckSingleOperation(conditionalOperation.Condition, context, currentState);
        if (!conditionResult.IsPure) return conditionResult;

        if (PurityAnalysisEngine.TryGetKnownConditionValueFromPathFacts(
                currentState,
                conditionalOperation.Condition,
                context.SemanticModel,
                context.SmtAnalysis,
                context.CancellationToken,
                out var constantCondition))
        {
            var reachableBranch = constantCondition ? conditionalOperation.WhenTrue : conditionalOperation.WhenFalse;
            var reachableBranchName = constantCondition ? "WhenTrue" : "WhenFalse";

            if (reachableBranch != null)
            {
                var reachableBranchState = currentState;
                PurityAnalysisEngine.TryCreateBranchAssumptionState(
                    currentState,
                    conditionalOperation.Condition,
                    context.SemanticModel,
                    constantCondition,
                    context.SmtAnalysis,
                    context.CancellationToken,
                    out reachableBranchState);
                var reachableBranchResult =
                    PurityAnalysisEngine.CheckSingleOperation(reachableBranch, context, reachableBranchState);
                if (!reachableBranchResult.IsPure) return reachableBranchResult;
            }

            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }

        if (conditionalOperation.WhenTrue != null)
            if (PurityAnalysisEngine.TryCreateBranchAssumptionState(
                    currentState,
                    conditionalOperation.Condition,
                    context.SemanticModel,
                    true,
                    context.SmtAnalysis,
                    context.CancellationToken,
                    out var whenTrueState))
            {
                var whenTrueResult =
                    PurityAnalysisEngine.CheckSingleOperation(conditionalOperation.WhenTrue, context, whenTrueState);
                if (!whenTrueResult.IsPure) return whenTrueResult;
            }


        if (conditionalOperation.WhenFalse != null)
            if (PurityAnalysisEngine.TryCreateBranchAssumptionState(
                    currentState,
                    conditionalOperation.Condition,
                    context.SemanticModel,
                    false,
                    context.SmtAnalysis,
                    context.CancellationToken,
                    out var whenFalseState))
            {
                var whenFalseResult =
                    PurityAnalysisEngine.CheckSingleOperation(conditionalOperation.WhenFalse, context, whenFalseState);
                if (!whenFalseResult.IsPure) return whenFalseResult;
            }


        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
    }
}
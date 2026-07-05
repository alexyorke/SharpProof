using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Generic;
using System.Linq;
using System.Collections.Immutable;
using SharpProof.Analyzer.Engine;

namespace SharpProof.Analyzer.Engine.Rules
{

    internal class ConditionalOperationPurityRule : IPurityRule
    {
        public IEnumerable<OperationKind> ApplicableOperationKinds => ImmutableArray.Create(OperationKind.Conditional);

        public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(IOperation operation, PurityAnalysisContext context, PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            if (!(operation is IConditionalOperation conditionalOperation))
            {

                PurityAnalysisEngine.LogDebug($"  [CondRule] WARNING: Incorrect operation type {operation.Kind}. Assuming Pure.");
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            PurityAnalysisEngine.LogDebug($"  [CondRule] Checking Conditional Operation: {conditionalOperation.Syntax}");


            var conditionResult = PurityAnalysisEngine.CheckSingleOperation(conditionalOperation.Condition, context, currentState);
            if (!conditionResult.IsPure)
            {
                PurityAnalysisEngine.LogDebug($"    [CondRule] Condition is Impure: {conditionalOperation.Condition.Syntax}");
                return conditionResult;
            }
            PurityAnalysisEngine.LogDebug($"    [CondRule] Condition is Pure.");

            if (PurityAnalysisEngine.TryGetKnownConditionValueFromPathFacts(
                    currentState,
                    conditionalOperation.Condition,
                    context.SemanticModel,
                    context.SmtAnalysis,
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
                        out reachableBranchState);
                    var reachableBranchResult = PurityAnalysisEngine.CheckSingleOperation(reachableBranch, context, reachableBranchState);
                    if (!reachableBranchResult.IsPure)
                    {
                        PurityAnalysisEngine.LogDebug($"    [CondRule] Reachable {reachableBranchName} is Impure: {reachableBranch.Syntax}");
                        return reachableBranchResult;
                    }
                }

                PurityAnalysisEngine.LogDebug($"    [CondRule] Condition is constant. Dead branch ignored; reachable {reachableBranchName} is Pure.");
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (conditionalOperation.WhenTrue != null)
            {
                if (PurityAnalysisEngine.TryCreateBranchAssumptionState(
                        currentState,
                        conditionalOperation.Condition,
                        context.SemanticModel,
                        branchWhenTrue: true,
                        context.SmtAnalysis,
                        out var whenTrueState))
                {
                    var whenTrueResult = PurityAnalysisEngine.CheckSingleOperation(conditionalOperation.WhenTrue, context, whenTrueState);
                    if (!whenTrueResult.IsPure)
                    {
                        PurityAnalysisEngine.LogDebug($"    [CondRule] WhenTrue is Impure: {conditionalOperation.WhenTrue.Syntax}");
                        return whenTrueResult;
                    }
                }
                else
                {
                    PurityAnalysisEngine.LogDebug($"    [CondRule] WhenTrue branch is SMT-unreachable. Skipping.");
                }
            }
            else
            {

                PurityAnalysisEngine.LogDebug($"    [CondRule] WhenTrue branch is null. Assuming pure.");
            }
            PurityAnalysisEngine.LogDebug($"    [CondRule] WhenTrue is Pure.");



            if (conditionalOperation.WhenFalse != null)
            {
                if (PurityAnalysisEngine.TryCreateBranchAssumptionState(
                        currentState,
                        conditionalOperation.Condition,
                        context.SemanticModel,
                        branchWhenTrue: false,
                        context.SmtAnalysis,
                        out var whenFalseState))
                {
                    var whenFalseResult = PurityAnalysisEngine.CheckSingleOperation(conditionalOperation.WhenFalse, context, whenFalseState);
                    if (!whenFalseResult.IsPure)
                    {
                        PurityAnalysisEngine.LogDebug($"    [CondRule] WhenFalse is Impure: {conditionalOperation.WhenFalse.Syntax}");
                        return whenFalseResult;
                    }
                }
                else
                {
                    PurityAnalysisEngine.LogDebug($"    [CondRule] WhenFalse branch is SMT-unreachable. Skipping.");
                }
            }
            else
            {

                PurityAnalysisEngine.LogDebug($"    [CondRule] WhenFalse branch is null. Assuming pure.");
            }
            PurityAnalysisEngine.LogDebug($"    [CondRule] WhenFalse is Pure.");



            PurityAnalysisEngine.LogDebug($"  [CondRule] Conditional Operation is Pure: {conditionalOperation.Syntax}");
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }
    }
}

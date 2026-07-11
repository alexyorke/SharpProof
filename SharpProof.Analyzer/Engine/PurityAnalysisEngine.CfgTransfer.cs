using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Analyzer.Engine.Rules;

namespace SharpProof.Analyzer.Engine;

internal partial class PurityAnalysisEngine
{
    internal static PurityAnalysisResult CheckSingleOperation(IOperation operation, PurityAnalysisContext context,
        PurityAnalysisState currentState)
    {
        if ((!currentState.PathState.Facts.IsDefaultOrEmpty ||
             !currentState.PathState.PathConditions.IsDefaultOrEmpty) &&
            IsPathStateUnsatisfiable(currentState, currentState.PathState, context.SmtAnalysis,
                operation.Syntax))
            return PurityAnalysisResult.Pure;

        if ((!currentState.PathState.Facts.IsDefaultOrEmpty ||
             !currentState.PathState.PathConditions.IsDefaultOrEmpty) &&
            ExecutionVisibility.IsEvaluationPathUnsatisfiableUsingSymbolicState(
                operation.Syntax,
                context.SemanticModel,
                context.CancellationToken,
                currentState.PathState,
                currentState.GetSmtSymbolVersion,
                context.SmtAnalysis))
            return PurityAnalysisResult.Pure;

        if (ExecutionVisibility.IsInStaticallyUnreachableBranchUsingSmt(
                operation.Syntax,
                context.SemanticModel,
                context.CancellationToken,
                context.SmtAnalysis))
            return PurityAnalysisResult.Pure;

        if (operation is IFlowCaptureReferenceOperation flowRef)
        {
            if (currentState.FlowCaptures.TryGetValue(flowRef.Id, out var capturedPurity)) return capturedPurity;

            return PurityAnalysisResult.Pure;
        }

        if (operation is IFlowCaptureOperation flowCap)
            return CheckSingleOperation(flowCap.Value, context, currentState);


        var isChecked = false;
        IMethodSymbol? operatorMethod = null;

        if (operation is IBinaryOperation binaryOp && binaryOp.IsChecked)
        {
            isChecked = true;
            operatorMethod = binaryOp.OperatorMethod;


            var leftResult = CheckSingleOperation(binaryOp.LeftOperand, context, currentState);
            if (!leftResult.IsPure) return leftResult;

            var rightResult = CheckSingleOperation(binaryOp.RightOperand, context, currentState);
            if (!rightResult.IsPure) return rightResult;
        }
        else if (operation is IUnaryOperation unaryOp && unaryOp.IsChecked)
        {
            isChecked = true;
            operatorMethod = unaryOp.OperatorMethod;


            var operandResult = CheckSingleOperation(unaryOp.Operand, context, currentState);
            if (!operandResult.IsPure) return operandResult;
        }

        if (isChecked)
        {
            if (operatorMethod != null)
            {
                if (context.PurityCache.TryGetValue(operatorMethod.OriginalDefinition, out var cachedResult))
                {
                    if (!cachedResult.IsPure) return PurityAnalysisResult.Impure(operation.Syntax);
                    return PurityAnalysisResult.Pure;
                }


                var hasTrustedGeneratedPurity = TryGetTrustedGeneratedPurityCoverage(
                    operatorMethod,
                    context.SemanticModel.Compilation,
                    out var generatedPurity);

                if (hasTrustedGeneratedPurity)
                {
                    if (generatedPurity.IsPure) return PurityAnalysisResult.Pure;

                    if (!generatedPurity.IsPure)
                        return PurityAnalysisResult.Impure(
                            operation.Syntax,
                            PurityEvidence.Create(
                                generatedPurity.PrimaryCategory,
                                syntaxNode: operation.Syntax,
                                symbol: operatorMethod.OriginalDefinition,
                                catalogSource: "generated_purity_summary"));
                }

                if (!hasTrustedGeneratedPurity &&
                    IsKnownPureBCLMember(operatorMethod, context.SemanticModel.Compilation))
                    return PurityAnalysisResult.Pure;

                if (IsKnownImpure(operatorMethod)) return PurityAnalysisResult.Impure(operation.Syntax);


                var operatorPurity = GetCalleePurity(operatorMethod, context);

                if (!operatorPurity.IsPure) return PurityAnalysisResult.Impure(operation.Syntax);
            }

            return PurityAnalysisResult.Pure;
        }


        if (operation.Kind == OperationKind.InterpolatedStringText ||
            operation.Kind == OperationKind.Interpolation)
            return PurityAnalysisResult.Pure;

        if (operation.Kind == OperationKind.Discard) return PurityAnalysisResult.Pure;

        _firstRuleByOperationKind.TryGetValue(operation.Kind, out var applicableRule);

        if (applicableRule != null)
        {
            var ruleResult = applicableRule.CheckPurity(operation, context, currentState);

            if (!ruleResult.IsPure)
            {
                if (ruleResult.ImpureSyntaxNode == null)
                    return operation.Syntax != null
                        ? PurityAnalysisResult.Impure(operation.Syntax)
                        : PurityAnalysisResult.ImpureUnknownLocation;
                return ruleResult;
            }

            return PurityAnalysisResult.Pure;
        }

        return ImpureResult(operation.Syntax, CreateUnsupportedOperationEvidence(operation));
    }


    private static void PropagateToSuccessor(
        BasicBlock? successor,
        PurityAnalysisState newState,
        Dictionary<BasicBlock, PurityAnalysisState> blockStates,
        Queue<BasicBlock> worklist,
        HashSet<BasicBlock> inQueue)
    {
        if (successor == null) return;


        var previouslyVisited = blockStates.TryGetValue(successor, out var existingState);
        if (!previouslyVisited) existingState = PurityAnalysisState.Pure;


        var mergedState = previouslyVisited ? MergeStates(existingState, newState) : newState;


        var stateChanged = !previouslyVisited || !mergedState.Equals(existingState);


        if (stateChanged)
        {
            blockStates[successor] = mergedState;
        }
        else
        {
            if (!previouslyVisited) blockStates[successor] = mergedState;
        }


        if (stateChanged || !inQueue.Contains(successor))
        {
            if (!inQueue.Contains(successor))
            {
                worklist.Enqueue(successor);
                inQueue.Add(successor);
            }
            else
            {
                if (stateChanged)
                {
                }
            }
        }
    }
}

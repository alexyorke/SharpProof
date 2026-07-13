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


        var isChecked = TryGetOperatorMethodForDirectPurityCheck(
            operation,
            includeCompoundAssignments: false,
            out var operatorMethod);

        if (isChecked && operation is IBinaryOperation binaryOp)
        {
            var leftResult = CheckSingleOperation(binaryOp.LeftOperand, context, currentState);
            if (!leftResult.IsPure) return leftResult;

            var rightResult = CheckSingleOperation(binaryOp.RightOperand, context, currentState);
            if (!rightResult.IsPure) return rightResult;
        }
        else if (isChecked && operation is IUnaryOperation unaryOp)
        {
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
                    PurityCatalogSemantics.IsKnownPureBCLMember(operatorMethod, context.SemanticModel.Compilation))
                    return PurityAnalysisResult.Pure;

                if (PurityCatalogSemantics.IsKnownImpure(operatorMethod)) return PurityAnalysisResult.Impure(operation.Syntax);


                var operatorPurity = PurityCalleeResolver.GetCalleePurity(operatorMethod, context);

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

    private static bool TryGetOperatorMethodForDirectPurityCheck(
        IOperation operation,
        bool includeCompoundAssignments,
        out IMethodSymbol? operatorMethod)
    {
        switch (operation)
        {
            case IBinaryOperation { IsChecked: true } binary:
                operatorMethod = binary.OperatorMethod;
                return true;
            case IUnaryOperation { IsChecked: true } unary:
                operatorMethod = unary.OperatorMethod;
                return true;
            case ICompoundAssignmentOperation { OperatorMethod: not null } compound
                when includeCompoundAssignments:
                operatorMethod = compound.OperatorMethod.OriginalDefinition;
                return true;
            default:
                operatorMethod = null;
                return false;
        }
    }


    private static void PropagateControlFlowBranch(
        ControlFlowBranch? branch,
        CfgFinallyContinuation? activeContinuation,
        IOperation? branchValue,
        PurityAnalysisState newState,
        ControlFlowGraph cfg,
        Dictionary<CfgTraversalPoint, PurityAnalysisState> blockStates,
        Queue<CfgTraversalPoint> worklist,
        HashSet<CfgTraversalPoint> inQueue)
    {
        if (branch == null) return;

        if (branch.Semantics == ControlFlowBranchSemantics.Return && branchValue != null)
            newState = PurityResourceStateFacts.AddReturnedOwnedResourceFacts(newState, branchValue, newState);

        if (!branch.FinallyRegions.IsDefaultOrEmpty)
        {
            var continuation = new CfgFinallyContinuation(
                branch.FinallyRegions,
                0,
                branch.Destination,
                activeContinuation);
            PropagateToSuccessor(
                new CfgTraversalPoint(
                    cfg.Blocks[branch.FinallyRegions[0].FirstBlockOrdinal],
                    continuation),
                newState,
                blockStates,
                worklist,
                inQueue);
            return;
        }

        if (branch.Destination != null)
        {
            PropagateToSuccessor(
                new CfgTraversalPoint(branch.Destination, activeContinuation),
                newState,
                blockStates,
                worklist,
                inQueue);
            return;
        }

        CompleteFinallyContinuation(
            activeContinuation,
            newState,
            cfg,
            blockStates,
            worklist,
            inQueue);
    }

    private static void CompleteFinallyContinuation(
        CfgFinallyContinuation? continuation,
        PurityAnalysisState state,
        ControlFlowGraph cfg,
        Dictionary<CfgTraversalPoint, PurityAnalysisState> blockStates,
        Queue<CfgTraversalPoint> worklist,
        HashSet<CfgTraversalPoint> inQueue)
    {
        if (continuation == null) return;

        var nextRegionIndex = continuation.RegionIndex + 1;
        if (nextRegionIndex < continuation.Regions.Length)
        {
            var nextContinuation = continuation with { RegionIndex = nextRegionIndex };
            PropagateToSuccessor(
                new CfgTraversalPoint(
                    cfg.Blocks[continuation.Regions[nextRegionIndex].FirstBlockOrdinal],
                    nextContinuation),
                state,
                blockStates,
                worklist,
                inQueue);
            return;
        }

        if (continuation.Destination != null)
        {
            PropagateToSuccessor(
                new CfgTraversalPoint(continuation.Destination, continuation.Parent),
                state,
                blockStates,
                worklist,
                inQueue);
            return;
        }

        CompleteFinallyContinuation(
            continuation.Parent,
            state,
            cfg,
            blockStates,
            worklist,
            inQueue);
    }

    private static void PropagateToSuccessor(
        CfgTraversalPoint successor,
        PurityAnalysisState newState,
        Dictionary<CfgTraversalPoint, PurityAnalysisState> blockStates,
        Queue<CfgTraversalPoint> worklist,
        HashSet<CfgTraversalPoint> inQueue)
    {
        var previouslyVisited = blockStates.TryGetValue(successor, out var existingState);
        if (!previouslyVisited) existingState = PurityAnalysisState.Pure;


        var mergedState = previouslyVisited
            ? PurityAnalysisStateMerger.MergeStates(existingState, newState, successor.Block.Ordinal)
            : newState;


        var stateChanged = !previouslyVisited || !mergedState.Equals(existingState);


        if (!stateChanged) return;

        blockStates[successor] = mergedState;
        if (!inQueue.Contains(successor))
        {
            worklist.Enqueue(successor);
            inQueue.Add(successor);
        }
    }

    private readonly record struct CfgTraversalPoint(
        BasicBlock Block,
        CfgFinallyContinuation? Continuation);

    private sealed record CfgFinallyContinuation(
        System.Collections.Immutable.ImmutableArray<ControlFlowRegion> Regions,
        int RegionIndex,
        BasicBlock? Destination,
        CfgFinallyContinuation? Parent);
}

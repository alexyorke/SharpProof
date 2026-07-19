using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Analyzer.Engine.Rules;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Analyzer.Engine;

internal partial class PurityAnalysisEngine
{
    private static PurityAnalysisResult AnalyzePurityUsingCFGInternal(
        SyntaxNode bodyNode,
        PurityAnalysisContext context,
        out PurityAnalysisState mergedNormalExitState)
    {
        var cancellationToken = context.CancellationToken;
        var semanticModel = context.SemanticModel;
        var smtAnalysis = context.SmtAnalysis;
        cancellationToken.ThrowIfCancellationRequested();
        mergedNormalExitState = PurityAnalysisState.Pure;
        // Roslyn 4.x: Create(BlockSyntax|ArrowClause, model) throws ("operation has a non-null parent").
        // Create(BaseMethodDeclarationSyntax|LocalFunctionStatement|ConstructorDeclaration|... , model) is the supported root.
        ControlFlowGraph? cfg;
        try
        {
            cfg = ControlFlowGraph.Create(bodyNode, semanticModel);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return PurityAnalysisResult.Impure(bodyNode);
        }

        if (cfg == null || cfg.Blocks.IsEmpty) return PurityAnalysisResult.Pure;

        var entry = (cfg.Blocks.First(), (CfgFinallyContinuation?)null);
        var entryState = CreateInitialRequiresState(context.ContainingMethodSymbol, bodyNode, semanticModel,
            context.AttributePolicy, cancellationToken);
        var states = new Dictionary<(BasicBlock Block, CfgFinallyContinuation? Continuation), PurityAnalysisState> { [entry] = entryState };
        var queue = new Queue<(BasicBlock Block, CfgFinallyContinuation? Continuation)>([entry]);
        var queued = new HashSet<(BasicBlock Block, CfgFinallyContinuation? Continuation)> { entry };
        var exitStates = new Dictionary<(BasicBlock Block, CfgFinallyContinuation? Continuation), PurityAnalysisState>();

        void SchedulePoint((BasicBlock Block, CfgFinallyContinuation? Continuation) point, PurityAnalysisState state)
        {
            if (states.TryGetValue(point, out var previous))
            {
                state = PurityAnalysisStateMerger.MergeStates(previous, state, point.Block.Ordinal);
                if (state.Equals(previous)) return;
            }
            states[point] = state;
            if (queued.Add(point)) queue.Enqueue(point);
        }

        void ScheduleBranch(ControlFlowBranch? branch, CfgFinallyContinuation? continuation, IOperation? branchValue,
            PurityAnalysisState state)
        {
            if (branch == null) return;
            if (branch.Semantics == ControlFlowBranchSemantics.Return && branchValue != null)
                state = PurityResourceStateFacts.AddReturnedOwnedResourceFacts(state, branchValue, state);
            if (!branch.FinallyRegions.IsDefaultOrEmpty)
            {
                continuation = new CfgFinallyContinuation(branch.FinallyRegions, 0, branch.Destination, continuation);
                SchedulePoint((cfg.Blocks[branch.FinallyRegions[0].FirstBlockOrdinal], continuation), state);
                return;
            }
            if (branch.Destination != null)
            {
                SchedulePoint((branch.Destination, continuation), state);
                return;
            }
            while (continuation != null)
            {
                var nextRegion = continuation.RegionIndex + 1;
                if (nextRegion < continuation.Regions.Length)
                {
                    continuation = continuation with { RegionIndex = nextRegion };
                    SchedulePoint((cfg.Blocks[continuation.Regions[nextRegion].FirstBlockOrdinal], continuation), state);
                    return;
                }
                var destination = continuation.Destination;
                continuation = continuation.Parent;
                if (destination != null)
                {
                    SchedulePoint((destination, continuation), state);
                    return;
                }
            }
        }

        var iterations = 0;
        while (queue.Count != 0 && iterations++ < cfg.Blocks.Length * 200)
        {
            var currentPoint = queue.Dequeue();
            queued.Remove(currentPoint);
            var currentBlock = currentPoint.Block;
            var stateAfter = ApplyTransferFunction(currentBlock, states[currentPoint], context);
            exitStates[currentPoint] = stateAfter;
            if (TryGetConstantBranchDecision(currentBlock.BranchValue, semanticModel, smtAnalysis, cancellationToken,
                    out var takeConditionalSuccessor))
            {
                var trueUsesConditionalSuccessor = BranchTrueUsesConditionalSuccessor(currentBlock);
                var takenBranch = takeConditionalSuccessor == trueUsesConditionalSuccessor
                    ? currentBlock.ConditionalSuccessor
                    : currentBlock.FallThroughSuccessor;
                if (TryCreateSuccessorState(stateAfter, currentBlock.BranchValue, semanticModel,
                        takeConditionalSuccessor, smtAnalysis, cancellationToken, out var takenState))
                    ScheduleBranch(takenBranch, currentPoint.Continuation, currentBlock.BranchValue, takenState);
            }
            else
            {
                var trueUsesConditionalSuccessor = BranchTrueUsesConditionalSuccessor(currentBlock);
                if (TryCreateSuccessorState(stateAfter, currentBlock.BranchValue, semanticModel,
                        trueUsesConditionalSuccessor, smtAnalysis, cancellationToken, out var conditionalState))
                    ScheduleBranch(currentBlock.ConditionalSuccessor, currentPoint.Continuation,
                        currentBlock.BranchValue, conditionalState);
                if (TryCreateSuccessorState(stateAfter, currentBlock.BranchValue, semanticModel,
                        !trueUsesConditionalSuccessor, smtAnalysis, cancellationToken, out var fallThroughState))
                    ScheduleBranch(currentBlock.FallThroughSuccessor, currentPoint.Continuation,
                        currentBlock.BranchValue, fallThroughState);
            }
        }

        if (queue.Count != 0) return PurityAnalysisResult.Impure(bodyNode);

        mergedNormalExitState = PurityAnalysisState.Merge(exitStates
            .Where(pair => pair.Key.Block.Kind == BasicBlockKind.Exit)
            .Select(static pair => pair.Value).ToArray());

        foreach (var exitState in exitStates.Values)
            if (exitState.HasPotentialImpurity)
                return exitState.FirstImpureSyntaxNode != null
                    ? PurityAnalysisResult.Impure(exitState.FirstImpureSyntaxNode, exitState.FirstImpurityEvidence)
                    : PurityAnalysisResult.ImpureUnknownLocation.WithEvidence(exitState.FirstImpurityEvidence);
        return CheckCfgImplicitSemantics(bodyNode, context, mergedNormalExitState);
    }

    private static PurityAnalysisState ApplyTransferFunction(
        BasicBlock block,
        PurityAnalysisState stateBefore,
        PurityAnalysisContext context)
    {
        var cancellationToken = context.CancellationToken;
        var semanticModel = context.SemanticModel;
        var smtAnalysis = context.SmtAnalysis;
        cancellationToken.ThrowIfCancellationRequested();

        if (stateBefore.HasPotentialImpurity) return stateBefore;

        if ((!stateBefore.PathState.Facts.IsDefaultOrEmpty ||
             !stateBefore.PathState.PathConditions.IsDefaultOrEmpty) &&
            IsPathStateUnsatisfiable(stateBefore.PathState, smtAnalysis))
            return stateBefore;

        var currentStateInBlock = stateBefore;
        PurityAnalysisResult? deferredRecursiveImpurity = null;
        SyntaxNode? deferredRecursiveSyntax = null;
        foreach (var op in block.Operations)
        {
            if (op == null) continue;
            var opResult = CheckAndTrackOperation(op, context, ref currentStateInBlock);
            if (opResult.IsPure) continue;
            if (IsImpurityProvenUnreachable(opResult, semanticModel, smtAnalysis, cancellationToken)) continue;
            if (op is not IFlowCaptureOperation && IsRecursivePlaceholderImpurity(opResult))
            {
                deferredRecursiveImpurity ??= opResult.WithEvidence(
                    opResult.Evidence.WithSymbol(context.ContainingMethodSymbol.ToDisplayString(_signatureFormat)));
                deferredRecursiveSyntax ??= op.Syntax;
                continue;
            }
            currentStateInBlock = currentStateInBlock.WithImpurity(opResult, op.Syntax);
            break;
        }

        if (!currentStateInBlock.HasPotentialImpurity && deferredRecursiveImpurity.HasValue)
        {
            var fallbackSyntax = deferredRecursiveSyntax ??
                                 block.Operations.FirstOrDefault()?.Syntax ??
                                 context.ContainingMethodSymbol.DeclaringSyntaxReferences.FirstOrDefault()
                                     ?.GetSyntax(cancellationToken);

            currentStateInBlock = currentStateInBlock.WithImpurity(
                deferredRecursiveImpurity.Value,
                fallbackSyntax!);
        }

        if (!currentStateInBlock.HasPotentialImpurity &&
            block.BranchValue != null &&
            TryCreateThrowBranchImpurity(block.BranchValue, context, currentStateInBlock,
                out var throwBranchResult))
        {
            currentStateInBlock = currentStateInBlock.WithImpurity(throwBranchResult,
                throwBranchResult.ImpureSyntaxNode ?? block.BranchValue.Syntax);
        }
        else if (!currentStateInBlock.HasPotentialImpurity &&
                 block.BranchValue != null &&
                 ShouldAnalyzeStateSensitiveBranchValue(block.BranchValue.Syntax))
        {
            var operationToCheck = TryGetCfgReturnOperation(
                block.BranchValue,
                semanticModel,
                cancellationToken) ?? block.BranchValue;
            var branchValueResult = CheckSingleOperation(operationToCheck, context, currentStateInBlock);
            if (!branchValueResult.IsPure)
            {
                if (!IsImpurityProvenUnreachable(branchValueResult, semanticModel, smtAnalysis, cancellationToken))
                    currentStateInBlock = currentStateInBlock.WithImpurity(branchValueResult, block.BranchValue.Syntax);
            }
            else
            {
                currentStateInBlock =
                    PurityAssignmentStateTransfer.UpdateDelegateMapForOperation(block.BranchValue, context, currentStateInBlock);
            }
        }

        return currentStateInBlock;
    }

    private static IReturnOperation? TryGetCfgReturnOperation(
        IOperation branchValue,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var returnStatement = branchValue.Syntax.FirstAncestorOrSelf<ReturnStatementSyntax>();
        if (returnStatement != null)
            return semanticModel.GetOperation(returnStatement, cancellationToken) as IReturnOperation;

        var arrowExpression = branchValue.Syntax.FirstAncestorOrSelf<ArrowExpressionClauseSyntax>();
        if (arrowExpression?.Parent == null) return null;

        var declarationOperation = semanticModel.GetOperation(arrowExpression.Parent, cancellationToken);
        return declarationOperation == null
            ? null
            : ExecutionVisibility.VisibleDescendants(declarationOperation)
                .OfType<IReturnOperation>()
                .FirstOrDefault(returnOperation =>
                    returnOperation.ReturnedValue?.Syntax.Span.Contains(branchValue.Syntax.Span) == true);
    }

    private static bool TryCreateThrowBranchImpurity(
        IOperation branchValue,
        PurityAnalysisContext context,
        PurityAnalysisState currentState,
        out PurityAnalysisResult result)
    {
        result = PurityAnalysisResult.Pure;

        var throwSyntax = branchValue.Syntax.FirstAncestorOrSelf<ThrowStatementSyntax>() ??
                          (SyntaxNode?)branchValue.Syntax.FirstAncestorOrSelf<ThrowExpressionSyntax>();
        if (throwSyntax == null) return false;

        var exceptionResult = CheckSingleOperation(branchValue, context, currentState);
        if (!exceptionResult.IsPure)
        {
            result = exceptionResult;
            return true;
        }

        result = PurityAnalysisResult.Impure(
            throwSyntax,
            PurityEvidence.Create(
                "throw",
                "ThrowOperationPurityRule",
                syntaxNode: throwSyntax,
                operationKindOverride: OperationKind.Throw.ToString()));
        return true;
    }

    internal static bool IsRecursivePlaceholderImpurity(PurityAnalysisResult result)
    {
        return !result.IsPure &&
               result.Evidence.RuleName == "RecursivePurityAnalysis" &&
               result.Evidence.CatalogSource == "recursive_call";
    }

    private static PurityAnalysisResult CheckAndTrackOperation(
        IOperation operation,
        PurityAnalysisContext context,
        ref PurityAnalysisState state)
    {
        var result = operation is IFlowCaptureOperation capture
            ? CheckSingleOperation(capture.Value, context, state)
            : CheckSingleOperation(operation, context, state);
        if (operation is IFlowCaptureOperation flowCapture)
            state = state.WithFlowCaptureResult(flowCapture.Id, result);
        if (result.IsPure)
            state = PurityAssignmentStateTransfer.UpdateDelegateMapForOperation(operation, context, state);
        return result;
    }


    private static PurityAnalysisResult AnalyzeOperationSubtreePurity(
        IOperation rootOperation,
        PurityAnalysisContext context)
    {
        var cancellationToken = context.CancellationToken;
        var semanticModel = context.SemanticModel;
        cancellationToken.ThrowIfCancellationRequested();

        var currentState = CreateInitialRequiresState(
            context.ContainingMethodSymbol,
            rootOperation.Syntax,
            semanticModel,
            context.AttributePolicy,
            cancellationToken);
        var visitedOperations = new HashSet<IOperation>();
        foreach (var operation in ExecutionVisibility.VisibleDescendants(rootOperation))
        {
            var operationToAnalyze = operation is IExpressionStatementOperation expressionStatementOperation
                ? expressionStatementOperation.Operation
                : operation;
            if (!visitedOperations.Add(operationToAnalyze)) continue;

            var operationResult = CheckAndTrackOperation(operationToAnalyze, context, ref currentState);
            if (!operationResult.IsPure) return operationResult;
        }

        return currentState.HasPotentialImpurity
            ? ImpureResult(currentState.FirstImpureSyntaxNode, currentState.FirstImpurityEvidence)
            : PurityAnalysisResult.Pure;
    }

}

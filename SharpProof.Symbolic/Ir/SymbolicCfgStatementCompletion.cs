using static SharpProof.Symbolic.Ir.SymbolicCfgProgramPointStateCollector;

namespace SharpProof.Symbolic.Ir;

internal static class SymbolicCfgStatementCompletion {
    internal static SymbolicLoweringResult<SymbolicState> CollectCompletedStatementState(
        StatementSyntax statement,
        SymbolicState entryState,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        using var limitScope = SymbolicAnalysisLimitContext.Push(
            SymbolicAnalysisLimitContext.Limits,
            statement);
        var result = CollectSeededCompletionTrace(
            statement,
            entryState,
            semanticModel,
            cancellationToken);
        return limitScope.Snapshot().IsTruncated
            ? Unsupported(statement, "trace.truncation")
            : result;
    }

    private static SymbolicLoweringResult<SymbolicState> CollectSeededCompletionTrace(
        StatementSyntax statement,
        SymbolicState entryState,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        if (statement is IfStatementSyntax abruptIf &&
            TryCollectAbruptIfCompletionState(
                abruptIf,
                entryState,
                semanticModel,
                cancellationToken,
                out var abruptCompletion))
            return abruptCompletion;
        if (statement is WhileStatementSyntax or DoStatementSyntax or ForStatementSyntax &&
            SymbolicControlFlowFacts.StatementDefinitelyExits(statement, semanticModel, cancellationToken)) {
            var terminalState = entryState;
            SymbolicStateInvalidator.InvalidateNestedMutations(
                ref terminalState,
                statement,
                semanticModel,
                cancellationToken);
            return Exact(SymbolicOperationTransferKernel.Complete(terminalState, statement.Span).State, statement);
        }

        var executionRoot = CSharpSyntaxFacts.GetContainingExecutionRoot(statement, ExecutionRootPolicy.Callable);
        if (executionRoot == null)
            return Unsupported(statement, "execution-root");
        if (!TryCreateGraph(executionRoot, semanticModel, cancellationToken, out var graph, out var graphFailure))
            return Unsupported(statement, graphFailure);
        if (!TryLowerLoopPlans(
                statement,
                allowAbruptCompletion: true,
                semanticModel,
                cancellationToken,
                out var loopPlans))
            return Unsupported(statement, "loop-lowering");
        if (!TryCreateRegionPlan(
                graph,
                statement,
                semanticModel,
                cancellationToken,
                out var region,
                out var failure))
            return Unsupported(statement, failure);

        var entryPoint = region.EntryPoint;
        var incoming = new Dictionary<CfgTraversalPoint, Dictionary<CfgIncomingEdge, CfgPathState>> {
            [entryPoint] = new() {
                [new CfgIncomingEdge(null, null, CfgIncomingEdgeKind.Entry)] = new(entryState, null)
            }
        };
        var queue = new Queue<CfgTraversalPoint>();
        var queued = new HashSet<CfgTraversalPoint> { entryPoint };
        var traversal = new CfgTraversalContext(
            graph,
            semanticModel,
            cancellationToken,
            incoming,
            queue,
            queued,
            new List<CfgPathState>(),
            loopPlans,
            null,
            null,
            region,
            statement);
        var summarizesLoop = statement is WhileStatementSyntax or DoStatementSyntax or ForStatementSyntax;
        queue.Enqueue(entryPoint);
        var iterations = 0;
        var iterationLimit = graph.Blocks.Length * (4 + loopPlans.Count * 2);
        while (queue.Count != 0 && iterations++ < iterationLimit) {
            var point = queue.Dequeue();
            queued.Remove(point);
            var currentPath = MergeIncomingStates(incoming[point].Values.ToArray(), statement);
            var state = currentPath.State;
            var slice = region.Blocks[point.Block.Ordinal];
            var completedInBlock = false;
            for (var index = point.OperationIndex; index < slice.EndOperationIndexExclusive; index++) {
                var operation = point.Block.Operations[index];
                if (operation.IsImplicit && ReferenceEquals(operation.Syntax, executionRoot) ||
                    operation is IFlowCaptureOperation)
                    continue;
                ISymbol? invalidatedGuardTarget = null;
                if (!summarizesLoop && !TryApplyOperation(
                        ref state,
                        operation,
                        GetActiveGuard(currentPath.GuardFrame),
                        allowGuardedReferenceAssignments: true,
                        allowGuardMutation: true,
                        allowExpressionStatementCompletion: true,
                        semanticModel,
                        cancellationToken,
                        "ir.path.prior-statement",
                        out invalidatedGuardTarget))
                    return Unsupported(operation.Syntax, "operation-" + operation.Kind);
                if (!summarizesLoop && invalidatedGuardTarget != null)
                    currentPath = currentPath with {
                        GuardFrame = InvalidateGuards(currentPath.GuardFrame, invalidatedGuardTarget)
                    };
                if (!summarizesLoop)
                    AddOperationNormalCompletionFacts(
                        ref state,
                        operation,
                        semanticModel,
                        cancellationToken);
                if (operation.Syntax is StatementSyntax operationStatement &&
                    SymbolicControlFlowFacts.StatementDefinitelyExits(
                        operationStatement,
                        semanticModel,
                        cancellationToken)) {
                    region.TerminalPaths.Add(currentPath with { State = state });
                    completedInBlock = true;
                    break;
                }
            }
            if (completedInBlock || slice.HasCursorExit)
                continue;
            if (!TryPropagateSuccessors(
                    point.Block,
                    point.Continuation,
                    currentPath with { State = state },
                    traversal))
                return Unsupported(point.Block.BranchValue?.Syntax ?? statement, "control-flow");
        }
        if (queue.Count != 0)
            return Unsupported(statement, "iteration-limit");
        if (summarizesLoop)
            return TryCreateCompletedLoopSummary(
                entryState,
                region,
                loopPlans,
                semanticModel,
                cancellationToken,
                out var loopState)
                ? Exact(loopState, statement)
                : Unsupported(statement, "statement-region.loop-summary");

        SymbolicState? result = null;
        if (region.CompletedPaths.Count != 0) {
            var path = MergeIncomingStates(
                region.CompletedPaths.Select(static completion => completion.Path).ToArray(),
                statement);
            result = OrderTargetState(
                path.State,
                path,
                region.CompletedPaths.Count == 1 && !HasInvalidatedGuard(path.GuardFrame));
        }
        else if (region.TerminalPaths.Count != 0) {
            var path = CollapseTerminalCompletionPaths(region.TerminalPaths, statement);
            result = OrderTargetState(
                SymbolicOperationTransferKernel.Complete(path.State, statement.Span).State,
                path,
                targetIsInsideBranch: false);
        }
        if (result != null && statement is SwitchStatementSyntax completedSwitch &&
            !TryApplyCompletedSwitchExitExclusions(
                ref result,
                completedSwitch,
                semanticModel,
                cancellationToken))
            return Unsupported(statement, "statement-region.switch-exit");
        return result == null
            ? Unsupported(statement, "target-block")
            : Exact(result, statement);
    }

    internal static bool TryCreateGraph(
        SyntaxNode executionRoot,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out ControlFlowGraph graph,
        out string failure) {
        try {
            graph = ControlFlowGraph.Create(executionRoot, semanticModel, cancellationToken)!;
            failure = graph == null || graph.Blocks.IsDefaultOrEmpty ? "cfg-empty" : string.Empty;
            return failure.Length == 0;
        }
        catch (Exception exception) when (exception is not OperationCanceledException) {
            graph = null!;
            failure = "cfg";
            return false;
        }
    }

}

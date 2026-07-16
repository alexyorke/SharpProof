using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Symbolic.Ir;

internal static class SymbolicCfgProgramPointStateCollector
{
    internal static SymbolicLoweringResult<SymbolicState> CollectState(
        SyntaxNode site,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SymbolicState? initialState = null,
        bool includeCurrentStatementCompletionFacts = false) =>
        CollectState(
            site,
            semanticModel,
            cancellationToken,
            initialState,
            includeCurrentStatementCompletionFacts
                ? CfgProgramPointTargetKind.CurrentCompletion
                : CfgProgramPointTargetKind.BeforeCurrent);

    internal static SymbolicLoweringResult<SymbolicState> CollectForInitialEntryState(
        ForStatementSyntax forStatement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) =>
        CollectState(
            forStatement,
            semanticModel,
            cancellationToken,
            initialState: null,
            CfgProgramPointTargetKind.ForInitialEntry);

    private static SymbolicLoweringResult<SymbolicState> CollectState(
        SyntaxNode site,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SymbolicState? initialState,
        CfgProgramPointTargetKind targetKind)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var includeCurrentStatementCompletionFacts =
            targetKind == CfgProgramPointTargetKind.CurrentCompletion;
        var forInitialEntry = targetKind == CfgProgramPointTargetKind.ForInitialEntry
            ? (ForStatementSyntax)site
            : null;
        var executionRoot = CSharpSyntaxFacts.GetContainingExecutionRoot(
            site,
            ExecutionRootPolicy.Callable);
        if (executionRoot == null)
            return Unsupported(site, "execution-root");
        var targetIsCompletedRootBlock = includeCurrentStatementCompletionFacts &&
                                         site is BlockSyntax &&
                                         ReferenceEquals(site, CSharpSyntaxFacts.GetBlockBody(executionRoot));
        var targetIsCompletedNestedBlock = includeCurrentStatementCompletionFacts &&
                                           site is BlockSyntax &&
                                           !targetIsCompletedRootBlock;
        if (!UsesDefaultAnalysisLimits(SymbolicAnalysisLimitContext.Limits))
            return Unsupported(site, "analysis-limits");
        if (!TryLowerLoopPlans(
                executionRoot,
                semanticModel,
                cancellationToken,
                out var loopPlans))
            return Unsupported(site, "loop-lowering");
        if (forInitialEntry != null &&
            (forInitialEntry.Condition == null ||
             loopPlans.Count(plan => ReferenceEquals(plan.Loop, forInitialEntry)) != 1))
            return Unsupported(site, "for-initial-entry-shape");
        var containingLoopPlans = loopPlans
            .Where(plan =>
                plan.Loop.Span.Contains(site.SpanStart) &&
                !ReferenceEquals(plan.Loop, forInitialEntry))
            .ToArray();
        if (forInitialEntry != null && containingLoopPlans.Length != 0 ||
            containingLoopPlans.Length > 1 ||
            containingLoopPlans.Any(plan =>
                plan.Loop is not WhileStatementSyntax &&
                plan.Loop is not DoStatementSyntax ||
                HasAbruptOrNestedLoopControlFlow(plan.Loop)))
            return Unsupported(site, "loop-local-target");
        var targetIsInsideLoop = containingLoopPlans.Length != 0;
        var finallyClause = site.AncestorsAndSelf().OfType<FinallyClauseSyntax>().FirstOrDefault();
        if (forInitialEntry != null && site.Ancestors().Any(static ancestor => ancestor is CatchClauseSyntax))
            return Unsupported(site, "finally-local-target");
        ControlFlowGraph? graph;
        try
        {
            graph = ControlFlowGraph.Create(executionRoot, semanticModel, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Unsupported(site, "cfg");
        }

        if (graph == null || graph.Blocks.IsDefaultOrEmpty)
            return Unsupported(site, "cfg-empty");
        CfgFinallyLocalTargetPlan? finallyLocalTarget = null;
        if (finallyClause != null &&
            !TryCreateFinallyLocalTargetPlan(
                site,
                executionRoot,
                finallyClause,
                graph,
                semanticModel,
                cancellationToken,
                out finallyLocalTarget))
            return Unsupported(site, "finally-local-target");
        BasicBlock? forInitialEntryHeader = null;
        if (forInitialEntry != null &&
            !TryGetForInitialEntryHeader(graph, forInitialEntry, out forInitialEntryHeader))
            return Unsupported(site, "for-initial-entry-header");
        IOperation? nestedBlockCompletionOperation = null;
        ISet<CfgEdge> nestedBlockCompletionEdges = new HashSet<CfgEdge>();
        ISet<ControlFlowBranch> nestedBlockTerminalBranches = new HashSet<ControlFlowBranch>();
        if (targetIsCompletedNestedBlock &&
            !TryGetNestedBlockCompletionTarget(
                graph,
                (BlockSyntax)site,
                out nestedBlockCompletionOperation,
                out nestedBlockCompletionEdges,
                out nestedBlockTerminalBranches))
            return Unsupported(site, "nested-block-completion");
        if (targetIsCompletedRootBlock && !SupportsRootBlockCompletion(graph))
            return Unsupported(site, "root-block-control-flow");
        var rootCompletion = targetIsCompletedRootBlock
            ? CreateRootCompletionPlan(graph, (BlockSyntax)site, semanticModel)
            : null;

        var state = initialState ?? new SymbolicState();
        SymbolicStatementStateTransfer.AddMethodEntryNullableFlowStateFacts(
            ref state,
            site,
            semanticModel,
            cancellationToken);

        var entryPoint = new CfgTraversalPoint(graph.Blocks[0], null);
        var incoming = new Dictionary<CfgTraversalPoint, Dictionary<CfgIncomingEdge, CfgPathState>>
        {
            [entryPoint] = new Dictionary<CfgIncomingEdge, CfgPathState>
            {
                [new CfgIncomingEdge(null, null, CfgIncomingEdgeKind.Entry)] = new(state, null)
            }
        };
        var queue = new Queue<CfgTraversalPoint>();
        var queued = new HashSet<CfgTraversalPoint> { entryPoint };
        var completedPaths = new List<CfgPathState>();
        var terminalPaths = new List<CfgPathState>();
        var nestedBlockCompletedPaths = new List<CfgPathState>();
        var nestedBlockTerminalPaths = new List<CfgPathState>();
        var loopTargetStates = new List<SymbolicState>();
        queue.Enqueue(entryPoint);
        SymbolicState? targetState = null;
        SymbolicState? guardedTargetState = null;
        CfgFinallyContinuation? observedFinallyTargetContinuation = null;
        var targetIsInsideBranch = site.Ancestors().Any(static ancestor =>
            ancestor is Microsoft.CodeAnalysis.CSharp.Syntax.IfStatementSyntax or
                Microsoft.CodeAnalysis.CSharp.Syntax.ElseClauseSyntax or
                Microsoft.CodeAnalysis.CSharp.Syntax.SwitchSectionSyntax);
        var iterations = 0;
        var iterationLimit = graph.Blocks.Length * (4 + loopPlans.Count * 2);
        while (queue.Count != 0 && iterations++ < iterationLimit)
        {
            var point = queue.Dequeue();
            queued.Remove(point);
            var block = point.Block;
            var currentPath = MergeIncomingStates(incoming[point].Values.ToArray(), site);
            state = currentPath.State;
            if (targetIsCompletedRootBlock && block.Kind == BasicBlockKind.Exit)
            {
                targetState = state;
                continue;
            }
            var foundTarget = false;
            var observedLoopTarget = false;
            foreach (var operation in block.Operations)
            {
                if (operation.IsImplicit && ReferenceEquals(operation.Syntax, executionRoot))
                    continue;
                if (includeCurrentStatementCompletionFacts &&
                    site is LocalDeclarationStatementSyntax &&
                    operation is IFlowCaptureOperation)
                    continue;
                if (!targetIsCompletedRootBlock &&
                    forInitialEntry == null &&
                    !observedLoopTarget &&
                    IsTargetOperation(
                        operation,
                        site,
                        includeCurrentStatementCompletionFacts,
                        targetIsCompletedNestedBlock,
                        nestedBlockCompletionOperation,
                        semanticModel,
                        cancellationToken))
                {
                    if (targetIsInsideLoop &&
                        includeCurrentStatementCompletionFacts &&
                        !SupportsLoopLocalCurrentCompletion(site, operation))
                        return Unsupported(site, "loop-current-completion");
                    if (targetIsInsideBranch && HasInvalidatedGuard(currentPath.GuardFrame))
                        return Unsupported(site, "branch-guard-mutation");
                    if (finallyLocalTarget != null &&
                        !TryObserveFinallyLocalTarget(
                            point.Continuation,
                            finallyLocalTarget,
                            ref observedFinallyTargetContinuation))
                        return Unsupported(site, "finally-local-continuation");
                    var activeGuard = GetActiveGuard(currentPath.GuardFrame);
                    if (includeCurrentStatementCompletionFacts &&
                        !(site is LocalDeclarationStatementSyntax declaration
                            ? TryApplyCurrentDeclarationCompletion(
                                ref state,
                                declaration,
                                activeGuard,
                                allowGuardedReferenceAssignments: true,
                                semanticModel,
                                cancellationToken)
                            : TryApplyCurrentCompletion(
                                ref state,
                                site,
                                operation,
                                activeGuard,
                                targetIsInsideBranch,
                                semanticModel,
                                cancellationToken)))
                        return Unsupported(site, "current-completion");
                    var observedState = OrderTargetState(state, currentPath, targetIsInsideBranch);
                    if (targetIsInsideLoop)
                        loopTargetStates.Add(observedState);
                    else if (currentPath.GuardFrame == null || targetIsInsideBranch)
                        targetState = observedState;
                    else
                        guardedTargetState = observedState;
                    if (targetIsInsideLoop)
                    {
                        observedLoopTarget = true;
                        if (includeCurrentStatementCompletionFacts ||
                            operation is IReturnOperation or IThrowOperation)
                            continue;
                    }
                    else
                    {
                        foundTarget = true;
                        break;
                    }
                }
                if (!targetIsCompletedRootBlock &&
                    forInitialEntry == null &&
                    !targetIsInsideLoop &&
                    operation.Syntax.SpanStart >= site.SpanStart &&
                    !(targetIsCompletedNestedBlock &&
                      site.Span.Contains(operation.Syntax.SpanStart)))
                    return Unsupported(site, "operation-order");
                if (forInitialEntry != null &&
                    !SupportsForInitialEntryOperation(operation, forInitialEntry))
                    return Unsupported(operation.Syntax, "for-initializer-operation");
                if (!TryApplyOperation(
                        ref state,
                        operation,
                        GetActiveGuard(currentPath.GuardFrame),
                        true,
                        targetIsCompletedNestedBlock,
                        semanticModel,
                        cancellationToken,
                        GetAssignmentProvenance(operation, forInitialEntry),
                        out var invalidatedGuardTarget))
                    return Unsupported(operation.Syntax, "operation-" + operation.Kind);
                if (invalidatedGuardTarget != null)
                    currentPath = currentPath with
                    {
                        GuardFrame = InvalidateGuards(
                            currentPath.GuardFrame,
                            invalidatedGuardTarget)
                    };
                if (targetIsCompletedNestedBlock && site.Span.Contains(operation.Syntax.SpanStart))
                    AddOperationNormalCompletionFacts(
                        ref state,
                        operation,
                        semanticModel,
                        cancellationToken);
                else if (forInitialEntry != null)
                    AddForDeclarationInitializerNormalCompletionFacts(
                        ref state,
                        operation,
                        forInitialEntry,
                        semanticModel,
                        cancellationToken);
            }

            if (foundTarget)
                continue;

            if (forInitialEntryHeader != null && ReferenceEquals(block, forInitialEntryHeader))
            {
                if (point.Continuation != null ||
                    targetIsInsideBranch && HasInvalidatedGuard(currentPath.GuardFrame))
                    return Unsupported(site, "for-initial-entry-path");
                return Exact(
                    OrderTargetState(state, currentPath, targetIsInsideBranch),
                    site);
            }

            if (block.BranchValue != null)
            {
                if (!targetIsCompletedRootBlock &&
                    ContainsSite(block.BranchValue.Syntax, site) &&
                    !(includeCurrentStatementCompletionFacts &&
                      (site is LocalDeclarationStatementSyntax ||
                       targetIsCompletedNestedBlock &&
                       site.Span.Contains(block.BranchValue.Syntax.SpanStart))))
                {
                    if (targetIsInsideBranch && HasInvalidatedGuard(currentPath.GuardFrame))
                        return Unsupported(site, "branch-guard-mutation");
                    if (finallyLocalTarget != null &&
                        !TryObserveFinallyLocalTarget(
                            point.Continuation,
                            finallyLocalTarget,
                            ref observedFinallyTargetContinuation))
                        return Unsupported(site, "finally-local-continuation");
                    var observedState = OrderTargetState(state, currentPath, targetIsInsideBranch);
                    if (targetIsInsideLoop)
                        loopTargetStates.Add(observedState);
                    else if (currentPath.GuardFrame == null || targetIsInsideBranch)
                        targetState = observedState;
                    else
                        guardedTargetState = observedState;
                    if (!targetIsInsideLoop)
                        continue;
                }
            }

            if (!TryPropagateSuccessors(
                    block,
                    point.Continuation,
                    currentPath with { State = state },
                    graph,
                    semanticModel,
                    cancellationToken,
                    incoming,
                    queue,
                    queued,
                    completedPaths,
                    terminalPaths,
                    nestedBlockCompletionEdges,
                    nestedBlockCompletedPaths,
                    nestedBlockTerminalBranches,
                    nestedBlockTerminalPaths,
                    rootCompletion,
                    loopPlans,
                    finallyLocalTarget))
                return Unsupported(block.BranchValue?.Syntax ?? site, "control-flow");
        }

        if (targetIsCompletedRootBlock && queue.Count == 0 && targetState == null)
        {
            if (completedPaths.Count != 0)
                targetState = MergeIncomingStates(completedPaths, site).State;
            else if (terminalPaths.Count != 0)
                targetState = SymbolicOperationTransferKernel.Complete(
                    CollapseTerminalCompletionPaths(terminalPaths, site).State,
                    site.Span).State;
        }
        if (targetIsCompletedNestedBlock && nestedBlockCompletedPaths.Count != 0)
        {
            var completedPath = MergeIncomingStates(nestedBlockCompletedPaths, site);
            if (targetIsInsideBranch && HasInvalidatedGuard(completedPath.GuardFrame))
                return Unsupported(site, "branch-guard-mutation");
            targetState = OrderTargetState(completedPath.State, completedPath, targetIsInsideBranch);
        }
        else if (targetIsCompletedNestedBlock && nestedBlockTerminalPaths.Count != 0)
        {
            var completedPath = CollapseTerminalCompletionPaths(nestedBlockTerminalPaths, site);
            if (targetIsInsideBranch && HasInvalidatedGuard(completedPath.GuardFrame))
                return Unsupported(site, "branch-guard-mutation");
            var completedState = SymbolicOperationTransferKernel.Complete(
                completedPath.State,
                site.Span).State;
            targetState = OrderTargetState(completedState, completedPath, targetIsInsideBranch);
        }
        if (targetIsInsideLoop && loopTargetStates.Count == 0)
            return Unsupported(site, "loop-target-unobserved");
        if (targetIsInsideLoop)
        {
            if (!TryMergeLoopTargetStates(loopTargetStates, site.SpanStart, out targetState))
                return Unsupported(site, "loop-target-merge");
        }
        targetState ??= guardedTargetState;
        if (targetState == null &&
            queue.Count == 0 &&
            completedPaths.Count != 0 &&
            IsUnreachableTarget(graph, site))
        {
            var completedState = MergeIncomingStates(completedPaths, site).State;
            targetState = SymbolicOperationTransferKernel.Complete(completedState, site.Span).State;
        }
        return targetState == null || queue.Count != 0
            ? Unsupported(site, queue.Count == 0 ? "target-block" : "iteration-limit")
            : Exact(targetState, site);
    }

    private static bool TryPropagateSuccessors(
        BasicBlock block,
        CfgFinallyContinuation? activeContinuation,
        CfgPathState path,
        ControlFlowGraph graph,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        IDictionary<CfgTraversalPoint, Dictionary<CfgIncomingEdge, CfgPathState>> incoming,
        Queue<CfgTraversalPoint> queue,
        ISet<CfgTraversalPoint> queued,
        ICollection<CfgPathState> completedPaths,
        ICollection<CfgPathState> terminalPaths,
        ISet<CfgEdge> nestedBlockCompletionEdges,
        ICollection<CfgPathState> nestedBlockCompletedPaths,
        ISet<ControlFlowBranch> nestedBlockTerminalBranches,
        ICollection<CfgPathState> nestedBlockTerminalPaths,
        CfgRootCompletionPlan? rootCompletion,
        IReadOnlyList<SymbolicLoopTransferPlan> loopPlans,
        CfgFinallyLocalTargetPlan? finallyLocalTarget)
    {
        if (block.ConditionKind != ControlFlowConditionKind.None)
        {
            if (block.BranchValue is not { } condition)
                return false;

            var conditionalIsTrue = block.ConditionKind == ControlFlowConditionKind.WhenTrue;
            return TryCreateBranchState(
                       path.State,
                       condition,
                       conditionalIsTrue,
                       semanticModel,
                       cancellationToken,
                       out var conditionalState,
                       out var conditionalGuard) &&
                   TryCreateBranchState(
                       path.State,
                       condition,
                       !conditionalIsTrue,
                       semanticModel,
                       cancellationToken,
                       out var fallThroughState,
                       out var fallThroughGuard) &&
                   TryPropagate(
                       block,
                       block.ConditionalSuccessor,
                       CfgIncomingEdgeKind.Conditional,
                       activeContinuation,
                       new CfgPathState(
                           conditionalState,
                           new CfgGuardFrame(
                               path.State,
                               conditionalGuard,
                               conditionalIsTrue,
                               false,
                               path.GuardFrame)),
                       graph,
                       incoming,
                       queue,
                       queued,
                       completedPaths,
                       terminalPaths,
                       nestedBlockCompletionEdges,
                       nestedBlockCompletedPaths,
                       nestedBlockTerminalBranches,
                       nestedBlockTerminalPaths,
                       rootCompletion,
                       loopPlans,
                       finallyLocalTarget) &&
                   TryPropagate(
                       block,
                       block.FallThroughSuccessor,
                       CfgIncomingEdgeKind.FallThrough,
                       activeContinuation,
                       new CfgPathState(
                           fallThroughState,
                           new CfgGuardFrame(
                               path.State,
                               fallThroughGuard,
                               !conditionalIsTrue,
                               false,
                               path.GuardFrame)),
                       graph,
                       incoming,
                       queue,
                       queued,
                       completedPaths,
                       terminalPaths,
                       nestedBlockCompletionEdges,
                       nestedBlockCompletedPaths,
                       nestedBlockTerminalBranches,
                       nestedBlockTerminalPaths,
                       rootCompletion,
                       loopPlans,
                       finallyLocalTarget);
        }

        return TryPropagate(
                   block,
                   block.FallThroughSuccessor,
                   CfgIncomingEdgeKind.FallThrough,
                   activeContinuation,
                   path,
                   graph,
                   incoming,
                   queue,
                   queued,
                   completedPaths,
                   terminalPaths,
                   nestedBlockCompletionEdges,
                   nestedBlockCompletedPaths,
                   nestedBlockTerminalBranches,
                   nestedBlockTerminalPaths,
                   rootCompletion,
                   loopPlans,
                   finallyLocalTarget) &&
               TryPropagate(
                   block,
                   block.ConditionalSuccessor,
                   CfgIncomingEdgeKind.Conditional,
                   activeContinuation,
                   path,
                   graph,
                   incoming,
                   queue,
                   queued,
                   completedPaths,
                   terminalPaths,
                   nestedBlockCompletionEdges,
                   nestedBlockCompletedPaths,
                   nestedBlockTerminalBranches,
                   nestedBlockTerminalPaths,
                   rootCompletion,
                   loopPlans,
                   finallyLocalTarget);
    }

    private static bool TryCreateBranchState(
        SymbolicState state,
        IOperation condition,
        bool branchWhenTrue,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SymbolicState branchState,
        out SymbolicCondition branchCondition)
    {
        var transition = SymbolicReachabilityLowerer.ApplyCondition(
            state,
            condition,
            branchWhenTrue,
            semanticModel,
            cancellationToken,
            out branchCondition);
        if (!transition.IsExact)
        {
            branchState = state;
            branchCondition = null!;
            return false;
        }

        branchState = transition.State;
        return true;
    }

    private static bool TryPropagate(
        BasicBlock source,
        ControlFlowBranch? branch,
        CfgIncomingEdgeKind edgeKind,
        CfgFinallyContinuation? activeContinuation,
        CfgPathState path,
        ControlFlowGraph graph,
        IDictionary<CfgTraversalPoint, Dictionary<CfgIncomingEdge, CfgPathState>> incoming,
        Queue<CfgTraversalPoint> queue,
        ISet<CfgTraversalPoint> queued,
        ICollection<CfgPathState> completedPaths,
        ICollection<CfgPathState> terminalPaths,
        ISet<CfgEdge> nestedBlockCompletionEdges,
        ICollection<CfgPathState> nestedBlockCompletedPaths,
        ISet<ControlFlowBranch> nestedBlockTerminalBranches,
        ICollection<CfgPathState> nestedBlockTerminalPaths,
        CfgRootCompletionPlan? rootCompletion,
        IReadOnlyList<SymbolicLoopTransferPlan> loopPlans,
        CfgFinallyLocalTargetPlan? finallyLocalTarget)
    {
        if (branch == null)
            return true;
        if (!branch.FinallyRegions.IsDefaultOrEmpty)
        {
            var continuation = new CfgFinallyContinuation(
                branch,
                branch.FinallyRegions,
                0,
                branch.Destination,
                branch.Semantics is
                    ControlFlowBranchSemantics.Regular or
                    ControlFlowBranchSemantics.StructuredExceptionHandling
                    ? null
                    : branch,
                activeContinuation);
            if (finallyLocalTarget != null &&
                branch.FinallyRegions.Any(region => ReferenceEquals(region, finallyLocalTarget.Region)))
            {
                if (!IsSupportedFinallyLocalContinuation(continuation, finallyLocalTarget) ||
                    path.GuardFrame != null ||
                    finallyLocalTarget.ProtectedMutations.HasUnsupportedMutation)
                    return false;
                path = path with
                {
                    State = SymbolicStateInvalidator.ApplyNestedMutationInvalidations(
                        path.State,
                        finallyLocalTarget.ProtectedMutations)
                };
            }
            var finallyEntry = graph.Blocks[branch.FinallyRegions[0].FirstBlockOrdinal];
            path = ApplyExitedRegionLocalInvalidation(
                source,
                finallyEntry,
                rootCompletion,
                path);
            return TryPropagateToPoint(
                new CfgTraversalPoint(
                    finallyEntry,
                    continuation),
                new CfgIncomingEdge(branch, continuation, edgeKind),
                loopPlans.Count != 0,
                path,
                incoming,
                queue,
                queued);
        }
        if (branch.Semantics is not (ControlFlowBranchSemantics.Regular or
            ControlFlowBranchSemantics.StructuredExceptionHandling))
        {
            if (!IsTerminalCompletionBranch(branch))
                return false;
            if (nestedBlockTerminalBranches.Contains(branch))
            {
                nestedBlockTerminalPaths.Add(path);
                return true;
            }
            RecordTerminalPath(branch, path, rootCompletion, completedPaths, terminalPaths);
            return true;
        }
        if (branch.Destination == null)
        {
            if (activeContinuation != null)
                return TryCompleteFinallyContinuation(
                    branch,
                    activeContinuation,
                    loopPlans.Count != 0,
                    rootCompletion,
                    path,
                    graph,
                    incoming,
                    queue,
                    queued,
                    completedPaths,
                    terminalPaths);
            completedPaths.Add(path);
            return true;
        }
        if (nestedBlockCompletionEdges.Contains(
                new CfgEdge(source.Ordinal, branch.Destination.Ordinal)))
        {
            nestedBlockCompletedPaths.Add(path);
            return true;
        }

        if (branch.Destination.Ordinal <= source.Ordinal)
        {
            if (!TryApplyLoopBackEdge(
                    source,
                    branch.Destination,
                    path,
                    loopPlans,
                    out path))
                return false;
        }
        else if (!TryApplyLoopExit(
                     source,
                     branch.Destination,
                     path,
                     loopPlans,
                     out path))
        {
            return false;
        }
        path = ApplyExitedRegionLocalInvalidation(
            source,
            branch.Destination,
            rootCompletion,
            path);

        return TryPropagateToPoint(
            new CfgTraversalPoint(branch.Destination, activeContinuation),
            new CfgIncomingEdge(branch, activeContinuation, edgeKind),
            loopPlans.Count != 0,
            path,
            incoming,
            queue,
            queued);
    }

    private static bool TryCompleteFinallyContinuation(
        ControlFlowBranch sourceBranch,
        CfgFinallyContinuation? continuation,
        bool preserveIncomingHistory,
        CfgRootCompletionPlan? rootCompletion,
        CfgPathState path,
        ControlFlowGraph graph,
        IDictionary<CfgTraversalPoint, Dictionary<CfgIncomingEdge, CfgPathState>> incoming,
        Queue<CfgTraversalPoint> queue,
        ISet<CfgTraversalPoint> queued,
        ICollection<CfgPathState> completedPaths,
        ICollection<CfgPathState> terminalPaths)
    {
        if (continuation == null)
        {
            completedPaths.Add(path);
            return true;
        }

        var nextRegionIndex = continuation.RegionIndex + 1;
        if (nextRegionIndex < continuation.Regions.Length)
        {
            var nextContinuation = continuation with { RegionIndex = nextRegionIndex };
            var nextEntry = graph.Blocks[continuation.Regions[nextRegionIndex].FirstBlockOrdinal];
            path = ApplyExitedRegionLocalInvalidation(
                sourceBranch.Source,
                nextEntry,
                rootCompletion,
                path);
            return TryPropagateToPoint(
                new CfgTraversalPoint(
                    nextEntry,
                    nextContinuation),
                new CfgIncomingEdge(
                    sourceBranch,
                    nextContinuation,
                    CfgIncomingEdgeKind.FinallyContinuation),
                preserveIncomingHistory,
                path,
                incoming,
                queue,
                queued);
        }
        if (continuation.TerminalBranch is { } terminalBranch)
        {
            RecordTerminalPath(
                terminalBranch,
                path,
                rootCompletion,
                completedPaths,
                terminalPaths);
            return true;
        }
        if (continuation.Destination != null)
        {
            path = ApplyExitedRegionLocalInvalidation(
                sourceBranch.Source,
                continuation.Destination,
                rootCompletion,
                path);
            return TryPropagateToPoint(
                new CfgTraversalPoint(continuation.Destination, continuation.Parent),
                new CfgIncomingEdge(
                    sourceBranch,
                    continuation,
                    CfgIncomingEdgeKind.FinallyContinuation),
                preserveIncomingHistory,
                path,
                incoming,
                queue,
                queued);
        }
        return TryCompleteFinallyContinuation(
            sourceBranch,
            continuation.Parent,
            preserveIncomingHistory,
            rootCompletion,
            path,
            graph,
            incoming,
            queue,
            queued,
            completedPaths,
            terminalPaths);
    }

    private static void RecordTerminalPath(
        ControlFlowBranch branch,
        CfgPathState path,
        CfgRootCompletionPlan? rootCompletion,
        ICollection<CfgPathState> completedPaths,
        ICollection<CfgPathState> terminalPaths) =>
        (rootCompletion == null || ReferenceEquals(rootCompletion.CompletionBranch, branch)
            ? completedPaths
            : terminalPaths).Add(path);

    private static CfgPathState ApplyExitedRegionLocalInvalidation(
        BasicBlock source,
        BasicBlock destination,
        CfgRootCompletionPlan? rootCompletion,
        CfgPathState path)
    {
        if (rootCompletion == null)
            return path;

        var destinationRegions = new HashSet<ControlFlowRegion>();
        for (var region = destination.EnclosingRegion; region != null; region = region.EnclosingRegion)
            destinationRegions.Add(region);

        var targets = ImmutableArray.CreateBuilder<SymbolicInvalidationTarget>();
        for (var region = source.EnclosingRegion;
             region != null && !destinationRegions.Contains(region);
             region = region.EnclosingRegion)
        {
            foreach (var local in region.Locals)
            {
                var symbol = local.OriginalDefinition;
                if (rootCompletion.PreservedLocals.Contains(symbol))
                    continue;
                var key = SymbolicFactFactory.GetSmtVariableName(symbol);
                targets.Add(new SymbolicInvalidationTarget(key));
            }
        }

        if (targets.Count == 0)
            return path;

        var sourceSpan = source.Operations.LastOrDefault()?.Syntax.Span ??
                         source.BranchValue?.Syntax.Span ??
                         default;
        var invalidations = targets.ToImmutable();
        return new CfgPathState(
            ApplyScopeExitInvalidation(path.State, invalidations, sourceSpan),
            InvalidateGuardFrameBaselines(path.GuardFrame, invalidations, sourceSpan));
    }

    private static CfgGuardFrame? InvalidateGuardFrameBaselines(
        CfgGuardFrame? frame,
        ImmutableArray<SymbolicInvalidationTarget> invalidations,
        Microsoft.CodeAnalysis.Text.TextSpan sourceSpan)
    {
        if (frame == null)
            return null;
        return frame with
        {
            Baseline = ApplyScopeExitInvalidation(frame.Baseline, invalidations, sourceSpan),
            Parent = InvalidateGuardFrameBaselines(frame.Parent, invalidations, sourceSpan)
        };
    }

    private static SymbolicState ApplyScopeExitInvalidation(
        SymbolicState state,
        ImmutableArray<SymbolicInvalidationTarget> invalidations,
        Microsoft.CodeAnalysis.Text.TextSpan sourceSpan) =>
        SymbolicOperationTransferKernel.Invalidate(
            state,
            invalidations,
            sourceSpan,
            "cfg-program-point.scope-exit").State;

    private static bool TryPropagateToPoint(
        CfgTraversalPoint destination,
        CfgIncomingEdge edge,
        bool preserveIncomingHistory,
        CfgPathState path,
        IDictionary<CfgTraversalPoint, Dictionary<CfgIncomingEdge, CfgPathState>> incoming,
        Queue<CfgTraversalPoint> queue,
        ISet<CfgTraversalPoint> queued)
    {
        var activeGuard = GetActiveGuard(path.GuardFrame);
        var guardKey = activeGuard == null
            ? string.Empty
            : SymbolicState.CreateProofConditionKey(activeGuard);
        if (preserveIncomingHistory)
        {
            // The bounded loop transfer still derives its fixed point from accumulated iterations.
            edge = new CfgIncomingEdge(
                null,
                null,
                CfgIncomingEdgeKind.History,
                path.State.NormalizedProofKey + "\nactive-guard:" + guardKey +
                "\nguard-invalidated:" + HasInvalidatedGuard(path.GuardFrame));
        }

        if (!incoming.TryGetValue(destination, out var states))
        {
            states = new Dictionary<CfgIncomingEdge, CfgPathState>();
            incoming.Add(destination, states);
        }

        if (states.TryGetValue(edge, out var existing) &&
                existing.State.NormalizedProofKey == path.State.NormalizedProofKey &&
                (GetActiveGuard(existing.GuardFrame) is not { } existingGuard
                    ? string.Empty
                    : SymbolicState.CreateProofConditionKey(existingGuard)) == guardKey &&
                HasInvalidatedGuard(existing.GuardFrame) ==
                HasInvalidatedGuard(path.GuardFrame))
            return true;
        states[edge] = path;
        if (queued.Add(destination))
            queue.Enqueue(destination);
        return true;
    }

    private static bool TryApplyLoopBackEdge(
        BasicBlock source,
        BasicBlock destination,
        CfgPathState path,
        IReadOnlyList<SymbolicLoopTransferPlan> loopPlans,
        out CfgPathState backEdgePath)
    {
        var plan = loopPlans
            .Where(candidate =>
                BlockIsWithinLoop(source, candidate.Loop) &&
                BlockIsWithinLoop(destination, candidate.Loop))
            .OrderBy(static candidate => candidate.Loop.Span.Length)
            .FirstOrDefault();
        if (plan == null)
        {
            backEdgePath = default;
            return false;
        }

        var transition = SymbolicOperationTransferKernel.Invalidate(
            path.State,
            plan.BackEdgeInvalidations,
            plan.Loop.Span,
            "cfg-program-point.loop-back-edge");
        if (!transition.IsExact)
        {
            backEdgePath = default;
            return false;
        }

        var state = transition.State;
        foreach (var invariant in plan.Invariants)
        {
            transition = SymbolicOperationTransferKernel.TransitionLoopEdge(
                state,
                SymbolicLoopEdgeKind.BackEdge,
                invariant,
                plan.Loop.Span,
                "cfg-program-point.loop-invariant");
            if (!transition.IsExact)
            {
                backEdgePath = default;
                return false;
            }
            state = transition.State;
        }

        backEdgePath = new CfgPathState(state, null);
        return true;
    }

    private static bool TryApplyLoopExit(
        BasicBlock source,
        BasicBlock destination,
        CfgPathState path,
        IReadOnlyList<SymbolicLoopTransferPlan> loopPlans,
        out CfgPathState exitPath)
    {
        var plan = loopPlans
            .Where(candidate => BlockIsWithinLoop(source, candidate.Loop) &&
                !BlockIsWithinLoop(destination, candidate.Loop))
            .OrderBy(static candidate => candidate.Loop.Span.Length)
            .FirstOrDefault();
        if (plan == null)
        {
            exitPath = path;
            return true;
        }

        var state = path.State;
        if (plan.Loop is DoStatementSyntax)
        {
            var invalidation = SymbolicOperationTransferKernel.Invalidate(
                state,
                plan.BackEdgeInvalidations,
                plan.Loop.Span,
                "cfg-program-point.loop-exit");
            if (!invalidation.IsExact)
            {
                exitPath = default;
                return false;
            }
            state = invalidation.State;
        }

        foreach (var invariant in plan.Invariants)
        {
            var transition = SymbolicOperationTransferKernel.TransitionLoopEdge(
                state,
                SymbolicLoopEdgeKind.Exit,
                invariant,
                plan.Loop.Span,
                "cfg-program-point.loop-invariant");
            if (!transition.IsExact)
            {
                exitPath = default;
                return false;
            }
            state = transition.State;
        }

        exitPath = path with { State = state };
        return true;
    }

    private static bool BlockIsWithinLoop(BasicBlock block, StatementSyntax loop) =>
        block.Operations.Any(operation => loop.Span.Contains(operation.Syntax.Span)) ||
        block.BranchValue != null && loop.Span.Contains(block.BranchValue.Syntax.Span);

    private static bool HasAbruptOrNestedLoopControlFlow(StatementSyntax loop) =>
        CSharpSyntaxFacts.DescendantNodesInExecution(loop, includeSelf: false)
            .Any(node => node is BreakStatementSyntax or
                ContinueStatementSyntax or
                GotoStatementSyntax or
                ReturnStatementSyntax or
                ThrowStatementSyntax or
                ThrowExpressionSyntax or
                YieldStatementSyntax or
                WhileStatementSyntax or
                DoStatementSyntax or
                ForStatementSyntax or
                ForEachStatementSyntax or
                ForEachVariableStatementSyntax);

    private static bool SupportsLoopLocalCurrentCompletion(
        SyntaxNode site,
        IOperation operation) =>
        site is LocalDeclarationStatementSyntax declaration &&
            declaration.Declaration.Variables.Count == 1 ||
        site is ExpressionStatementSyntax &&
            operation is IExpressionStatementOperation
            {
                Operation: ISimpleAssignmentOperation
            };

    private static bool TryMergeLoopTargetStates(
        IReadOnlyList<SymbolicState> states,
        int phiScope,
        out SymbolicState merged)
    {
        if (states.Any(static state => state.IsContradictory))
        {
            merged = null!;
            return false;
        }
        if (states.Count == 1)
        {
            merged = states[0];
            return true;
        }

        merged = SymbolicStateMerger.MergePathStatesAcrossAll(
            states,
            SymbolicStateMerger.AreEvidenceEquivalentFacts,
            phiScope);
        return true;
    }

    private static bool TryLowerLoopPlans(
        SyntaxNode executionRoot,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out IReadOnlyList<SymbolicLoopTransferPlan> plans)
    {
        var lowered = new List<SymbolicLoopTransferPlan>();
        foreach (var loop in CSharpSyntaxFacts.DescendantNodesInExecution(executionRoot)
                     .OfType<StatementSyntax>()
                     .Where(static statement => statement is WhileStatementSyntax or
                         DoStatementSyntax or ForStatementSyntax or ForEachStatementSyntax or
                         ForEachVariableStatementSyntax))
        {
            var result = SymbolicLoopTransferLowerer.Lower(
                loop,
                semanticModel,
                cancellationToken);
            if (result is not { IsExact: true, Value: { } plan })
            {
                plans = Array.Empty<SymbolicLoopTransferPlan>();
                return false;
            }
            if (plan.Loop is not (WhileStatementSyntax or DoStatementSyntax or ForStatementSyntax) ||
                plan.Loop is not ForStatementSyntax && plan.BackEdgeInvalidations.Any(target =>
                    SymbolicIrReferenceScanner.ContainsVariableOrMember(
                        plan.EntryCondition,
                        target.Key)))
            {
                plans = Array.Empty<SymbolicLoopTransferPlan>();
                return false;
            }
            lowered.Add(plan);
        }

        plans = lowered;
        return true;
    }

    private static bool IsUnreachableTarget(ControlFlowGraph graph, SyntaxNode site) =>
        graph.Blocks.Any(block =>
            !block.IsReachable &&
            (block.Operations.Any(operation => ContainsSite(operation.Syntax, site)) ||
             block.BranchValue != null && ContainsSite(block.BranchValue.Syntax, site)));

    private static CfgPathState MergeIncomingStates(
        IReadOnlyList<CfgPathState> paths,
        SyntaxNode source)
    {
        var feasiblePaths = paths.Where(static path => !path.State.IsContradictory).ToArray();
        if (feasiblePaths.Length != 0 && feasiblePaths.Length != paths.Count)
            return MergeIncomingStates(feasiblePaths, source);
        if (paths.Count == 1)
            return paths[0];
        if (TryMergeGuardedPaths(paths, source, out var merged))
            return merged;

        if (TryGetSiblingPathIndexes(paths, out var siblingIndexes) &&
            TryMergeGuardedPaths(
                siblingIndexes.Select(index => paths[index]).ToArray(),
                source,
                out var siblingMerge))
            return MergeIncomingStates(
                ReplaceSiblingPaths(paths, siblingIndexes, siblingMerge),
                source);

        return new CfgPathState(
            SymbolicStateMerger.MergePathStatesAcrossAll(
                paths.Select(static path => path.State).ToArray(),
                SymbolicStateMerger.AreEvidenceEquivalentFacts,
                source.SpanStart),
            null);
    }

    private static CfgPathState CollapseTerminalCompletionPaths(
        IReadOnlyList<CfgPathState> paths,
        SyntaxNode source)
    {
        if (paths.Count == 1)
            return paths[0];
        if (TryCollapseTerminalGuardedPaths(paths, out var collapsed))
            return collapsed;
        if (TryGetSiblingPathIndexes(paths, out var siblingIndexes) &&
            TryCollapseTerminalGuardedPaths(
                siblingIndexes.Select(index => paths[index]).ToArray(),
                out var siblingCollapse))
            return CollapseTerminalCompletionPaths(
                ReplaceSiblingPaths(paths, siblingIndexes, siblingCollapse),
                source);
        return MergeIncomingStates(paths, source);
    }

    private static bool TryGetSiblingPathIndexes(
        IReadOnlyList<CfgPathState> paths,
        out int[] siblingIndexes)
    {
        for (var firstIndex = 0; firstIndex < paths.Count; firstIndex++)
        {
            var firstFrame = paths[firstIndex].GuardFrame;
            if (firstFrame == null)
                continue;

            var candidates = Enumerable.Range(firstIndex, paths.Count - firstIndex)
                .Where(index => paths[index].GuardFrame is { } candidate &&
                    candidate.Baseline.NormalizedProofKey == firstFrame.Baseline.NormalizedProofKey &&
                    TryMergeGuardFrames(
                        new[] { firstFrame.Parent, candidate.Parent },
                        out _))
                .ToArray();
            if (candidates.Length <= 1)
                continue;
            siblingIndexes = candidates;
            return true;
        }

        siblingIndexes = Array.Empty<int>();
        return false;
    }

    private static IReadOnlyList<CfgPathState> ReplaceSiblingPaths(
        IReadOnlyList<CfgPathState> paths,
        IReadOnlyList<int> siblingIndexes,
        CfgPathState replacement)
    {
        var siblingSet = new HashSet<int>(siblingIndexes);
        var firstIndex = siblingIndexes[0];
        var reduced = new List<CfgPathState>(paths.Count - siblingIndexes.Count + 1);
        for (var index = 0; index < paths.Count; index++)
        {
            if (index == firstIndex)
                reduced.Add(replacement);
            else if (!siblingSet.Contains(index))
                reduced.Add(paths[index]);
        }
        return reduced;
    }

    private static bool TryCollapseTerminalGuardedPaths(
        IReadOnlyList<CfgPathState> paths,
        out CfgPathState collapsed)
    {
        var frame = paths[0].GuardFrame;
        if (frame != null &&
            paths.All(path => path.GuardFrame is { } candidate &&
                              candidate.Baseline.NormalizedProofKey == frame.Baseline.NormalizedProofKey) &&
            paths.Any(static path => path.GuardFrame!.GuardWhenTrue) &&
            paths.Any(static path => !path.GuardFrame!.GuardWhenTrue) &&
            TryMergeGuardFrames(
                paths.Select(static path => path.GuardFrame!.Parent).ToArray(),
                out var parentFrame))
        {
            collapsed = new CfgPathState(frame.Baseline, parentFrame);
            return true;
        }

        collapsed = default;
        return false;
    }

    private static bool TryMergeGuardedPaths(
        IReadOnlyList<CfgPathState> paths,
        SyntaxNode source,
        out CfgPathState merged)
    {
        var frame = paths[0].GuardFrame;
        if (frame != null &&
            paths.All(path => path.GuardFrame is { } candidate &&
                              candidate.Baseline.NormalizedProofKey == frame.Baseline.NormalizedProofKey) &&
            TryMergeGuardFrames(
                paths.Select(static path => path.GuardFrame!.Parent).ToArray(),
                out var parentFrame))
        {
            var orderedPaths = paths
                .OrderByDescending(static path => path.GuardFrame!.GuardWhenTrue)
                .ToArray();
            var completedStates = orderedPaths.Select(static path => path.State).ToArray();
            var mergeBaseline = SymbolicStateMerger.MergeCommonStates(
                new SymbolicState(),
                completedStates);
            if (paths.Any(static path => path.GuardFrame!.GuardInvalidated))
            {
                merged = new CfgPathState(mergeBaseline, parentFrame);
                return true;
            }
            merged = new CfgPathState(
                SymbolicStateMerger.MergeGuardedStates(
                    mergeBaseline,
                    orderedPaths.Select(path =>
                        new SymbolicStateMerger.GuardedState(path.GuardFrame!.Guard, path.State)).ToArray(),
                    source,
                    SymbolicAnalysisLimitKind.IfElseFactMerge,
                    SymbolicAnalysisLimitContext.Limits.MaxMergedIfElseFacts,
                    "cfg-program-point.if-merge"),
                parentFrame);
            return true;
        }

        merged = default;
        return false;
    }

    private static SymbolicState OrderTargetState(
        SymbolicState state,
        CfgPathState path,
        bool targetIsInsideBranch) =>
        targetIsInsideBranch && path.GuardFrame != null
            ? new SymbolicState(
                state.Facts,
                GetGuardsOuterToInner(path.GuardFrame).Concat(state.PathConditions),
                state.SymbolVersions,
                state.IsContradictory)
            : state;

    private readonly record struct CfgPathState(
        SymbolicState State,
        CfgGuardFrame? GuardFrame);

    private sealed record CfgRootCompletionPlan(
        ISet<ISymbol> PreservedLocals,
        ControlFlowBranch? CompletionBranch);

    private readonly record struct CfgIncomingEdge(
        ControlFlowBranch? Branch,
        CfgFinallyContinuation? Continuation,
        CfgIncomingEdgeKind Kind,
        string? HistoryKey = null);

    private enum CfgIncomingEdgeKind
    {
        Entry,
        Conditional,
        FallThrough,
        FinallyContinuation,
        History
    }

    private sealed record CfgGuardFrame(
        SymbolicState Baseline,
        SymbolicCondition Guard,
        bool GuardWhenTrue,
        bool GuardInvalidated,
        CfgGuardFrame? Parent);

    private static SymbolicCondition? GetActiveGuard(CfgGuardFrame? frame)
    {
        if (frame == null)
            return null;

        var parent = GetActiveGuard(frame.Parent);
        return parent == null
            ? frame.Guard
            : new SymbolicBinaryCondition(
                SymbolicConditionOperator.And,
                parent,
                frame.Guard);
    }

    private static IReadOnlyList<SymbolicCondition> GetGuardsOuterToInner(CfgGuardFrame frame)
    {
        var guards = new List<SymbolicCondition>();
        for (var current = frame; current != null; current = current.Parent)
            guards.Add(current.Guard);
        guards.Reverse();
        return guards;
    }

    private static bool HasInvalidatedGuard(CfgGuardFrame? frame) =>
        frame != null && (frame.GuardInvalidated || HasInvalidatedGuard(frame.Parent));

    private static CfgGuardFrame? InvalidateGuards(CfgGuardFrame? frame, ISymbol target) =>
        frame == null
            ? null
            : frame with
            {
                GuardInvalidated = frame.GuardInvalidated || GuardReferencesTarget(frame.Guard, target),
                Parent = InvalidateGuards(frame.Parent, target)
            };

    private static bool TryMergeGuardFrames(
        IReadOnlyList<CfgGuardFrame?> frames,
        out CfgGuardFrame? merged)
    {
        var first = frames[0];
        if (first == null)
        {
            merged = null;
            return frames.All(static frame => frame == null);
        }
        if (frames.Any(frame => frame == null ||
                frame.GuardWhenTrue != first.GuardWhenTrue ||
                frame.Baseline.NormalizedProofKey != first.Baseline.NormalizedProofKey ||
                SymbolicState.CreateProofConditionKey(frame.Guard) !=
                SymbolicState.CreateProofConditionKey(first.Guard)) ||
            !TryMergeGuardFrames(
                frames.Select(static frame => frame!.Parent).ToArray(),
                out var parent))
        {
            merged = null;
            return false;
        }

        merged = first with
        {
            GuardInvalidated = frames.Any(static frame => frame!.GuardInvalidated),
            Parent = parent
        };
        return true;
    }

    private static bool TryCreateFinallyLocalTargetPlan(
        SyntaxNode site,
        SyntaxNode executionRoot,
        FinallyClauseSyntax finallyClause,
        ControlFlowGraph graph,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out CfgFinallyLocalTargetPlan? plan)
    {
        plan = null;
        var targetStatement = site.AncestorsAndSelf().OfType<StatementSyntax>().FirstOrDefault();
        if (targetStatement == null ||
            !ReferenceEquals(targetStatement.Parent, finallyClause.Block) ||
            finallyClause.Parent is not TryStatementSyntax tryStatement ||
            !ReferenceEquals(tryStatement.Finally, finallyClause) ||
            tryStatement.Catches.Count != 0 ||
            CSharpSyntaxFacts.GetBlockBody(executionRoot) is not { } rootBlock ||
            !ReferenceEquals(tryStatement.Parent, rootBlock) ||
            !tryStatement.Block.Statements.All(statement =>
                SupportsFinallyLinearStatement(statement, semanticModel, cancellationToken)) ||
            !finallyClause.Block.Statements.All(statement =>
                SupportsFinallyLinearStatement(statement, semanticModel, cancellationToken)))
            return false;

        var regions = EnumerateRegions(graph.Root)
            .Where(region => region.Kind == ControlFlowRegionKind.Finally &&
                             RegionContainsSyntax(region, graph, finallyClause.Block))
            .ToArray();
        if (regions.Length != 1)
            return false;

        var protectedMutations = SymbolicStateInvalidator.LowerNestedMutations(
            tryStatement.Block,
            semanticModel,
            cancellationToken);
        if (protectedMutations.HasUnsupportedMutation)
            return false;

        plan = new CfgFinallyLocalTargetPlan(regions[0], protectedMutations);
        return true;
    }

    private static bool SupportsFinallyLinearStatement(
        StatementSyntax statement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (statement is LocalDeclarationStatementSyntax declaration &&
            declaration.UsingKeyword.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.None) &&
            declaration.AwaitKeyword.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.None))
        {
            return declaration.Declaration.Variables.All(variable =>
                semanticModel.GetDeclaredSymbol(variable, cancellationToken) is ILocalSymbol { RefKind: RefKind.None } &&
                (variable.Initializer == null ||
                 SupportsFinallyLinearValue(
                     variable.Initializer.Value,
                     semanticModel,
                     cancellationToken)));
        }

        if (statement is not ExpressionStatementSyntax
            {
                Expression: AssignmentExpressionSyntax assignment
            } ||
            !assignment.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.SimpleAssignmentExpression) ||
            CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(assignment.Left) is not IdentifierNameSyntax left ||
            semanticModel.GetSymbolInfo(left, cancellationToken).Symbol is not
                (ILocalSymbol { RefKind: RefKind.None } or IParameterSymbol { RefKind: RefKind.None }))
            return false;

        return SupportsFinallyLinearValue(assignment.Right, semanticModel, cancellationToken);
    }

    private static bool SupportsFinallyLinearValue(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        expression = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(expression);
        if (expression is LiteralExpressionSyntax)
            return true;
        if (expression is not IdentifierNameSyntax identifier ||
            semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol is not
                (ILocalSymbol { RefKind: RefKind.None } or IParameterSymbol { RefKind: RefKind.None }))
            return false;

        var typeInfo = semanticModel.GetTypeInfo(identifier, cancellationToken);
        return typeInfo.Type != null &&
               SymbolEqualityComparer.Default.Equals(typeInfo.Type, typeInfo.ConvertedType);
    }

    private static IEnumerable<ControlFlowRegion> EnumerateRegions(ControlFlowRegion region)
    {
        yield return region;
        foreach (var nested in region.NestedRegions)
            foreach (var descendant in EnumerateRegions(nested))
                yield return descendant;
    }

    private static bool RegionContainsSyntax(
        ControlFlowRegion region,
        ControlFlowGraph graph,
        SyntaxNode syntax)
    {
        for (var ordinal = region.FirstBlockOrdinal; ordinal <= region.LastBlockOrdinal; ordinal++)
        {
            var block = graph.Blocks[ordinal];
            if (block.Operations.Any(operation => syntax.Span.Contains(operation.Syntax.SpanStart)) ||
                block.BranchValue != null && syntax.Span.Contains(block.BranchValue.Syntax.SpanStart))
                return true;
        }
        return false;
    }

    private static bool IsSupportedFinallyLocalContinuation(
        CfgFinallyContinuation continuation,
        CfgFinallyLocalTargetPlan plan) =>
        continuation.Regions.Length == 1 &&
        continuation.RegionIndex == 0 &&
        ReferenceEquals(continuation.Regions[0], plan.Region) &&
        continuation.Parent == null &&
        continuation.TerminalBranch == null;

    private static bool TryObserveFinallyLocalTarget(
        CfgFinallyContinuation? continuation,
        CfgFinallyLocalTargetPlan plan,
        ref CfgFinallyContinuation? observed)
    {
        if (continuation == null || !IsSupportedFinallyLocalContinuation(continuation, plan))
            return false;
        if (observed == null)
        {
            observed = continuation;
            return true;
        }
        return observed == continuation;
    }

    private readonly record struct CfgTraversalPoint(
        BasicBlock Block,
        CfgFinallyContinuation? Continuation);

    private sealed record CfgFinallyLocalTargetPlan(
        ControlFlowRegion Region,
        SymbolicNestedMutationInvalidationPlan ProtectedMutations);

    private sealed record CfgFinallyContinuation(
        ControlFlowBranch OriginBranch,
        ImmutableArray<ControlFlowRegion> Regions,
        int RegionIndex,
        BasicBlock? Destination,
        ControlFlowBranch? TerminalBranch,
        CfgFinallyContinuation? Parent);

    private static bool TryApplyOperation(
        ref SymbolicState state,
        IOperation operation,
        SymbolicCondition? guard,
        bool allowGuardedReferenceAssignments,
        bool allowGuardMutation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string assignmentProvenance,
        out ISymbol? invalidatedGuardTarget)
    {
        invalidatedGuardTarget = null;
        if (operation is IVariableDeclarationGroupOperation declarations)
        {
            foreach (var declarator in declarations.Declarations
                         .SelectMany(static declaration => declaration.Declarators))
            {
                if (declarator.Initializer?.Value is not { } value ||
                    !TryApplyAssignment(
                        ref state,
                        declarator.Symbol,
                        value,
                        guard,
                        allowGuardedReferenceAssignments,
                        allowGuardMutation,
                        semanticModel,
                        cancellationToken,
                        assignmentProvenance,
                        out var declaratorInvalidatedGuardTarget))
                    return false;
                invalidatedGuardTarget ??= declaratorInvalidatedGuardTarget;
            }

            return true;
        }

        var assignment = operation switch
        {
            IExpressionStatementOperation { Operation: ISimpleAssignmentOperation nested } => nested,
            ISimpleAssignmentOperation direct => direct,
            _ => null
        };
        if (assignment != null)
            return TryGetDirectTarget(assignment.Target, out var target)
                ? TryApplyAssignment(
                    ref state,
                    target,
                    assignment.Value,
                    guard,
                    allowGuardedReferenceAssignments,
                    allowGuardMutation,
                    semanticModel,
                    cancellationToken,
                    assignmentProvenance,
                    out invalidatedGuardTarget)
                : TryApplyExplicitTargetAssignment(
                    ref state,
                    assignment,
                    guard,
                    semanticModel,
                    cancellationToken,
                    out invalidatedGuardTarget);

        var increment = operation switch
        {
            IExpressionStatementOperation { Operation: IIncrementOrDecrementOperation nested } => nested,
            IIncrementOrDecrementOperation direct => direct,
            _ => null
        };
        if (increment != null)
            return TryGetDirectTarget(increment.Target, out var target) &&
                   increment.Syntax is Microsoft.CodeAnalysis.CSharp.Syntax.ExpressionSyntax expression &&
                   SymbolicStateValueFacts.TryGetCurrentValue(state, target, out var previousValue) &&
                   SymbolicAssignmentValueUpdater.TryCreateIncrementOrDecrement(
                       previousValue,
                       increment.Kind == OperationKind.Increment ? 1 : -1,
                       expression,
                       semanticModel,
                       cancellationToken,
                       target,
                       out var updatedValue,
                       out var isChecked) &&
                   TryApplyComputedUpdate(
                       ref state,
                       target,
                       updatedValue,
                       expression,
                       guard,
                       semanticModel,
                       cancellationToken,
                       increment.Kind == OperationKind.Increment
                           ? SymbolicComputedUpdateKind.Increment
                           : SymbolicComputedUpdateKind.Decrement,
                       isChecked,
                       increment.Kind == OperationKind.Increment
                            ? "ir.path.prior-statement.increment"
                            : "ir.path.prior-statement.decrement",
                        out invalidatedGuardTarget);

        var compound = operation switch
        {
            IExpressionStatementOperation { Operation: ICompoundAssignmentOperation nested } => nested,
            ICompoundAssignmentOperation direct => direct,
            _ => null
        };
        return compound != null &&
               TryGetDirectTarget(compound.Target, out var compoundTarget) &&
               compound.Syntax is Microsoft.CodeAnalysis.CSharp.Syntax.AssignmentExpressionSyntax compoundSyntax &&
               SymbolicStateValueFacts.TryGetCurrentValue(state, compoundTarget, out var compoundPreviousValue) &&
               SymbolicAssignmentValueUpdater.TryCreateCompoundAssignment(
                   compoundPreviousValue,
                   compoundSyntax,
                   semanticModel,
                   cancellationToken,
                   compoundTarget,
                   out var compoundValue,
                   out var compoundIsChecked) &&
               TryApplyComputedUpdate(
                   ref state,
                   compoundTarget,
                   compoundValue,
                   compoundSyntax,
                   guard,
                   semanticModel,
                   cancellationToken,
                   SymbolicComputedUpdateKind.CompoundAssignment,
                    compoundIsChecked,
                    "ir.path.prior-statement.compound-assignment",
                    out invalidatedGuardTarget);
    }

    private static bool TryApplyCurrentCompletion(
        ref SymbolicState state,
        SyntaxNode site,
        IOperation operation,
        SymbolicCondition? guard,
        bool allowGuardedReferenceAssignments,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (site is ExpressionSyntax expression &&
            CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(expression) is not AssignmentExpressionSyntax)
            return SymbolicExpressionStateTransfer.TryApplyCurrentExpressionCompletion(
                ref state,
                expression,
                semanticModel,
                cancellationToken);

        var completedState = state;
        if (!TryApplyOperation(
                ref completedState,
                operation,
                guard,
                allowGuardedReferenceAssignments,
                false,
                semanticModel,
                cancellationToken,
                "ir.path.prior-statement",
                out var invalidatedGuardTarget) ||
            invalidatedGuardTarget != null)
            return false;

        state = completedState;

        if (site is ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax assignment } statement)
            SymbolicNormalCompletionStateTransfer.AddNormalCompletionStateFacts(
                ref state,
                assignment.Right,
                statement,
                semanticModel.GetSymbolInfo(assignment.Left, cancellationToken).Symbol is
                    not (ILocalSymbol or IParameterSymbol),
                semanticModel,
                cancellationToken);
        else if (site is BlockSyntax)
            AddOperationNormalCompletionFacts(
                ref state,
                operation,
                semanticModel,
                cancellationToken);

        return true;
    }

    private static void AddOperationNormalCompletionFacts(
        ref SymbolicState state,
        IOperation operation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var assignment = operation switch
        {
            IExpressionStatementOperation { Operation: ISimpleAssignmentOperation nested } => nested,
            ISimpleAssignmentOperation direct => direct,
            _ => null
        };
        if (assignment?.Value.Syntax is not ExpressionSyntax value ||
            assignment.Syntax.FirstAncestorOrSelf<StatementSyntax>() is not { } statement)
            return;

        SymbolicNormalCompletionStateTransfer.AddNormalCompletionStateFacts(
            ref state,
            value,
            statement,
            !TryGetDirectTarget(assignment.Target, out var target) ||
            target is not (ILocalSymbol or IParameterSymbol),
            semanticModel,
            cancellationToken);
    }

    private static string GetAssignmentProvenance(
        IOperation operation,
        ForStatementSyntax? forInitialEntry) =>
        forInitialEntry != null && IsForInitializerSyntax(operation.Syntax, forInitialEntry)
            ? "ir.path.for-initializer"
            : "ir.path.prior-statement";

    private static bool IsForInitializerSyntax(
        SyntaxNode syntax,
        ForStatementSyntax forStatement) =>
        forStatement.Declaration?.Variables.Any(variable =>
            variable.Span.Contains(syntax.SpanStart)) == true ||
        forStatement.Initializers.Any(initializer =>
            initializer.Span.Contains(syntax.SpanStart));

    private static bool SupportsForInitialEntryOperation(
        IOperation operation,
        ForStatementSyntax forStatement)
    {
        if (!IsForInitializerSyntax(operation.Syntax, forStatement))
            return true;
        var assignment = operation switch
        {
            IExpressionStatementOperation { Operation: ISimpleAssignmentOperation nested } => nested,
            ISimpleAssignmentOperation direct => direct,
            _ => null
        };
        return assignment != null && TryGetDirectTarget(assignment.Target, out _);
    }

    private static void AddForDeclarationInitializerNormalCompletionFacts(
        ref SymbolicState state,
        IOperation operation,
        ForStatementSyntax forStatement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var assignment = operation switch
        {
            IExpressionStatementOperation { Operation: ISimpleAssignmentOperation nested } => nested,
            ISimpleAssignmentOperation direct => direct,
            _ => null
        };
        if (assignment == null ||
            !TryGetDirectTarget(assignment.Target, out var assignmentTarget) ||
            forStatement.Declaration?.Variables.FirstOrDefault(variable =>
                variable.Span.Contains(operation.Syntax.SpanStart)) is not
                {
                    Initializer.Value: { } value
                } declarator ||
            !SymbolEqualityComparer.Default.Equals(
                semanticModel.GetDeclaredSymbol(declarator, cancellationToken),
                assignmentTarget))
            return;

        SymbolicNormalCompletionStateTransfer.AddNormalCompletionStateFacts(
            ref state,
            value,
            forStatement.Statement,
            includeThrowGuardFacts: false,
            semanticModel,
            cancellationToken);
    }

    private static bool TryApplyCurrentDeclarationCompletion(
        ref SymbolicState state,
        LocalDeclarationStatementSyntax declaration,
        SymbolicCondition? guard,
        bool allowGuardedReferenceAssignments,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var completedState = RemoveMatchingThrowGuard(
            state,
            declaration,
            guard,
            semanticModel,
            cancellationToken);
        foreach (var declarator in declaration.Declaration.Variables)
        {
            if (declarator.Initializer is not { } initializer ||
                semanticModel.GetOperation(declarator, cancellationToken) is not
                    IVariableDeclaratorOperation
                    {
                        Symbol: var declaratorSymbol,
                        Initializer.Value: { } value
                    } ||
                !TryApplyAssignment(
                    ref completedState,
                    declaratorSymbol,
                    value,
                    guard,
                    allowGuardedReferenceAssignments,
                    false,
                    semanticModel,
                    cancellationToken,
                    "ir.path.prior-statement",
                    out _))
                return false;

            SymbolicNormalCompletionStateTransfer.AddNormalCompletionStateFacts(
                ref completedState,
                initializer.Value,
                declaration,
                false,
                semanticModel,
                cancellationToken);
        }

        state = completedState;
        return true;
    }

    private static SymbolicState RemoveMatchingThrowGuard(
        SymbolicState state,
        LocalDeclarationStatementSyntax declaration,
        SymbolicCondition? guard,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (guard == null) return state;

        var guardKey = SymbolicState.CreateProofConditionKey(guard);
        var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
        foreach (var variable in declaration.Declaration.Variables)
        {
            if (variable.Initializer is not { Value: { } value } ||
                semanticModel.GetDeclaredSymbol(variable, cancellationToken) is not { } target)
                continue;
            var completion = SymbolicOperationLowerer.LowerThrowGuardedAssignmentPostcondition(
                target,
                SymbolicAssignmentStateTransfer.GetThrowGuardedValue(value),
                context,
                "ir.path.prior-statement");
            if (completion == null ||
                SymbolicState.CreateProofConditionKey(completion) != guardKey)
                continue;

            return new SymbolicState(
                state.Facts,
                state.PathConditions.Where(condition =>
                    SymbolicState.CreateProofConditionKey(condition) != guardKey),
                state.SymbolVersions,
                state.IsContradictory);
        }

        return state;
    }

    private static bool TryApplyComputedUpdate(
        ref SymbolicState state,
        ISymbol target,
        SymbolicTerm updatedValue,
        SyntaxNode source,
        SymbolicCondition? guard,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SymbolicComputedUpdateKind updateKind,
        bool isChecked,
        string provenance,
        out ISymbol? invalidatedGuardTarget)
    {
        invalidatedGuardTarget = GuardReferencesTarget(guard, target) ? target : null;
        var transition = SymbolicOperationTransferAdapter.ApplyComputedUpdate(
            state,
            target,
            updatedValue,
            source,
            semanticModel,
            cancellationToken,
            updateKind,
            isChecked,
            provenance);
        if (!transition.IsExact)
            return false;

        state = transition.State;
        return true;
    }

    private static bool TryApplyAssignment(
        ref SymbolicState state,
        ISymbol target,
        IOperation value,
        SymbolicCondition? guard,
        bool allowGuardedReferenceAssignments,
        bool allowGuardMutation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string provenance,
        out ISymbol? invalidatedGuardTarget)
    {
        if (RequiresStructuralAssignmentFallback(
                target,
                guard,
                allowGuardedReferenceAssignments,
                allowGuardMutation))
        {
            invalidatedGuardTarget = null;
            return false;
        }

        invalidatedGuardTarget = GuardReferencesTarget(guard, target) ? target : null;
        if (value.Syntax is not Microsoft.CodeAnalysis.CSharp.Syntax.ExpressionSyntax expression ||
            SymbolMutationFacts.ExpressionReferencesSymbol(
                expression,
                target,
                semanticModel,
                cancellationToken))
            return false;

        var transition = SymbolicOperationTransferAdapter.ApplyAssignment(
            state,
            target,
            expression,
            semanticModel,
            cancellationToken,
            provenance: provenance,
            bindingProvenance: provenance + ".assigned-value",
            asExpressionProvenanceRoot: provenance + ".as",
            postconditionProfile: SymbolicAssignmentPostconditionProfile.Symbolic);
        if (!transition.IsExact)
            return false;

        state = transition.State;
        return true;
    }

    private static bool TryApplyExplicitTargetAssignment(
        ref SymbolicState state,
        ISimpleAssignmentOperation assignment,
        SymbolicCondition? guard,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out ISymbol? invalidatedGuardTarget)
    {
        invalidatedGuardTarget = null;
        if (guard != null || assignment.Syntax is not AssignmentExpressionSyntax syntax)
            return false;

        SymbolicStateInvalidator.InvalidateMutationTarget(
            ref state,
            syntax.Left,
            semanticModel,
            cancellationToken);
        SymbolicStateInvalidator.InvalidateNestedAssignmentMutations(
            ref state,
            syntax,
            semanticModel,
            cancellationToken);
        var transition = SymbolicOperationTransferAdapter.ApplyLowering(
            state,
            SymbolicOperationLowerer.LowerExplicitTargetAssignment(
                syntax,
                new SymbolicLoweringContext(semanticModel, cancellationToken)));
        if (!transition.IsExact)
            return false;

        state = transition.State;
        return true;
    }

    private static bool RequiresStructuralAssignmentFallback(
        ISymbol target,
        SymbolicCondition? guard,
        bool allowGuardedReferenceAssignments,
        bool allowGuardMutation)
    {
        var type = target switch
        {
            ILocalSymbol local => local.Type,
            IParameterSymbol parameter => parameter.Type,
            _ => null
        };
        return guard != null &&
            type?.IsReferenceType == true &&
            (!allowGuardedReferenceAssignments ||
             !allowGuardMutation && GuardReferencesTarget(guard, target));
    }

    private static bool GuardReferencesTarget(SymbolicCondition? guard, ISymbol target) =>
        guard != null &&
        SymbolicIrReferenceScanner.ContainsVariableOrMember(
            guard,
            SymbolicFactFactory.GetSmtVariableName(target));

    private static bool TryGetDirectTarget(IOperation operation, out ISymbol target)
    {
        target = operation switch
        {
            ILocalReferenceOperation local => local.Local,
            IParameterReferenceOperation parameter => parameter.Parameter,
            _ => null!
        };
        return target != null;
    }

    private static bool ContainsSite(SyntaxNode container, SyntaxNode site) =>
        container.Span.Contains(site.SpanStart) || site.Span.Contains(container.SpanStart);

    private static bool TryGetForInitialEntryHeader(
        ControlFlowGraph graph,
        ForStatementSyntax forStatement,
        out BasicBlock header)
    {
        var matches = graph.Blocks.Where(block =>
            block.ConditionKind != ControlFlowConditionKind.None &&
            block.BranchValue != null &&
            ContainsSite(block.BranchValue.Syntax, forStatement.Condition!)).ToArray();
        if (matches.Length != 1)
        {
            header = null!;
            return false;
        }

        header = matches[0];
        return HasLinearInitialEntryPrefix(header);
    }

    private static bool HasLinearInitialEntryPrefix(BasicBlock header)
    {
        var visited = new HashSet<BasicBlock>();
        var current = header;
        while (current.Kind != BasicBlockKind.Entry)
        {
            if (!visited.Add(current))
                return false;
            var forwardPredecessors = current.Predecessors.Where(predecessor =>
                predecessor.Source.Ordinal < current.Ordinal &&
                predecessor.Semantics is
                    ControlFlowBranchSemantics.Regular or
                    ControlFlowBranchSemantics.StructuredExceptionHandling).ToArray();
            if (forwardPredecessors.Length != 1)
                return false;
            current = forwardPredecessors[0].Source;
        }

        return true;
    }

    private static bool TryGetNestedBlockCompletionTarget(
        ControlFlowGraph graph,
        BlockSyntax block,
        out IOperation? completionOperation,
        out ISet<CfgEdge> completionEdges,
        out ISet<ControlFlowBranch> completionTerminalBranches)
    {
        var operations = graph.Blocks
            .SelectMany(cfgBlock => cfgBlock.Operations.Select((operation, index) =>
                new NestedBlockOperation(cfgBlock, operation, index)))
            .Where(candidate => block.Span.Contains(candidate.Operation.Syntax.SpanStart))
            .OrderBy(static candidate => candidate.Operation.Syntax.SpanStart)
            .ThenBy(static candidate => candidate.Block.Ordinal)
            .ThenBy(static candidate => candidate.Index)
            .ToArray();
        var internalBranches = graph.Blocks.Where(cfgBlock =>
            cfgBlock.ConditionKind != ControlFlowConditionKind.None &&
            cfgBlock.BranchValue != null &&
            block.Span.Contains(cfgBlock.BranchValue.Syntax.SpanStart)).ToArray();
        var terminalBranches = new HashSet<ControlFlowBranch>(graph.Blocks
            .Where(cfgBlock => BlockContainsSyntax(cfgBlock, block))
            .SelectMany(GetSuccessors)
            .Where(branch =>
                branch.FinallyRegions.IsDefaultOrEmpty &&
                IsTerminalCompletionBranch(branch)));
        if (operations.Length != 0)
        {
            var completion = operations[operations.Length - 1];
            if (internalBranches.All(branch =>
                    branch.Ordinal < completion.Block.Ordinal &&
                    AllRegularPathsReach(branch.ConditionalSuccessor, completion.Block) &&
                    AllRegularPathsReach(branch.FallThroughSuccessor, completion.Block)))
            {
                completionOperation = completion.Operation;
                completionEdges = new HashSet<CfgEdge>();
                completionTerminalBranches = new HashSet<ControlFlowBranch>();
                return true;
            }
        }

        var edges = new HashSet<CfgEdge>(graph.Blocks
            .Where(cfgBlock => BlockContainsSyntax(cfgBlock, block))
            .SelectMany(GetSuccessors)
            .Where(branch => branch is
            {
                Semantics: ControlFlowBranchSemantics.Regular,
                Destination: { } destination
            } && !BlockContainsSyntax(destination, block))
            .Select(static branch => new CfgEdge(
                branch.Source.Ordinal,
                branch.Destination!.Ordinal)));
        var completesOnlyThroughTerminalStatement = edges.Count == 0 &&
            operations.Length == 0 &&
            terminalBranches.Count >= 2 &&
            block.Statements is { Count: 1 } &&
            block.Statements[0] is IfStatementSyntax { Else: not null };
        if (edges.Count == 0 && !completesOnlyThroughTerminalStatement ||
            internalBranches.Any(branch =>
                operations.Length != 0 &&
                branch.Ordinal >= operations[operations.Length - 1].Block.Ordinal ||
                !AllPathsReachExitOrComplete(
                    branch,
                    branch.ConditionalSuccessor,
                    edges,
                    terminalBranches) ||
                !AllPathsReachExitOrComplete(
                    branch,
                    branch.FallThroughSuccessor,
                    edges,
                    terminalBranches)))
        {
            completionOperation = null;
            completionEdges = new HashSet<CfgEdge>();
            completionTerminalBranches = new HashSet<ControlFlowBranch>();
            return false;
        }

        completionOperation = null;
        completionEdges = edges;
        completionTerminalBranches = terminalBranches;
        return true;
    }

    private static bool BlockContainsSyntax(BasicBlock cfgBlock, BlockSyntax block) =>
        cfgBlock.Operations.Any(operation => block.Span.Contains(operation.Syntax.SpanStart)) ||
        cfgBlock.BranchValue != null &&
        block.Span.Contains(cfgBlock.BranchValue.Syntax.SpanStart);

    private static IEnumerable<ControlFlowBranch> GetSuccessors(BasicBlock block)
    {
        if (block.FallThroughSuccessor != null)
            yield return block.FallThroughSuccessor;
        if (block.ConditionalSuccessor != null &&
            !ReferenceEquals(block.ConditionalSuccessor, block.FallThroughSuccessor))
            yield return block.ConditionalSuccessor;
    }

    private static bool IsTerminalCompletionBranch(ControlFlowBranch branch) =>
        branch.Semantics is
            ControlFlowBranchSemantics.Return or
            ControlFlowBranchSemantics.Throw or
            ControlFlowBranchSemantics.Rethrow or
            ControlFlowBranchSemantics.ProgramTermination;

    private static bool SupportsRootBlockCompletion(ControlFlowGraph graph)
    {
        if (ContainsRegionKind(graph.Root, ControlFlowRegionKind.TryAndCatch))
            return false;
        if (graph.Blocks.Count(static block =>
                block.Operations.Length != 0 || block.BranchValue != null) <= 1)
            return true;
        return graph.Blocks.All(source => GetSuccessors(source).All(branch =>
            branch.Semantics is
                ControlFlowBranchSemantics.Regular or
                ControlFlowBranchSemantics.StructuredExceptionHandling
                ? branch.Destination == null || branch.Destination.Ordinal > source.Ordinal
                : IsTerminalCompletionBranch(branch)));
    }

    private static bool ContainsRegionKind(ControlFlowRegion region, ControlFlowRegionKind kind) =>
        region.Kind == kind || region.NestedRegions.Any(nested => ContainsRegionKind(nested, kind));

    private static CfgRootCompletionPlan CreateRootCompletionPlan(
        ControlFlowGraph graph,
        BlockSyntax root,
        SemanticModel semanticModel)
    {
        var preservedLocals = new HashSet<ISymbol>(
            semanticModel.LookupSymbols(Math.Max(
                    root.OpenBraceToken.Span.End,
                    root.CloseBraceToken.SpanStart - 1))
                .OfType<ILocalSymbol>()
                .Select(static local => local.OriginalDefinition),
            SymbolEqualityComparer.Default);
        ControlFlowBranch? completionBranch = null;
        if (root.Statements.LastOrDefault() is ReturnStatementSyntax returnStatement)
        {
            var returnBranches = graph.Blocks
                .SelectMany(GetSuccessors)
                .Where(static branch => branch.Semantics == ControlFlowBranchSemantics.Return)
                .ToArray();
            completionBranch = returnStatement.Expression == null
                ? null
                : returnBranches.FirstOrDefault(branch =>
                    branch.Source.BranchValue != null &&
                    ContainsSite(branch.Source.BranchValue.Syntax, returnStatement.Expression));
            completionBranch ??= returnBranches
                .OrderByDescending(static branch => branch.Source.Ordinal)
                .FirstOrDefault();
        }
        return new CfgRootCompletionPlan(preservedLocals, completionBranch);
    }

    private static bool AllPathsReachExitOrComplete(
        BasicBlock source,
        ControlFlowBranch? branch,
        ISet<CfgEdge> exits,
        ISet<ControlFlowBranch> terminalBranches) =>
        AllPathsReachExitOrComplete(
            source,
            branch,
            exits,
            terminalBranches,
            new HashSet<BasicBlock>());

    private static bool AllPathsReachExitOrComplete(
        BasicBlock source,
        ControlFlowBranch? branch,
        ISet<CfgEdge> exits,
        ISet<ControlFlowBranch> terminalBranches,
        ISet<BasicBlock> visiting)
    {
        if (branch != null && terminalBranches.Contains(branch))
            return true;
        if (branch is not
            {
                Semantics: ControlFlowBranchSemantics.Regular,
                Destination: { } destination
            })
            return false;
        if (exits.Contains(new CfgEdge(source.Ordinal, destination.Ordinal)))
            return true;
        if (destination.Ordinal <= source.Ordinal || !visiting.Add(destination))
            return false;

        var successors = GetSuccessors(destination).ToArray();
        var reachesExit = successors.Length != 0 && successors.All(successor =>
            AllPathsReachExitOrComplete(
                destination,
                successor,
                exits,
                terminalBranches,
                visiting));
        visiting.Remove(destination);
        return reachesExit;
    }

    private static bool AllRegularPathsReach(
        ControlFlowBranch? branch,
        BasicBlock destination) =>
        AllRegularPathsReach(branch, destination, new HashSet<BasicBlock>());

    private static bool AllRegularPathsReach(
        ControlFlowBranch? branch,
        BasicBlock destination,
        ISet<BasicBlock> visiting) =>
        branch is
        {
            Semantics: ControlFlowBranchSemantics.Regular,
            Destination: { } successor
        } && AllRegularPathsReach(successor, destination, visiting);

    private static bool AllRegularPathsReach(
        BasicBlock block,
        BasicBlock destination,
        ISet<BasicBlock> visiting)
    {
        if (ReferenceEquals(block, destination))
            return true;
        if (block.Ordinal >= destination.Ordinal || !visiting.Add(block))
            return false;

        var reachesDestination = block.ConditionKind == ControlFlowConditionKind.None
            ? AllRegularPathsReach(block.FallThroughSuccessor, destination, visiting) &&
              (block.ConditionalSuccessor == null ||
               AllRegularPathsReach(block.ConditionalSuccessor, destination, visiting))
            : AllRegularPathsReach(block.ConditionalSuccessor, destination, visiting) &&
              AllRegularPathsReach(block.FallThroughSuccessor, destination, visiting);
        visiting.Remove(block);
        return reachesDestination;
    }

    private readonly record struct NestedBlockOperation(
        BasicBlock Block,
        IOperation Operation,
        int Index);

    private readonly record struct CfgEdge(int SourceOrdinal, int DestinationOrdinal);

    private static bool IsTargetOperation(
        IOperation operation,
        SyntaxNode site,
        bool includeCurrentStatementCompletionFacts,
        bool targetIsCompletedNestedBlock,
        IOperation? nestedBlockCompletionOperation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (targetIsCompletedNestedBlock)
            return nestedBlockCompletionOperation != null &&
                   ReferenceEquals(operation, nestedBlockCompletionOperation);
        if (!includeCurrentStatementCompletionFacts || site is not LocalDeclarationStatementSyntax declaration)
            return ContainsSite(operation.Syntax, site);
        if (operation is IVariableDeclarationGroupOperation)
            return ContainsSite(operation.Syntax, declaration);

        ISymbol? target = operation switch
        {
            IVariableDeclaratorOperation declarator => declarator.Symbol,
            ISimpleAssignmentOperation assignment when TryGetDirectTarget(assignment.Target, out var symbol) =>
                symbol,
            _ => null
        };
        return target != null && declaration.Declaration.Variables.Any(variable =>
            SymbolEqualityComparer.Default.Equals(
                semanticModel.GetDeclaredSymbol(variable, cancellationToken),
                target));
    }

    private static bool UsesDefaultAnalysisLimits(SymbolicAnalysisLimits limits)
    {
        var defaults = SymbolicAnalysisLimits.Default;
        return limits.MaxMergedIfElseFacts == defaults.MaxMergedIfElseFacts &&
               limits.MaxMergedSwitchFacts == defaults.MaxMergedSwitchFacts &&
               limits.MaxMergedTryFacts == defaults.MaxMergedTryFacts &&
               limits.MaxTryCompletionBranches == defaults.MaxTryCompletionBranches &&
               limits.MaxFiniteForeachElementFacts == defaults.MaxFiniteForeachElementFacts &&
               limits.MaxScopedBlockCompletionStatements == defaults.MaxScopedBlockCompletionStatements &&
               limits.MaxStructuralNullStateDepth == defaults.MaxStructuralNullStateDepth &&
               limits.MaxMergedPathConditions == defaults.MaxMergedPathConditions &&
               limits.MaxMergeableFactsPerTargetPerState == defaults.MaxMergeableFactsPerTargetPerState &&
               limits.MaxFactChoiceCombinationsPerTarget == defaults.MaxFactChoiceCombinationsPerTarget &&
               limits.MaxGuardFactsPerTargetPerState == defaults.MaxGuardFactsPerTargetPerState;
    }

    private static SymbolicLoweringResult<SymbolicState> Exact(
        SymbolicState state,
        SyntaxNode site) =>
        SymbolicLoweringResult<SymbolicState>.Exact(
            state.Normalize(),
            Provenance(site, "exact"));

    private static SymbolicLoweringResult<SymbolicState> Unsupported(
        SyntaxNode site,
        string detail) =>
        SymbolicLoweringResult<SymbolicState>.Unsupported(Provenance(site, detail));

    private static SymbolicLoweringProvenance Provenance(SyntaxNode site, string detail) =>
        new("cfg-program-point", site.Span, detail);

    private enum CfgProgramPointTargetKind
    {
        BeforeCurrent,
        CurrentCompletion,
        ForInitialEntry
    }
}

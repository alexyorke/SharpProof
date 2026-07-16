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
        bool includeCurrentStatementCompletionFacts = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
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
        if (loopPlans.Any(plan => plan.Loop.Span.Contains(site.SpanStart)))
            return Unsupported(site, "loop-local-target");
        if (site.Ancestors().Any(static ancestor => ancestor is FinallyClauseSyntax))
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
        IOperation? nestedBlockCompletionOperation = null;
        ISet<CfgEdge> nestedBlockCompletionEdges = new HashSet<CfgEdge>();
        if (targetIsCompletedNestedBlock &&
            !TryGetNestedBlockCompletionTarget(
                graph,
                (BlockSyntax)site,
                out nestedBlockCompletionOperation,
                out nestedBlockCompletionEdges))
            return Unsupported(site, "nested-block-completion");
        if (targetIsCompletedRootBlock && !SupportsRootBlockCompletion(graph))
            return Unsupported(site, "root-block-control-flow");

        var state = initialState ?? new SymbolicState();
        SymbolicStatementStateTransfer.AddMethodEntryNullableFlowStateFacts(
            ref state,
            site,
            semanticModel,
            cancellationToken);

        var entryPoint = new CfgTraversalPoint(graph.Blocks[0], null);
        var incoming = new Dictionary<CfgTraversalPoint, List<CfgPathState>>
        {
            [entryPoint] = new List<CfgPathState> { new(state, null) }
        };
        var queue = new Queue<CfgTraversalPoint>();
        var queued = new HashSet<CfgTraversalPoint> { entryPoint };
        var completedPaths = new List<CfgPathState>();
        var nestedBlockCompletedPaths = new List<CfgPathState>();
        queue.Enqueue(entryPoint);
        SymbolicState? targetState = null;
        SymbolicState? guardedTargetState = null;
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
            var currentPath = MergeIncomingStates(incoming[point], site);
            state = currentPath.State;
            if (targetIsCompletedRootBlock && block.Kind == BasicBlockKind.Exit)
            {
                targetState = state;
                continue;
            }
            var foundTarget = false;
            foreach (var operation in block.Operations)
            {
                if (operation.IsImplicit && ReferenceEquals(operation.Syntax, executionRoot))
                    continue;
                if (includeCurrentStatementCompletionFacts &&
                    site is LocalDeclarationStatementSyntax &&
                    operation is IFlowCaptureOperation)
                    continue;
                if (!targetIsCompletedRootBlock &&
                    IsTargetOperation(
                        operation,
                        site,
                        includeCurrentStatementCompletionFacts,
                        targetIsCompletedNestedBlock,
                        nestedBlockCompletionOperation,
                        semanticModel,
                        cancellationToken))
                {
                    if (targetIsInsideBranch && HasInvalidatedGuard(currentPath.GuardFrame))
                        return Unsupported(site, "branch-guard-mutation");
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
                    if (currentPath.GuardFrame == null || targetIsInsideBranch)
                        targetState = observedState;
                    else
                        guardedTargetState = observedState;
                    foundTarget = true;
                    break;
                }
                if (!targetIsCompletedRootBlock &&
                    operation.Syntax.SpanStart >= site.SpanStart &&
                    !(targetIsCompletedNestedBlock &&
                      site.Span.Contains(operation.Syntax.SpanStart)))
                    return Unsupported(site, "operation-order");
                if (!TryApplyOperation(
                        ref state,
                        operation,
                        GetActiveGuard(currentPath.GuardFrame),
                        targetIsInsideBranch,
                        semanticModel,
                        cancellationToken,
                        out var guardInvalidated))
                    return Unsupported(operation.Syntax, "operation-" + operation.Kind);
                if (guardInvalidated)
                    currentPath = currentPath with
                    {
                        GuardFrame = InvalidateGuards(currentPath.GuardFrame)
                    };
                if (targetIsCompletedNestedBlock && site.Span.Contains(operation.Syntax.SpanStart))
                    AddOperationNormalCompletionFacts(
                        ref state,
                        operation,
                        semanticModel,
                        cancellationToken);
            }

            if (foundTarget)
                continue;

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
                    var observedState = OrderTargetState(state, currentPath, targetIsInsideBranch);
                    if (currentPath.GuardFrame == null || targetIsInsideBranch)
                        targetState = observedState;
                    else
                        guardedTargetState = observedState;
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
                    nestedBlockCompletionEdges,
                    nestedBlockCompletedPaths,
                    loopPlans))
                return Unsupported(block.BranchValue?.Syntax ?? site, "control-flow");
        }

        if (targetIsCompletedRootBlock && queue.Count == 0 && completedPaths.Count != 0)
            targetState = MergeIncomingStates(completedPaths, site).State;
        if (targetIsCompletedNestedBlock && nestedBlockCompletedPaths.Count != 0)
        {
            var completedPath = MergeIncomingStates(nestedBlockCompletedPaths, site);
            if (targetIsInsideBranch && HasInvalidatedGuard(completedPath.GuardFrame))
                return Unsupported(site, "branch-guard-mutation");
            targetState = OrderTargetState(completedPath.State, completedPath, targetIsInsideBranch);
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
        IDictionary<CfgTraversalPoint, List<CfgPathState>> incoming,
        Queue<CfgTraversalPoint> queue,
        ISet<CfgTraversalPoint> queued,
        ICollection<CfgPathState> completedPaths,
        ISet<CfgEdge> nestedBlockCompletionEdges,
        ICollection<CfgPathState> nestedBlockCompletedPaths,
        IReadOnlyList<SymbolicLoopTransferPlan> loopPlans)
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
                       nestedBlockCompletionEdges,
                       nestedBlockCompletedPaths,
                       loopPlans) &&
                   TryPropagate(
                       block,
                       block.FallThroughSuccessor,
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
                       nestedBlockCompletionEdges,
                       nestedBlockCompletedPaths,
                       loopPlans);
        }

        return TryPropagate(
                   block,
                   block.FallThroughSuccessor,
                   activeContinuation,
                   path,
                   graph,
                   incoming,
                   queue,
                   queued,
                   completedPaths,
                   nestedBlockCompletionEdges,
                   nestedBlockCompletedPaths,
                   loopPlans) &&
               TryPropagate(
                   block,
                   block.ConditionalSuccessor,
                   activeContinuation,
                   path,
                   graph,
                   incoming,
                   queue,
                   queued,
                   completedPaths,
                   nestedBlockCompletionEdges,
                   nestedBlockCompletedPaths,
                   loopPlans);
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
        CfgFinallyContinuation? activeContinuation,
        CfgPathState path,
        ControlFlowGraph graph,
        IDictionary<CfgTraversalPoint, List<CfgPathState>> incoming,
        Queue<CfgTraversalPoint> queue,
        ISet<CfgTraversalPoint> queued,
        ICollection<CfgPathState> completedPaths,
        ISet<CfgEdge> nestedBlockCompletionEdges,
        ICollection<CfgPathState> nestedBlockCompletedPaths,
        IReadOnlyList<SymbolicLoopTransferPlan> loopPlans)
    {
        if (branch == null)
            return true;
        if (!branch.FinallyRegions.IsDefaultOrEmpty)
        {
            var continuation = new CfgFinallyContinuation(
                branch.FinallyRegions,
                0,
                branch.Destination,
                activeContinuation);
            return TryPropagateToPoint(
                new CfgTraversalPoint(
                    graph.Blocks[branch.FinallyRegions[0].FirstBlockOrdinal],
                    continuation),
                path,
                incoming,
                queue,
                queued);
        }
        if (branch.Semantics is not (ControlFlowBranchSemantics.Regular or
            ControlFlowBranchSemantics.StructuredExceptionHandling))
        {
            completedPaths.Add(path);
            return true;
        }
        if (branch.Destination == null)
        {
            if (activeContinuation != null)
                return TryCompleteFinallyContinuation(
                    activeContinuation,
                    path,
                    graph,
                    incoming,
                    queue,
                    queued,
                    completedPaths);
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

        return TryPropagateToPoint(
            new CfgTraversalPoint(branch.Destination, activeContinuation),
            path,
            incoming,
            queue,
            queued);
    }

    private static bool TryCompleteFinallyContinuation(
        CfgFinallyContinuation? continuation,
        CfgPathState path,
        ControlFlowGraph graph,
        IDictionary<CfgTraversalPoint, List<CfgPathState>> incoming,
        Queue<CfgTraversalPoint> queue,
        ISet<CfgTraversalPoint> queued,
        ICollection<CfgPathState> completedPaths)
    {
        if (continuation == null)
        {
            completedPaths.Add(path);
            return true;
        }

        var nextRegionIndex = continuation.RegionIndex + 1;
        if (nextRegionIndex < continuation.Regions.Length)
            return TryPropagateToPoint(
                new CfgTraversalPoint(
                    graph.Blocks[continuation.Regions[nextRegionIndex].FirstBlockOrdinal],
                    continuation with { RegionIndex = nextRegionIndex }),
                path,
                incoming,
                queue,
                queued);
        if (continuation.Destination != null)
            return TryPropagateToPoint(
                new CfgTraversalPoint(continuation.Destination, continuation.Parent),
                path,
                incoming,
                queue,
                queued);
        return TryCompleteFinallyContinuation(
            continuation.Parent,
            path,
            graph,
            incoming,
            queue,
            queued,
            completedPaths);
    }

    private static bool TryPropagateToPoint(
        CfgTraversalPoint destination,
        CfgPathState path,
        IDictionary<CfgTraversalPoint, List<CfgPathState>> incoming,
        Queue<CfgTraversalPoint> queue,
        ISet<CfgTraversalPoint> queued)
    {
        if (!incoming.TryGetValue(destination, out var states))
        {
            states = new List<CfgPathState>();
            incoming.Add(destination, states);
        }

        var activeGuard = GetActiveGuard(path.GuardFrame);
        var guardKey = activeGuard == null
            ? string.Empty
            : SymbolicState.CreateProofConditionKey(activeGuard);
        if (states.Any(existing =>
                existing.State.NormalizedProofKey == path.State.NormalizedProofKey &&
                (GetActiveGuard(existing.GuardFrame) is not { } existingGuard
                    ? string.Empty
                    : SymbolicState.CreateProofConditionKey(existingGuard)) == guardKey &&
                HasInvalidatedGuard(existing.GuardFrame) ==
                HasInvalidatedGuard(path.GuardFrame)))
            return true;
        states.Add(path);
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
        if (paths.Count == 1)
            return paths[0];

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
                return new CfgPathState(mergeBaseline, parentFrame);
            return new CfgPathState(
                SymbolicStateMerger.MergeGuardedStates(
                    mergeBaseline,
                    orderedPaths.Select(path =>
                        new SymbolicStateMerger.GuardedState(path.GuardFrame!.Guard, path.State)).ToArray(),
                    source,
                    SymbolicAnalysisLimitKind.IfElseFactMerge,
                    SymbolicAnalysisLimitContext.Limits.MaxMergedIfElseFacts,
                    "cfg-program-point.if-merge"),
                parentFrame);
        }

        return new CfgPathState(
            SymbolicStateMerger.MergePathStatesAcrossAll(
                paths.Select(static path => path.State).ToArray(),
                SymbolicStateMerger.AreEvidenceEquivalentFacts,
                source.SpanStart),
            null);
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

    private static CfgGuardFrame? InvalidateGuards(CfgGuardFrame? frame) =>
        frame == null
            ? null
            : frame with
            {
                GuardInvalidated = true,
                Parent = InvalidateGuards(frame.Parent)
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

    private readonly record struct CfgTraversalPoint(
        BasicBlock Block,
        CfgFinallyContinuation? Continuation);

    private sealed record CfgFinallyContinuation(
        ImmutableArray<ControlFlowRegion> Regions,
        int RegionIndex,
        BasicBlock? Destination,
        CfgFinallyContinuation? Parent);

    private static bool TryApplyOperation(
        ref SymbolicState state,
        IOperation operation,
        SymbolicCondition? guard,
        bool allowGuardedReferenceAssignments,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out bool guardInvalidated)
    {
        guardInvalidated = false;
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
                        semanticModel,
                        cancellationToken,
                        "ir.path.prior-statement",
                        out var declaratorInvalidatedGuard))
                    return false;
                guardInvalidated |= declaratorInvalidatedGuard;
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
                    semanticModel,
                    cancellationToken,
                    "ir.path.prior-statement",
                    out guardInvalidated)
                : TryApplyExplicitTargetAssignment(
                    ref state,
                    assignment,
                    guard,
                    semanticModel,
                    cancellationToken,
                    out guardInvalidated);

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
                       out guardInvalidated);

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
                   out guardInvalidated);
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
        if (!TryApplyOperation(
                ref state,
                operation,
                guard,
                allowGuardedReferenceAssignments,
                semanticModel,
                cancellationToken,
                out _))
            return false;

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
        out bool guardInvalidated)
    {
        guardInvalidated = GuardReferencesTarget(guard, target);
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
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string provenance,
        out bool guardInvalidated)
    {
        if (RequiresStructuralAssignmentFallback(target, guard, allowGuardedReferenceAssignments))
        {
            guardInvalidated = false;
            return false;
        }

        guardInvalidated = GuardReferencesTarget(guard, target);
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
        out bool guardInvalidated)
    {
        guardInvalidated = false;
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
        bool allowGuardedReferenceAssignments)
    {
        var type = target switch
        {
            ILocalSymbol local => local.Type,
            IParameterSymbol parameter => parameter.Type,
            _ => null
        };
        return type is INamedTypeSymbol
        {
            OriginalDefinition.SpecialType: SpecialType.System_Nullable_T
        } || guard != null &&
            type?.IsReferenceType == true &&
            (!allowGuardedReferenceAssignments || GuardReferencesTarget(guard, target));
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

    private static bool TryGetNestedBlockCompletionTarget(
        ControlFlowGraph graph,
        BlockSyntax block,
        out IOperation? completionOperation,
        out ISet<CfgEdge> completionEdges)
    {
        var operations = graph.Blocks
            .SelectMany(cfgBlock => cfgBlock.Operations.Select((operation, index) =>
                new NestedBlockOperation(cfgBlock, operation, index)))
            .Where(candidate => block.Span.Contains(candidate.Operation.Syntax.SpanStart))
            .OrderBy(static candidate => candidate.Operation.Syntax.SpanStart)
            .ThenBy(static candidate => candidate.Block.Ordinal)
            .ThenBy(static candidate => candidate.Index)
            .ToArray();
        if (operations.Length == 0)
        {
            completionOperation = null;
            completionEdges = new HashSet<CfgEdge>();
            return false;
        }

        var completion = operations[operations.Length - 1];
        var internalBranches = graph.Blocks.Where(cfgBlock =>
            cfgBlock.BranchValue != null &&
            block.Span.Contains(cfgBlock.BranchValue.Syntax.SpanStart)).ToArray();
        if (internalBranches.All(branch =>
                branch.Ordinal < completion.Block.Ordinal &&
                AllRegularPathsReach(branch.ConditionalSuccessor, completion.Block) &&
                AllRegularPathsReach(branch.FallThroughSuccessor, completion.Block)))
        {
            completionOperation = completion.Operation;
            completionEdges = new HashSet<CfgEdge>();
            return true;
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
        if (edges.Count == 0 || internalBranches.Any(branch =>
                branch.Ordinal >= completion.Block.Ordinal ||
                !AllRegularPathsReachExit(branch, branch.ConditionalSuccessor, edges) ||
                !AllRegularPathsReachExit(branch, branch.FallThroughSuccessor, edges)))
        {
            completionOperation = null;
            completionEdges = new HashSet<CfgEdge>();
            return false;
        }

        completionOperation = null;
        completionEdges = edges;
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

    private static bool SupportsRootBlockCompletion(ControlFlowGraph graph)
    {
        if (graph.Blocks.Count(static block =>
                block.Operations.Length != 0 || block.BranchValue != null) <= 1)
            return true;
        if (graph.Blocks.Any(static block =>
                block.Kind != BasicBlockKind.Exit && block.Predecessors.Length > 1))
            return false;

        return graph.Blocks.All(source => GetSuccessors(source).All(branch =>
            branch.Semantics == ControlFlowBranchSemantics.Regular &&
            (branch.Destination == null || branch.Destination.Ordinal > source.Ordinal)));
    }

    private static bool AllRegularPathsReachExit(
        BasicBlock source,
        ControlFlowBranch? branch,
        ISet<CfgEdge> exits) =>
        AllRegularPathsReachExit(source, branch, exits, new HashSet<BasicBlock>());

    private static bool AllRegularPathsReachExit(
        BasicBlock source,
        ControlFlowBranch? branch,
        ISet<CfgEdge> exits,
        ISet<BasicBlock> visiting)
    {
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
            AllRegularPathsReachExit(destination, successor, exits, visiting));
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
}

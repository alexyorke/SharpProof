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
        SymbolicState? initialState = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var executionRoot = CSharpSyntaxFacts.GetContainingExecutionRoot(
            site,
            ExecutionRootPolicy.Callable);
        if (executionRoot == null)
            return Unsupported(site, "execution-root");
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
        if (HasUnsupportedAssignmentTargetBeforeSite(
                executionRoot,
                site,
                semanticModel,
                cancellationToken))
            return Unsupported(site, "assignment-target");
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

        var state = initialState ?? new SymbolicState();
        SymbolicStatementStateTransfer.AddMethodEntryNullableFlowStateFacts(
            ref state,
            site,
            semanticModel,
            cancellationToken);

        var entryPoint = new CfgTraversalPoint(graph.Blocks[0], null);
        var incoming = new Dictionary<CfgTraversalPoint, List<CfgPathState>>
        {
            [entryPoint] = new List<CfgPathState> { new(state, null, null, null, false) }
        };
        var queue = new Queue<CfgTraversalPoint>();
        var queued = new HashSet<CfgTraversalPoint> { entryPoint };
        var completedPaths = new List<CfgPathState>();
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
            var foundTarget = false;
            foreach (var operation in block.Operations)
            {
                if (ContainsSite(operation.Syntax, site))
                {
                    var observedState = OrderTargetState(state, currentPath, targetIsInsideBranch);
                    if (currentPath.Guard == null || targetIsInsideBranch)
                        targetState = observedState;
                    else
                        guardedTargetState = observedState;
                    foundTarget = true;
                    break;
                }
                if (operation.Syntax.SpanStart >= site.SpanStart)
                    return Unsupported(site, "operation-order");
                if (!TryApplyOperation(
                        ref state,
                        operation,
                        currentPath.Guard,
                        targetIsInsideBranch,
                        semanticModel,
                        cancellationToken,
                        out var guardInvalidated))
                    return Unsupported(operation.Syntax, "operation-" + operation.Kind);
                currentPath = currentPath with
                {
                    GuardInvalidated = currentPath.GuardInvalidated || guardInvalidated
                };
            }

            if (foundTarget)
                continue;

            if (block.BranchValue != null)
            {
                if (ContainsSite(block.BranchValue.Syntax, site))
                {
                    var observedState = OrderTargetState(state, currentPath, targetIsInsideBranch);
                    if (currentPath.Guard == null || targetIsInsideBranch)
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
                    loopPlans))
                return Unsupported(block.BranchValue?.Syntax ?? site, "control-flow");
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
        IReadOnlyList<SymbolicLoopTransferPlan> loopPlans)
    {
        if (block.ConditionKind != ControlFlowConditionKind.None)
        {
            if (path.Guard != null)
                return false;
            if (block.BranchValue?.Syntax is not Microsoft.CodeAnalysis.CSharp.Syntax.ExpressionSyntax condition)
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
                           path.State,
                           conditionalGuard,
                           conditionalIsTrue,
                           false),
                       graph,
                       incoming,
                       queue,
                       queued,
                       completedPaths,
                       loopPlans) &&
                   TryPropagate(
                       block,
                       block.FallThroughSuccessor,
                       activeContinuation,
                       new CfgPathState(
                           fallThroughState,
                           path.State,
                           fallThroughGuard,
                           !conditionalIsTrue,
                           false),
                       graph,
                       incoming,
                       queue,
                       queued,
                       completedPaths,
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
                   loopPlans);
    }

    private static bool TryCreateBranchState(
        SymbolicState state,
        Microsoft.CodeAnalysis.CSharp.Syntax.ExpressionSyntax condition,
        bool branchWhenTrue,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SymbolicState branchState,
        out SymbolicCondition branchCondition)
    {
        var lowering = SymbolicSemanticPipeline.LowerBranchCondition(
            condition,
            branchWhenTrue,
            new SymbolicLoweringContext(semanticModel, cancellationToken));
        if (lowering is not { IsExact: true, Value: { } exactCondition })
        {
            branchState = state;
            branchCondition = null!;
            return false;
        }

        var transition = SymbolicOperationTransferKernel.Assume(
            state,
            exactCondition,
            assumeTrue: true,
            condition.Span,
            "operation-transfer.branch-assumption");
        branchState = transition.State;
        branchCondition = exactCondition;
        return transition.IsExact;
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

        var guardKey = path.Guard == null
            ? string.Empty
            : SymbolicState.CreateProofConditionKey(path.Guard);
        if (states.Any(existing =>
                existing.State.NormalizedProofKey == path.State.NormalizedProofKey &&
                (existing.Guard == null
                    ? string.Empty
                    : SymbolicState.CreateProofConditionKey(existing.Guard)) == guardKey))
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

        backEdgePath = new CfgPathState(state, null, null, null, false);
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

        var baseline = paths[0].Baseline;
        if (baseline != null &&
            paths.All(path => path.Guard != null &&
                              path.Baseline?.NormalizedProofKey == baseline.NormalizedProofKey))
        {
            var orderedPaths = paths
                .OrderByDescending(static path => path.GuardWhenTrue == true)
                .ToArray();
            var completedStates = orderedPaths.Select(static path => path.State).ToArray();
            var mergeBaseline = SymbolicStateMerger.MergeCommonStates(
                new SymbolicState(),
                completedStates);
            if (paths.Any(static path => path.GuardInvalidated))
                return new CfgPathState(mergeBaseline, null, null, null, false);
            return new CfgPathState(
                SymbolicStateMerger.MergeGuardedStates(
                    mergeBaseline,
                    orderedPaths.Select(path =>
                        new SymbolicStateMerger.GuardedState(path.Guard!, path.State)).ToArray(),
                    source,
                    SymbolicAnalysisLimitKind.IfElseFactMerge,
                    SymbolicAnalysisLimitContext.Limits.MaxMergedIfElseFacts,
                    "cfg-program-point.if-merge"),
                null,
                null,
                null,
                false);
        }

        return new CfgPathState(
            SymbolicStateMerger.MergePathStatesAcrossAll(
                paths.Select(static path => path.State).ToArray(),
                SymbolicStateMerger.AreEvidenceEquivalentFacts,
                source.SpanStart),
            null,
            null,
            null,
            false);
    }

    private static SymbolicState OrderTargetState(
        SymbolicState state,
        CfgPathState path,
        bool targetIsInsideBranch) =>
        targetIsInsideBranch && path.Guard != null
            ? new SymbolicState(
                state.Facts,
                new[] { path.Guard }.Concat(state.PathConditions),
                state.SymbolVersions,
                state.IsContradictory)
            : state;

    private readonly record struct CfgPathState(
        SymbolicState State,
        SymbolicState? Baseline,
        SymbolicCondition? Guard,
        bool? GuardWhenTrue,
        bool GuardInvalidated);

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
            return TryGetDirectTarget(assignment.Target, out var target) &&
                   TryApplyAssignment(
                       ref state,
                       target,
                       assignment.Value,
                       guard,
                       allowGuardedReferenceAssignments,
                       semanticModel,
                       cancellationToken,
                       "ir.path.prior-statement",
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

    private static bool HasUnsupportedAssignmentTargetBeforeSite(
        SyntaxNode executionRoot,
        SyntaxNode site,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var assignment in CSharpSyntaxFacts.DescendantNodesInExecution(executionRoot)
                     .OfType<AssignmentExpressionSyntax>())
        {
            if (assignment.SpanStart >= site.SpanStart)
                continue;

            var target = semanticModel.GetSymbolInfo(assignment.Left, cancellationToken).Symbol;
            if (target is not ILocalSymbol and not IParameterSymbol)
                return true;
        }

        return false;
    }

    private static bool ContainsSite(SyntaxNode container, SyntaxNode site) =>
        container.Span.Contains(site.SpanStart) || site.Span.Contains(container.SpanStart);

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

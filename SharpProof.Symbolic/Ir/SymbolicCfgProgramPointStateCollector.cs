using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Symbolic.Ir;

internal static partial class SymbolicCfgProgramPointStateCollector
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

    internal static SymbolicLoweringResult<SymbolicState> CollectCompletedStatementState(
        StatementSyntax statement,
        SymbolicState entryState,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) =>
        CollectState(
            statement,
            semanticModel,
            cancellationToken,
            entryState,
            CfgProgramPointTargetKind.CompletedStatement);

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
        var completedStatement = targetKind == CfgProgramPointTargetKind.CompletedStatement
            ? (StatementSyntax)site
            : null;
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
        if (!UsesDefaultAnalysisLimits(SymbolicAnalysisLimitContext.Limits) &&
            completedStatement == null)
            return Unsupported(site, "analysis-limits");
        if (completedStatement != null &&
            completedStatement is not (IfStatementSyntax or SwitchStatementSyntax or
                WhileStatementSyntax or DoStatementSyntax or ForStatementSyntax or
                ForEachStatementSyntax or ForEachVariableStatementSyntax or LockStatementSyntax))
            return Unsupported(site, "statement-region.kind");
        IReadOnlyList<SymbolicLoopTransferPlan> loopPlans;
        if (completedStatement is ForEachStatementSyntax or
            ForEachVariableStatementSyntax or LockStatementSyntax)
            loopPlans = Array.Empty<SymbolicLoopTransferPlan>();
        else if (!TryLowerLoopPlans(
                     completedStatement ?? executionRoot,
                     completedStatement != null,
                     semanticModel,
                     cancellationToken,
                     out loopPlans))
            return Unsupported(site, "loop-lowering");
        if (forInitialEntry != null &&
            (forInitialEntry.Condition == null ||
             loopPlans.Count(plan => ReferenceEquals(plan.Loop, forInitialEntry)) != 1))
            return Unsupported(site, "for-initial-entry-shape");
        var containingLoopPlans = completedStatement == null
            ? loopPlans
                .Where(plan =>
                    plan.Loop.Span.Contains(site.SpanStart) &&
                    !ReferenceEquals(plan.Loop, forInitialEntry))
                .ToArray()
            : Array.Empty<SymbolicLoopTransferPlan>();
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
        if (targetIsCompletedRootBlock)
        {
            if (!SupportsRootBlockCompletion(graph) ||
                CSharpSyntaxFacts.DescendantNodesInExecution(site, includeSelf: false)
                    .Any(static node => node is InvocationExpressionSyntax))
                return Unsupported(site, "root-block-control-flow");
            var completedRootState = initialState ?? new SymbolicState();
            SymbolicStatementStateTransfer.AddMethodEntryNullableFlowStateFacts(
                ref completedRootState,
                site,
                semanticModel,
                cancellationToken);
            SymbolicStatementStateTransfer.AddCompletedBlockStateFacts(
                ref completedRootState,
                (BlockSyntax)site,
                semanticModel,
                cancellationToken);
            return Exact(completedRootState, site);
        }
        if (completedStatement is IfStatementSyntax abruptIf &&
            TryCollectAbruptIfCompletionState(
                abruptIf,
                initialState!,
                semanticModel,
                cancellationToken,
                out var abruptIfCompletion))
            return abruptIfCompletion;
        if (completedStatement is WhileStatementSyntax or DoStatementSyntax or ForStatementSyntax &&
            SymbolicControlFlowFacts.StatementDefinitelyExits(
                completedStatement,
                semanticModel,
                cancellationToken))
        {
            var completedState = initialState!;
            SymbolicStateInvalidator.InvalidateNestedMutations(
                ref completedState,
                completedStatement,
                semanticModel,
                cancellationToken);
            return Exact(
                SymbolicOperationTransferKernel.Complete(
                    completedState,
                    completedStatement.Span).State,
                completedStatement);
        }
        if (completedStatement is ForEachStatementSyntax or
            ForEachVariableStatementSyntax or LockStatementSyntax)
            return CollectProtocolCompletionState(
                completedStatement,
                initialState!,
                semanticModel,
                cancellationToken);
        CfgRegionPlan? statementRegion = null;
        if (completedStatement != null &&
            !TryCreateRegionPlan(
                graph,
                completedStatement,
                targetKind,
                semanticModel,
                cancellationToken,
                out statementRegion,
                out var statementRegionFailure))
            return Unsupported(site, statementRegionFailure);
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
        if (targetIsCompletedNestedBlock &&
            !SupportsCanonicalNestedBlockCompletion(
                    (BlockSyntax)site,
                    semanticModel,
                    cancellationToken))
            return Unsupported(site, "nested-block-completion");
        if (targetIsCompletedNestedBlock)
            return Exact(
                SymbolicProgramPointFacts.MergeStates(
                    SymbolicProgramPointFacts.CollectAncestorReachabilityState(
                        site,
                        semanticModel,
                        cancellationToken),
                    SymbolicProgramPointFacts.CollectPriorAssignmentState(
                        site,
                        semanticModel,
                        cancellationToken,
                        includeCurrentStatementCompletionFacts: true,
                        initialState)),
                site);
        var state = initialState ?? new SymbolicState();
        var summarizesCompletedLoop =
            statementRegion?.TargetKind == CfgProgramPointTargetKind.CompletedStatement &&
            statementRegion.Target is WhileStatementSyntax or DoStatementSyntax or ForStatementSyntax;
        if (statementRegion == null)
            SymbolicStatementStateTransfer.AddMethodEntryNullableFlowStateFacts(
                ref state,
                site,
                semanticModel,
                cancellationToken);

        var entryPoint = statementRegion?.EntryPoint ?? new CfgTraversalPoint(graph.Blocks[0], null);
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
        var traversal = new CfgTraversalContext(
            graph,
            semanticModel,
            cancellationToken,
            incoming,
            queue,
            queued,
            completedPaths,
            loopPlans,
            finallyLocalTarget,
            statementRegion);
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
            var statementSlice = statementRegion?.Blocks[block.Ordinal];
            var foundTarget = false;
            var observedLoopTarget = false;
            for (var operationIndex = point.OperationIndex;
                 operationIndex < (statementSlice?.EndOperationIndexExclusive ?? block.Operations.Length);
                 operationIndex++)
            {
                var operation = block.Operations[operationIndex];
                if (operation.IsImplicit && ReferenceEquals(operation.Syntax, executionRoot))
                    continue;
                if (includeCurrentStatementCompletionFacts &&
                    site is LocalDeclarationStatementSyntax &&
                    operation is IFlowCaptureOperation)
                    continue;
                if (statementRegion != null &&
                    operation is IFlowCaptureOperation flowCapture &&
                    statementRegion.FlowCaptureIds.Contains(flowCapture.Id))
                    continue;
                if (statementRegion == null &&
                    forInitialEntry == null &&
                    !observedLoopTarget &&
                    IsTargetOperation(
                        operation,
                        site,
                        includeCurrentStatementCompletionFacts,
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
                                allowUnsupportedValueCompletion: false,
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
                if (forInitialEntry == null &&
                    !targetIsInsideLoop &&
                    operation.Syntax.SpanStart >= site.SpanStart &&
                    !(statementRegion != null &&
                      site.Span.Contains(operation.Syntax.SpanStart)))
                    return Unsupported(site, "operation-order");
                if (forInitialEntry != null &&
                    !SupportsForInitialEntryOperation(operation, forInitialEntry))
                    return Unsupported(operation.Syntax, "for-initializer-operation");
                ISymbol? invalidatedGuardTarget = null;
                if (!summarizesCompletedLoop && !TryApplyOperation(
                        ref state,
                        operation,
                        GetActiveGuard(currentPath.GuardFrame),
                        true,
                        statementRegion != null,
                        statementRegion != null,
                        semanticModel,
                        cancellationToken,
                        forInitialEntry != null && IsForInitializerSyntax(operation.Syntax, forInitialEntry)
                            ? "ir.path.for-initializer"
                            : "ir.path.prior-statement",
                        out invalidatedGuardTarget))
                    return Unsupported(operation.Syntax, "operation-" + operation.Kind);
                if (!summarizesCompletedLoop && invalidatedGuardTarget != null)
                    currentPath = currentPath with
                    {
                        GuardFrame = InvalidateGuards(
                            currentPath.GuardFrame,
                            invalidatedGuardTarget)
                    };
                if (!summarizesCompletedLoop &&
                    statementRegion != null && site.Span.Contains(operation.Syntax.SpanStart))
                    AddOperationNormalCompletionFacts(
                        ref state,
                        operation,
                        semanticModel,
                        cancellationToken);
                else if (!summarizesCompletedLoop && forInitialEntry != null)
                    AddForDeclarationInitializerNormalCompletionFacts(
                        ref state,
                        operation,
                        forInitialEntry,
                        semanticModel,
                        cancellationToken);
                if (statementRegion != null &&
                    operation.Syntax is StatementSyntax operationStatement &&
                    SymbolicControlFlowFacts.StatementDefinitelyExits(
                        operationStatement,
                        semanticModel,
                        cancellationToken))
                {
                    statementRegion!.TerminalPaths.Add(currentPath with { State = state });
                    foundTarget = true;
                    break;
                }
            }

            if (foundTarget)
                continue;

            if (statementSlice is { HasCursorExit: true })
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
                if (statementRegion == null &&
                    ContainsSite(block.BranchValue.Syntax, site) &&
                    !(includeCurrentStatementCompletionFacts &&
                      site is LocalDeclarationStatementSyntax))
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
                    traversal))
                return Unsupported(block.BranchValue?.Syntax ?? site, "control-flow");
        }

        if (statementRegion is
            {
                TargetKind: CfgProgramPointTargetKind.CompletedStatement,
                Target: WhileStatementSyntax or DoStatementSyntax or ForStatementSyntax
            })
        {
            if (!TryCreateCompletedLoopSummary(
                    initialState!,
                    statementRegion,
                    loopPlans,
                    semanticModel,
                    cancellationToken,
                    out var completedLoopState))
                return Unsupported(site, "statement-region.loop-summary");
            return Exact(completedLoopState, site);
        }

        if (statementRegion is
                {
                    TargetKind: CfgProgramPointTargetKind.CompletedStatement,
                    Target: IfStatementSyntax completedIf
                } &&
            TryCreateInvalidatedIfSummary(
                initialState!,
                completedIf,
                statementRegion.CompletedPaths,
                semanticModel,
                cancellationToken,
                out var invalidatedIfState))
            return Exact(invalidatedIfState, site);

        if (statementRegion != null && statementRegion.CompletedPaths.Count != 0)
        {
            var completedPath = MergeIncomingStates(
                statementRegion.CompletedPaths.Select(static completion => completion.Path).ToArray(),
                site);
            if (targetIsInsideBranch && HasInvalidatedGuard(completedPath.GuardFrame))
                return Unsupported(site, "branch-guard-mutation");
            targetState = OrderTargetState(
                completedPath.State,
                completedPath,
                targetIsInsideBranch ||
                statementRegion.CompletedPaths.Count == 1 &&
                !HasInvalidatedGuard(completedPath.GuardFrame));
        }
        else if (statementRegion != null && statementRegion.TerminalPaths.Count != 0)
        {
            var completedPath = CollapseTerminalCompletionPaths(statementRegion.TerminalPaths, site);
            if (targetIsInsideBranch && HasInvalidatedGuard(completedPath.GuardFrame))
                return Unsupported(site, "branch-guard-mutation");
            var completedState = SymbolicOperationTransferKernel.Complete(
                completedPath.State,
                site.Span).State;
            targetState = OrderTargetState(completedState, completedPath, targetIsInsideBranch);
        }
        if (targetState != null &&
            statementRegion is
                {
                    TargetKind: CfgProgramPointTargetKind.CompletedStatement,
                    Target: SwitchStatementSyntax completedSwitch
                } &&
            !TryApplyCompletedSwitchExitExclusions(
                ref targetState,
                completedSwitch,
                semanticModel,
                cancellationToken))
            return Unsupported(site, "statement-region.switch-exit");
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
        CfgTraversalContext context)
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
                       context.SemanticModel,
                       context.CancellationToken,
                       out var conditionalState,
                       out var conditionalGuard) &&
                   TryCreateBranchState(
                       path.State,
                       condition,
                       !conditionalIsTrue,
                        context.SemanticModel,
                        context.CancellationToken,
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
                                block,
                                conditionalGuard,
                               conditionalIsTrue,
                               false,
                               path.GuardFrame)),
                        context) &&
                   TryPropagate(
                       block,
                       block.FallThroughSuccessor,
                       CfgIncomingEdgeKind.FallThrough,
                       activeContinuation,
                       new CfgPathState(
                           fallThroughState,
                            new CfgGuardFrame(
                                path.State,
                                block,
                                fallThroughGuard,
                               !conditionalIsTrue,
                               false,
                               path.GuardFrame)),
                        context);
        }

        return TryPropagate(
                   block,
                   block.FallThroughSuccessor,
                   CfgIncomingEdgeKind.FallThrough,
                   activeContinuation,
                   path,
                    context) &&
               TryPropagate(
                   block,
                   block.ConditionalSuccessor,
                   CfgIncomingEdgeKind.Conditional,
                   activeContinuation,
                   path,
                    context);
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
        if (condition.Syntax.FirstAncestorOrSelf<SwitchLabelSyntax>() is { } label &&
            label.FirstAncestorOrSelf<SwitchStatementSyntax>() is { } switchStatement &&
            SwitchPathConditionBuilder.TryCreateSwitchStatementLabelSymbolicCondition(
                switchStatement.Expression,
                label,
                semanticModel,
                cancellationToken,
                out var labelCondition))
        {
            branchCondition = branchWhenTrue
                ? labelCondition
                : new SymbolicNotCondition(labelCondition);
            var switchTransition = SymbolicOperationTransferKernel.Assume(
                state,
                branchCondition,
                assumeTrue: true,
                label.Span,
                "cfg-program-point.switch-label");
            branchState = switchTransition.State;
            return switchTransition.IsExact;
        }

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
        CfgTraversalContext context)
    {
        var graph = context.Graph;
        var completedPaths = context.CompletedPaths;
        var loopPlans = context.LoopPlans;
        var statementRegion = context.RegionPlan;
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
            if (context.FinallyLocalTarget != null &&
                branch.FinallyRegions.Any(region => ReferenceEquals(region, context.FinallyLocalTarget.Region)))
            {
                if (!IsSupportedFinallyLocalContinuation(continuation, context.FinallyLocalTarget) ||
                    path.GuardFrame != null ||
                    context.FinallyLocalTarget.ProtectedMutations.HasUnsupportedMutation)
                    return false;
                path = path with
                {
                    State = SymbolicStateInvalidator.ApplyNestedMutationInvalidations(
                        path.State,
                        context.FinallyLocalTarget.ProtectedMutations)
                };
            }
            var finallyEntry = graph.Blocks[branch.FinallyRegions[0].FirstBlockOrdinal];
            var finallyEntryPoint = statementRegion == null
                ? new CfgTraversalPoint(finallyEntry, continuation)
                : statementRegion.GetEntryPoint(finallyEntry, continuation);
            if (statementRegion != null && finallyEntryPoint == default)
                return false;
            return TryPropagateToPoint(
                finallyEntryPoint,
                new CfgIncomingEdge(branch, continuation, edgeKind),
                path,
                context);
        }
        if (branch.Semantics is not (ControlFlowBranchSemantics.Regular or
            ControlFlowBranchSemantics.StructuredExceptionHandling))
        {
            if (!IsTerminalCompletionBranch(branch))
                return false;
            if (statementRegion?.TerminalBranches.Contains(branch) == true)
            {
                statementRegion.TerminalPaths.Add(path);
                return true;
            }
            completedPaths.Add(path);
            return true;
        }
        if (branch.Destination == null)
        {
            if (activeContinuation != null)
                return TryCompleteFinallyContinuation(
                    branch,
                    activeContinuation,
                    path,
                    context);
            completedPaths.Add(path);
            return true;
        }
        if (statementRegion?.CompletionBranches.Contains(branch) == true)
        {
            if (!TryApplyLoopExit(
                    source,
                    branch.Destination,
                    path,
                    loopPlans,
                    out path))
                return false;
            path = ApplyExitedRegionLocalInvalidation(
                source,
                branch.Destination,
                path,
                statementRegion.InvalidatesExitedLocals);
            statementRegion.CompletedPaths.Add((branch, path));
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
            path,
            statementRegion?.InvalidatesExitedLocals == true);

        var destinationPoint = statementRegion == null
            ? new CfgTraversalPoint(branch.Destination, activeContinuation)
            : statementRegion.GetEntryPoint(branch.Destination, activeContinuation);
        if (statementRegion != null && destinationPoint == default)
            return false;
        return TryPropagateToPoint(
            destinationPoint,
            new CfgIncomingEdge(branch, activeContinuation, edgeKind),
            path,
            context);
    }

    private static bool TryCompleteFinallyContinuation(
        ControlFlowBranch sourceBranch,
        CfgFinallyContinuation? continuation,
        CfgPathState path,
        CfgTraversalContext context)
    {
        if (continuation == null)
        {
            context.CompletedPaths.Add(path);
            return true;
        }

        var nextRegionIndex = continuation.RegionIndex + 1;
        if (nextRegionIndex < continuation.Regions.Length)
        {
            var nextContinuation = continuation with { RegionIndex = nextRegionIndex };
            var nextEntry = context.Graph.Blocks[continuation.Regions[nextRegionIndex].FirstBlockOrdinal];
            return TryPropagateToPoint(
                context.RegionPlan == null
                    ? new CfgTraversalPoint(nextEntry, nextContinuation)
                    : context.RegionPlan.GetEntryPoint(nextEntry, nextContinuation),
                new CfgIncomingEdge(
                    sourceBranch,
                    nextContinuation,
                    CfgIncomingEdgeKind.FinallyContinuation),
                path,
                context);
        }
        if (continuation.TerminalBranch is { } terminalBranch)
        {
            context.CompletedPaths.Add(path);
            return true;
        }
        if (continuation.Destination != null)
        {
            if (context.RegionPlan?.CompletionBranches.Contains(
                    continuation.OriginBranch) == true)
            {
                if (continuation.Parent != null)
                    return false;
                context.RegionPlan.CompletedPaths.Add((continuation.OriginBranch, path));
                return true;
            }
            return TryPropagateToPoint(
                new CfgTraversalPoint(continuation.Destination, continuation.Parent),
                new CfgIncomingEdge(
                    sourceBranch,
                    continuation,
                    CfgIncomingEdgeKind.FinallyContinuation),
                path,
                context);
        }
        return TryCompleteFinallyContinuation(
            sourceBranch,
            continuation.Parent,
            path,
            context);
    }

    private static CfgPathState ApplyExitedRegionLocalInvalidation(
        BasicBlock source,
        BasicBlock destination,
        CfgPathState path,
        bool invalidate)
    {
        if (!invalidate)
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
        CfgPathState path,
        CfgTraversalContext context)
    {
        var activeGuard = GetActiveGuard(path.GuardFrame);
        var guardKey = activeGuard == null
            ? string.Empty
            : SymbolicState.CreateProofConditionKey(activeGuard);
        if (context.LoopPlans.Count != 0)
        {
            // The bounded loop transfer still derives its fixed point from accumulated iterations.
            edge = new CfgIncomingEdge(
                null,
                null,
                CfgIncomingEdgeKind.History,
                path.State.NormalizedProofKey + "\nactive-guard:" + guardKey +
                "\nguard-invalidated:" + HasInvalidatedGuard(path.GuardFrame));
        }

        if (!context.Incoming.TryGetValue(destination, out var states))
        {
            states = new Dictionary<CfgIncomingEdge, CfgPathState>();
            context.Incoming.Add(destination, states);
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
        if (context.Queued.Add(destination))
            context.Queue.Enqueue(destination);
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

    private static bool TryCreateCompletedLoopSummary(
        SymbolicState entryState,
        CfgRegionPlan statementRegion,
        IReadOnlyList<SymbolicLoopTransferPlan> loopPlans,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SymbolicState summary)
    {
        var loop = (StatementSyntax)statementRegion.Target;
        var plan = loopPlans.SingleOrDefault(candidate => ReferenceEquals(candidate.Loop, loop));
        if (plan == null)
        {
            summary = default!;
            return false;
        }

        var mutations = SymbolicStateInvalidator.LowerNestedMutations(
            loop,
            semanticModel,
            cancellationToken);
        summary = SymbolicStateInvalidator.ApplyNestedMutationInvalidations(entryState, mutations);
        if (loop.DescendantNodes()
            .OfType<BreakStatementSyntax>()
            .Where(breakStatement => BreakTargetsLoop(breakStatement, loop))
            .Any(breakStatement => breakStatement.Ancestors()
                .TakeWhile(ancestor => !ReferenceEquals(ancestor, loop))
                .Any(static ancestor => ancestor is TryStatementSyntax)))
            return true;

        var exitConditions = new List<SymbolicCondition>();
        var hasConditionExit = false;
        foreach (var completion in statementRegion.CompletedPaths
                     .GroupBy(static completion => completion.Branch)
                     .Select(static group => group
                         .OrderByDescending(completion => GetGuardDepth(completion.Path.GuardFrame))
                         .First()))
        {
            if (IsLoopConditionFalseExit(completion.Branch, loop))
            {
                if (!hasConditionExit)
                {
                    exitConditions.Add(plan.ExitCondition);
                    hasConditionExit = true;
                }
                continue;
            }

            if (!TryCreateAbruptLoopExitCondition(
                    completion.Path.GuardFrame,
                    completion.Branch,
                    plan.EntryCondition,
                    statementRegion,
                    mutations,
                    out var breakCondition))
                return true;
            exitConditions.Add(breakCondition);
        }

        if (exitConditions.Count == 0)
            return true;
        var exitCondition = exitConditions.Aggregate(static (left, right) =>
            new SymbolicBinaryCondition(SymbolicConditionOperator.Or, left, right));
        var transition = SymbolicOperationTransferKernel.TransitionLoopEdge(
            summary,
            SymbolicLoopEdgeKind.Exit,
            exitCondition,
            loop.Span,
            "ir.path.loop-exit");
        if (!transition.IsExact)
            return false;
        summary = transition.State;
        foreach (var invariant in plan.Invariants)
        {
            transition = SymbolicOperationTransferKernel.TransitionLoopEdge(
                summary,
                SymbolicLoopEdgeKind.Exit,
                invariant,
                loop.Span,
                "ir.path.loop-invariant");
            if (!transition.IsExact)
                return false;
            summary = transition.State;
        }
        return true;
    }

    private static bool TryCreateInvalidatedIfSummary(
        SymbolicState entryState,
        IfStatementSyntax statement,
        IReadOnlyList<(ControlFlowBranch Branch, CfgPathState Path)> completedPaths,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SymbolicState summary)
    {
        var invalidatedSymbols = SymbolicLoopStateTransfer.GetConditionDependencySymbols(
                statement.Condition,
                semanticModel,
                cancellationToken)
            .ToArray();
        if (invalidatedSymbols.Length == 0 || completedPaths.Count == 0 ||
            !SymbolicLoopStateTransfer.AnyConditionSymbolInvalidatedInStatement(
                statement.Condition,
                statement,
                semanticModel,
                cancellationToken))
        {
            summary = default!;
            return false;
        }

        var baseline = entryState;
        SymbolicStateInvalidator.InvalidateNestedMutations(
            ref baseline,
            statement,
            semanticModel,
            cancellationToken);
        summary = completedPaths.Count == 1
            ? baseline
            : SymbolicStateMerger.MergeCommonStates(
                baseline,
                completedPaths.Select(static path => path.Path.State).ToArray());
        foreach (var symbol in invalidatedSymbols)
            SymbolicStateInvalidator.InvalidateSymbol(ref summary, symbol, statement);
        return true;
    }

    private static bool BreakTargetsLoop(
        BreakStatementSyntax breakStatement,
        StatementSyntax loop)
    {
        for (var ancestor = breakStatement.Parent; ancestor != null; ancestor = ancestor.Parent)
        {
            if (ReferenceEquals(ancestor, loop))
                return true;
            if (ancestor is SwitchStatementSyntax or WhileStatementSyntax or DoStatementSyntax or
                ForStatementSyntax or ForEachStatementSyntax or ForEachVariableStatementSyntax)
                return false;
        }
        return false;
    }

    private static int GetGuardDepth(CfgGuardFrame? frame) =>
        frame == null ? 0 : 1 + GetGuardDepth(frame.Parent);

    private static bool IsLoopConditionFalseExit(
        ControlFlowBranch branch,
        StatementSyntax loop)
    {
        var condition = loop switch
        {
            WhileStatementSyntax whileStatement => whileStatement.Condition,
            DoStatementSyntax doStatement => doStatement.Condition,
            ForStatementSyntax forStatement => forStatement.Condition,
            _ => null
        };
        if (condition == null ||
            branch.Source.BranchValue == null ||
            !condition.Span.Contains(branch.Source.BranchValue.Syntax.SpanStart))
            return false;
        var branchWhenTrue = ReferenceEquals(branch, branch.Source.ConditionalSuccessor)
            ? branch.Source.ConditionKind == ControlFlowConditionKind.WhenTrue
            : branch.Source.ConditionKind == ControlFlowConditionKind.WhenFalse;
        return !branchWhenTrue;
    }

    private static bool TryCreateAbruptLoopExitCondition(
        CfgGuardFrame? frame,
        ControlFlowBranch completionBranch,
        SymbolicCondition entryCondition,
        CfgRegionPlan statementRegion,
        SymbolicNestedMutationInvalidationPlan mutations,
        out SymbolicCondition condition)
    {
        condition = null!;
        if (frame == null || HasInvalidatedGuard(frame))
            return false;
        var entryKey = SymbolicState.CreateProofConditionKey(entryCondition);
        var completionSpanEnd = completionBranch.Source.Operations.LastOrDefault()?.Syntax.Span.End ??
                                completionBranch.Source.BranchValue?.Syntax.Span.End ??
                                statementRegion.Target.Span.End;
        var frames = GetGuardFramesOuterToInner(frame);
        var guards = new List<SymbolicCondition>();
        foreach (var guardFrame in frames)
        {
            if (SymbolicState.CreateProofConditionKey(guardFrame.Guard) == entryKey ||
                OppositeBranchCompletesStatement(guardFrame, statementRegion))
                continue;
            var guardSpanStart = guardFrame.Source.BranchValue?.Syntax.SpanStart ??
                                 completionSpanEnd;
            var referencingMutations = mutations.Steps.Where(step =>
                    step.SourceSpan.Start < completionSpanEnd &&
                    step.Targets.Any(target => SymbolicIrReferenceScanner.ContainsVariableOrMember(
                        guardFrame.Guard,
                        target.Key)))
                .ToArray();
            if (referencingMutations.Any(step => step.SourceSpan.Start < guardSpanStart))
                return false;
            if (referencingMutations.Length != 0)
                continue;
            guards.Add(guardFrame.Guard);
        }
        if (guards.Count == 0)
            return false;
        condition = guards.Aggregate(static (left, right) =>
            new SymbolicBinaryCondition(SymbolicConditionOperator.And, left, right));
        return true;
    }

    private static IReadOnlyList<CfgGuardFrame> GetGuardFramesOuterToInner(CfgGuardFrame frame)
    {
        var frames = new List<CfgGuardFrame>();
        for (var current = frame; current != null; current = current.Parent)
            frames.Add(current);
        frames.Reverse();
        return frames;
    }

    private static bool OppositeBranchCompletesStatement(
        CfgGuardFrame frame,
        CfgRegionPlan statementRegion)
    {
        var oppositeIsTrue = !frame.GuardWhenTrue;
        var opposite = oppositeIsTrue ==
                       (frame.Source.ConditionKind == ControlFlowConditionKind.WhenTrue)
            ? frame.Source.ConditionalSuccessor
            : frame.Source.FallThroughSuccessor;
        return opposite != null && statementRegion.CompletionBranches.Contains(opposite);
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
        bool allowAbruptCompletion,
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
                cancellationToken,
                allowAbruptCompletion);
            if (result is not { IsExact: true, Value: { } plan })
            {
                plans = Array.Empty<SymbolicLoopTransferPlan>();
                return false;
            }
            if (plan.Loop is not (WhileStatementSyntax or DoStatementSyntax or ForStatementSyntax) ||
                !allowAbruptCompletion &&
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
            var (limitKind, limit, provenance) = source is SwitchStatementSyntax
                ? (SymbolicAnalysisLimitKind.SwitchFactMerge,
                    SymbolicAnalysisLimitContext.Limits.MaxMergedSwitchFacts,
                    "cfg-program-point.switch-merge")
                : (SymbolicAnalysisLimitKind.IfElseFactMerge,
                    SymbolicAnalysisLimitContext.Limits.MaxMergedIfElseFacts,
                    "cfg-program-point.if-merge");
            merged = new CfgPathState(
                SymbolicStateMerger.MergeGuardedStates(
                    mergeBaseline,
                    orderedPaths.Select(path =>
                        new SymbolicStateMerger.GuardedState(path.GuardFrame!.Guard, path.State)).ToArray(),
                    source,
                    limitKind,
                    limit,
                    provenance),
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

    private sealed record CfgTraversalContext(
        ControlFlowGraph Graph,
        SemanticModel SemanticModel,
        CancellationToken CancellationToken,
        IDictionary<CfgTraversalPoint, Dictionary<CfgIncomingEdge, CfgPathState>> Incoming,
        Queue<CfgTraversalPoint> Queue,
        ISet<CfgTraversalPoint> Queued,
        ICollection<CfgPathState> CompletedPaths,
        IReadOnlyList<SymbolicLoopTransferPlan> LoopPlans,
        CfgFinallyLocalTargetPlan? FinallyLocalTarget,
        CfgRegionPlan? RegionPlan);

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
        BasicBlock Source,
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
                !ReferenceEquals(frame.Source, first.Source) ||
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

}

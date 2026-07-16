using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using static SharpProof.Symbolic.Ir.SymbolicCfgProgramPointStateCollector;

namespace SharpProof.Symbolic.Ir;

internal static class SymbolicCfgExecutionTrace
{
    private static readonly ConditionalWeakTable<SemanticModel, ConditionalWeakTable<SyntaxNode, TraceCacheEntry>>
        s_executionTraces = new();

    private sealed class ExecutionTrace
    {
        private readonly IReadOnlyList<KeyValuePair<IOperation, CfgPathState>> _observations;

        internal ExecutionTrace(IReadOnlyList<KeyValuePair<IOperation, CfgPathState>> observations) =>
            _observations = observations;

        internal SymbolicLoweringResult<SymbolicState> CollectState(
            SyntaxNode site,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var targetIsInsideBranch = site.Ancestors().Any(static ancestor =>
                ancestor is IfStatementSyntax or ElseClauseSyntax or SwitchSectionSyntax);
            for (var index = 0; index < _observations.Count; index++)
            {
                var operation = _observations[index].Key;
                var path = _observations[index].Value;
                if (!IsTargetOperation(
                        operation,
                        site,
                        includeCurrentStatementCompletionFacts: false,
                        semanticModel,
                        cancellationToken))
                    continue;
                if (targetIsInsideBranch && HasInvalidatedGuard(path.GuardFrame))
                    return Unsupported(site, "trace.branch-guard-mutation");
                var state = OrderTargetState(path.State, path, targetIsInsideBranch);
                return Exact(RebaseMethodEntryEvidence(state, site), site);
            }

            return Unsupported(site, "trace.target-block");
        }
    }

    internal static SymbolicLoweringResult<SymbolicState> CollectCachedStateFromExecutionTrace(
        SyntaxNode site,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var executionRoot = CSharpSyntaxFacts.GetContainingExecutionRoot(
            site,
            ExecutionRootPolicy.Callable);
        if (executionRoot == null)
            return Unsupported(site, "trace.execution-root");
        var roots = s_executionTraces.GetValue(
            semanticModel,
            static _ => new ConditionalWeakTable<SyntaxNode, TraceCacheEntry>());
        var holder = roots.GetValue(executionRoot, static _ => new TraceCacheEntry());
        SymbolicLoweringResult<ExecutionTrace>? trace;
        lock (holder.Gate)
            trace = holder.Result;
        if (trace == null)
        {
            var candidate = CollectExecutionTrace(executionRoot, semanticModel, cancellationToken);
            lock (holder.Gate)
            {
                holder.Result ??= candidate;
                trace = holder.Result;
            }
        }
        cancellationToken.ThrowIfCancellationRequested();

        return trace is { IsExact: true, Value: { } value }
            ? value.CollectState(site, semanticModel, cancellationToken)
            : SymbolicLoweringResult<SymbolicState>.Unsupported(trace.Provenance.Single());
    }

    internal static SymbolicLoweringResult<SymbolicState> CollectStateFromExecutionTrace(
        SyntaxNode site,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var executionRoot = CSharpSyntaxFacts.GetContainingExecutionRoot(
            site,
            ExecutionRootPolicy.Callable);
        if (executionRoot == null)
            return Unsupported(site, "trace.execution-root");
        var trace = CollectExecutionTrace(executionRoot, semanticModel, cancellationToken);
        return trace is { IsExact: true, Value: { } value }
            ? value.CollectState(site, semanticModel, cancellationToken)
            : SymbolicLoweringResult<SymbolicState>.Unsupported(trace.Provenance.Single());
    }

    internal static SymbolicLoweringResult<SymbolicState> CollectCompletedStatementState(
        StatementSyntax statement,
        SymbolicState entryState,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var limitScope = SymbolicAnalysisLimitContext.Push(
            SymbolicAnalysisLimitContext.Limits,
            statement);
        var result = CollectCompletedStatementStateCore(
            statement,
            entryState,
            semanticModel,
            cancellationToken);
        return limitScope.Snapshot().IsTruncated
            ? Unsupported(statement, "trace.truncation")
            : result;
    }

    private static SymbolicLoweringResult<ExecutionTrace> CollectExecutionTrace(
        SyntaxNode executionRoot,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (!UsesDefaultAnalysisLimits(SymbolicAnalysisLimitContext.Limits))
            return TraceUnsupported(executionRoot, "trace.analysis-limits");
        using var traceScope = SymbolicAnalysisLimitContext.PushIsolated(
            SymbolicAnalysisLimitContext.Limits,
            executionRoot);
        var result = CollectExecutionTraceCore(executionRoot, semanticModel, cancellationToken);
        return traceScope.Snapshot().IsTruncated
            ? TraceUnsupported(executionRoot, "trace.truncation")
            : result;
    }

    private static SymbolicLoweringResult<ExecutionTrace> CollectExecutionTraceCore(
        SyntaxNode executionRoot,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (executionRoot.DescendantNodes(static node =>
                !CSharpSyntaxFacts.IsNestedLocalCallableBoundary(node)).Any(static node =>
                node is TryStatementSyntax or UsingStatementSyntax or LockStatementSyntax or
                    CommonForEachStatementSyntax or WhileStatementSyntax or DoStatementSyntax or
                    ForStatementSyntax))
            return TraceUnsupported(executionRoot, "trace.control-flow-shape");

        ControlFlowGraph? graph;
        try
        {
            graph = ControlFlowGraph.Create(executionRoot, semanticModel, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return TraceUnsupported(executionRoot, "trace.cfg");
        }

        if (graph == null || graph.Blocks.IsDefaultOrEmpty)
            return TraceUnsupported(executionRoot, "trace.cfg-empty");
        if (EnumerateRegions(graph.Root).Any(static region =>
                region.Kind is not (ControlFlowRegionKind.Root or ControlFlowRegionKind.LocalLifetime)))
            return TraceUnsupported(executionRoot, "trace.region");

        var state = new SymbolicState();
        var entryEvidenceSite = CSharpSyntaxFacts.GetBlockBody(executionRoot) ??
                                (CSharpSyntaxFacts.TryGetExpressionBody(executionRoot, out var expressionBody)
                                    ? expressionBody
                                    : executionRoot);
        SymbolicStatementStateTransfer.AddMethodEntryNullableFlowStateFacts(
            ref state,
            entryEvidenceSite,
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
        var traversal = new CfgTraversalContext(
            graph,
            semanticModel,
            cancellationToken,
            incoming,
            queue,
            queued,
            new List<CfgPathState>(),
            Array.Empty<SymbolicLoopTransferPlan>(),
            null,
            null);
        var observations = new List<KeyValuePair<IOperation, CfgPathState>>();
        queue.Enqueue(entryPoint);
        var iterations = 0;
        var iterationLimit = graph.Blocks.Length * 4;
        while (queue.Count != 0 && iterations++ < iterationLimit)
        {
            var point = queue.Dequeue();
            queued.Remove(point);
            var block = point.Block;
            var currentPath = MergeIncomingStates(incoming[point].Values.ToArray(), executionRoot);
            state = currentPath.State;
            for (var operationIndex = point.OperationIndex;
                 operationIndex < block.Operations.Length;
                 operationIndex++)
            {
                var operation = block.Operations[operationIndex];
                if (operation.IsImplicit && ReferenceEquals(operation.Syntax, executionRoot))
                    continue;
                RecordObservation(
                    observations,
                    operation,
                    currentPath with { State = state });
                if (!TryApplyOperation(
                        ref state,
                        operation,
                        GetActiveGuard(currentPath.GuardFrame),
                        allowGuardedReferenceAssignments: true,
                        allowGuardMutation: false,
                        allowExpressionStatementCompletion: false,
                        semanticModel,
                        cancellationToken,
                        "ir.path.prior-statement",
                        out var invalidatedGuardTarget))
                    return TraceUnsupported(operation.Syntax, "trace.operation-" + operation.Kind);
                if (invalidatedGuardTarget != null)
                    currentPath = currentPath with
                    {
                        GuardFrame = InvalidateGuards(currentPath.GuardFrame, invalidatedGuardTarget)
                    };
            }

            if (block.BranchValue != null)
                RecordObservation(
                    observations,
                    block.BranchValue,
                    currentPath with { State = state });
            if (!TryPropagateSuccessors(
                    block,
                    point.Continuation,
                    currentPath with { State = state },
                    traversal))
                return TraceUnsupported(block.BranchValue?.Syntax ?? executionRoot, "trace.control-flow");
        }

        if (queue.Count != 0)
            return TraceUnsupported(executionRoot, "trace.iteration-limit");
        return SymbolicLoweringResult<ExecutionTrace>.Exact(
            new ExecutionTrace(observations),
            Provenance(executionRoot, "trace.exact"));
    }

    private static SymbolicLoweringResult<ExecutionTrace> TraceUnsupported(SyntaxNode site, string detail) =>
        SymbolicLoweringResult<ExecutionTrace>.Unsupported(Provenance(site, detail));

    private static void RecordObservation(
        IList<KeyValuePair<IOperation, CfgPathState>> observations,
        IOperation operation,
        CfgPathState path)
    {
        for (var index = 0; index < observations.Count; index++)
            if (ReferenceEquals(observations[index].Key, operation))
            {
                observations[index] = new KeyValuePair<IOperation, CfgPathState>(operation, path);
                return;
            }
        observations.Add(new KeyValuePair<IOperation, CfgPathState>(operation, path));
    }

    private static SymbolicState RebaseMethodEntryEvidence(SymbolicState state, SyntaxNode site) =>
        new(
            state.Facts.Select(fact => RebaseMethodEntryEvidence(fact, site)),
            state.PathConditions.Select(condition => RebaseMethodEntryEvidence(condition, site)),
            state.SymbolVersions,
            state.IsContradictory);

    private static SymbolicCondition RebaseMethodEntryEvidence(SymbolicCondition condition, SyntaxNode site) =>
        condition switch
        {
            SymbolicFactCondition fact => new SymbolicFactCondition(RebaseMethodEntryEvidence(fact.Fact, site)),
            SymbolicNotCondition not => new SymbolicNotCondition(RebaseMethodEntryEvidence(not.Operand, site)),
            SymbolicBinaryCondition binary => new SymbolicBinaryCondition(
                binary.Operator,
                RebaseMethodEntryEvidence(binary.Left, site),
                RebaseMethodEntryEvidence(binary.Right, site)),
            _ => condition
        };

    private static SymbolicFact RebaseMethodEntryEvidence(SymbolicFact fact, SyntaxNode site) =>
        fact.Provenance is "ir.path.method-entry.this-non-null" or "ir.path.method-entry.nullability-contract"
            ? fact with { SourceSpan = site.Span }
            : fact;

    private sealed class TraceCacheEntry
    {
        internal object Gate { get; } = new();

        internal SymbolicLoweringResult<ExecutionTrace>? Result { get; set; }
    }
}

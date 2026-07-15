using Microsoft.CodeAnalysis;
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
        if (CSharpSyntaxFacts.DescendantNodesInExecution(executionRoot).Any(static node =>
                node is Microsoft.CodeAnalysis.CSharp.Syntax.WhileStatementSyntax or
                    Microsoft.CodeAnalysis.CSharp.Syntax.DoStatementSyntax or
                    Microsoft.CodeAnalysis.CSharp.Syntax.ForStatementSyntax or
                    Microsoft.CodeAnalysis.CSharp.Syntax.ForEachStatementSyntax or
                    Microsoft.CodeAnalysis.CSharp.Syntax.ForEachVariableStatementSyntax))
            return Unsupported(site, "loop-fixed-point");

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

        var incoming = new Dictionary<int, List<CfgPathState>>
        {
            [0] = new List<CfgPathState> { new(state, null, null, false) }
        };
        var queue = new Queue<BasicBlock>();
        var queued = new HashSet<int> { 0 };
        queue.Enqueue(graph.Blocks[0]);
        SymbolicState? targetState = null;
        SymbolicState? guardedTargetState = null;
        var targetIsInsideBranch = site.Ancestors().Any(static ancestor =>
            ancestor is Microsoft.CodeAnalysis.CSharp.Syntax.IfStatementSyntax or
                Microsoft.CodeAnalysis.CSharp.Syntax.ElseClauseSyntax or
                Microsoft.CodeAnalysis.CSharp.Syntax.SwitchSectionSyntax);
        var iterations = 0;
        while (queue.Count != 0 && iterations++ < graph.Blocks.Length * 4)
        {
            var block = queue.Dequeue();
            queued.Remove(block.Ordinal);
            var currentPath = MergeIncomingStates(incoming[block.Ordinal], site);
            state = currentPath.State;
            var foundTarget = false;
            foreach (var operation in block.Operations)
            {
                if (ContainsSite(operation.Syntax, site))
                {
                    if (currentPath.Guard == null)
                        targetState = state;
                    else if (!targetIsInsideBranch)
                        guardedTargetState = state;
                    foundTarget = true;
                    break;
                }
                if (operation.Syntax.SpanStart >= site.SpanStart)
                    return Unsupported(site, "operation-order");
                if (!TryApplyOperation(
                        ref state,
                        operation,
                        currentPath.Guard,
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
                    if (currentPath.Guard == null)
                        targetState = state;
                    else if (!targetIsInsideBranch)
                        guardedTargetState = state;
                    continue;
                }
            }

            if (!TryPropagateSuccessors(
                    block,
                    currentPath with { State = state },
                    semanticModel,
                    cancellationToken,
                    incoming,
                    queue,
                    queued))
                return Unsupported(block.BranchValue?.Syntax ?? site, "control-flow");
        }

        targetState ??= guardedTargetState;
        return targetState == null || queue.Count != 0
            ? Unsupported(site, queue.Count == 0 ? "target-block" : "iteration-limit")
            : Exact(targetState, site);
    }

    private static bool TryPropagateSuccessors(
        BasicBlock block,
        CfgPathState path,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        IDictionary<int, List<CfgPathState>> incoming,
        Queue<BasicBlock> queue,
        ISet<int> queued)
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
                       new CfgPathState(conditionalState, path.State, conditionalGuard, false),
                       incoming,
                       queue,
                       queued) &&
                   TryPropagate(
                       block,
                       block.FallThroughSuccessor,
                       new CfgPathState(fallThroughState, path.State, fallThroughGuard, false),
                       incoming,
                       queue,
                       queued);
        }

        return TryPropagate(
                   block,
                   block.FallThroughSuccessor,
                   path,
                   incoming,
                   queue,
                   queued) &&
               TryPropagate(
                   block,
                   block.ConditionalSuccessor,
                   path,
                   incoming,
                   queue,
                   queued);
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
        CfgPathState path,
        IDictionary<int, List<CfgPathState>> incoming,
        Queue<BasicBlock> queue,
        ISet<int> queued)
    {
        if (branch == null || branch.Destination == null)
            return true;
        if (!branch.FinallyRegions.IsDefaultOrEmpty || branch.Destination.Ordinal <= source.Ordinal)
            return false;

        var destination = branch.Destination;
        if (!incoming.TryGetValue(destination.Ordinal, out var states))
        {
            states = new List<CfgPathState>();
            incoming.Add(destination.Ordinal, states);
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
        if (queued.Add(destination.Ordinal))
            queue.Enqueue(destination);
        return true;
    }

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
            var completedStates = paths.Select(static path => path.State).ToArray();
            var mergeBaseline = SymbolicStateMerger.MergeCommonStates(
                new SymbolicState(),
                completedStates);
            if (paths.Any(static path => path.GuardInvalidated))
                return new CfgPathState(mergeBaseline, null, null, false);
            return new CfgPathState(
                SymbolicStateMerger.MergeGuardedStates(
                    mergeBaseline,
                    paths.Select(path => new SymbolicStateMerger.GuardedState(path.Guard!, path.State)).ToArray(),
                    source,
                    SymbolicAnalysisLimitKind.IfElseFactMerge,
                    SymbolicAnalysisLimitContext.Limits.MaxMergedIfElseFacts,
                    "cfg-program-point.if-merge"),
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
            false);
    }

    private readonly record struct CfgPathState(
        SymbolicState State,
        SymbolicState? Baseline,
        SymbolicCondition? Guard,
        bool GuardInvalidated);

    private static bool TryApplyOperation(
        ref SymbolicState state,
        IOperation operation,
        SymbolicCondition? guard,
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
                        semanticModel,
                        cancellationToken,
                        "operation-lowering.declaration",
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
                       semanticModel,
                       cancellationToken,
                       "operation-lowering.assignment",
                       out guardInvalidated);

        return false;
    }

    private static bool TryApplyAssignment(
        ref SymbolicState state,
        ISymbol target,
        IOperation value,
        SymbolicCondition? guard,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string provenance,
        out bool guardInvalidated)
    {
        guardInvalidated = guard != null &&
                           SymbolicIrReferenceScanner.ContainsVariableOrMember(
                               guard,
                               SymbolicFactFactory.GetSmtVariableName(target));
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

namespace SharpProof.Symbolic.Ir;

/// <summary>Projects symbolic program-point state from the compiler CFG runner.</summary>
internal static class CompilerProgramPointAnalysis {
    internal static SymbolicLoweringResult<SymbolicState> Collect(
        SyntaxNode site,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SymbolicState? initialState = null,
        bool includeCurrentStatementCompletionFacts = false,
        bool forInitialEntry = false) {
        cancellationToken.ThrowIfCancellationRequested();
        var searchNode = site is LocalFunctionStatementSyntax ? site.Parent ?? site : site;
        var root = CSharpSyntaxFacts.GetContainingExecutionRoot(searchNode, ExecutionRootPolicy.Callable);
        if (root == null) return Unsupported(site, "execution-root");
        ControlFlowGraph? graph;
        try { graph = ControlFlowGraph.Create(root, semanticModel, cancellationToken); }
        catch (Exception exception) when (exception is not OperationCanceledException) {
            return Unsupported(site, "cfg");
        }
        var owner = semanticModel.GetDeclaredSymbol(root, cancellationToken) ??
                    semanticModel.GetEnclosingSymbol(root.SpanStart, cancellationToken);
        if (graph == null || owner == null) return Unsupported(site, "cfg-empty");
        var state = initialState ?? new SymbolicState();
        SymbolicStatementStateTransfer.AddMethodEntryNullableFlowStateFacts(
            ref state, site, semanticModel, cancellationToken);
        var domain = new ProgramPointDomain(
            site, semanticModel, cancellationToken, includeCurrentStatementCompletionFacts, forInitialEntry,
            state.NormalizedProofKey);
        var result = AnalyzerUtilitiesControlFlowAnalysis.Run(
            graph, state, domain, semanticModel.Compilation, owner, cancellationToken);
        var captured = domain.CapturedState;
        if (captured == null && domain.TargetIsCompilerUnreachable)
            captured = result.ExitState.MarkContradictory();
        return captured == null
            ? Unsupported(site, result.Truncated ? "iteration-limit" : "target-block")
            : SymbolicLoweringResult<SymbolicState>.Exact(
                captured.Normalize(), new("compiler-cfg-program-point", site.Span, "exact"));
    }

    private static SymbolicLoweringResult<SymbolicState> Unsupported(SyntaxNode site, string detail) =>
        SymbolicLoweringResult<SymbolicState>.Unsupported(
            new SymbolicLoweringProvenance("compiler-cfg-program-point", site.Span, detail));

    private sealed class ProgramPointDomain(
        SyntaxNode site,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        bool includeCompletion,
        bool forInitialEntry,
        string initialKey) : IControlFlowDomain<SymbolicState> {
        private SymbolicState? captured;
        private bool targetIsCompilerUnreachable;
        internal bool TargetIsCompilerUnreachable => targetIsCompilerUnreachable;
        internal SymbolicState? CapturedState => captured;
        public void SetControlFlowGraph(ControlFlowGraph graph, PointsToAnalysisResult? pointsToAnalysisResult) =>
            targetIsCompilerUnreachable = graph.Blocks.Any(block => !block.IsReachable &&
                block.Operations.Append(block.BranchValue).Where(static operation => operation != null)
                    .Any(operation => Contains(operation!.Syntax, site)));
        public SymbolicState Transfer(SymbolicState state, IOperation operation) {
            if (state.IsContradictory || operation is ILocalFunctionOperation) return state;
            var target = IsTarget(operation);
            if (target && !includeCompletion) Capture(state);
            var updated = state;
            if (!SymbolicCfgProgramPointStateCollector.TryApplyOperation(
                    ref updated, operation, null, true, true, true,
                    semanticModel, cancellationToken, "compiler-flow", out _)) {
                SymbolicStateInvalidator.InvalidateNestedMutations(
                    ref updated, operation.Syntax, semanticModel, cancellationToken);
            }
            if (target && includeCompletion) Capture(updated);
            return updated;
        }
        public SymbolicState Refine(
            SymbolicState state,
            IOperation? condition,
            ControlFlowConditionKind kind,
            bool conditionalSuccessor,
            BasicBlock _) {
            if (condition?.Syntax is not ExpressionSyntax expression) return state;
            if (forInitialEntry && site is ForStatementSyntax statement &&
                statement.Condition != null && Contains(condition.Syntax, statement.Condition))
                Capture(state);
            var branchWhenTrue = kind switch {
                ControlFlowConditionKind.WhenTrue => conditionalSuccessor,
                ControlFlowConditionKind.WhenFalse => !conditionalSuccessor,
                _ => conditionalSuccessor
            };
            var transition = SymbolicReachabilityLowerer.Apply(
                state, expression, branchWhenTrue, semanticModel, cancellationToken);
            return transition.IsExact ? transition.State : state;
        }
        public SymbolicState Merge(SymbolicState current, SymbolicState incoming) {
            if (current.NormalizedProofKey == initialKey && incoming.NormalizedProofKey != initialKey ||
                IsSubset(current, incoming))
                return incoming;
            if (IsSubset(incoming, current)) return current;
            return SymbolicStateMerger.MergePathStatesAcrossAll(
                [current, incoming], static (left, right) => SymbolicState.CreateProofFactKey(left) ==
                                                         SymbolicState.CreateProofFactKey(right), site.SpanStart);
        }
        public SymbolicState CompleteBlock(SymbolicState state, BasicBlock block) => state;
        public bool Equivalent(SymbolicState left, SymbolicState right) =>
            string.Equals(left.NormalizedProofKey, right.NormalizedProofKey, StringComparison.Ordinal);
        public bool IsUnreachable(SymbolicState state) => state.IsContradictory;
        public string GetKey(SymbolicState state) => state.NormalizedProofKey;
        private bool IsTarget(IOperation operation) => !forInitialEntry &&
            SymbolicCfgProgramPointStateCollector.IsTargetOperation(
                operation, site, includeCompletion, semanticModel, cancellationToken);
        private void Capture(SymbolicState state) {
            foreach (var conditional in site.Ancestors().OfType<ConditionalExpressionSyntax>()) {
                bool? branchWhenTrue = conditional.WhenTrue.Span.Contains(site.SpanStart)
                    ? true
                    : conditional.WhenFalse.Span.Contains(site.SpanStart)
                        ? false
                        : null;
                if (!branchWhenTrue.HasValue) continue;
                var enclosingTransition = SymbolicReachabilityLowerer.ApplyConditionOnly(
                    state,
                    conditional.Condition,
                    branchWhenTrue.Value,
                    semanticModel,
                    cancellationToken);
                if (enclosingTransition.IsExact) state = enclosingTransition.State;
            }
            if (site.FirstAncestorOrSelf<StatementSyntax>() is { Parent: BlockSyntax block } target) {
                foreach (var statement in block.Statements.TakeWhile(candidate => !ReferenceEquals(candidate, target))) {
                    if (statement is not IfStatementSyntax { Else: null } conditional ||
                        !AlwaysCompletes(conditional.Statement))
                        continue;
                    var transition = SymbolicReachabilityLowerer.ApplyConditionOnly(
                        state, conditional.Condition, false, semanticModel, cancellationToken);
                    if (transition.IsExact) state = transition.State;
                }
            }
            captured = state;
        }
        private static bool AlwaysCompletes(StatementSyntax statement) => statement switch {
            ReturnStatementSyntax or ThrowStatementSyntax or ContinueStatementSyntax or BreakStatementSyntax => true,
            BlockSyntax block => block.Statements.LastOrDefault() is { } last && AlwaysCompletes(last),
            _ => false
        };
        private static bool IsSubset(SymbolicState subset, SymbolicState superset) {
            if (subset.IsContradictory && !superset.IsContradictory) return false;
            var factKeys = new HashSet<string>(superset.Facts.Select(SymbolicState.CreateProofFactKey), StringComparer.Ordinal);
            var conditionKeys = new HashSet<string>(
                superset.PathConditions.Select(SymbolicState.CreateProofConditionKey), StringComparer.Ordinal);
            return subset.Facts.All(fact => factKeys.Contains(SymbolicState.CreateProofFactKey(fact))) &&
                   subset.PathConditions.All(condition => conditionKeys.Contains(SymbolicState.CreateProofConditionKey(condition)));
        }
        private static bool Contains(SyntaxNode container, SyntaxNode candidate) =>
            container.Span.Contains(candidate.SpanStart) || candidate.Span.Contains(container.SpanStart);
    }
}

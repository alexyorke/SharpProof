namespace SharpProof.Symbolic.Ir;

internal static class SymbolicCfgExceptionRegionTransfer {
    internal static SymbolicLoweringResult<SymbolicState> CollectCompletedTryState(
        ControlFlowGraph graph,
        TryStatementSyntax statement,
        SymbolicState entryState,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        if (!TryCreatePlan(graph, statement, semanticModel, cancellationToken, out var plan))
            return Unsupported(statement, "statement-region.try-shape");

        var completionStates = new List<SymbolicState>();
        AddCompletion(statement.Block, entryState);
        foreach (var route in plan.Catches) {
            if (!CanHandle(route, plan.KnownThrownType, plan.HasKnownThrownType, semanticModel, cancellationToken))
                continue;
            var branchLimit = SymbolicAnalysisLimitContext.Limits.MaxTryCompletionBranches;
            if (completionStates.Count >= branchLimit) {
                SymbolicAnalysisLimitContext.Record(
                    SymbolicAnalysisLimitKind.TryCompletionBranches,
                    branchLimit,
                    completionStates.Count + 1,
                    statement,
                    "program_point.try_completion_branches");
                break;
            }
            AddCompletion(
                route.Clause.Block,
                SymbolicStateInvalidator.ApplyNestedMutationInvalidations(entryState, plan.ProtectedMutations));
        }
        if (completionStates.Count == 0)
            return Exact(SymbolicOperationTransferKernel.Complete(entryState, statement.Span).State, statement);

        var state = SymbolicOperationTransferKernel.Merge(entryState, [.. completionStates], statement).State;
        if (statement.Finally?.Block is { } finallyBlock) {
            state = SymbolicCfgProgramPointStateCollector.CollectCompletedStatementState(
                finallyBlock,
                state,
                semanticModel,
                cancellationToken).Value!;
            if (SymbolicControlFlowFacts.StatementDefinitelyExits(finallyBlock, semanticModel, cancellationToken))
                state = SymbolicOperationTransferKernel.Complete(state, finallyBlock.Span).State;
        }
        foreach (var hiddenSymbol in SymbolicBranchCompletionStateTransfer.GetLocalsDeclaredInside(
                     statement,
                     semanticModel,
                     cancellationToken))
            state = SymbolicStateValueFacts.RemoveReferences(state, hiddenSymbol);
        return Exact(state, statement);

        void AddCompletion(BlockSyntax block, SymbolicState branchState) {
            if (SymbolicControlFlowFacts.StatementDefinitelyExits(block, semanticModel, cancellationToken))
                return;
            branchState = SymbolicCfgProgramPointStateCollector.CollectCompletedStatementState(
                block,
                branchState,
                semanticModel,
                cancellationToken).Value!;
            if (!branchState.IsContradictory)
                completionStates.Add(branchState);
        }
    }
    private static bool TryCreatePlan(
        ControlFlowGraph graph,
        TryStatementSyntax statement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out CfgExceptionRegionPlan plan) {
        var regions = SymbolicCfgProgramPointStateCollector.EnumerateRegions(graph.Root).ToArray();
        var candidates = regions
            .Where(static region => region.Kind == ControlFlowRegionKind.TryAndCatch)
            .Select(region => TryCreateCatchRoutes(region, graph, statement, out var routes) ? routes : default)
            .Where(static routes => !routes.IsDefault)
            .ToArray();
        if (statement.Catches.Count == 0)
            candidates = [[]];
        if (candidates.Length != 1 ||
            statement.Finally != null && !regions.Any(region =>
                region.Kind == ControlFlowRegionKind.Finally &&
                SymbolicCfgProgramPointStateCollector.RegionContainsSyntax(region, graph, statement.Finally.Block))) {
            plan = null!;
            return false;
        }
        ITypeSymbol? knownThrownType = null;
        var hasKnownThrownType = false;
        if (statement.Block.Statements.Count == 1 &&
            statement.Block.Statements[0] is
                ThrowStatementSyntax { Expression: { } thrownExpression } throwStatement)
            hasKnownThrownType = SymbolicRuntimeTypeFacts.TryGetExactRuntimeType(
                thrownExpression,
                throwStatement,
                semanticModel,
                cancellationToken,
                out knownThrownType);
        plan = new CfgExceptionRegionPlan(
            candidates[0],
            SymbolicStateInvalidator.LowerNestedMutations(statement.Block, semanticModel, cancellationToken),
            hasKnownThrownType,
            knownThrownType);
        return true;
    }
    private static bool TryCreateCatchRoutes(
        ControlFlowRegion region,
        ControlFlowGraph graph,
        TryStatementSyntax statement,
        out ImmutableArray<CfgCatchRoute> routes) {
        routes = default;
        var tryRegion = region.NestedRegions.FirstOrDefault(static nested => nested.Kind == ControlFlowRegionKind.Try);
        if (tryRegion == null ||
            !SymbolicCfgProgramPointStateCollector.RegionContainsSyntax(tryRegion, graph, statement.Block))
            return false;

        var catchRegions = region.NestedRegions
            .Where(static nested => nested.Kind is ControlFlowRegionKind.Catch or ControlFlowRegionKind.FilterAndHandler)
            .Select(static nested => nested.Kind == ControlFlowRegionKind.Catch
                ? nested
                : nested.NestedRegions.SingleOrDefault(static child => child.Kind == ControlFlowRegionKind.Catch))
            .ToArray();
        if (catchRegions.Length != statement.Catches.Count || catchRegions.Any(static item => item == null))
            return false;

        var builder = ImmutableArray.CreateBuilder<CfgCatchRoute>(catchRegions.Length);
        for (var index = 0; index < catchRegions.Length; index++) {
            var clause = statement.Catches[index];
            var catchRegion = catchRegions[index]!;
            if (!SymbolicCfgProgramPointStateCollector.RegionContainsSyntax(catchRegion, graph, clause.Block))
                return false;
            builder.Add(new CfgCatchRoute(clause, catchRegion.ExceptionType));
        }
        routes = builder.MoveToImmutable();
        return true;
    }
    private static bool CanHandle(
        CfgCatchRoute route,
        ITypeSymbol? knownThrownType,
        bool hasKnownThrownType,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        if (route.Clause.Filter?.FilterExpression is { } filterExpression &&
            semanticModel.GetConstantValue(filterExpression, cancellationToken) is { HasValue: true, Value: false })
            return false;
        return !hasKnownThrownType || route.ExceptionType == null ||
               semanticModel.Compilation.ClassifyConversion(knownThrownType!, route.ExceptionType).IsImplicit;
    }
    private static SymbolicLoweringResult<SymbolicState> Exact(SymbolicState state, SyntaxNode source) =>
        SymbolicLoweringResult<SymbolicState>.Exact(
            state,
            new SymbolicLoweringProvenance("cfg-program-point", source.Span, "statement-region.try"));

    private static SymbolicLoweringResult<SymbolicState> Unsupported(SyntaxNode source, string detail) =>
        SymbolicLoweringResult<SymbolicState>.Unsupported(new SymbolicLoweringProvenance("cfg-program-point", source.Span, detail));

    sealed record CfgExceptionRegionPlan(
        ImmutableArray<CfgCatchRoute> Catches,
        SymbolicNestedMutationInvalidationPlan ProtectedMutations,
        bool HasKnownThrownType,
        ITypeSymbol? KnownThrownType);

    readonly record struct CfgCatchRoute(CatchClauseSyntax Clause, ITypeSymbol? ExceptionType);
}

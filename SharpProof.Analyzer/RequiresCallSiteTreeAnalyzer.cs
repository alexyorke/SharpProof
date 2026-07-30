namespace SharpProof.Analyzer;

internal static class RequiresCallSiteTreeAnalyzer
{
    internal static AnalyzerSemanticOutcome Analyze(
        IMethodSymbol caller,
        SyntaxNode declaration,
        SemanticModel semanticModel,
        AnalyzerSession session,
        Action<Diagnostic> reportDiagnostic,
        CancellationToken cancellationToken)
    {
        caller = ContractClauseInventoryBuilder
            .NormalizeCallable(caller);
        var discovery = new RequiresCallSiteDiscovery(
            caller,
            declaration,
            semanticModel,
            cancellationToken);
        var potentialOwners =
            discovery.GetPotentialCallOwners(
                session.HasPotentialCallPreconditions);
        if (potentialOwners == null)
        {
            return session.TryBeginRequiresCallSiteAnalysis(
                    caller)
                ? RequiresCallSiteAnalyzer.AnalyzeCallable(
                    caller,
                    declaration,
                    semanticModel,
                    session,
                    reportDiagnostic,
                    graph: null,
                    operationRoot: null,
                    screenForPotentialCalls: false,
                    cancellationToken:
                        cancellationToken)
                : AnalyzerSemanticOutcome.NotApplicable;
        }

        if (potentialOwners.IsEmpty)
        {
            return AnalyzerSemanticOutcome.NotApplicable;
        }

        if (!discovery.TryCreateGraph(
                out var operationRoot,
                out var graph))
        {
            return RecordUnavailableOwners(
                caller,
                potentialOwners,
                session);
        }

        return new TreeAnalysis(
                caller,
                declaration,
                semanticModel,
                session,
                reportDiagnostic,
                potentialOwners,
                cancellationToken)
            .Run(graph, operationRoot);
    }

    private static AnalyzerSemanticOutcome
        RecordUnavailableOwners(
            IMethodSymbol root,
            ImmutableHashSet<IMethodSymbol> owners,
            AnalyzerSession session)
    {
        var rootOutcome =
            AnalyzerSemanticOutcome.NotApplicable;
        foreach (var owner in owners)
        {
            if (!session.TryBeginRequiresCallSiteAnalysis(
                    owner))
            {
                continue;
            }

            if (SymbolEqualityComparer.Default.Equals(
                    owner,
                    root))
            {
                rootOutcome =
                    AnalyzerSemanticOutcome.Unknown;
            }
            else
            {
                session.RecordSemanticOutcome(
                    owner,
                    AnalyzerSemanticOutcome.Unknown);
            }
        }

        return rootOutcome;
    }

    private sealed class TreeAnalysis(
        IMethodSymbol root,
        SyntaxNode rootDeclaration,
        SemanticModel semanticModel,
        AnalyzerSession session,
        Action<Diagnostic> reportDiagnostic,
        ImmutableHashSet<IMethodSymbol> potentialOwners,
        CancellationToken cancellationToken)
    {
        private readonly HashSet<IMethodSymbol>
            _visitedPotentialOwners =
                new(SymbolEqualityComparer.Default);
        private AnalyzerSemanticOutcome _rootOutcome =
            AnalyzerSemanticOutcome.NotApplicable;

        internal AnalyzerSemanticOutcome Run(
            ControlFlowGraph graph,
            IOperation? operationRoot)
        {
            AnalyzeGraph(
                root,
                rootDeclaration,
                graph,
                operationRoot,
                isRoot: true);
            foreach (var owner in potentialOwners)
            {
                cancellationToken
                    .ThrowIfCancellationRequested();
                if (_visitedPotentialOwners.Contains(owner) ||
                    !session
                        .TryBeginRequiresCallSiteAnalysis(
                            owner))
                {
                    continue;
                }

                session.RecordSemanticOutcome(
                    owner,
                    AnalyzerSemanticOutcome.Unknown);
            }

            return _rootOutcome;
        }

        private void AnalyzeGraph(
            IMethodSymbol caller,
            SyntaxNode declaration,
            ControlFlowGraph graph,
            IOperation? operationRoot,
            bool isRoot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            caller = ContractClauseInventoryBuilder
                .NormalizeCallable(caller);
            if (potentialOwners.Contains(caller))
            {
                _visitedPotentialOwners.Add(caller);
                var outcome = session
                    .TryBeginRequiresCallSiteAnalysis(caller)
                    ? RequiresCallSiteAnalyzer
                        .AnalyzeCallable(
                            caller,
                            declaration,
                            semanticModel,
                            session,
                            reportDiagnostic,
                            graph,
                            operationRoot,
                            screenForPotentialCalls: false,
                            cancellationToken:
                                cancellationToken)
                    : AnalyzerSemanticOutcome
                        .NotApplicable;
                if (isRoot)
                {
                    _rootOutcome = outcome;
                }
                else
                {
                    session.RecordSemanticOutcome(
                        caller,
                        outcome);
                }
            }

            foreach (var nested in
                     GetNestedCallables(graph))
            {
                cancellationToken
                    .ThrowIfCancellationRequested();
                if (nested.IsExpressionTree)
                {
                    RecordUnsupportedNested(
                        nested.Method);
                    continue;
                }

                ControlFlowGraph childGraph;
                try
                {
                    childGraph =
                        nested.AnonymousFunction == null
                            ? graph
                                .GetLocalFunctionControlFlowGraph(
                                    nested.Method,
                                    cancellationToken)
                            : graph
                                .GetAnonymousFunctionControlFlowGraph(
                                    nested.AnonymousFunction,
                                    cancellationToken);
                }
                catch (Exception exception) when (
                    exception is ArgumentException or
                        InvalidOperationException)
                {
                    RecordUnsupportedNested(
                        nested.Method);
                    continue;
                }

                AnalyzeGraph(
                    nested.Method,
                    nested.Declaration,
                    childGraph,
                    childGraph.OriginalOperation,
                    isRoot: false);
            }
        }

        private void RecordUnsupportedNested(
            IMethodSymbol method)
        {
            method = ContractClauseInventoryBuilder
                .NormalizeCallable(method);
            if (!potentialOwners.Contains(method) ||
                !_visitedPotentialOwners.Add(method) ||
                !session.TryBeginRequiresCallSiteAnalysis(
                    method))
            {
                return;
            }

            session.RecordSemanticOutcome(
                method,
                AnalyzerSemanticOutcome.Unknown);
        }

        private ImmutableArray<NestedCallable>
            GetNestedCallables(
                ControlFlowGraph graph)
        {
            var result =
                ImmutableArray.CreateBuilder<
                    NestedCallable>();
            var seen = new HashSet<IMethodSymbol>(
                SymbolEqualityComparer.Default);
            foreach (var local in graph.LocalFunctions)
            {
                var method =
                    ContractClauseInventoryBuilder
                        .NormalizeCallable(local);
                var declaration =
                    GetDeclaration(method);
                if (declaration != null &&
                    seen.Add(method))
                {
                    result.Add(new(
                        method,
                        declaration,
                        AnonymousFunction: null,
                        IsExpressionTree: false));
                }
            }

            foreach (var anonymous in
                     GetAnonymousFunctions(graph))
            {
                var method =
                    ContractClauseInventoryBuilder
                        .NormalizeCallable(
                            anonymous.Symbol);
                if (seen.Add(method))
                {
                    result.Add(new(
                        method,
                        anonymous.Syntax,
                        anonymous,
                        IsExpressionTree(
                            anonymous.Syntax)));
                }
            }

            return [
                .. result.OrderBy(static nested =>
                    nested.Declaration.SyntaxTree
                        .FilePath,
                    StringComparer.Ordinal)
                    .ThenBy(static nested =>
                        nested.Declaration.SpanStart)
            ];
        }

        private SyntaxNode? GetDeclaration(
            IMethodSymbol method)
        {
            return method.DeclaringSyntaxReferences
                .Select(reference =>
                    reference.GetSyntax(
                        cancellationToken))
                .OrderBy(static syntax =>
                    syntax.SyntaxTree.FilePath,
                    StringComparer.Ordinal)
                .ThenBy(static syntax =>
                    syntax.SpanStart)
                .FirstOrDefault();
        }

        private bool IsExpressionTree(
            SyntaxNode declaration)
        {
            var expression = semanticModel.Compilation
                .GetTypeByMetadataName(
                    FrameworkTypeMetadataNames
                        .ExpressionOfT);
            return expression != null &&
                semanticModel.GetTypeInfo(
                        declaration,
                        cancellationToken)
                    .ConvertedType is
                    INamedTypeSymbol converted &&
                SymbolEqualityComparer.Default.Equals(
                    converted.OriginalDefinition,
                    expression.OriginalDefinition);
        }

        private static IEnumerable<
            IFlowAnonymousFunctionOperation>
            GetAnonymousFunctions(
                ControlFlowGraph graph)
        {
            return graph.Blocks
                .SelectMany(static block =>
                    block.Operations.Concat(
                        block.BranchValue == null
                            ? []
                            : [block.BranchValue]))
                .SelectMany(static operation =>
                    operation.DescendantsAndSelf())
                .OfType<
                    IFlowAnonymousFunctionOperation>();
        }
    }

    private readonly record struct NestedCallable(
        IMethodSymbol Method,
        SyntaxNode Declaration,
        IFlowAnonymousFunctionOperation?
            AnonymousFunction,
        bool IsExpressionTree);
}

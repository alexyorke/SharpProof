namespace SharpProof.Analyzer.Engine.Analysis;

internal static class WorklistPuritySolver
{
    public static ImmutableDictionary<IMethodSymbol, PurityAnalysisEngine.PurityAnalysisResult> Solve(
        ImmutableDictionary<IMethodSymbol, ImmutableHashSet<IMethodSymbol>> graph,
        Compilation compilation,
        INamedTypeSymbol enforcePureAttributeSymbol,
        INamedTypeSymbol? allowSynchronizationAttributeSymbol,
        SmtAnalysisService smtAnalysis,
        SharpProofAttributeIdentityPolicy attributePolicy,
        Func<SyntaxTree, SemanticModel> getSemanticModel,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var results =
            new Dictionary<IMethodSymbol, PurityAnalysisEngine.PurityAnalysisResult>(SymbolEq.Default);
        var engine = new PurityAnalysisEngine(smtAnalysis, attributePolicy);
        var worklist = new Queue<IMethodSymbol>();
        var reverse = new Dictionary<IMethodSymbol, HashSet<IMethodSymbol>>(SymbolEq.Default);

        foreach (var method in graph
                     .OrderBy(kvp => kvp.Value.Count)
                     .Select(kvp => kvp.Key))
        {
            cancellationToken.ThrowIfCancellationRequested();
            worklist.Enqueue(method);
            if (graph.TryGetValue(method, out var succs))
                foreach (var callee in succs)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!reverse.TryGetValue(callee, out var callers))
                    {
                        callers = new HashSet<IMethodSymbol>(SymbolEq.Default);
                        reverse[callee] = callers;
                    }

                    callers.Add(method);
                }
        }

        while (worklist.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var method = worklist.Dequeue();
            var syntaxRef = method.DeclaringSyntaxReferences.FirstOrDefault();
            var semanticTree = syntaxRef?.SyntaxTree ?? compilation.SyntaxTrees.FirstOrDefault();
            var purity = semanticTree == null
                ? CreateUnknownExternalResult(method)
                : engine.IsConsideredPure(
                    method,
                    getSemanticModel(semanticTree),
                    enforcePureAttributeSymbol,
                    allowSynchronizationAttributeSymbol,
                    cancellationToken,
                    results);
            if (!results.TryGetValue(method, out var prior) || prior.IsPure != purity.IsPure)
            {
                results[method] = purity;
                if (reverse.TryGetValue(method, out var callers))
                    foreach (var caller in callers)
                        worklist.Enqueue(caller);
            }
        }

        return results.ToImmutableDictionary(SymbolEq.Default);
    }

    private static PurityAnalysisEngine.PurityAnalysisResult CreateUnknownExternalResult(IMethodSymbol method)
    {
        return PurityAnalysisEngine.PurityAnalysisResult.ImpureUnknownLocation.WithEvidence(
            PurityAnalysisEngine.PurityEvidence.Create(
                "unknown_external_call",
                nameof(WorklistPuritySolver),
                symbol: method,
                catalogSource: "metadata"));
    }
}

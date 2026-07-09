using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using SharpProof.Analyzer;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Analyzer.Engine.Analysis
{
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
            var results = new Dictionary<IMethodSymbol, PurityAnalysisEngine.PurityAnalysisResult>(SymbolEqualityComparer.Default);
            var engine = new PurityAnalysisEngine(smtAnalysis, attributePolicy);
            var worklist = new Queue<IMethodSymbol>();
            var reverse = new Dictionary<IMethodSymbol, HashSet<IMethodSymbol>>(SymbolEqualityComparer.Default);

            foreach (var method in graph
                         .OrderBy(kvp => kvp.Value.Count)
                         .Select(kvp => kvp.Key))
            {
                cancellationToken.ThrowIfCancellationRequested();
                worklist.Enqueue(method);
                if (graph.TryGetValue(method, out var succs))
                {
                    foreach (var callee in succs)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (!reverse.TryGetValue(callee, out var callers))
                        {
                            callers = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
                            reverse[callee] = callers;
                        }
                        callers.Add(method);
                    }
                }
            }

            while (worklist.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var method = worklist.Dequeue();
                if (method.DeclaringSyntaxReferences.Length == 0)
                {
                    results[method] = PurityAnalysisEngine.PurityAnalysisResult.Pure;
                    continue;
                }
                var syntaxRef = method.DeclaringSyntaxReferences[0];
                var model = getSemanticModel(syntaxRef.SyntaxTree);
                var purity = engine.IsConsideredPure(
                    method,
                    model,
                    enforcePureAttributeSymbol,
                    allowSynchronizationAttributeSymbol,
                    cancellationToken,
                    results);
                if (!results.TryGetValue(method, out var prior) || prior.IsPure != purity.IsPure)
                {
                    results[method] = purity;
                    if (reverse.TryGetValue(method, out var callers))
                    {
                        foreach (var caller in callers)
                        {
                            worklist.Enqueue(caller);
                        }
                    }
                }
            }

            return results.ToImmutableDictionary(SymbolEqualityComparer.Default);
        }
    }
}

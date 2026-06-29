using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using PurelySharp.Symbolic.Smt;

namespace PurelySharp.Analyzer.Engine.Analysis
{
	internal static class WorklistPuritySolver
	{
		public static ImmutableDictionary<IMethodSymbol, PurityAnalysisEngine.PurityAnalysisResult> Solve(
			CallGraph graph,
			Compilation compilation,
			INamedTypeSymbol enforcePureAttributeSymbol,
			INamedTypeSymbol? allowSynchronizationAttributeSymbol,
			SmtAnalysisService smtAnalysis)
		{
			var results = new Dictionary<IMethodSymbol, PurityAnalysisEngine.PurityAnalysisResult>(SymbolEqualityComparer.Default);
			var engine = new PurityAnalysisEngine(smtAnalysis);
			var worklist = new Queue<IMethodSymbol>();
			var reverse = new Dictionary<IMethodSymbol, HashSet<IMethodSymbol>>(SymbolEqualityComparer.Default);

			foreach (var method in graph.Edges.Keys)
			{
				worklist.Enqueue(method);
				if (graph.Edges.TryGetValue(method, out var succs))
				{
					foreach (var callee in succs)
					{
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
				var method = worklist.Dequeue();
				if (method.DeclaringSyntaxReferences.Length == 0)
				{
					results[method] = PurityAnalysisEngine.PurityAnalysisResult.Pure;
					continue;
				}
				var syntaxRef = method.DeclaringSyntaxReferences[0];
				var model = compilation.GetSemanticModel(syntaxRef.SyntaxTree);
				var purity = engine.IsConsideredPure(method, model, enforcePureAttributeSymbol, allowSynchronizationAttributeSymbol);
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

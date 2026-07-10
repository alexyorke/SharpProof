using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.FlowAnalysis;
using System.Collections.Immutable;
using System;
using System.IO;
using System.Globalization;
using SharpProof.Analyzer.Engine.Analysis;
using SharpProof.Analyzer.Engine.Rules;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;
using SearchLib.Smt;
using System.Threading;

namespace SharpProof.Analyzer.Engine
{

    internal partial class PurityAnalysisEngine
    {

        internal PurityAnalysisResult IsConsideredPure(
            IMethodSymbol methodSymbol,
            SemanticModel semanticModel,
            INamedTypeSymbol enforcePureAttributeSymbol,
            INamedTypeSymbol? allowSynchronizationAttributeSymbol,
            CancellationToken cancellationToken,
            IReadOnlyDictionary<IMethodSymbol, PurityAnalysisResult>? initialPurityCache = null)
        {




            cancellationToken.ThrowIfCancellationRequested();
            var visited = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
            var purityCache = new Dictionary<IMethodSymbol, PurityAnalysisResult>(SymbolEqualityComparer.Default);
            if (initialPurityCache != null)
            {
                foreach (var entry in initialPurityCache)
                {
                    purityCache[entry.Key] = entry.Value;
                }
            }



            var result = DeterminePurityRecursiveInternal(
                methodSymbol,
                semanticModel,
                enforcePureAttributeSymbol,
                allowSynchronizationAttributeSymbol,
                visited,
                purityCache,
                _smtAnalysis,
                _attributePolicy,
                cancellationToken,
                _purityService
            );



            purityCache[methodSymbol] = result;

            return result;
        }


        private static string GetPuritySource(PurityAnalysisResult result)
        {

            if (result.IsPure) return "Assumed/Analyzed Pure";
            if (result.ImpureSyntaxNode != null) return "Analyzed Impure";

            return "Unknown/Default Impure";
        }

    }
}

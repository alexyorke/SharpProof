using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Analyzer.Engine;
using SharpProof.Analyzer.Engine.Analysis;
using SharpProof.Symbolic;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;

namespace SharpProof.Analyzer.Engine.Rules
{

    internal partial class MethodInvocationPurityRule
    {

        private static string GetCatalogHitCategory(ISymbol symbol) =>
            PurityAnalysisEngine.GetKnownImpureCatalogHitCategory(symbol, includeSynchronizationCategory: true);

        private static bool IsContractGuardInvocation(IMethodSymbol methodSymbol)
        {
            return methodSymbol.ContainingType?.OriginalDefinition.ToDisplayString() == "System.Diagnostics.Contracts.Contract" &&
                methodSymbol.Name is "Requires" or "Ensures";
        }

        private static bool ShouldPreferSemanticImpurityEvidence(string? knownImpureMemberSource)
        {
            return knownImpureMemberSource is
                "array_mutation_semantic_rule" or
                "random_semantic_rule" or
                "string_builder_semantic_rule" or
                "threading_semantic_rule";
        }
    }
}

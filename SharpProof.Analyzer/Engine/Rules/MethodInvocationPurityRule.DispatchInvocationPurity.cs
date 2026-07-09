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

        private static PurityAnalysisEngine.PurityAnalysisResult CheckDispatchedInvocationPurity(
            IInvocationOperation invocationOperation,
            PurityAnalysisContext context,
            INamedTypeSymbol? knownReceiverType,
            bool hasExactReceiverType)
        {
            var invokedMethodSymbol = invocationOperation.TargetMethod;
            if (invokedMethodSymbol == null)
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(invocationOperation.Syntax);
            }

            var originalDefinition = invokedMethodSymbol.OriginalDefinition;
            var knownImpureMemberSource = PurityAnalysisEngine.GetKnownImpureMemberSource(originalDefinition);
            if (string.Equals(knownImpureMemberSource, "random_semantic_rule", StringComparison.Ordinal))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    invocationOperation.Syntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        GetCatalogHitCategory(originalDefinition),
                        nameof(MethodInvocationPurityRule),
                        invocationOperation,
                        symbol: originalDefinition,
                        catalogSource: knownImpureMemberSource));
            }

            if (TryCheckArrayInterfaceGetEnumeratorPurity(invocationOperation, context, out var arrayEnumeratorResult))
            {
                return arrayEnumeratorResult;
            }

            var candidateMethods = ResolvePotentialDispatchTargets(
                invokedMethodSymbol,
                context.SemanticModel,
                knownReceiverType,
                invocationOperation.Instance,
                hasExactReceiverType,
                context.CancellationToken)
                .Where(method => !method.IsAbstract && !method.IsExtern)
                .ToImmutableHashSet<IMethodSymbol>(SymbolEqualityComparer.Default);

            if (CanHaveExternalDispatchTargets(invokedMethodSymbol, invocationOperation, knownReceiverType, hasExactReceiverType))
            {
                var isTypeParameterReceiver = invocationOperation.Instance?.Type?.TypeKind == TypeKind.TypeParameter;
                var hasConcreteImplementationCandidate =
                    invokedMethodSymbol.ContainingType?.TypeKind == TypeKind.Interface &&
                    !isTypeParameterReceiver &&
                    candidateMethods.Any(method => method.ContainingType?.TypeKind != TypeKind.Interface);

                if (!hasConcreteImplementationCandidate)
                {
                    PurityAnalysisEngine.LogDebug($"  [MIR] Method {invokedMethodSymbol.ContainingType?.Name}.{invokedMethodSymbol.Name} can dispatch to unknown external targets; treating as impure conservatively.");
                    return PurityAnalysisEngine.ImpureResult(
                        invocationOperation,
                        "unknown_external_call",
                        nameof(MethodInvocationPurityRule),
                        invokedMethodSymbol);
                }
            }

            if (candidateMethods.Count == 0)
            {
                PurityAnalysisEngine.LogDebug($"  [MIR] No concrete dispatch candidates found for {invokedMethodSymbol.Name}; treating unresolved closed-world dispatch as impure conservatively.");
                return PurityAnalysisEngine.ImpureResult(
                    invocationOperation,
                    "dynamic_dispatch",
                    nameof(MethodInvocationPurityRule),
                    invokedMethodSymbol);
            }

            foreach (var candidateMethod in candidateMethods)
            {
                PurityAnalysisEngine.LogDebug($"  [MIR]   Evaluating dispatch candidate: {candidateMethod.ToDisplayString()}");
                if (SymbolEqualityComparer.Default.Equals(
                        candidateMethod.OriginalDefinition,
                        context.ContainingMethodSymbol.OriginalDefinition))
                {
                    PurityAnalysisEngine.LogDebug("  [MIR]   Direct self-recursive dispatch candidate is purity-neutral.");
                    continue;
                }

                var candidatePurity = PurityAnalysisEngine.GetCalleePurity(candidateMethod, context);
                if (!candidatePurity.IsPure)
                {
                    PurityAnalysisEngine.LogDebug($"  [MIR] --> IMPURE dispatch candidate found: {candidateMethod.ToDisplayString()}");
                    return candidatePurity.WithCallee(candidateMethod, invocationOperation.Syntax);
                }
            }

            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }
    }
}

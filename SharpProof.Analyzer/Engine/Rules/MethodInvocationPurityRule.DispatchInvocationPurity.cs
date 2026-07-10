using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Analyzer.Engine.Rules;

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
            return PurityAnalysisEngine.PurityAnalysisResult.Impure(invocationOperation.Syntax);

        var originalDefinition = invokedMethodSymbol.OriginalDefinition;
        var knownImpureMemberSource = PurityAnalysisEngine.GetKnownImpureMemberSource(originalDefinition);
        if (string.Equals(knownImpureMemberSource, "random_semantic_rule", StringComparison.Ordinal))
            return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                invocationOperation.Syntax,
                PurityAnalysisEngine.PurityEvidence.Create(
                    GetCatalogHitCategory(originalDefinition),
                    nameof(MethodInvocationPurityRule),
                    invocationOperation,
                    symbol: originalDefinition,
                    catalogSource: knownImpureMemberSource));

        if (TryCheckArrayInterfaceGetEnumeratorPurity(invocationOperation, context, out var arrayEnumeratorResult))
            return arrayEnumeratorResult;

        var candidateMethods = ResolvePotentialDispatchTargets(
                invokedMethodSymbol,
                context.SemanticModel,
                knownReceiverType,
                invocationOperation.Instance,
                hasExactReceiverType,
                context.CancellationToken)
            .Where(method => !method.IsAbstract && !method.IsExtern)
            .ToImmutableHashSet<IMethodSymbol>(SymbolEqualityComparer.Default);

        if (CanHaveExternalDispatchTargets(invokedMethodSymbol, invocationOperation, knownReceiverType,
                hasExactReceiverType))
        {
            var isTypeParameterReceiver = invocationOperation.Instance?.Type?.TypeKind == TypeKind.TypeParameter;
            var hasConcreteImplementationCandidate =
                invokedMethodSymbol.ContainingType?.TypeKind == TypeKind.Interface &&
                !isTypeParameterReceiver &&
                candidateMethods.Any(method => method.ContainingType?.TypeKind != TypeKind.Interface);

            if (!hasConcreteImplementationCandidate)
                return PurityAnalysisEngine.ImpureResult(
                    invocationOperation,
                    "unknown_external_call",
                    nameof(MethodInvocationPurityRule),
                    invokedMethodSymbol);
        }

        if (candidateMethods.Count == 0)
            return PurityAnalysisEngine.ImpureResult(
                invocationOperation,
                "dynamic_dispatch",
                nameof(MethodInvocationPurityRule),
                invokedMethodSymbol);

        foreach (var candidateMethod in candidateMethods)
        {
            if (SymbolEqualityComparer.Default.Equals(
                    candidateMethod.OriginalDefinition,
                    context.ContainingMethodSymbol.OriginalDefinition))
                continue;

            var candidatePurity = PurityAnalysisEngine.GetCalleePurity(candidateMethod, context);
            if (!candidatePurity.IsPure) return candidatePurity.WithCallee(candidateMethod, invocationOperation.Syntax);
        }

        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
    }
}
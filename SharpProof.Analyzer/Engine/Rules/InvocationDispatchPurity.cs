using static SharpProof.Analyzer.Engine.Rules.InvocationEvidence;
using static SharpProof.Analyzer.Engine.Rules.MethodInvocationPurityRule;

namespace SharpProof.Analyzer.Engine.Rules;

internal static class InvocationDispatchPurity {
    internal static PurityAnalysisEngine.PurityAnalysisResult CheckDispatchedInvocationPurity(
        IInvocationOperation invocationOperation,
        PurityAnalysisContext context,
        INamedTypeSymbol? knownReceiverType,
        bool hasExactReceiverType) {
        var invokedMethodSymbol = invocationOperation.TargetMethod;
        if (invokedMethodSymbol == null)
            return PurityAnalysisEngine.PurityAnalysisResult.Impure(invocationOperation.Syntax);

        var originalDefinition = invokedMethodSymbol.OriginalDefinition;
        var knownImpureMemberSource = PurityCalleeResolver.GetKnownImpureMemberSource(originalDefinition);
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
            .ToImmutableHashSet<IMethodSymbol>(SymbolEq.Default);

        if (CanHaveExternalDispatchTargets(invokedMethodSymbol, invocationOperation, knownReceiverType,
                hasExactReceiverType)) {
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

        foreach (var candidateMethod in candidateMethods) {
            if (SymbolEq.AreEqual(
                    candidateMethod.OriginalDefinition,
                    context.ContainingMethodSymbol.OriginalDefinition))
                continue;

            var candidatePurity = PurityCalleeResolver.GetCalleePurityAtUse(candidateMethod, invocationOperation.Syntax, context);
            if (!candidatePurity.IsPure) return candidatePurity;
        }

        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
    }
}

namespace SharpProof.Analyzer.Engine.Rules;

internal static class PropertyAccessorDispatchTargetResolver {
    internal static PurityAnalysisEngine.PurityAnalysisResult CheckPotentialTargetPurity(
        IPropertyReferenceOperation propertyReference,
        PurityAnalysisContext context,
        INamedTypeSymbol? knownReceiverType,
        bool hasExactReceiverType,
        bool useSetter,
        string ruleName) {
        var accessor = useSetter ? propertyReference.Property.SetMethod : propertyReference.Property.GetMethod;
        var candidates = accessor == null
            ? Array.Empty<IMethodSymbol>()
            : MethodInvocationPurityRule.ResolvePotentialDispatchTargets(
                    accessor,
                    context.SemanticModel,
                    knownReceiverType,
                    propertyReference.Instance,
                    hasExactReceiverType,
                    context.CancellationToken)
                .ToArray();
        if (candidates.Length == 0)
            return PurityAnalysisEngine.ImpureResult(
                propertyReference,
                "dynamic_dispatch",
                ruleName,
                accessor);

        foreach (var candidate in candidates) {
            var candidateResult = PurityCalleeResolver.GetCalleePurityAtUse(candidate, propertyReference.Syntax, context);
            if (!candidateResult.IsPure) return candidateResult;
        }

        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
    }
}

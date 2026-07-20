namespace SharpProof.Analyzer.Engine.Rules;

internal partial class PropertyReferencePurityRule
{
    private static bool IsPotentiallyDispatchedProperty(IPropertySymbol propertySymbol, Compilation compilation)
    {
        return propertySymbol.ContainingType?.TypeKind == TypeKind.Interface ||
               propertySymbol.IsAbstract ||
               (propertySymbol.GetMethod != null &&
                 DispatchedMemberResolution.IsPotentiallyDispatchedMethod(propertySymbol.GetMethod, compilation));
    }

    private static PurityAnalysisEngine.PurityAnalysisResult CheckDispatchedGetterPurity(
        IPropertyReferenceOperation propertyReferenceOperation,
        PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState)
    {
        var hasExactReceiverType = PurityConcreteReceiverResolver.TryResolveKnownConcreteType(
            propertyReferenceOperation.Instance,
            currentState,
            context.SemanticModel.Compilation,
            out var exactReceiverType);

        var knownReceiverType = hasExactReceiverType
            ? exactReceiverType
            : MethodInvocationPurityRule.GetKnownReceiverType(propertyReferenceOperation.Instance);

        if (hasExactReceiverType && knownReceiverType != null)
        {
            var exactGetter = PurityConcreteReceiverResolver.ResolvePropertyAccessorTargetForConcreteReceiver(
                propertyReferenceOperation.Property,
                knownReceiverType,
                false);
            if (exactGetter != null)
            {
                return PurityCalleeResolver.GetCalleePurityAtUse(
                    exactGetter,
                    propertyReferenceOperation.Syntax,
                    context);
            }
        }

        if (propertyReferenceOperation.Property.GetMethod is { } runtimeBackedGetter &&
            PurityConcreteReceiverResolver.IsKnownSystemTypeRuntimeReceiver(propertyReferenceOperation.Instance) &&
            EffectSummaryCatalog.Current.TryGetSystemTypeRuntimeImplementationPurity(
                runtimeBackedGetter.OriginalDefinition,
                context.SemanticModel.Compilation,
                out var runtimeImplementationPurity))
        {
            if (runtimeImplementationPurity.IsPure) return PurityAnalysisEngine.PurityAnalysisResult.Pure;

            if (runtimeImplementationPurity.IsNonPure)
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    propertyReferenceOperation.Syntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        runtimeImplementationPurity.PrimaryCategory,
                        nameof(PropertyReferencePurityRule),
                        propertyReferenceOperation,
                        symbol: runtimeBackedGetter,
                        catalogSource: "generated_purity_summary"));
        }

        if (!hasExactReceiverType &&
            CanDispatchToUnknownGetterTarget(
                propertyReferenceOperation.Property,
                knownReceiverType,
                context.SemanticModel.Compilation))
            return PurityAnalysisEngine.ImpureResult(
                propertyReferenceOperation,
                "dynamic_dispatch",
                nameof(PropertyReferencePurityRule),
                propertyReferenceOperation.Property.GetMethod);

        return PropertyAccessorDispatchTargetResolver.CheckPotentialTargetPurity(
            propertyReferenceOperation,
            context,
            knownReceiverType,
            hasExactReceiverType,
            false,
            nameof(PropertyReferencePurityRule));
    }

    private static bool CanDispatchToUnknownGetterTarget(
        IPropertySymbol propertySymbol,
        INamedTypeSymbol? knownReceiverType,
        Compilation compilation)
    {
        if (knownReceiverType == null) return true;

        if (knownReceiverType.TypeKind == TypeKind.Interface) return true;

        if (knownReceiverType.TypeKind == TypeKind.Class &&
            !knownReceiverType.IsSealed &&
            IsPotentiallyDispatchedProperty(propertySymbol, compilation))
            return true;

        return false;
    }

    private static PurityAnalysisEngine.PurityAnalysisResult GetterResultOrPure(
        PurityAnalysisEngine.PurityAnalysisResult getterResult,
        IPropertySymbol propertySymbol,
        IMethodSymbol getterSymbol,
        IPropertyReferenceOperation propertyReferenceOperation)
    {
        if (!getterResult.IsPure &&
            string.Equals(getterResult.Evidence.Category, "unknown_external_call", StringComparison.Ordinal) &&
            string.IsNullOrEmpty(getterResult.Evidence.CatalogSource) &&
            string.Equals(GetCatalogHitCategory(propertySymbol), "reflection_environment_source",
                StringComparison.Ordinal))
            return CreateReflectionEnvironmentSourceResult(propertySymbol, propertyReferenceOperation);

        return getterResult.IsPure
            ? PurityAnalysisEngine.PurityAnalysisResult.Pure
            : getterResult.WithCallee(getterSymbol, propertyReferenceOperation.Syntax);
    }

    private static PurityAnalysisEngine.PurityAnalysisResult GetterResultOrImpure(
        IPropertySymbol propertySymbol,
        IPropertyReferenceOperation propertyReferenceOperation,
        PurityAnalysisContext context)
    {
        if (propertySymbol.GetMethod is not { } getter)
            return PurityAnalysisEngine.PurityAnalysisResult.Impure(propertyReferenceOperation.Syntax);

        var getterResult = PurityCalleeResolver.GetCalleePurity(getter, context);
        return GetterResultOrPure(getterResult, propertySymbol, getter, propertyReferenceOperation);
    }

    private static string GetCatalogHitCategory(ISymbol symbol) =>
        ImpurityCatalog.GetKnownImpureCatalogHitCategory(symbol);

    private static PurityAnalysisEngine.PurityAnalysisResult CreateReflectionEnvironmentSourceResult(
        IPropertySymbol propertySymbol,
        IPropertyReferenceOperation propertyReferenceOperation,
        string? catalogSource = null)
    {
        return PurityAnalysisEngine.PurityAnalysisResult.Impure(
            propertyReferenceOperation.Syntax,
            PurityAnalysisEngine.PurityEvidence.Create(
                "reflection_environment_source",
                nameof(PropertyReferencePurityRule),
                propertyReferenceOperation,
                propertyReferenceOperation.Syntax,
                propertySymbol,
                catalogSource));
    }
}

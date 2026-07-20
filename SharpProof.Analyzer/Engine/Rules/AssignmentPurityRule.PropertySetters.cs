namespace SharpProof.Analyzer.Engine.Rules;

internal partial class AssignmentPurityRule {
    internal static PurityAnalysisEngine.PurityAnalysisResult CheckPropertySetterPurity(
        IOperation targetOperation,
        PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState) {
        if (targetOperation is not IPropertyReferenceOperation propertyReference ||
            propertyReference.Property.SetMethod is not { } setter)
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        if (IsValueTypeWithInitializerAssignment(propertyReference, context))
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        if (RuleAnalysisHelper.IsSourceAutoPropertyAccessor(
                propertyReference.Property,
                getter: false,
                cancellationToken: context.CancellationToken))
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        if (IsPotentiallyDispatchedSetter(setter))
            return CheckDispatchedSetterPurity(propertyReference, context, currentState);

        return PurityCalleeResolver.GetCanonicalCalleePurityAtUse(setter, targetOperation.Syntax, context);
    }

    private static bool IsPotentiallyDispatchedSetter(IMethodSymbol setterSymbol) => setterSymbol.ContainingType?.TypeKind == TypeKind.Interface ||
               setterSymbol.IsVirtual ||
               setterSymbol.IsAbstract ||
               setterSymbol.IsOverride;

    private static PurityAnalysisEngine.PurityAnalysisResult CheckDispatchedSetterPurity(
        IPropertyReferenceOperation propertyReferenceOperation,
        PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState) => PropertyAccessorDispatchTargetResolver.CheckPotentialTargetPurity(
            propertyReferenceOperation,
            context,
            GetTrackedLocalReceiverType(propertyReferenceOperation.Instance, currentState,
                context.SemanticModel.Compilation) ??
            MethodInvocationPurityRule.GetKnownReceiverType(propertyReferenceOperation.Instance),
            false,
            true,
            nameof(AssignmentPurityRule));

    private static INamedTypeSymbol? GetTrackedLocalReceiverType(
        IOperation? instanceOperation,
        PurityAnalysisEngine.PurityAnalysisState currentState,
        Compilation compilation) => PurityConcreteReceiverResolver.TryResolveKnownConcreteType(instanceOperation, currentState, compilation,
            out var concreteType)
            ? concreteType
            : null;

}

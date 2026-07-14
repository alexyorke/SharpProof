using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Analyzer.Engine.Rules;

internal partial class AssignmentPurityRule : IPurityRule
{
    internal static PurityAnalysisEngine.PurityAnalysisResult CheckPropertySetterPurity(
        IOperation targetOperation,
        PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState)
    {
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

    private static bool IsPotentiallyDispatchedSetter(IMethodSymbol setterSymbol)
    {
        return setterSymbol.ContainingType?.TypeKind == TypeKind.Interface ||
               setterSymbol.IsVirtual ||
               setterSymbol.IsAbstract ||
               setterSymbol.IsOverride;
    }

    private static PurityAnalysisEngine.PurityAnalysisResult CheckDispatchedSetterPurity(
        IPropertyReferenceOperation propertyReferenceOperation,
        PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState)
    {
        return PropertyAccessorDispatchTargetResolver.CheckPotentialTargetPurity(
            propertyReferenceOperation,
            context,
            GetTrackedLocalReceiverType(propertyReferenceOperation.Instance, currentState,
                context.SemanticModel.Compilation) ??
            PropertyDispatchHelper.GetKnownReceiverType(propertyReferenceOperation.Instance),
            false,
            true,
            nameof(AssignmentPurityRule));
    }

    private static INamedTypeSymbol? GetTrackedLocalReceiverType(
        IOperation? instanceOperation,
        PurityAnalysisEngine.PurityAnalysisState currentState,
        Compilation compilation)
    {
        return PurityConcreteReceiverResolver.TryResolveKnownConcreteType(instanceOperation, currentState, compilation,
            out var concreteType)
            ? concreteType
            : null;
    }

}

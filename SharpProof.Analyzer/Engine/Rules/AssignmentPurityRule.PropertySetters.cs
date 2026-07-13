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

        var setterResult = PurityCalleeResolver.GetCalleePurity(setter.OriginalDefinition, context);
        return setterResult.IsPure
            ? PurityAnalysisEngine.PurityAnalysisResult.Pure
            : setterResult.WithCallee(setter.OriginalDefinition, targetOperation.Syntax);
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
        var candidates = PropertyAccessorDispatchTargetResolver.ResolvePotentialTargets(
            propertyReferenceOperation.Property,
            context.SemanticModel,
            GetTrackedLocalReceiverType(propertyReferenceOperation.Instance, currentState,
                context.SemanticModel.Compilation) ??
            PropertyDispatchHelper.GetKnownReceiverType(propertyReferenceOperation.Instance),
            false,
            true,
            context.CancellationToken);

        if (candidates.IsDefaultOrEmpty)
            return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                propertyReferenceOperation.Syntax,
                PurityAnalysisEngine.PurityEvidence.Create(
                    "dynamic_dispatch",
                    nameof(AssignmentPurityRule),
                    propertyReferenceOperation,
                    symbol: propertyReferenceOperation.Property.SetMethod));

        foreach (var setterCandidate in candidates)
        {
            var setterResult = PurityCalleeResolver.GetCalleePurity(setterCandidate, context);
            if (!setterResult.IsPure)
                return setterResult.WithCallee(setterCandidate, propertyReferenceOperation.Syntax);
        }

        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
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

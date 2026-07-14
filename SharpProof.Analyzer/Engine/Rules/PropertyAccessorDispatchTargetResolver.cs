using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Analyzer.Engine.Analysis;

namespace SharpProof.Analyzer.Engine.Rules;

internal static class PropertyAccessorDispatchTargetResolver
{
    internal static PurityAnalysisEngine.PurityAnalysisResult CheckPotentialTargetPurity(
        IPropertyReferenceOperation propertyReference,
        PurityAnalysisContext context,
        INamedTypeSymbol? knownReceiverType,
        bool hasExactReceiverType,
        bool useSetter,
        string ruleName)
    {
        var accessor = useSetter ? propertyReference.Property.SetMethod : propertyReference.Property.GetMethod;
        var candidates = ResolvePotentialTargets(
            propertyReference.Property,
            context.SemanticModel,
            knownReceiverType,
            hasExactReceiverType,
            useSetter,
            context.CancellationToken);
        if (candidates.IsDefaultOrEmpty)
            return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                propertyReference.Syntax,
                PurityAnalysisEngine.PurityEvidence.Create(
                    "dynamic_dispatch",
                    ruleName,
                    propertyReference,
                    symbol: accessor));

        foreach (var candidate in candidates)
        {
            var candidateResult = PurityCalleeResolver.GetCalleePurity(candidate, context);
            if (!candidateResult.IsPure) return candidateResult.WithCallee(candidate, propertyReference.Syntax);
        }

        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
    }

    internal static ImmutableArray<IMethodSymbol> ResolvePotentialTargets(
        IPropertySymbol propertySymbol,
        SemanticModel semanticModel,
        INamedTypeSymbol? knownReceiverType,
        bool hasExactReceiverType,
        bool useSetter,
        CancellationToken cancellationToken)
    {
        var targets = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        var targetProperty = propertySymbol.OriginalDefinition;

        if (knownReceiverType != null && hasExactReceiverType)
        {
            AddAccessorForReceiverType(knownReceiverType, targetProperty, useSetter, targets);
            return targets.ToImmutableArray();
        }

        if (knownReceiverType != null &&
            (knownReceiverType.TypeKind == TypeKind.Struct || knownReceiverType.IsSealed))
        {
            AddAccessorForReceiverType(knownReceiverType, targetProperty, useSetter, targets);
            return targets.ToImmutableArray();
        }

        if (targetProperty.ContainingType?.TypeKind == TypeKind.Interface)
        {
            foreach (var type in TypeHierarchyEnumeration.EnumerateAllNamedTypes(
                         semanticModel.Compilation.Assembly.GlobalNamespace, cancellationToken))
            {
                if (type.TypeKind != TypeKind.Class && type.TypeKind != TypeKind.Struct) continue;

                if (!TypeHierarchyEnumeration.ImplementsInterface(type, targetProperty.ContainingType)) continue;

                AddAccessorForReceiverType(type, targetProperty, useSetter, targets);
            }

            AddConcreteAccessor(targetProperty, useSetter, targets);
            return targets.ToImmutableArray();
        }

        var baseProperty = DispatchedMemberResolution.GetRootOverriddenProperty(targetProperty);
        var baseType = baseProperty.ContainingType;
        if (baseType != null)
            foreach (var type in TypeHierarchyEnumeration.EnumerateAllNamedTypes(
                         semanticModel.Compilation.Assembly.GlobalNamespace, cancellationToken))
            {
                if (!TypeHierarchyEnumeration.DerivesFrom(type, baseType, true)) continue;

                foreach (var property in type.GetMembers(baseProperty.Name).OfType<IPropertySymbol>())
                    if (DispatchedMemberResolution.OverridesProperty(property, baseProperty))
                        AddAccessor(property, useSetter, targets);
            }

        AddConcreteAccessor(baseProperty, useSetter, targets);
        return targets.ToImmutableArray();
    }

    private static void AddAccessorForReceiverType(
        INamedTypeSymbol receiverType,
        IPropertySymbol targetProperty,
        bool useSetter,
        HashSet<IMethodSymbol> targets)
    {
        var accessor = PurityConcreteReceiverResolver.ResolvePropertyAccessorTargetForConcreteReceiver(
            targetProperty,
            receiverType,
            useSetter);
        if (accessor != null) targets.Add(accessor.OriginalDefinition);
    }

    private static void AddConcreteAccessor(
        IPropertySymbol property,
        bool useSetter,
        HashSet<IMethodSymbol> targets)
    {
        var accessor = GetAccessor(property, useSetter);
        if (accessor != null && !accessor.IsAbstract) targets.Add(accessor.OriginalDefinition);
    }

    private static void AddAccessor(
        IPropertySymbol property,
        bool useSetter,
        HashSet<IMethodSymbol> targets)
    {
        var accessor = GetAccessor(property, useSetter);
        if (accessor != null) targets.Add(accessor.OriginalDefinition);
    }

    private static IMethodSymbol? GetAccessor(IPropertySymbol property, bool useSetter)
    {
        return useSetter ? property.SetMethod : property.GetMethod;
    }
}

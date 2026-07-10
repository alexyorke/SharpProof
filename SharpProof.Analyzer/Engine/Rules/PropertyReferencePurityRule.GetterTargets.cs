using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using SharpProof.Analyzer.Engine.Analysis;

namespace SharpProof.Analyzer.Engine.Rules;

internal partial class PropertyReferencePurityRule
{
    private static ImmutableArray<IMethodSymbol> ResolvePotentialGetterTargets(
        IPropertySymbol propertySymbol,
        SemanticModel semanticModel,
        INamedTypeSymbol? knownReceiverType,
        bool hasExactReceiverType,
        CancellationToken cancellationToken)
    {
        var targets = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        var targetProperty = propertySymbol.OriginalDefinition;

        if (knownReceiverType != null && hasExactReceiverType)
        {
            var exactGetter = PurityAnalysisEngine.ResolvePropertyAccessorTargetForConcreteReceiver(
                targetProperty,
                knownReceiverType,
                false);
            if (exactGetter != null) targets.Add(exactGetter.OriginalDefinition);

            return targets.ToImmutableArray();
        }

        if (knownReceiverType != null &&
            (knownReceiverType.TypeKind == TypeKind.Struct || knownReceiverType.IsSealed))
        {
            AddGetterForReceiverType(knownReceiverType, targetProperty, targets);
            return targets.ToImmutableArray();
        }

        if (targetProperty.ContainingType?.TypeKind == TypeKind.Interface)
        {
            foreach (var type in TypeHierarchyEnumeration.EnumerateAllNamedTypes(
                         semanticModel.Compilation.Assembly.GlobalNamespace, cancellationToken))
            {
                if (type.TypeKind != TypeKind.Class && type.TypeKind != TypeKind.Struct) continue;

                if (!TypeHierarchyEnumeration.ImplementsInterface(type, targetProperty.ContainingType)) continue;

                AddGetterForReceiverType(type, targetProperty, targets);
            }

            if (targetProperty.GetMethod != null && !targetProperty.GetMethod.IsAbstract)
                targets.Add(targetProperty.GetMethod.OriginalDefinition);

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
                    if (DispatchedMemberResolution.OverridesProperty(property, baseProperty) &&
                        property.GetMethod != null)
                        targets.Add(property.GetMethod.OriginalDefinition);
            }

        if (baseProperty.GetMethod != null && !baseProperty.GetMethod.IsAbstract)
            targets.Add(baseProperty.GetMethod.OriginalDefinition);

        return targets.ToImmutableArray();
    }

    private static void AddGetterForReceiverType(
        INamedTypeSymbol receiverType,
        IPropertySymbol targetProperty,
        HashSet<IMethodSymbol> targets)
    {
        var getter = PurityAnalysisEngine.ResolvePropertyAccessorTargetForConcreteReceiver(
            targetProperty,
            receiverType,
            false);
        if (getter != null) targets.Add(getter.OriginalDefinition);
    }
}
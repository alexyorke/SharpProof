using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Analyzer.Engine.Analysis;

namespace SharpProof.Analyzer.Engine.Rules;

internal partial class MethodInvocationPurityRule
{
    private static bool CanHaveExternalOverrides(IMethodSymbol methodSymbol, INamedTypeSymbol? knownReceiverType)
    {
        if (methodSymbol.IsSealed) return false;

        if (!methodSymbol.IsVirtual) return false;

        if (methodSymbol.DeclaredAccessibility == Accessibility.Private ||
            methodSymbol.DeclaredAccessibility == Accessibility.Internal ||
            methodSymbol.DeclaredAccessibility == Accessibility.ProtectedAndInternal)
            return false;

        if (methodSymbol.ContainingType == null || methodSymbol.ContainingType.TypeKind != TypeKind.Class) return false;

        if (methodSymbol.ContainingType.IsSealed) return false;

        if (knownReceiverType != null &&
            knownReceiverType.IsSealed &&
            (SymbolEqualityComparer.Default.Equals(knownReceiverType.OriginalDefinition,
                 methodSymbol.ContainingType.OriginalDefinition) ||
             TypeHierarchyEnumeration.DerivesFrom(knownReceiverType, methodSymbol.ContainingType)))
            return false;

        return IsTypeEffectivelyExternallyAccessible(methodSymbol.ContainingType);
    }

    private static bool CanHaveExternalDispatchTargets(
        IMethodSymbol methodSymbol,
        IInvocationOperation invocationOperation,
        INamedTypeSymbol? knownReceiverType,
        bool hasExactReceiverType)
    {
        if (methodSymbol.ContainingType?.TypeKind == TypeKind.Interface)
            return CanHaveExternalInterfaceImplementations(
                methodSymbol.ContainingType,
                invocationOperation.Instance,
                knownReceiverType,
                hasExactReceiverType);

        if (hasExactReceiverType &&
            knownReceiverType != null &&
            (SymbolEqualityComparer.Default.Equals(knownReceiverType.OriginalDefinition,
                 methodSymbol.ContainingType?.OriginalDefinition) ||
             (methodSymbol.ContainingType != null &&
              TypeHierarchyEnumeration.DerivesFrom(knownReceiverType, methodSymbol.ContainingType))))
            return false;

        return CanHaveExternalOverrides(methodSymbol, knownReceiverType);
    }

    private static bool CanHaveExternalInterfaceImplementations(
        INamedTypeSymbol interfaceSymbol,
        IOperation? invocationInstance,
        INamedTypeSymbol? knownReceiverType,
        bool hasExactReceiverType)
    {
        if (!CanInterfaceHaveExternalImplementations(interfaceSymbol)) return false;

        var concreteReceiverType = GetKnownReceiverType(invocationInstance) ?? knownReceiverType;
        if (concreteReceiverType == null) return true;

        if (hasExactReceiverType) return false;

        if (IsAllocationOnlyInterfaceReceiver(invocationInstance)) return false;

        if (!IsTypeEffectivelyExternallyAccessible(concreteReceiverType)) return false;

        if (concreteReceiverType.TypeKind == TypeKind.Interface &&
            SymbolEqualityComparer.Default.Equals(
                concreteReceiverType.OriginalDefinition,
                interfaceSymbol.OriginalDefinition))
            return true;

        if (concreteReceiverType.TypeKind == TypeKind.Struct) return false;

        if (concreteReceiverType.TypeKind == TypeKind.Class && concreteReceiverType.IsSealed) return false;

        return true;
    }

    private static bool CanInterfaceHaveExternalImplementations(INamedTypeSymbol interfaceSymbol)
    {
        if (!IsTypeEffectivelyExternallyAccessible(interfaceSymbol)) return false;

        foreach (var baseInterface in interfaceSymbol.AllInterfaces)
            if (!IsTypeEffectivelyExternallyAccessible(baseInterface))
                return false;

        return true;
    }
}
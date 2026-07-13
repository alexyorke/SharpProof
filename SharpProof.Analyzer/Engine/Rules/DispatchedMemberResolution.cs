using Microsoft.CodeAnalysis;
using SharpProof.Analyzer.Engine.Analysis;

namespace SharpProof.Analyzer.Engine.Rules;

internal static class DispatchedMemberResolution
{
    internal static IMethodSymbol? ResolveGetter(
        IPropertySymbol propertySymbol,
        INamedTypeSymbol? receiverType,
        bool hasStableConcreteReceiver,
        Compilation compilation)
    {
        if (propertySymbol.GetMethod == null) return null;

        if (!IsPotentiallyDispatchedGetter(propertySymbol.GetMethod, compilation))
            return propertySymbol.GetMethod.OriginalDefinition;

        if (receiverType == null || !hasStableConcreteReceiver) return null;

        if (propertySymbol.ContainingType?.TypeKind == TypeKind.Interface)
        {
            var implementation = receiverType.FindImplementationForInterfaceMember(propertySymbol) ??
                                 receiverType.FindImplementationForInterfaceMember(propertySymbol.GetMethod);
            return implementation switch
            {
                IPropertySymbol implementationProperty when implementationProperty.GetMethod != null =>
                    implementationProperty.GetMethod.OriginalDefinition,
                IMethodSymbol implementationMethod => implementationMethod.OriginalDefinition,
                _ => null
            };
        }

        var rootProperty = GetRootOverriddenProperty(propertySymbol);
        foreach (var current in TypeHierarchyEnumeration.EnumerateBaseTypes(receiverType))
            foreach (var member in current.GetMembers(rootProperty.Name))
                if (member is IPropertySymbol candidate &&
                    (SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition,
                         rootProperty.OriginalDefinition) ||
                     OverridesProperty(candidate, rootProperty)) &&
                    candidate.GetMethod != null)
                    return candidate.GetMethod.OriginalDefinition;

        return propertySymbol.GetMethod.IsAbstract ? null : propertySymbol.GetMethod.OriginalDefinition;
    }

    internal static IMethodSymbol? ResolveMethod(
        IMethodSymbol methodSymbol,
        INamedTypeSymbol? receiverType,
        bool hasStableConcreteReceiver,
        Compilation compilation)
    {
        if (!IsPotentiallyDispatchedMethod(methodSymbol, compilation)) return methodSymbol.OriginalDefinition;

        if (receiverType == null || !hasStableConcreteReceiver) return null;

        if (methodSymbol.ContainingType?.TypeKind == TypeKind.Interface)
            return receiverType.FindImplementationForInterfaceMember(methodSymbol) as IMethodSymbol;

        var rootMethod = GetRootOverriddenMethod(methodSymbol);
        foreach (var current in TypeHierarchyEnumeration.EnumerateBaseTypes(receiverType))
            foreach (var member in current.GetMembers(rootMethod.Name))
                if (member is IMethodSymbol candidate &&
                    candidate.Parameters.Length == rootMethod.Parameters.Length &&
                    (SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition,
                         rootMethod.OriginalDefinition) ||
                     OverridesMethod(candidate, rootMethod)))
                    return candidate.OriginalDefinition;

        return methodSymbol.IsAbstract ? null : methodSymbol.OriginalDefinition;
    }

    internal static PurityAnalysisEngine.PurityAnalysisResult CheckGetterPurity(
        IPropertySymbol propertySymbol,
        INamedTypeSymbol? receiverType,
        bool hasStableConcreteReceiver,
        IOperation operation,
        PurityAnalysisContext context,
        string ruleName)
    {
        var getter = ResolveGetter(
            propertySymbol,
            receiverType,
            hasStableConcreteReceiver,
            context.SemanticModel.Compilation);
        if (getter == null) return DynamicDispatch(operation, ruleName, propertySymbol.GetMethod);

        var getterPurity = PurityAnalysisEngine.GetCalleePurity(getter, context);
        return getterPurity.IsPure
            ? PurityAnalysisEngine.PurityAnalysisResult.Pure
            : getterPurity.WithCallee(getter, operation.Syntax);
    }

    internal static PurityAnalysisEngine.PurityAnalysisResult CheckMethodPurity(
        IMethodSymbol? methodSymbol,
        INamedTypeSymbol? receiverType,
        bool hasStableConcreteReceiver,
        IOperation operation,
        PurityAnalysisContext context,
        string ruleName)
    {
        if (methodSymbol == null) return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        var targetMethod = ResolveMethod(
            methodSymbol,
            receiverType,
            hasStableConcreteReceiver,
            context.SemanticModel.Compilation);
        if (targetMethod == null) return DynamicDispatch(operation, ruleName, methodSymbol);

        var methodPurity = PurityAnalysisEngine.GetCalleePurity(targetMethod, context);
        return methodPurity.IsPure
            ? PurityAnalysisEngine.PurityAnalysisResult.Pure
            : methodPurity.WithCallee(targetMethod, operation.Syntax);
    }

    private static PurityAnalysisEngine.PurityAnalysisResult DynamicDispatch(
        IOperation operation,
        string ruleName,
        ISymbol? symbol)
    {
        return PurityAnalysisEngine.PurityAnalysisResult.Impure(
            operation.Syntax,
            PurityAnalysisEngine.PurityEvidence.Create(
                "dynamic_dispatch",
                ruleName,
                operation,
                operation.Syntax,
                symbol));
    }

    internal static INamedTypeSymbol? GetKnownReceiverType(
        IOperation? instanceOperation,
        PurityAnalysisEngine.PurityAnalysisState currentState,
        Compilation compilation,
        out bool hasStableConcreteReceiver)
    {
        if (PurityConcreteReceiverResolver.TryResolveKnownConcreteType(instanceOperation, currentState, compilation,
                out var concreteType))
        {
            hasStableConcreteReceiver = true;
            return concreteType;
        }

        var receiverType = PurityAnalysisEngine.SkipImplicitConversions(instanceOperation)?.Type as INamedTypeSymbol;
        hasStableConcreteReceiver = receiverType != null &&
                                    (receiverType.TypeKind == TypeKind.Struct || receiverType.IsSealed);
        return receiverType;
    }

    internal static bool IsPotentiallyDispatchedGetter(IMethodSymbol getterSymbol, Compilation compilation)
    {
        if (getterSymbol.ContainingType?.TypeKind == TypeKind.Interface ||
            getterSymbol.IsAbstract)
            return true;

        if (!getterSymbol.IsVirtual && !getterSymbol.IsOverride) return false;

        if (GeneratedPurityCatalog.TryCanMetadataMethodBeOverridden(getterSymbol, compilation, out var canBeOverridden))
            return canBeOverridden;

        return !getterSymbol.IsSealed;
    }

    internal static bool IsPotentiallyDispatchedMethod(IMethodSymbol methodSymbol, Compilation compilation)
    {
        if (methodSymbol.ContainingType?.TypeKind == TypeKind.Interface ||
            methodSymbol.IsAbstract)
            return true;

        if (!methodSymbol.IsVirtual && !methodSymbol.IsOverride) return false;

        if (GeneratedPurityCatalog.TryCanMetadataMethodBeOverridden(methodSymbol, compilation, out var canBeOverridden))
            return canBeOverridden;

        return !methodSymbol.IsSealed;
    }

    internal static IPropertySymbol GetRootOverriddenProperty(IPropertySymbol propertySymbol)
    {
        var current = propertySymbol;
        while (current.OverriddenProperty != null) current = current.OverriddenProperty;

        return current.OriginalDefinition;
    }

    internal static IMethodSymbol GetRootOverriddenMethod(IMethodSymbol methodSymbol)
    {
        var current = methodSymbol;
        while (current.OverriddenMethod != null) current = current.OverriddenMethod;

        return current.OriginalDefinition;
    }

    internal static bool OverridesProperty(IPropertySymbol property, IPropertySymbol target)
    {
        for (var current = property; current != null; current = current.OverriddenProperty)
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, target.OriginalDefinition))
                return true;

        return false;
    }

    internal static bool OverridesMethod(IMethodSymbol method, IMethodSymbol target)
    {
        for (var current = method; current != null; current = current.OverriddenMethod)
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, target.OriginalDefinition))
                return true;

        return false;
    }

    internal static bool TryGetIEquatableEqualsImplementation(
        ITypeSymbol type,
        out IMethodSymbol implementation)
    {
        return TryGetInterfaceMethodImplementation(
            type,
            interfaceType => interfaceType.OriginalDefinition.ToDisplayString() == "System.IEquatable<T>" &&
                             interfaceType.TypeArguments.Length == 1 &&
                             SymbolEqualityComparer.Default.Equals(interfaceType.TypeArguments[0], type),
            nameof(IEquatable<object>.Equals),
            1,
            out implementation);
    }

    internal static bool TryGetIComparableCompareToImplementation(
        ITypeSymbol type,
        out IMethodSymbol implementation)
    {
        return TryGetInterfaceMethodImplementation(
            type,
            interfaceType => interfaceType.OriginalDefinition.ToDisplayString() == "System.IComparable<T>" &&
                             interfaceType.TypeArguments.Length == 1 &&
                             SymbolEqualityComparer.Default.Equals(interfaceType.TypeArguments[0], type),
            nameof(IComparable<object>.CompareTo),
            1,
            out implementation);
    }

    internal static bool TryGetIComparableObjectCompareToImplementation(
        ITypeSymbol type,
        out IMethodSymbol implementation)
    {
        return TryGetInterfaceMethodImplementation(
            type,
            interfaceType => interfaceType.ToDisplayString() == "System.IComparable",
            nameof(IComparable.CompareTo),
            1,
            out implementation);
    }

    private static bool TryGetInterfaceMethodImplementation(
        ITypeSymbol type,
        Func<INamedTypeSymbol, bool> matchesInterface,
        string memberName,
        int parameterCount,
        out IMethodSymbol implementation)
    {
        implementation = null!;

        if (type is not INamedTypeSymbol namedType) return false;

        implementation = TypeHierarchyEnumeration.EnumerateInterfaceMethodImplementations(
                namedType,
                memberName,
                matchesInterface,
                method => method.Parameters.Length == parameterCount,
                includeTypeSelf: false,
                includeUnimplementedInterfaceMember: false)
            .FirstOrDefault()!;
        return implementation != null;
    }

    internal static bool TryGetObjectOverride(
        ITypeSymbol type,
        string memberName,
        int parameterCount,
        out IMethodSymbol implementation)
    {
        implementation = null!;

        if (type is not INamedTypeSymbol namedType) return false;

        var foundImplementation = namedType
            .GetMembers(memberName)
            .OfType<IMethodSymbol>()
            .FirstOrDefault(method => method.IsOverride && method.Parameters.Length == parameterCount);
        if (foundImplementation == null) return false;

        implementation = foundImplementation;
        return true;
    }
}

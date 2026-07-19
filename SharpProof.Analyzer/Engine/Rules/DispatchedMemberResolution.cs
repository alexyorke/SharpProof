namespace SharpProof.Analyzer.Engine.Rules;

internal static class DispatchedMemberResolution
{
    internal static IMethodSymbol? ResolveGetter(
        IPropertySymbol propertySymbol,
        INamedTypeSymbol? receiverType,
        bool hasStableConcreteReceiver,
        Compilation compilation)
    {
        return propertySymbol.GetMethod is { } getter
            ? ResolveMethod(getter, receiverType, hasStableConcreteReceiver, compilation)
            : null;
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
        foreach (var candidate in TypeHierarchyEnumeration.EnumerateBaseTypeMembers<IMethodSymbol>(
                     receiverType,
                     rootMethod.Name))
            if (candidate.Parameters.Length == rootMethod.Parameters.Length &&
                (SymbolEq.AreEqual(candidate.OriginalDefinition, rootMethod.OriginalDefinition) ||
                 TypeHierarchyEnumeration.IsSameOrOverridesTargetMethod(candidate, rootMethod)))
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
        if (getter == null)
            return PurityAnalysisEngine.ImpureResult(
                operation,
                "dynamic_dispatch",
                ruleName,
                propertySymbol.GetMethod);

        return PurityCalleeResolver.GetCalleePurityAtUse(getter, operation.Syntax, context);
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
        if (targetMethod == null)
            return PurityAnalysisEngine.ImpureResult(
                operation,
                "dynamic_dispatch",
                ruleName,
                methodSymbol);

        return PurityCalleeResolver.GetCalleePurityAtUse(targetMethod, operation.Syntax, context);
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

    internal static IMethodSymbol GetRootOverriddenMethod(IMethodSymbol methodSymbol)
    {
        var current = methodSymbol;
        while (current.OverriddenMethod != null) current = current.OverriddenMethod;

        return current.OriginalDefinition;
    }

    internal static bool OverridesProperty(IPropertySymbol property, IPropertySymbol target)
    {
        for (var current = property; current != null; current = current.OverriddenProperty)
            if (SymbolEq.AreEqual(current.OriginalDefinition, target.OriginalDefinition))
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
                             SymbolEq.AreEqual(interfaceType.TypeArguments[0], type),
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
                             SymbolEq.AreEqual(interfaceType.TypeArguments[0], type),
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

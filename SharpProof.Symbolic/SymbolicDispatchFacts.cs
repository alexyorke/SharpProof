namespace SharpProof.Symbolic;
internal static class SymbolicDispatchFacts {
    public static bool ShouldTreatAsDynamicDispatch(IMethodSymbol methodSymbol, IOperation operation) {
        var receiver = GetReceiverOperation(operation);
        return ResolveExactDispatchTarget(methodSymbol, receiver) == null;
    }
    public static IMethodSymbol? ResolveExactDispatchTarget(
        IMethodSymbol methodSymbol,
        IOperation? receiver,
        INamedTypeSymbol? knownExactReceiverType = null) {
        var method = methodSymbol.OriginalDefinition;
        if (method.IsStatic ||
            method.MethodKind is MethodKind.Constructor or MethodKind.StaticConstructor ||
            !method.IsVirtual && !method.IsOverride && method.ContainingType?.TypeKind != TypeKind.Interface ||
            method.IsSealed || method.ContainingType?.IsSealed == true || IsBaseReference(receiver))
            return method;
        var exactType = knownExactReceiverType ?? GetSyntacticallyExactReceiverType(receiver);
        if (exactType == null || exactType.TypeKind == TypeKind.Interface || exactType.IsAbstract) return null;
        if (method.ContainingType?.TypeKind == TypeKind.Interface)
            return exactType.FindImplementationForInterfaceMember(method) as IMethodSymbol;
        for (var currentType = exactType; currentType != null; currentType = currentType.BaseType)
            foreach (var candidate in currentType.GetMembers(method.Name).OfType<IMethodSymbol>())
                if (Overrides(candidate, method))
                    return candidate;
        return null;
    }
    public static IOperation? GetReceiverOperation(IOperation operation) => operation switch {
        IInvocationOperation invocationOperation => UnwrapImplicitConversion(invocationOperation.Instance),
        IPropertyReferenceOperation propertyReferenceOperation => UnwrapImplicitConversion(propertyReferenceOperation.Instance),
        _ => null
    };
    private static INamedTypeSymbol? GetSyntacticallyExactReceiverType(IOperation? operation) {
        var receiver = UnwrapImplicitConversion(operation);
        return receiver switch {
            IObjectCreationOperation { Type: INamedTypeSymbol created } => created,
            { Type: INamedTypeSymbol { TypeKind: TypeKind.Struct } valueType } => valueType,
            { Type: INamedTypeSymbol { IsSealed: true } sealedType } => sealedType,
            _ => null
        };
    }
    private static bool Overrides(IMethodSymbol candidate, IMethodSymbol method) {
        for (var current = candidate; current != null; current = current.OverriddenMethod)
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, method.OriginalDefinition))
                return true;
        return false;
    }
    private static IOperation? UnwrapImplicitConversion(IOperation? operation) {
        var current = operation;
        while (current is IConversionOperation conversionOperation && conversionOperation.IsImplicit)
            current = conversionOperation.Operand;
        return current;
    }
    public static bool IsBaseReference(IOperation? operation) {
        var unwrappedOperation = UnwrapImplicitConversion(operation);
        return unwrappedOperation is IInstanceReferenceOperation instanceReferenceOperation &&
               instanceReferenceOperation.ReferenceKind == InstanceReferenceKind.ContainingTypeInstance &&
               unwrappedOperation.Syntax is BaseExpressionSyntax;
    }
}

namespace SharpProof.Symbolic;

internal static class SymbolicDispatchFacts {
    public static bool ShouldTreatAsDynamicDispatch(IMethodSymbol methodSymbol, IOperation operation) {
        var originalMethod = methodSymbol.OriginalDefinition;
        if (originalMethod.IsStatic ||
            originalMethod.MethodKind is MethodKind.Constructor or MethodKind.StaticConstructor ||
            IsBaseReference(GetReceiverOperation(operation)))
            return false;

        if (originalMethod.ContainingType?.TypeKind == TypeKind.Interface) return !HasExactReceiverType(operation);

        if (originalMethod.IsSealed ||
            originalMethod.ContainingType?.IsSealed == true)
            return false;

        if (originalMethod.IsAbstract) return true;

        if (!originalMethod.IsVirtual && !originalMethod.IsOverride) return false;

        return !HasExactReceiverType(operation);
    }

    public static IOperation? GetReceiverOperation(IOperation operation) {
        return operation switch {
            IInvocationOperation invocationOperation => UnwrapImplicitConversion(invocationOperation.Instance),
            IPropertyReferenceOperation propertyReferenceOperation => UnwrapImplicitConversion(
                propertyReferenceOperation.Instance),
            _ => null
        };
    }

    private static bool HasExactReceiverType(IOperation operation) {
        var receiver = GetReceiverOperation(operation);
        var receiverType = receiver?.Type as INamedTypeSymbol;
        return receiverType is { TypeKind: TypeKind.Struct } ||
               receiverType is { IsSealed: true };
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

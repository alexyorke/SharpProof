namespace SharpProof.Analyzer.Engine;

internal static class ConcreteReceiverResolver {
    internal static bool TryResolveExactConcreteType(
        IOperation? operation,
        SyntaxNode useNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out INamedTypeSymbol concreteType) {
        operation = Unwrap(operation);
        if (operation?.Syntax is ExpressionSyntax expression &&
            SymbolicRuntimeTypeFacts.TryGetExactRuntimeType(
                expression,
                useNode,
                semanticModel,
                cancellationToken,
                out var exactType) &&
            exactType is INamedTypeSymbol namedType) {
            concreteType = namedType;
            return true;
        }

        if (operation is IObjectCreationOperation { Type: INamedTypeSymbol createdType }) {
            concreteType = createdType;
            return true;
        }

        concreteType = null!;
        return false;
    }

    internal static IMethodSymbol? ResolveMethodTargetForConcreteReceiver(
        IMethodSymbol target,
        INamedTypeSymbol receiverType) {
        if (target.ContainingType?.TypeKind == TypeKind.Interface)
            return receiverType.FindImplementationForInterfaceMember(target) as IMethodSymbol ??
                   receiverType.FindImplementationForInterfaceMember(target.OriginalDefinition) as IMethodSymbol;

        var original = target.OriginalDefinition;
        if (!(original.IsVirtual || original.IsAbstract || original.IsOverride)) return original;
        for (var current = receiverType; current != null; current = current.BaseType)
            foreach (var candidate in current.GetMembers(original.Name).OfType<IMethodSymbol>())
                if (SymbolEq.AreEqual(candidate.OriginalDefinition, original) ||
                    TypeHierarchyEnumeration.OverridesTargetMethod(candidate, original))
                    return candidate;
        return original.IsAbstract ? null : original;
    }

    internal static IMethodSymbol? ResolvePropertyAccessorTargetForConcreteReceiver(
        IPropertySymbol property,
        INamedTypeSymbol receiverType,
        bool preferSetter) {
        var implementation = property.ContainingType?.TypeKind == TypeKind.Interface
            ? receiverType.FindImplementationForInterfaceMember(property) as IPropertySymbol
            : receiverType.GetMembers(property.Name).OfType<IPropertySymbol>().FirstOrDefault(candidate =>
                candidate.Parameters.Length == property.Parameters.Length);
        implementation ??= property;
        return preferSetter ? implementation.SetMethod : implementation.GetMethod;
    }

    private static IOperation? Unwrap(IOperation? operation) {
        while (operation is IConversionOperation { IsImplicit: true } conversion)
            operation = conversion.Operand;
        while (operation is IParenthesizedOperation parenthesized)
            operation = parenthesized.Operand;
        return operation;
    }
}

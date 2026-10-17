namespace SharpProof.Frontend;

internal static partial class CSharpOperationSemantics
{
    // Smaller integer implementations can return subtraction, not -1/0/1.
    internal static bool IsIntegerCompareTo(IMethodSymbol method)
    {
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        return !method.IsStatic && method.Name == "CompareTo" && method.DeclaringSyntaxReferences.IsEmpty &&
            method.ReturnType.SpecialType == SpecialType.System_Int32 && method.Parameters.Length == 1 &&
            method.Parameters[0].RefKind == RefKind.None &&
            method.ContainingType.SpecialType is SpecialType.System_Int32 or SpecialType.System_Int64 or
                SpecialType.System_UInt32 or SpecialType.System_UInt64 &&
            SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, method.ContainingType);
    }

    internal static bool IsIntegerComparerDeclaration(IMethodSymbol method)
    {
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        return !method.IsStatic && method.Name == "Compare" && method.Parameters.Length == 2 &&
            method.ReturnType.SpecialType == SpecialType.System_Int32 &&
            method.ContainingType.OriginalDefinition.GetDocumentationCommentId() == "T:System.Collections.Generic.Comparer`1" &&
            IsFrameworkCollectionMember(method) &&
            method.Parameters.All(parameter => parameter is null ? throw new System.ArgumentNullException(nameof(parameter)) : (parameter.RefKind == RefKind.None &&
                SymbolEqualityComparer.Default.Equals(parameter.Type, method.ContainingType.TypeArguments[0])));
    }

    internal static bool IsDefaultIntegerComparison(IInvocationOperation invocation)
    {
        invocation = RepositoryParameterGuard.NotNull(invocation, nameof(invocation));
        return IsIntegerComparerDeclaration(invocation.TargetMethod) &&
            invocation.Instance != null && IsDefaultIntegerComparer(invocation.Instance);
    }

    internal static bool IsDefaultIntegerComparer(IOperation operation)
    {
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
        return IsComparisonIntegerType(DefaultComparerElement(operation));
    }

    internal static bool IsComparisonIntegerType(ITypeSymbol? type)
    {
        return type?.SpecialType is SpecialType.System_Int32 or SpecialType.System_Int64 or
            SpecialType.System_UInt32 or SpecialType.System_UInt64;
    }

    internal static ITypeSymbol? DefaultComparerElement(IOperation operation)
    {
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
        return operation is IPropertyReferenceOperation
        {
            Instance: null, Arguments.Length: 0,
            Property: { IsStatic: true, Name: "Default", GetMethod: { } getter }
        } property && IsFrameworkCollectionMember(getter) &&
            property.Type is INamedTypeSymbol { TypeArguments.Length: 1 } type &&
            type.OriginalDefinition.GetDocumentationCommentId() == "T:System.Collections.Generic.Comparer`1" &&
            SymbolEqualityComparer.Default.Equals(property.Property.ContainingType, type) ? type.TypeArguments[0] : null;
    }
    internal static bool IsNonnegativeCollectionCount(IMethodSymbol method)
    {
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        return !method.IsStatic && !method.IsVirtual && method.Parameters.IsEmpty &&
            method.ReturnType.SpecialType == SpecialType.System_Int32 && IsFrameworkCollectionMember(method) &&
            method.OriginalDefinition.AssociatedSymbol?.GetDocumentationCommentId() is
                "P:System.Collections.Generic.List`1.Count" or "P:System.Collections.Generic.Dictionary`2.Count" or
                "P:System.Collections.Generic.Queue`1.Count" or "P:System.Collections.Generic.Stack`1.Count";
    }

    private static bool IsFrameworkCollectionMember(IMethodSymbol method)
    {
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        if (!method.DeclaringSyntaxReferences.IsEmpty)
        { return false; }
        var identity = method.ContainingAssembly.Identity;
        var token = string.Concat(identity.PublicKeyToken.Select(value => value.ToString("x2", CultureInfo.InvariantCulture)));
        return (identity.Name, token) is
            ("System.Private.CoreLib", "7cec85d7bea7798e") or ("mscorlib", "b77a5c561934e089") or
            ("System.Runtime", "b03f5f7f11d50a3a") or ("System.Collections", "b03f5f7f11d50a3a") or
            ("System", "b77a5c561934e089") or ("netstandard", "cc7b13ffcd2ddd51");
    }

    internal static IrTerm IntegerComparison(IrFactory factory, IrTerm left, IrTerm right)
    {
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
        left = RepositoryParameterGuard.NotNull(left, nameof(left));
        right = RepositoryParameterGuard.NotNull(right, nameof(right));
        return factory.Conditional(factory.Binary(IrBinaryOperator.LessThan, left, right), factory.Integer(-1),
            factory.Conditional(factory.Binary(IrBinaryOperator.GreaterThan, left, right), factory.Integer(1), factory.Integer(0)));
    }
}

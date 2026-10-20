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
            IsComparisonIntegerType(method.ContainingType) &&
            SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, method.ContainingType);
    }

    internal static bool HasRefComparableReceiver(IInvocationOperation invocation)
    {
        invocation = RepositoryParameterGuard.NotNull(invocation, nameof(invocation));
        return invocation.Syntax is Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax
        { Expression: Microsoft.CodeAnalysis.CSharp.Syntax.MemberAccessExpressionSyntax member } &&
            member.Expression.DescendantNodesAndSelf().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.RefExpressionSyntax>().Any();
    }

    internal static bool IsGenericComparableDeclaration(IMethodSymbol method)
    {
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        return !method.IsStatic && method.Parameters.Length == 1 && method.Parameters[0].RefKind == RefKind.None &&
            method.ReturnType.SpecialType == SpecialType.System_Int32 && IsFrameworkCollectionMember(method) &&
            method.OriginalDefinition.GetDocumentationCommentId() == "M:System.IComparable`1.CompareTo(`0)" &&
            method.ContainingType.TypeArguments.Length == 1 &&
            SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, method.ContainingType.TypeArguments[0]);
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
        return type != null && TryGetScalarInteger(type.SpecialType, out _);
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
    internal static bool IsDefaultComparerValueDeclaration(ISymbol symbol)
    {
        symbol = RepositoryParameterGuard.NotNull(symbol, nameof(symbol));
        var getter = symbol as IMethodSymbol ?? (symbol as IPropertySymbol)?.GetMethod;
        return getter is { IsStatic: true, Parameters.Length: 0, AssociatedSymbol: IPropertySymbol property } &&
            property.OriginalDefinition.GetDocumentationCommentId() == "P:System.Collections.Generic.Comparer`1.Default" &&
            IsFrameworkCollectionMember(getter) && getter.ContainingAssembly.Identity.Version.Major == 10 &&
            getter.ContainingType.BaseType?.SpecialType == SpecialType.System_Object &&
            SymbolEqualityComparer.Default.Equals(getter.ReturnType, getter.ContainingType);
    }

    internal static bool IsNonnegativeCollectionCount(IMethodSymbol method)
    {
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        return !method.IsStatic && !method.IsVirtual && method.Parameters.IsEmpty &&
            method.ReturnType.SpecialType == SpecialType.System_Int32 && IsFrameworkCollectionMember(method) &&
            method.OriginalDefinition.AssociatedSymbol?.GetDocumentationCommentId() is
                "P:System.Collections.Generic.List`1.Count" or
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
        // CoreLib's 8/16-bit CompareTo returns the promoted difference, not its sign.
        // Both operand domains fit Int32, so this subtraction cannot overflow.
        if (factory.GetTypeInfo(left.Type) is { Kind: IrTypeKind.Integer, Width: < 32 })
        { return factory.Binary(IrBinaryOperator.Subtract, factory.Cast(factory.IntegerType, left), factory.Cast(factory.IntegerType, right)); }
        return factory.Conditional(factory.Binary(IrBinaryOperator.LessThan, left, right), factory.Integer(-1),
            factory.Conditional(factory.Binary(IrBinaryOperator.GreaterThan, left, right), factory.Integer(1), factory.Integer(0)));
    }
}

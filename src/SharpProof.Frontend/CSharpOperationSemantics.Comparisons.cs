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
        return identity.Name switch
        {
            "System.Private.CoreLib" => identity.PublicKeyToken.SequenceEqual(CoreLibToken),
            "mscorlib" or "System" => identity.PublicKeyToken.SequenceEqual(EcmaToken),
            "System.Runtime" or "System.Collections" => identity.PublicKeyToken.SequenceEqual(MicrosoftToken),
            "netstandard" => identity.PublicKeyToken.SequenceEqual(NetStandardToken),
            _ => false
        };
    }

    // Public key tokens 7cec85d7bea7798e, b77a5c561934e089, b03f5f7f11d50a3a
    // and cc7b13ffcd2ddd51, compared as bytes so no hex text is built.
    private static readonly byte[] CoreLibToken = [0x7c, 0xec, 0x85, 0xd7, 0xbe, 0xa7, 0x79, 0x8e];
    private static readonly byte[] EcmaToken = [0xb7, 0x7a, 0x5c, 0x56, 0x19, 0x34, 0xe0, 0x89];
    private static readonly byte[] MicrosoftToken = [0xb0, 0x3f, 0x5f, 0x7f, 0x11, 0xd5, 0x0a, 0x3a];
    private static readonly byte[] NetStandardToken = [0xcc, 0x7b, 0x13, 0xff, 0xcd, 0x2d, 0xdd, 0x51];

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

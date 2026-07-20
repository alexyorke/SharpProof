namespace SharpProof.Analyzer;

internal static class RoslynStructuralMethodIdentity
{
    internal static StructuralMethodIdentity Create(IMethodSymbol method)
    {
        if (method == null) throw new ArgumentNullException(nameof(method));
        method = method.OriginalDefinition;

        return new StructuralMethodIdentity(
            GetMetadataTypeName(method.ContainingType),
            GetMethodKind(method),
            GetLogicalName(method),
            method.Arity,
            method.Parameters.Select(static parameter => new StructuralParameterIdentity(
                GetTypeKey(parameter.Type),
                GetRefKind(parameter.RefKind))),
            method.MethodKind is MethodKind.Constructor or MethodKind.StaticConstructor
                ? "named:System.Void"
                : GetTypeKey(method.ReturnType),
            method.MethodKind is MethodKind.Constructor or MethodKind.StaticConstructor
                ? StructuralRefKinds.None
                : method.ReturnsByRefReadonly
                    ? StructuralRefKinds.RefReadonly
                    : method.ReturnsByRef
                        ? StructuralRefKinds.Ref
                        : StructuralRefKinds.None);
    }

    internal static string GetCanonicalKey(IMethodSymbol method) =>
        Create(method).ToCanonicalKey();

    internal static ImmutableArray<string> GetCanonicalKeys(IMethodSymbol method) =>
        ImmutableArray.Create(GetCanonicalKey(method));

    internal static string GetTypeKey(ITypeSymbol type)
    {
        if (type == null) throw new ArgumentNullException(nameof(type));

        switch (type)
        {
            case IDynamicTypeSymbol:
                return "named:System.Object";
            case IArrayTypeSymbol array:
                return "array:" + array.Rank.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                       "[" + GetTypeKey(array.ElementType) + "]";
            case IPointerTypeSymbol pointer:
                return "pointer[" + GetTypeKey(pointer.PointedAtType) + "]";
            case IFunctionPointerTypeSymbol functionPointer:
                return GetFunctionPointerKey(functionPointer);
            case ITypeParameterSymbol typeParameter:
                return typeParameter.TypeParameterKind == TypeParameterKind.Method
                    ? "mparam:" + GetFlattenedMethodTypeParameterOrdinal(typeParameter)
                        .ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : "tparam:" + GetFlattenedTypeParameterOrdinal(typeParameter)
                        .ToString(System.Globalization.CultureInfo.InvariantCulture);
            case INamedTypeSymbol named when named.IsTupleType && named.TupleUnderlyingType != null:
                return GetTypeKey(named.TupleUnderlyingType);
            case INamedTypeSymbol named:
                return GetNamedTypeKey(named);
            default:
                return "unsupported:" + type.TypeKind.ToString().ToLowerInvariant();
        }
    }

    internal static string GetMetadataTypeName(INamedTypeSymbol type)
    {
        if (type == null) throw new ArgumentNullException(nameof(type));
        var definition = type.OriginalDefinition;
        if (definition.ContainingType != null)
            return GetMetadataTypeName(definition.ContainingType) + "+" + definition.MetadataName;

        var namespaceName = GetNamespaceName(definition.ContainingNamespace);
        return namespaceName.Length == 0
            ? definition.MetadataName
            : namespaceName + "." + definition.MetadataName;
    }

    private static string GetNamedTypeKey(INamedTypeSymbol type)
    {
        var definitionName = GetMetadataTypeName(type.OriginalDefinition);
        var typeArguments = GetFlattenedTypeArguments(type);
        if (typeArguments.IsDefaultOrEmpty) return "named:" + definitionName;

        return "named:" + definitionName + "[" +
               string.Join(";", typeArguments.Select(GetTypeKey)) + "]";
    }

    private static string GetFunctionPointerKey(IFunctionPointerTypeSymbol functionPointer)
    {
        var signature = functionPointer.Signature;
        var parameters = string.Join(
            ";",
            signature.Parameters.Select(static parameter =>
                GetRefKind(parameter.RefKind) + ":" + GetTypeKey(parameter.Type)));
        var returnRefKind = signature.ReturnsByRefReadonly
            ? StructuralRefKinds.RefReadonly
            : signature.ReturnsByRef
                ? StructuralRefKinds.Ref
                : StructuralRefKinds.None;
        return "fnptr:" + signature.CallingConvention.ToString().ToLowerInvariant() + "(" + parameters + ")->" +
               returnRefKind + ":" + GetTypeKey(signature.ReturnType);
    }

    private static ImmutableArray<ITypeSymbol> GetFlattenedTypeArguments(INamedTypeSymbol type)
    {
        if (!type.IsGenericType && type.ContainingType == null) return ImmutableArray<ITypeSymbol>.Empty;

        var builder = ImmutableArray.CreateBuilder<ITypeSymbol>();
        AppendFlattenedTypeArguments(type, builder);
        return builder.ToImmutable();
    }

    private static void AppendFlattenedTypeArguments(
        INamedTypeSymbol type,
        ImmutableArray<ITypeSymbol>.Builder builder)
    {
        if (type.ContainingType != null) AppendFlattenedTypeArguments(type.ContainingType, builder);
        foreach (var argument in type.TypeArguments) builder.Add(argument);
    }

    private static int GetFlattenedTypeParameterOrdinal(ITypeParameterSymbol parameter)
    {
        if (parameter.ContainingSymbol is not INamedTypeSymbol owner) return parameter.Ordinal;

        var offset = 0;
        for (var containing = owner.ContainingType; containing != null; containing = containing.ContainingType)
            offset += containing.Arity;
        return offset + parameter.Ordinal;
    }

    private static int GetFlattenedMethodTypeParameterOrdinal(ITypeParameterSymbol parameter)
    {
        var offset = parameter.Ordinal;
        for (var containing = parameter.ContainingSymbol?.ContainingSymbol;
             containing != null;
             containing = containing.ContainingSymbol)
            if (containing is IMethodSymbol containingMethod)
                offset += containingMethod.Arity;

        return offset;
    }

    private static string GetMethodKind(IMethodSymbol method)
    {
        return method.MethodKind switch
        {
            MethodKind.Constructor => "constructor",
            MethodKind.StaticConstructor => "static-constructor",
            MethodKind.PropertyGet => "property-get",
            MethodKind.PropertySet => "property-set",
            MethodKind.EventAdd => "event-add",
            MethodKind.EventRemove => "event-remove",
            MethodKind.UserDefinedOperator => "operator",
            MethodKind.Conversion => "conversion",
            MethodKind.Destructor => "destructor",
            MethodKind.LocalFunction => "local-function",
            MethodKind.AnonymousFunction => "anonymous-function",
            _ => "ordinary"
        };
    }

    private static string GetLogicalName(IMethodSymbol method)
    {
        string? name = method.MethodKind switch
        {
            MethodKind.Constructor => ".ctor",
            MethodKind.StaticConstructor => ".cctor",
            MethodKind.Destructor => "Finalize",
            MethodKind.PropertyGet or MethodKind.PropertySet or MethodKind.EventAdd or MethodKind.EventRemove =>
                method.AssociatedSymbol?.MetadataName,
            _ => method.MetadataName
        };
        if (!string.IsNullOrWhiteSpace(name)) return name!;

        if (!string.IsNullOrWhiteSpace(method.Name)) return method.Name;

        return method.MethodKind.ToString();
    }

    private static string GetRefKind(RefKind refKind)
    {
        return refKind switch
        {
            RefKind.Ref => StructuralRefKinds.Ref,
            RefKind.Out => StructuralRefKinds.Out,
            RefKind.In => StructuralRefKinds.In,
            RefKind.RefReadOnlyParameter => StructuralRefKinds.RefReadonly,
            _ => StructuralRefKinds.None
        };
    }

    private static string GetNamespaceName(INamespaceSymbol? namespaceSymbol)
    {
        if (namespaceSymbol == null || namespaceSymbol.IsGlobalNamespace) return string.Empty;

        var segments = new Stack<string>();
        for (var current = namespaceSymbol; current is { IsGlobalNamespace: false }; current = current.ContainingNamespace)
            segments.Push(current.Name);
        return string.Join(".", segments);
    }
}

namespace SharpProof.Symbolic;
internal static class SymbolicTypeFacts {
    internal static bool IsBuiltInIntegralType(ITypeSymbol? typeSymbol) => typeSymbol?.SpecialType is
            SpecialType.System_Char or
            SpecialType.System_SByte or
            SpecialType.System_Byte or
            SpecialType.System_Int16 or
            SpecialType.System_UInt16 or
            SpecialType.System_Int32 or
            SpecialType.System_UInt32 or
            SpecialType.System_Int64 or
            SpecialType.System_UInt64;
    internal static bool IsBuiltInNumericSpecialType(SpecialType type) => type is
        SpecialType.System_SByte or SpecialType.System_Byte or SpecialType.System_Int16 or
        SpecialType.System_UInt16 or SpecialType.System_Char or SpecialType.System_Int32 or
        SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_UInt64 or
        SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal;
    internal static bool IsBuiltInIntegralOrEnumType(ITypeSymbol? typeSymbol) =>
        IsBuiltInIntegralType(typeSymbol) || typeSymbol?.TypeKind == TypeKind.Enum;
    public static string? GetFullMetadataName(INamedTypeSymbol? type) {
        if (type == null) return null;
        var namespaceName = type.ContainingNamespace?.IsGlobalNamespace == false
            ? type.ContainingNamespace.ToDisplayString()
            : string.Empty;
        return string.IsNullOrEmpty(namespaceName)
            ? type.MetadataName
            : namespaceName + "." + type.MetadataName;
    }
    public static bool IsReferenceType(ITypeSymbol? typeSymbol) {
        if (typeSymbol == null) return false;
        if (typeSymbol is ITypeParameterSymbol typeParameter)
            return IsKnownReferenceTypeParameter(typeParameter, new HashSet<ITypeParameterSymbol>(SymbolEqualityComparer.Default));
        return typeSymbol.IsReferenceType;
    }
    public static bool IsReferenceLikeType(ITypeSymbol? typeSymbol) => typeSymbol?.TypeKind == TypeKind.Dynamic ||
               IsReferenceType(typeSymbol);
    public static bool IsSymbolicReferenceLikeType(ITypeSymbol? typeSymbol) => typeSymbol != null &&
               (IsReferenceLikeType(typeSymbol) ||
                IsBuiltInSpanOrMemoryType(typeSymbol) ||
                IsSupportedTupleCarrierType(typeSymbol));
    public static bool IsNullableType(ITypeSymbol? typeSymbol) => typeSymbol is INamedTypeSymbol {
        OriginalDefinition.SpecialType: SpecialType.System_Nullable_T
    };
    public static bool TryGetNullableUnderlyingType(ITypeSymbol? typeSymbol, out ITypeSymbol underlyingType) {
        if (typeSymbol is INamedTypeSymbol namedType &&
            namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
            namedType.TypeArguments.Length == 1) {
            underlyingType = namedType.TypeArguments[0];
            return true;
        }
        underlyingType = null!;
        return false;
    }
    public static bool IsDynamicExpression(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        Func<ExpressionSyntax, ExpressionSyntax> unwrapExpression) {
        expression = unwrapExpression(expression);
        var typeInfo = semanticModel.GetTypeInfo(expression, cancellationToken);
        return typeInfo.Type?.TypeKind == TypeKind.Dynamic ||
               typeInfo.ConvertedType?.TypeKind == TypeKind.Dynamic;
    }
    public static bool IsSystemRangeType(ITypeSymbol? typeSymbol) => typeSymbol is INamedTypeSymbol {
        Name: "Range",
        ContainingNamespace: { } containingNamespace
    } &&
               containingNamespace.ToDisplayString() == "System";
    public static bool IsSystemIndexType(ITypeSymbol? typeSymbol) => typeSymbol is INamedTypeSymbol {
        Name: "Index",
        ContainingNamespace: { } containingNamespace
    } &&
               containingNamespace.ToDisplayString() == "System";
    public static bool IsBuiltInSpanType(ITypeSymbol? typeSymbol) => typeSymbol is INamedTypeSymbol namedType &&
               namedType.OriginalDefinition.ToDisplayString() is "System.Span<T>" or "System.ReadOnlySpan<T>";
    public static bool IsCharArrayType(ITypeSymbol? typeSymbol) => typeSymbol is IArrayTypeSymbol {
        Rank: 1,
        ElementType.SpecialType: SpecialType.System_Char
    };
    public static bool IsReadOnlySpanOfCharType(ITypeSymbol? typeSymbol) => typeSymbol is INamedTypeSymbol namedType &&
               namedType.OriginalDefinition.ToDisplayString() == "System.ReadOnlySpan<T>" &&
               namedType.TypeArguments.Length == 1 &&
               namedType.TypeArguments[0].SpecialType == SpecialType.System_Char;
    public static bool IsBuiltInMemoryType(ITypeSymbol? typeSymbol) => typeSymbol is INamedTypeSymbol namedType &&
               namedType.OriginalDefinition.ToDisplayString() is "System.Memory<T>" or "System.ReadOnlyMemory<T>";
    public static bool IsBuiltInSpanOrMemoryType(ITypeSymbol? typeSymbol) => IsBuiltInSpanType(typeSymbol) ||
               IsBuiltInMemoryType(typeSymbol);
    public static bool IsNullableValueAccess(
        MemberAccessExpressionSyntax memberAccess,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) => memberAccess.Name.Identifier.ValueText == "Value" &&
               semanticModel.GetSymbolInfo(memberAccess, cancellationToken).Symbol is IPropertySymbol {
                   Name: "Value",
                   ContainingType.OriginalDefinition.SpecialType: SpecialType.System_Nullable_T
               };
    public static bool IsThrowingDivideByZeroType(ITypeSymbol? typeSymbol) => typeSymbol?.SpecialType is
        SpecialType.System_Byte or SpecialType.System_SByte or SpecialType.System_Int16 or
        SpecialType.System_UInt16 or SpecialType.System_Int32 or SpecialType.System_UInt32 or
        SpecialType.System_Int64 or SpecialType.System_UInt64 or SpecialType.System_Decimal ||
        IsBigIntegerType(typeSymbol);
    /// <summary>
    /// The single owner of the BigInteger check. Matched on namespace and name rather
    /// than a display string so it holds for any <see cref="ITypeSymbol" />.
    /// </summary>
    internal static bool IsBigIntegerType(ITypeSymbol? typeSymbol) =>
        typeSymbol != null &&
        string.Equals(typeSymbol.ContainingNamespace?.ToDisplayString(), "System.Numerics", StringComparison.Ordinal) &&
        string.Equals(typeSymbol.Name, "BigInteger", StringComparison.Ordinal);
    public static bool TryGetCheckedIntegralRange(ITypeSymbol? typeSymbol, out long minValue, out long maxValue) {
        if (typeSymbol?.SpecialType is not (SpecialType.System_Int32 or
            SpecialType.System_UInt32 or
            SpecialType.System_Int64)) {
            minValue = default;
            maxValue = default;
            return false;
        }
        return TryGetCheckedNumericConversionRange(typeSymbol, out minValue, out maxValue);
    }
    public static bool TryGetBoundedIntegralRange(ITypeSymbol? typeSymbol, out long minValue, out long maxValue)
        => TryGetCheckedNumericConversionRange(typeSymbol, out minValue, out maxValue);
    public static bool TryGetCheckedNumericConversionRange(ITypeSymbol? typeSymbol, out long minValue, out long maxValue) {
        (minValue, maxValue) = typeSymbol?.SpecialType switch {
            SpecialType.System_Char => (char.MinValue, char.MaxValue),
            SpecialType.System_SByte => (sbyte.MinValue, sbyte.MaxValue),
            SpecialType.System_Byte => (byte.MinValue, byte.MaxValue),
            SpecialType.System_Int16 => (short.MinValue, short.MaxValue),
            SpecialType.System_UInt16 => (ushort.MinValue, ushort.MaxValue),
            SpecialType.System_Int32 => (int.MinValue, int.MaxValue),
            SpecialType.System_UInt32 => (uint.MinValue, uint.MaxValue),
            SpecialType.System_Int64 => (long.MinValue, long.MaxValue),
            _ => (default, default)
        };
        return typeSymbol?.SpecialType is SpecialType.System_Char or SpecialType.System_SByte or
            SpecialType.System_Byte or SpecialType.System_Int16 or SpecialType.System_UInt16 or
            SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Int64;
    }
    private static bool IsKnownReferenceTypeParameter(ITypeParameterSymbol typeParameter, HashSet<ITypeParameterSymbol> visited) {
        if (!visited.Add(typeParameter)) return false;
        if (typeParameter.HasReferenceTypeConstraint) return true;
        foreach (var constraint in typeParameter.ConstraintTypes) {
            if (constraint.IsReferenceType) return true;
            if (constraint is ITypeParameterSymbol nestedTypeParameter &&
                IsKnownReferenceTypeParameter(nestedTypeParameter, visited))
                return true;
        }
        return false;
    }
    public static bool HasInstanceInt32Member(ITypeSymbol? typeSymbol, string memberName) {
        if (typeSymbol == null) return false;
        for (var current = typeSymbol; current != null; current = (current as INamedTypeSymbol)?.BaseType)
            if (HasDeclaredInstanceInt32Member(current, memberName))
                return true;
        foreach (var interfaceType in typeSymbol.AllInterfaces)
            if (HasDeclaredInstanceInt32Member(interfaceType, memberName))
                return true;
        return false;
    }
    public static bool IsKnownNonNegativeCollectionCountProperty(
        IPropertySymbol propertySymbol,
        ITypeSymbol? receiverType,
        Compilation compilation) {
        if (receiverType == null ||
            propertySymbol is not
            {
                Name: "Count",
                IsStatic: false,
                Parameters.Length: 0,
                Type.SpecialType: SpecialType.System_Int32
            })
            return false;
        foreach (var interfaceType in EnumerateKnownNonNegativeCountInterfaces(receiverType, compilation))
            foreach (var interfaceCount in interfaceType.GetMembers("Count").OfType<IPropertySymbol>()) {
                if (interfaceCount is not
                    {
                        IsStatic: false,
                        Parameters.Length: 0,
                        Type.SpecialType: SpecialType.System_Int32
                    })
                    continue;
                if (SymbolEqualityComparer.Default.Equals(propertySymbol, interfaceCount)) return true;
                if (receiverType is INamedTypeSymbol namedReceiver &&
                    namedReceiver.FindImplementationForInterfaceMember(interfaceCount) is { } implementation &&
                    implementation.DeclaringSyntaxReferences.Length == 0 &&
                    SymbolEqualityComparer.Default.Equals(propertySymbol, implementation))
                    return true;
            }
        return false;
    }
    private static IEnumerable<INamedTypeSymbol> EnumerateKnownNonNegativeCountInterfaces(ITypeSymbol receiverType,
        Compilation compilation) {
        if (receiverType is INamedTypeSymbol namedReceiver &&
            IsKnownNonNegativeCountInterface(namedReceiver, compilation))
            yield return namedReceiver;
        foreach (var interfaceType in receiverType.AllInterfaces)
            if (IsKnownNonNegativeCountInterface(interfaceType, compilation))
                yield return interfaceType;
    }
    private static bool IsKnownNonNegativeCountInterface(INamedTypeSymbol typeSymbol, Compilation compilation)
        => IsSameOriginalType(typeSymbol, compilation.GetTypeByMetadataName("System.Collections.ICollection")) ||
               IsSameOriginalType(typeSymbol, compilation.GetTypeByMetadataName("System.Collections.Generic.ICollection`1")) ||
               IsSameOriginalType(typeSymbol, compilation.GetTypeByMetadataName("System.Collections.Generic.IReadOnlyCollection`1"));
    private static bool IsSameOriginalType(INamedTypeSymbol candidate, INamedTypeSymbol? target) => target != null &&
               SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, target);
    public static bool HasDeclaredInstanceInt32Member(ITypeSymbol typeSymbol, string memberName) =>
        typeSymbol.GetMembers(memberName).Any(static member => !member.IsStatic && member is
            IPropertySymbol { Parameters.Length: 0, Type.SpecialType: SpecialType.System_Int32 } or
            IFieldSymbol { Type.SpecialType: SpecialType.System_Int32 });
    public static bool HasInt32Indexer(ITypeSymbol? typeSymbol) {
        if (typeSymbol == null) return false;
        for (var current = typeSymbol; current != null; current = (current as INamedTypeSymbol)?.BaseType)
            if (HasDeclaredInt32Indexer(current))
                return true;
        foreach (var interfaceType in typeSymbol.AllInterfaces)
            if (HasDeclaredInt32Indexer(interfaceType))
                return true;
        return false;
    }
    public static bool HasDeclaredInt32Indexer(ITypeSymbol typeSymbol) {
        foreach (var property in typeSymbol.GetMembers().OfType<IPropertySymbol>())
            if (property is { IsIndexer: true, IsStatic: false, Parameters.Length: 1 } &&
                property.Parameters[0].Type.SpecialType == SpecialType.System_Int32)
                return true;
        return false;
    }
    public static bool TryGetTuplePositionalField(ITypeSymbol? receiverType, int position, out IFieldSymbol fieldSymbol) {
        fieldSymbol = null!;
        if (receiverType is not INamedTypeSymbol namedType) return false;
        if (namedType.IsTupleType) {
            if (position < 0 || position >= namedType.TupleElements.Length) return false;
            fieldSymbol = namedType.TupleElements[position];
            return true;
        }
        var storageName = "Item" + (position + 1).ToString(CultureInfo.InvariantCulture);
        fieldSymbol = namedType
            .GetMembers(storageName)
            .OfType<IFieldSymbol>()
            .FirstOrDefault(static field => !field.IsStatic)!;
        return fieldSymbol != null;
    }
    public static bool IsSupportedTupleCarrierType(ITypeSymbol type) {
        if (type is not INamedTypeSymbol namedType) return false;
        if (namedType.IsTupleType && namedType.TupleElements.Length > 0) return true;
        return namedType
            .GetMembers()
            .OfType<IFieldSymbol>()
            .Any(static field => !field.IsStatic && IsTupleElementStorageName(field.Name));
    }
    public static bool IsTupleElementStorageName(string name) => name.Length > 4 &&
               name.StartsWith("Item", StringComparison.Ordinal) &&
               name.Skip(4).All(char.IsDigit);
}

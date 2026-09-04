namespace SharpProof.Frontend;

public static class CompilerIdentityBridge
{
    public static IrIdentityId InternSymbol(
        IrFactory factory,
        ISymbol symbol)
    {
        factory = ArgumentNullGuard.NotNull(factory, nameof(factory));
        symbol = ArgumentNullGuard.NotNull(symbol, nameof(symbol));

        return factory.InternExternalIdentity<ISymbol>(
            symbol,
            SymbolEqualityComparer.Default);
    }

    public static IrIdentityId InternType(
        IrFactory factory,
        ITypeSymbol type)
    {
        return InternSymbol(factory, type);
    }

    internal static IrIdentityId InternOperation(
        IrFactory factory,
        IOperation operation,
        ISymbol? symbol,
        bool isPure)
    {
        factory = ArgumentNullGuard.NotNull(factory, nameof(factory));
        operation = ArgumentNullGuard.NotNull(operation, nameof(operation));

        if (symbol != null)
        {
            return InternSymbol(factory, symbol);
        }

        if (isPure)
        {
            return factory.InternExternalIdentity(
                CreateSemanticOperationIdentity(factory, operation),
                EqualityComparer<OperationSemanticIdentity>.Default);
        }

        return factory.InternExternalIdentity(
            operation,
            ReferenceComparer<IOperation>.Instance);
    }

    internal static bool IsIntrinsicSequenceLength(
        IPropertyReferenceOperation property)
    {
        if (property.Instance == null ||
            !property.Arguments.IsDefaultOrEmpty)
        {
            return false;
        }

        var definition = property.Property.OriginalDefinition;
        if (definition is not
            {
                IsStatic: false,
                IsIndexer: false,
                GetMethod: not null,
                SetMethod: null,
                Parameters.IsEmpty: true
            })
        {
            return false;
        }

        if (property.Instance.Type?.SpecialType ==
            SpecialType.System_String)
        {
            return definition.ContainingType.SpecialType ==
                    SpecialType.System_String &&
                definition.MetadataName == "Length" &&
                definition.Type.SpecialType == SpecialType.System_Int32;
        }

        if (property.Instance.Type is not IArrayTypeSymbol)
        {
            return false;
        }

        return definition.ContainingType.SpecialType ==
            SpecialType.System_Array &&
            (definition.MetadataName == "Length" &&
             definition.Type.SpecialType == SpecialType.System_Int32 ||
             definition.MetadataName == "LongLength" &&
             definition.Type.SpecialType == SpecialType.System_Int64);
    }

    internal static bool IsSupportedValueDomain(ITypeSymbol? type)
    {
        if (type is IArrayTypeSymbol array)
        {
            return IsSupportedValueDomain(array.ElementType);
        }
        if (type == null || type.TypeKind == TypeKind.Error)
        {
            return false;
        }
        if (type.TypeKind is TypeKind.Pointer or
            TypeKind.FunctionPointer or TypeKind.TypeParameter)
        {
            return false;
        }
        if (type.IsReferenceType)
        {
            return true;
        }
        return type.SpecialType == SpecialType.System_Boolean ||
            CSharpScalarSemantics.IsSupportedInteger(type.SpecialType);
    }

    internal static ITypeSymbol? GetNullableUnderlyingType(ITypeSymbol? type)
    {
        return type is INamedTypeSymbol
        {
            OriginalDefinition.SpecialType: SpecialType.System_Nullable_T,
            TypeArguments.Length: 1
        } nullable
            ? nullable.TypeArguments[0]
            : null;
    }

    private static OperationSemanticIdentity CreateSemanticOperationIdentity(
        IrFactory factory,
        IOperation operation)
    {
        return new(
            operation.Kind,
            operation.Type == null
                ? default
                : InternType(factory, operation.Type),
            operation switch
            {
                ITypeOfOperation typeOf =>
                    InternType(factory, typeOf.TypeOperand),
                ISizeOfOperation sizeOf =>
                    InternType(factory, sizeOf.TypeOperand),
                _ => default
            },
            (operation as IBinaryOperation)?.OperatorKind,
            (operation as IUnaryOperation)?.OperatorKind,
            (operation as IInstanceReferenceOperation)?.ReferenceKind,
            CompilerIdentityProjections.IsChecked(operation),
            CompilerIdentityProjections.IsLifted(operation),
            CompilerIdentityProjections.IsTryCast(operation),
            UnsupportedConstantIdentity(operation));
    }

    private static string? UnsupportedConstantIdentity(IOperation operation)
    {
        // Unsupported constants still participate in pure opaque-term
        // interning. Preserve their payload so distinct values cannot become
        // the same semantic term merely because their CLR types match.
        if (!operation.ConstantValue.HasValue)
        {
            return null;
        }
        return operation.ConstantValue.Value switch
        {
            null => "<null>",
            IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
            object value => value.ToString()
        };
    }

    public static string CreateSymbolDisplay(ISymbol? symbol)
    {
        if (symbol == null)
        {
            return "<operation>";
        }

        return AssemblyIdentity(symbol.ContainingAssembly) + "::" +
            SymbolReference(symbol);
    }

    public static string CreateTypeDisplay(ITypeSymbol? type)
    {
        return type == null
            ? "<no-type>"
            : AssemblyIdentity(type.ContainingAssembly) + "::" +
              TypeReference(type);
    }

    private static string AssemblyIdentity(IAssemblySymbol? assembly)
    {
        return assembly?.Identity.ToString() ?? "<no-assembly>";
    }

    private static string SymbolReference(ISymbol symbol)
    {
        return CreateReference(
            symbol,
            DocumentationCommentId.CreateDeclarationId);
    }

    private static string TypeReference(ITypeSymbol type)
    {
        return CreateReference(
            type,
            DocumentationCommentId.CreateReferenceId);
    }

    private static string CreateReference<T>(
        T symbol,
        Func<T, string?> createId)
        where T : ISymbol
    {
        return createId(symbol) is { Length: > 0 } id
            ? id
            : FallbackReference(symbol);
    }

    private static string FallbackReference(ISymbol symbol)
    {
        var owner = symbol.ContainingSymbol;
        var prefix = owner switch
        {
            null => string.Empty,
            IAssemblySymbol assembly => assembly.Identity.ToString(),
            ITypeSymbol type => TypeReference(type),
            _ => SymbolReference(owner)
        };
        return prefix + "/" + symbol.Kind + ":" + symbol.MetadataName;
    }

    private readonly struct OperationSemanticIdentity(
        OperationKind kind, IrIdentityId type, IrIdentityId typeOperand,
        BinaryOperatorKind? binaryOperator,
        UnaryOperatorKind? unaryOperator,
        InstanceReferenceKind? instanceReference,
        bool isChecked, bool isLifted, bool isTryCast, string? constantIdentity)
        : IEquatable<OperationSemanticIdentity>
    {
        private readonly (
            OperationKind, IrIdentityId, IrIdentityId,
            BinaryOperatorKind?, UnaryOperatorKind?,
            InstanceReferenceKind?, bool, bool, bool, string?) _value =
            (kind, type, typeOperand, binaryOperator, unaryOperator,
             instanceReference, isChecked, isLifted, isTryCast, constantIdentity);

        public bool Equals(OperationSemanticIdentity other)
        {
            return _value.Equals(other._value);
        }

        public override bool Equals(object? obj)
        {
            return obj is OperationSemanticIdentity other && Equals(other);
        }

        public override int GetHashCode()
        {
            return _value.GetHashCode();
        }
    }
}

internal sealed class ReferenceComparer<T> : IEqualityComparer<T>
    where T : class
{
    internal static ReferenceComparer<T> Instance { get; } = new();

    public bool Equals(T? x, T? y)
    {
        return ReferenceEquals(x, y);
    }

    public int GetHashCode(T obj)
    {
        return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}

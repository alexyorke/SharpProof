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
                OperationSemanticIdentityComparer);
        }

        return factory.InternExternalIdentity(
            operation,
            OperationReferenceComparer.Instance);
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
        if (definition is
            {
                IsStatic: false,
                IsIndexer: false,
                GetMethod: not null,
                SetMethod: null,
                Parameters.IsEmpty: true
            } &&
            property.Instance.Type?.SpecialType ==
                SpecialType.System_String)
        {
            return definition.ContainingType.SpecialType ==
                    SpecialType.System_String &&
                definition.MetadataName == "Length" &&
                definition.Type.SpecialType == SpecialType.System_Int32;
        }

        if (property.Instance.Type is not IArrayTypeSymbol { Rank: 1 })
        {
            return false;
        }

        return definition is
        {
            IsStatic: false,
            IsIndexer: false,
            GetMethod: not null,
            SetMethod: null,
            Parameters.IsEmpty: true,
            ContainingType.SpecialType: SpecialType.System_Array
        } &&
            (definition.MetadataName == "Length" &&
             definition.Type.SpecialType == SpecialType.System_Int32 ||
             definition.MetadataName == "LongLength" &&
             definition.Type.SpecialType == SpecialType.System_Int64);
    }

    internal static bool IsSupportedValueDomain(ITypeSymbol? type)
    {
        if (type is IArrayTypeSymbol array)
        {
            return array.Rank == 1 && IsSupportedValueDomain(array.ElementType);
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

    private static readonly IEqualityComparer<OperationSemanticIdentity>
        OperationSemanticIdentityComparer =
            EqualityComparer<OperationSemanticIdentity>.Default;

    private static OperationSemanticIdentity CreateSemanticOperationIdentity(
        IrFactory factory,
        IOperation operation)
    {
        return new(
            operation.Kind,
            operation.Type == null
                ? default
                : InternType(factory, operation.Type),
            (operation as IBinaryOperation)?.OperatorKind,
            (operation as IUnaryOperation)?.OperatorKind,
            (operation as IInstanceReferenceOperation)?.ReferenceKind,
            CompilerIdentityProjections.IsChecked(operation),
            CompilerIdentityProjections.IsLifted(operation));
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
        return DocumentationCommentId.CreateDeclarationId(symbol) is { Length: > 0 } id
            ? id
            : FallbackReference(symbol);
    }

    private static string TypeReference(ITypeSymbol type)
    {
        return DocumentationCommentId.CreateReferenceId(type) is { Length: > 0 } id
            ? id
            : FallbackReference(type);
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

    private sealed class OperationReferenceComparer : IEqualityComparer<IOperation>
    {
        internal static OperationReferenceComparer Instance { get; } = new();

        public bool Equals(IOperation? left, IOperation? right)
        {
            return ReferenceEquals(left, right);
        }

        public int GetHashCode(IOperation operation)
        {
            return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(
                operation);
        }
    }

    private readonly struct OperationSemanticIdentity(
        OperationKind kind, IrIdentityId type,
        BinaryOperatorKind? binaryOperator,
        UnaryOperatorKind? unaryOperator,
        InstanceReferenceKind? instanceReference,
        bool isChecked, bool isLifted)
        : IEquatable<OperationSemanticIdentity>
    {
        private readonly (
            OperationKind, IrIdentityId, BinaryOperatorKind?,
            UnaryOperatorKind?, InstanceReferenceKind?, bool, bool) _value =
            (kind, type, binaryOperator, unaryOperator, instanceReference,
             isChecked, isLifted);

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

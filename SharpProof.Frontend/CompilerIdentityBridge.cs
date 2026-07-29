namespace SharpProof.Frontend;

public static class CompilerIdentityBridge
{
    public static IrIdentityId InternSymbol(
        IrFactory factory,
        ISymbol symbol)
    {
        if (factory == null)
        {
            throw new ArgumentNullException(nameof(factory));
        }

        if (symbol == null)
        {
            throw new ArgumentNullException(nameof(symbol));
        }

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
        if (factory == null)
        {
            throw new ArgumentNullException(nameof(factory));
        }

        if (operation == null)
        {
            throw new ArgumentNullException(nameof(operation));
        }

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
            operation switch
            {
                IBinaryOperation binary => binary.IsChecked,
                IUnaryOperation unary => unary.IsChecked,
                IConversionOperation conversion => conversion.IsChecked,
                _ => false
            },
            operation switch
            {
                IBinaryOperation binary => binary.IsLifted,
                IUnaryOperation unary => unary.IsLifted,
                _ => false
            });
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

namespace SharpProof.Frontend;

public static class CompilerIdentityBridge {
    public static IrIdentityId InternSymbol(
        IrFactory factory,
        ISymbol symbol) {
        if (factory == null) throw new ArgumentNullException(nameof(factory));
        if (symbol == null) throw new ArgumentNullException(nameof(symbol));
        return factory.InternExternalIdentity<ISymbol>(
            symbol,
            SymbolEqualityComparer.Default);
    }

    public static IrIdentityId InternType(
        IrFactory factory,
        ITypeSymbol type) =>
        InternSymbol(factory, type);

    internal static IrIdentityId InternOperation(
        IrFactory factory,
        IOperation operation,
        ISymbol? symbol,
        bool isPure) {
        if (factory == null) throw new ArgumentNullException(nameof(factory));
        if (operation == null) throw new ArgumentNullException(nameof(operation));
        if (symbol != null) return InternSymbol(factory, symbol);
        if (isPure)
            return factory.InternExternalIdentity(
                CreateSemanticOperationIdentity(factory, operation),
                OperationSemanticIdentityComparer);
        return factory.InternExternalIdentity(
            operation,
            OperationReferenceComparer.Instance);
    }

    private static readonly IEqualityComparer<OperationSemanticIdentity>
        OperationSemanticIdentityComparer =
            EqualityComparer<OperationSemanticIdentity>.Default;

    private static OperationSemanticIdentity CreateSemanticOperationIdentity(
        IrFactory factory,
        IOperation operation) =>
        new(
            operation.Kind,
            operation.Type == null
                ? default
                : InternType(factory, operation.Type),
            operation switch {
                IBinaryOperation binary => (int)binary.OperatorKind,
                IUnaryOperation unary => (int)unary.OperatorKind,
                IInstanceReferenceOperation instance =>
                    (int)instance.ReferenceKind,
                _ => 0
            },
            operation switch {
                IBinaryOperation binary => binary.IsChecked,
                IUnaryOperation unary => unary.IsChecked,
                IConversionOperation conversion => conversion.IsChecked,
                _ => false
            },
            operation switch {
                IBinaryOperation binary => binary.IsLifted,
                IUnaryOperation unary => unary.IsLifted,
                _ => false
            });

    public static string CreateSymbolDisplay(ISymbol? symbol) {
        if (symbol == null) return "<operation>";
        var builder = new StringBuilder();
        AppendAssembly(builder, symbol.ContainingAssembly);
        builder.Append("::");
        AppendContainingSymbol(builder, symbol.ContainingSymbol);
        builder.Append('/');
        AppendSymbol(builder, symbol);
        return builder.ToString();
    }

    public static string CreateTypeDisplay(ITypeSymbol? type) {
        if (type == null) return "<no-type>";
        var builder = new StringBuilder();
        AppendType(builder, type);
        return builder.ToString();
    }

    private static void AppendAssembly(StringBuilder builder, IAssemblySymbol? assembly) {
        if (assembly == null) {
            builder.Append("<no-assembly>");
            return;
        }
        builder.Append(assembly.Identity.Name);
        builder.Append(',');
        builder.Append(assembly.Identity.Version);
        builder.Append(',');
        builder.Append(assembly.Identity.CultureName ?? "neutral");
        builder.Append(',');
        var key = assembly.Identity.PublicKeyToken;
        if (key.IsDefaultOrEmpty) {
            builder.Append("null");
        }
        else {
            foreach (var value in key)
                builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
        }
    }

    private static void AppendContainingSymbol(StringBuilder builder, ISymbol? symbol) {
        if (symbol == null) return;
        if (symbol is INamespaceSymbol namespaceSymbol) {
            AppendNamespace(builder, namespaceSymbol);
            return;
        }
        AppendContainingSymbol(builder, symbol.ContainingSymbol);
        if (builder.Length != 0 && builder[builder.Length - 1] != ':') builder.Append('.');
        AppendSymbol(builder, symbol);
    }

    private static void AppendNamespace(StringBuilder builder, INamespaceSymbol value) {
        if (value.IsGlobalNamespace) return;
        AppendNamespace(builder, value.ContainingNamespace);
        if (builder.Length != 0 && builder[builder.Length - 1] != ':') builder.Append('.');
        builder.Append(value.MetadataName);
    }

    private static void AppendSymbol(StringBuilder builder, ISymbol symbol) {
        builder.Append(symbol.Kind);
        builder.Append(':');
        builder.Append(symbol.MetadataName);
        switch (symbol) {
            case INamedTypeSymbol namedType:
                builder.Append('`');
                builder.Append(namedType.Arity.ToString(CultureInfo.InvariantCulture));
                break;
            case IMethodSymbol method:
                builder.Append('`');
                builder.Append(method.Arity.ToString(CultureInfo.InvariantCulture));
                AppendParameters(builder, method.Parameters);
                break;
            case IPropertySymbol property:
                AppendParameters(builder, property.Parameters);
                break;
        }
    }

    private static void AppendParameters(
        StringBuilder builder,
        ImmutableArray<IParameterSymbol> parameters) {
        builder.Append('(');
        for (var index = 0; index < parameters.Length; index++) {
            if (index != 0) builder.Append(',');
            builder.Append(parameters[index].RefKind);
            builder.Append(':');
            AppendType(builder, parameters[index].Type);
        }
        builder.Append(')');
    }

    private static void AppendType(StringBuilder builder, ITypeSymbol type) {
        switch (type) {
            case IArrayTypeSymbol array:
                AppendType(builder, array.ElementType);
                builder.Append('[');
                builder.Append(',', array.Rank - 1);
                builder.Append(']');
                return;
            case IPointerTypeSymbol pointer:
                AppendType(builder, pointer.PointedAtType);
                builder.Append('*');
                return;
            case ITypeParameterSymbol parameter:
                builder.Append('!');
                builder.Append(parameter.TypeParameterKind);
                builder.Append(':');
                builder.Append(parameter.Ordinal.ToString(CultureInfo.InvariantCulture));
                return;
            case INamedTypeSymbol named:
                AppendContainingSymbol(builder, named.ContainingSymbol);
                if (builder.Length != 0 && builder[builder.Length - 1] != ':') builder.Append('.');
                builder.Append(named.MetadataName);
                if (!named.TypeArguments.IsDefaultOrEmpty) {
                    builder.Append('<');
                    for (var index = 0; index < named.TypeArguments.Length; index++) {
                        if (index != 0) builder.Append(',');
                        AppendType(builder, named.TypeArguments[index]);
                    }
                    builder.Append('>');
                }
                return;
            default:
                builder.Append(type.TypeKind);
                builder.Append(':');
                builder.Append(type.MetadataName);
                return;
        }
    }

    private sealed class OperationReferenceComparer : IEqualityComparer<IOperation> {
        internal static OperationReferenceComparer Instance { get; } = new();

        public bool Equals(IOperation? left, IOperation? right) =>
            ReferenceEquals(left, right);

        public int GetHashCode(IOperation operation) =>
            System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(
                operation);
    }

    private readonly struct OperationSemanticIdentity(
        OperationKind kind, IrIdentityId type, int variant,
        bool isChecked, bool isLifted)
        : IEquatable<OperationSemanticIdentity> {
        private OperationKind Kind { get; } = kind;
        private IrIdentityId Type { get; } = type;
        private int Variant { get; } = variant;
        private bool IsChecked { get; } = isChecked;
        private bool IsLifted { get; } = isLifted;

        public bool Equals(OperationSemanticIdentity other) =>
            Kind == other.Kind &&
            Type == other.Type &&
            Variant == other.Variant &&
            IsChecked == other.IsChecked &&
            IsLifted == other.IsLifted;

        public override bool Equals(object? obj) =>
            obj is OperationSemanticIdentity other && Equals(other);

        public override int GetHashCode() {
            unchecked {
                var hash = (int)Kind;
                hash = hash * 397 ^ Type.GetHashCode();
                hash = hash * 397 ^ Variant;
                hash = hash * 397 ^ (IsChecked ? 1 : 0);
                return hash * 397 ^ (IsLifted ? 1 : 0);
            }
        }
    }
}

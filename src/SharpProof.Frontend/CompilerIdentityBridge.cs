namespace SharpProof.Frontend;

internal static class CompilerIdentityBridge
{
    internal static bool IsClosedSealedReferenceType(ITypeSymbol type, CancellationToken cancellationToken)
    {
        if (type is not INamedTypeSymbol { TypeKind: TypeKind.Class, IsSealed: true, IsStatic: false, SpecialType: SpecialType.None })
        { return false; }
        return IsClosedReferenceType(type, cancellationToken);
    }

    internal static bool IsClosedReferenceType(ITypeSymbol type, CancellationToken cancellationToken)
    {
        if (type is not INamedTypeSymbol { TypeKind: TypeKind.Class, IsStatic: false, SpecialType: SpecialType.None })
        { return false; }
        var remainingWork = 4096;
        var visited = new Dictionary<ITypeSymbol, bool>(SymbolEqualityComparer.Default);
        bool Closed(ITypeSymbol current, int depth)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (current.TypeKind == TypeKind.Error || depth > 64 || --remainingWork < 0)
            { return false; }
            if (visited.TryGetValue(current, out var closed))
            { return closed; }
            if (current is IArrayTypeSymbol array)
            { closed = Closed(array.ElementType, depth + 1); }
            else
            {
                closed = current is INamedTypeSymbol named && !named.IsUnboundGenericType &&
                    named.TypeArguments.All(argument => argument is null ? throw new System.ArgumentNullException(nameof(argument)) : (Closed(argument, depth + 1))) &&
                    (named.ContainingType == null || Closed(named.ContainingType, depth + 1));
            }
            visited[current] = closed;
            return closed;
        }
        return Closed(type, 0);
    }

    public static IrIdentityId InternSymbol(
        IrFactory factory,
        ISymbol symbol)
    {
        factory = ArgumentNullGuard.NotNull(factory, nameof(factory));
        symbol = ArgumentNullGuard.NotNull(symbol, nameof(symbol));

        if (symbol is ITypeSymbol type)
        { return InternType(factory, type); }

        return factory.InternExternalIdentity<ISymbol>(
            symbol,
            SymbolEqualityComparer.Default);
    }

    public static IrIdentityId InternType(
        IrFactory factory,
        ITypeSymbol type)
    {
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
        type = RepositoryParameterGuard.NotNull(type, nameof(type));
        return factory.InternExternalIdentity<ITypeSymbol>(type, RuntimeTypeComparer.Instance);
    }

    internal static bool SameRuntimeType(ITypeSymbol left, ITypeSymbol right)
    {
        left = ArgumentNullGuard.NotNull(left, nameof(left));
        right = ArgumentNullGuard.NotNull(right, nameof(right));
        var same = RuntimeTypeComparer.Compare(left, right, out var incomplete);
        return same || incomplete;
    }

    private sealed class RuntimeTypeComparer : IEqualityComparer<ITypeSymbol>
    {
        internal static readonly RuntimeTypeComparer Instance = new();
        public bool Equals(ITypeSymbol? left, ITypeSymbol? right)
        { return Compare(left, right, out _); }

        internal static bool Compare(ITypeSymbol? left, ITypeSymbol? right, out bool incomplete)
        {
            var remaining = 4096;
            var limited = false;
            bool Equal(ITypeSymbol? a, ITypeSymbol? b, int depth)
            {
                if (SymbolEqualityComparer.Default.Equals(a, b))
                { return true; }
                if (a == null || b == null)
                { return false; }
                if (depth > 64 || --remaining < 0)
                { limited = true; return false; }
                if (a is INamedTypeSymbol { IsTupleType: true, TupleUnderlyingType: { } firstTuple })
                { a = firstTuple; }
                if (b is INamedTypeSymbol { IsTupleType: true, TupleUnderlyingType: { } secondTuple })
                { b = secondTuple; }
                if (a.TypeKind == TypeKind.Dynamic && b.SpecialType == SpecialType.System_Object ||
                    b.TypeKind == TypeKind.Dynamic && a.SpecialType == SpecialType.System_Object)
                { return true; }
                if (a is IArrayTypeSymbol firstArray && b is IArrayTypeSymbol secondArray)
                { return firstArray.Rank == secondArray.Rank && firstArray.IsSZArray == secondArray.IsSZArray && Equal(firstArray.ElementType, secondArray.ElementType, depth + 1); }
                if (a is not INamedTypeSymbol first || b is not INamedTypeSymbol second ||
                    !SymbolEqualityComparer.Default.Equals(first.OriginalDefinition, second.OriginalDefinition) ||
                    first.TypeArguments.Length != second.TypeArguments.Length || !Equal(first.ContainingType, second.ContainingType, depth + 1))
                { return false; }
                for (var index = 0; index < first.TypeArguments.Length; index++)
                {
                    if (!Equal(first.TypeArguments[index], second.TypeArguments[index], depth + 1))
                    { return false; }
                }
                return true;
            }
            var result = Equal(left, right, 0);
            incomplete = limited;
            return result;
        }

        public int GetHashCode(ITypeSymbol type)
        {
            type = ArgumentNullGuard.NotNull(type, nameof(type));
            if (type is INamedTypeSymbol { IsTupleType: true, TupleUnderlyingType: { } tuple })
            { type = tuple; }
            if (type.TypeKind == TypeKind.Dynamic || type.SpecialType == SpecialType.System_Object)
            { return (int)SpecialType.System_Object; }
            // Arguments do not enter the bucket hash: nested tuple names and
            // dynamic spellings erase at runtime. Equality still checks them.
            return type is INamedTypeSymbol named ? SymbolEqualityComparer.Default.GetHashCode(named.OriginalDefinition) :
                type is IArrayTypeSymbol ? (int)TypeKind.Array : SymbolEqualityComparer.Default.GetHashCode(type);
        }
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
                OperationSemanticIdentityComparer.Instance);
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
            CSharpOperationSemantics.IsSupportedInteger(type.SpecialType);
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
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
        var binary = operation as IBinaryOperation;
        var unary = operation as IUnaryOperation;
        var conversion = operation as IConversionOperation;
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
            binary?.OperatorKind,
            unary?.OperatorKind,
            (operation as IInstanceReferenceOperation)?.ReferenceKind,
            binary?.IsChecked ?? unary?.IsChecked ?? conversion?.IsChecked ?? false,
            binary?.IsLifted ?? unary?.IsLifted ?? false,
            conversion?.IsTryCast ?? false,
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
        symbol = RepositoryParameterGuard.NotNull(symbol, nameof(symbol));
        return CreateReference(
            symbol,
            DocumentationCommentId.CreateDeclarationId);
    }

    private static string TypeReference(ITypeSymbol type)
    {
        type = RepositoryParameterGuard.NotNull(type, nameof(type));
        return CreateReference(
            type,
            DocumentationCommentId.CreateReferenceId);
    }

    private static string CreateReference<T>(
        T symbol,
        Func<T, string?> createId)
        where T : ISymbol
    {
        createId = RepositoryParameterGuard.NotNull(createId, nameof(createId));
        ArgumentNullGuard.NotNull(symbol, nameof(symbol));
        return createId(symbol) is { Length: > 0 } id
            ? id
            : FallbackReference(symbol);
    }

    private static string FallbackReference(ISymbol symbol)
    {
        symbol = RepositoryParameterGuard.NotNull(symbol, nameof(symbol));
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
        OperationKind kind,
        /* SPINT001: all values valid - default denotes an operation without a result type */ IrIdentityId type,
        /* SPINT001: all values valid - default denotes an operation without a type operand */ IrIdentityId typeOperand,
        BinaryOperatorKind? binaryOperator,
        UnaryOperatorKind? unaryOperator,
        InstanceReferenceKind? instanceReference,
        bool isChecked, bool isLifted, bool isTryCast, string? constantIdentity)
    {
        private readonly (
            OperationKind, IrIdentityId, IrIdentityId,
            BinaryOperatorKind?, UnaryOperatorKind?,
            InstanceReferenceKind?, bool, bool, bool, string?) _value =
            (kind, type, typeOperand, binaryOperator, unaryOperator,
             instanceReference, isChecked, isLifted, isTryCast, constantIdentity);

        internal bool HasSameValue(OperationSemanticIdentity other)
        {
            return _value.Equals(other._value);
        }

        internal int ValueHashCode()
        {
            return _value.GetHashCode();
        }
    }
    private sealed class OperationSemanticIdentityComparer : IEqualityComparer<OperationSemanticIdentity>
    {
        internal static readonly OperationSemanticIdentityComparer Instance = new();

        public bool Equals(OperationSemanticIdentity left, OperationSemanticIdentity right)
        {
            return left.HasSameValue(right);
        }

        public int GetHashCode(OperationSemanticIdentity value /* SPINT001: all values valid - all identity tuples, including default, support equality and hashing */)
        {
            return value.ValueHashCode();
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
        obj = RepositoryParameterGuard.NotNull(obj, nameof(obj));
        return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}

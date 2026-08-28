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
            operation switch
            {
                ITypeOfOperation typeOf => InternType(factory, typeOf.TypeOperand),
                ISizeOfOperation sizeOf => InternType(factory, sizeOf.TypeOperand),
                _ => default
            },
            (operation as IBinaryOperation)?.OperatorKind,
            (operation as IUnaryOperation)?.OperatorKind,
            (operation as IInstanceReferenceOperation)?.ReferenceKind,
            CompilerIdentityProjections.IsChecked(operation),
            CompilerIdentityProjections.IsLifted(operation),
            operation.ConstantValue.HasValue,
            operation.ConstantValue.HasValue
                ? operation.ConstantValue.Value
                : null);
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
        var reference = DocumentationCommentId.CreateDeclarationId(symbol) is
            { Length: > 0 } id
            ? id
            : FallbackReference(symbol);
        return ContainsStructuralType(symbol)
            ? reference + "|sig:" + StructuralSymbolIdentity(symbol)
            : reference;
    }

    private static string TypeReference(ITypeSymbol type)
    {
        var reference = DocumentationCommentId.CreateReferenceId(type) is
            { Length: > 0 } id
            ? id
            : FallbackReference(type);
        return ContainsStructuralType(type)
            ? reference + "|sig:" + StructuralTypeIdentity(type)
            : reference;
    }

    private static bool ContainsStructuralType(ISymbol symbol)
    {
        return symbol switch
        {
            IMethodSymbol method =>
                method.Parameters.Any(static parameter =>
                    ContainsStructuralType(parameter.Type)) ||
                ContainsStructuralType(method.ReturnType),
            IPropertySymbol property => ContainsStructuralType(property.Type) ||
                property.Parameters.Any(static parameter =>
                    ContainsStructuralType(parameter.Type)),
            IFieldSymbol field => ContainsStructuralType(field.Type),
            IEventSymbol @event => ContainsStructuralType(@event.Type),
            ITypeSymbol type => ContainsStructuralType(type),
            _ => false
        };
    }

    private static bool ContainsStructuralType(ITypeSymbol type)
    {
        return type switch
        {
            IFunctionPointerTypeSymbol => true,
            INamedTypeSymbol { IsAnonymousType: true } => true,
            IArrayTypeSymbol array => ContainsStructuralType(array.ElementType),
            IPointerTypeSymbol pointer => ContainsStructuralType(pointer.PointedAtType),
            INamedTypeSymbol named => named.TypeArguments.Any(ContainsStructuralType),
            _ => false
        };
    }

    private static string StructuralSymbolIdentity(ISymbol symbol)
    {
        return symbol switch
        {
            IMethodSymbol method =>
                "method:" + method.RefKind + ":" +
                string.Join(",", method.Parameters.Select(static parameter =>
                    parameter.RefKind + ":" + StructuralTypeIdentity(parameter.Type))) +
                "->" + StructuralTypeIdentity(method.ReturnType),
            IPropertySymbol property =>
                "property:" + string.Join(",", property.Parameters.Select(
                    static parameter => parameter.RefKind + ":" +
                        StructuralTypeIdentity(parameter.Type))) +
                "->" + StructuralTypeIdentity(property.Type),
            IFieldSymbol field => "field:" + StructuralTypeIdentity(field.Type),
            IEventSymbol @event => "event:" + StructuralTypeIdentity(@event.Type),
            ITypeSymbol type => StructuralTypeIdentity(type),
            _ => symbol.Kind + ":" + symbol.MetadataName
        };
    }

    private static string StructuralTypeIdentity(ITypeSymbol type)
    {
        return StructuralTypeIdentity(type, 0);
    }

    private static string StructuralTypeIdentity(ITypeSymbol type, int depth)
    {
        if (depth >= 64)
        {
            return "depth-limit";
        }

        return type switch
        {
            IFunctionPointerTypeSymbol functionPointer =>
                "fnptr[" + functionPointer.Signature.CallingConvention + ";" +
                string.Join(",", functionPointer.Signature.UnmanagedCallingConventionTypes
                    .Select(static convention => convention.ToDisplayString())) +
                "](" +
                string.Join(",", functionPointer.Signature.Parameters.Select(
                    parameter => parameter.RefKind + ":" +
                        StructuralTypeIdentity(parameter.Type, depth + 1))) +
                ")->" + functionPointer.Signature.RefKind + ":" +
                StructuralTypeIdentity(functionPointer.Signature.ReturnType, depth + 1),
            IArrayTypeSymbol array =>
                "array" + array.Rank + "[" +
                StructuralTypeIdentity(array.ElementType, depth + 1) + "]",
            IPointerTypeSymbol pointer =>
                "pointer[" + StructuralTypeIdentity(pointer.PointedAtType, depth + 1) + "]",
            INamedTypeSymbol { IsAnonymousType: true } anonymous =>
                "anonymous{" + string.Join(";", anonymous.GetMembers()
                    .OfType<IPropertySymbol>()
                    .Select(property => property.Name + ":" +
                        StructuralTypeIdentity(property.Type, depth + 1))) + "}",
            INamedTypeSymbol named =>
                (DocumentationCommentId.CreateReferenceId(named) ??
                    named.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)) +
                (named.IsGenericType
                    ? "<" + string.Join(",", named.TypeArguments.Select(
                        argument => StructuralTypeIdentity(argument, depth + 1))) + ">"
                    : string.Empty),
            ITypeParameterSymbol parameter =>
                "typeparam[" + parameter.TypeParameterKind + ":" +
                parameter.Ordinal + "]",
            _ => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
        };
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
        OperationKind kind, IrIdentityId type, IrIdentityId operandType,
        BinaryOperatorKind? binaryOperator,
        UnaryOperatorKind? unaryOperator,
        InstanceReferenceKind? instanceReference,
        bool isChecked, bool isLifted, bool hasConstantValue,
        object? constantValue)
        : IEquatable<OperationSemanticIdentity>
    {
        private readonly (
            OperationKind, IrIdentityId, IrIdentityId, BinaryOperatorKind?,
            UnaryOperatorKind?, InstanceReferenceKind?, bool, bool, bool, object?) _value =
            (kind, type, operandType, binaryOperator, unaryOperator, instanceReference,
             isChecked, isLifted, hasConstantValue, constantValue);

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

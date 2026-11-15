using System.Numerics;

namespace SharpProof.Frontend;

internal enum TotalOperationAdmission { Scalar, Structure, Intrinsic, Incomplete, Invalid }

internal readonly struct TotalThrow(IrExceptionKind kind, IrTerm condition)
{
    internal IrExceptionKind Kind { get; } = kind;
    internal IrTerm Condition { get; } = (condition ?? throw new System.ArgumentNullException(nameof(condition)));
}

internal readonly struct TotalScalarRule(IrTerm value, ImmutableArray<TotalThrow> throws,
    FrontendSubsetClassification classification /* SPINT001: all values valid - every classification, including default Exact, is valid */)
{
    internal IrTerm Value { get; } = (value ?? throw new System.ArgumentNullException(nameof(value)));
    internal ImmutableArray<TotalThrow> Throws { get; } = (throws.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(throws)) : throws);
    internal FrontendSubsetClassification Classification { get; } = classification;
}

// Body and clauses use these same scalar values and local C# faults.
// Resolved Roslyn conversions determine promotions before an operator is applied.
internal static partial class CSharpOperationSemantics
{
    internal static bool IsScalar(ITypeSymbol? type)
    {
        return type != null && (type.SpecialType == SpecialType.System_Boolean || TryGetScalarInteger(ScalarSpecialType(type), out _));
    }

    // An enum value is its underlying integer: comparisons, bitwise operators
    // and conversions act on that integer. Boxing (a boxed enum is not a boxed
    // integer), enum arithmetic and increments are refused where they occur.
    internal static SpecialType ScalarSpecialType(ITypeSymbol type)
    {
        type = RepositoryParameterGuard.NotNull(type, nameof(type));
        return type is INamedTypeSymbol { TypeKind: TypeKind.Enum, EnumUnderlyingType: { } underlying } ? underlying.SpecialType : type.SpecialType;
    }

    internal static bool IsEnum(ITypeSymbol? type)
    {
        return type is INamedTypeSymbol { TypeKind: TypeKind.Enum };
    }

    internal static IrTypeId? MapType(IrFactory factory, SpecialType type)
    {
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
        return type == SpecialType.System_Boolean ? factory.BooleanType
            : TryGetScalarInteger(type, out var integer)
                ? factory.GetOrCreateIntegerType(integer.BitWidth, integer.IsSigned) : null;
    }

    internal static IrTerm Literal(IrFactory factory, ITypeSymbol? type, object? value)
    {
        if (value == null && (type == null || IsReferenceDomain(type)))
        { return factory.Null(new RoslynTypeMapper(factory).GetTypeId(type)); }
        if (type?.SpecialType == SpecialType.System_String && value is string text)
        { return factory.String(text); }
        if (type == null)
        { throw new ArgumentNullException(nameof(type), "A non-null literal requires its type."); }
        var mapped = MapType(factory, ScalarSpecialType(type))
            ?? throw new InvalidOperationException("The literal type has no scalar IR domain.");
        return type.SpecialType == SpecialType.System_Boolean ? factory.Boolean(value is true)
            : value is ulong unsigned ? factory.Integer(mapped, unsigned)
            : factory.Integer(mapped, value == null ? 0 : value is char character ? character : Convert.ToInt64(value, CultureInfo.InvariantCulture));
    }

    internal static TotalScalarRule Apply(IrFactory factory, IOperation operation, ImmutableArray<IrTerm> operands,
        bool? checkOverflow = null)
    {
        if (ReferenceRule(factory, operation, operands) is { } reference)
        { return reference; }
        if (!IsScalar(operation.Type))
        {
            return Fail(factory, FrontendAbstention.UnsupportedType);
        }
        var value = operands[0];
        var target = MapType(factory, ScalarSpecialType(operation.Type!))!.Value;
        if (operation is IConversionOperation conversion)
        {
            if (conversion.OperatorMethod != null)
            {
                return Fail(factory, FrontendAbstention.UserDefinedOperator);
            }
            // Roslyn reports IsChecked for numeric conversions only. An
            // enumeration conversion converts the underlying integers and
            // faults like them in a checked context.
            if (IsEnum(conversion.Type) || IsEnum(conversion.Operand.Type))
            {
                return CheckedContext(conversion, checkOverflow ?? conversion.SemanticModel?.Compilation.Options.CheckOverflow) is { } isChecked
                    ? ConvertInteger(factory, value, target, isChecked) : Fail(factory, FrontendAbstention.UnsupportedOperationKind);
            }
            return ConvertInteger(factory, value, target, conversion.IsChecked);
        }
        if (operation is IUnaryOperation unary)
        {
            if (unary.OperatorMethod != null || unary.IsLifted)
            {
                return Fail(factory, unary.IsLifted ? FrontendAbstention.LiftedOperator : FrontendAbstention.UserDefinedOperator);
            }
            if (IsEnum(unary.Operand.Type))
            {
                // `~e` is `(E)~(U)e`: the complement at the underlying width,
                // also for a byte, sbyte, short or ushort enum.
                var info = factory.GetTypeInfo(value.Type);
                if (unary.OperatorKind != UnaryOperatorKind.BitwiseNegation || value.Type != target || info.Kind != IrTypeKind.Integer ||
                    info.Width is not (8 or 16 or 32 or 64))
                { return Fail(factory, FrontendAbstention.UnsupportedOperationKind); }
                var ones = info.Width == 64 ? ulong.MaxValue : (1UL << info.Width) - 1;
                return Exact(factory.Binary(IrBinaryOperator.BitwiseXor, value, factory.IntegerBits(value.Type, ones)));
            }
            return ApplyUnaryScalar(factory, unary.OperatorKind, unary.IsChecked, value, target);
        }
        if (operation is not IBinaryOperation binary || operands.Length != 2)
        {
            return Fail(factory, FrontendAbstention.UnsupportedOperationKind);
        }
        if (binary.OperatorMethod != null || binary.IsLifted)
        {
            return Fail(factory, binary.IsLifted ? FrontendAbstention.LiftedOperator : FrontendAbstention.UserDefinedOperator);
        }
        var right = operands[1];
        // Enum addition and subtraction convert through the underlying type
        // with their own result type; only comparisons and bitwise operators
        // on enums are modeled.
        if ((IsEnum(binary.LeftOperand.Type) || IsEnum(binary.RightOperand.Type)) &&
            binary.OperatorKind is not (BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals or BinaryOperatorKind.LessThan or
                BinaryOperatorKind.LessThanOrEqual or BinaryOperatorKind.GreaterThan or BinaryOperatorKind.GreaterThanOrEqual or
                BinaryOperatorKind.And or BinaryOperatorKind.Or or BinaryOperatorKind.ExclusiveOr))
        { return Fail(factory, FrontendAbstention.UnsupportedOperationKind); }
        return ApplyBinaryScalar(factory, binary.OperatorKind, binary.IsChecked, value, right);
    }

    // A built-in binary operator on two scalar values of its operand type;
    // lifted operators apply it to the underlying values.
    internal static TotalScalarRule ApplyBinaryScalar(IrFactory factory, BinaryOperatorKind operatorKind, bool isChecked, IrTerm value, IrTerm right)
    {
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
        value = RepositoryParameterGuard.NotNull(value, nameof(value));
        right = RepositoryParameterGuard.NotNull(right, nameof(right));
        if (IsShift(operatorKind))
        { return Shift(factory, operatorKind, value, right); }
        if (value.Type != right.Type || !TryBinary(operatorKind, out var kind))
        {
            return Fail(factory, FrontendAbstention.UnsupportedOperationKind);
        }
        if (IsBitwise(kind))
        { return Bitwise(factory, kind, value, right); }
        if (kind is IrBinaryOperator.Add or IrBinaryOperator.Subtract or IrBinaryOperator.Multiply)
        { return IntegerArithmetic(factory, kind, value, right, isChecked); }
        if (kind is IrBinaryOperator.Divide or IrBinaryOperator.Remainder)
        { return DivideOrRemainder(factory, kind, value, right); }
        return new(factory.Binary(kind, value, right), [], FrontendSubsetClassification.Exact);
    }

    // Applies the owned scalar unary algebra after compiler-shape admission.
    internal static TotalScalarRule ApplyUnaryScalar(
        IrFactory factory, UnaryOperatorKind kind, bool isChecked, IrTerm value, IrTypeId target)
    {
        RepositoryParameterGuard.ThrowIfInvalid(target.IsDefault, nameof(target));
        if (kind == UnaryOperatorKind.Not && value.Type == factory.BooleanType)
        {
            return Exact(factory.Unary(IrUnaryOperator.Not, value));
        }
        if (kind == UnaryOperatorKind.BitwiseNegation)
        { return Complement(factory, value, target); }
        if (factory.GetTypeInfo(value.Type).Kind != IrTypeKind.Integer ||
            kind is not (UnaryOperatorKind.Plus or UnaryOperatorKind.Minus))
        {
            return Fail(factory, FrontendAbstention.UnsupportedOperationKind);
        }
        var promoted = value.Type == target ? value : factory.Cast(target, value);
        if (kind == UnaryOperatorKind.Plus)
        { return Exact(promoted); }
        return new(factory.Unary(IrUnaryOperator.Negate, promoted), isChecked
            ? [new(IrExceptionKind.Overflow, factory.Binary(IrBinaryOperator.Equal, promoted,
                Number(factory, target, Bounds(factory.GetTypeInfo(target)).Minimum)))] : [], FrontendSubsetClassification.Exact);
    }

    internal static TotalScalarRule DivideOrRemainder(IrFactory factory, IrBinaryOperator kind, IrTerm value, IrTerm right)
    {
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
        right = RepositoryParameterGuard.NotNull(right, nameof(right));
        value = RepositoryParameterGuard.NotNull(value, nameof(value));
        var faults = ImmutableArray.CreateBuilder<TotalThrow>();
        faults.Add(new(IrExceptionKind.DivideByZero,
                factory.Binary(IrBinaryOperator.Equal, right, factory.Integer(right.Type, 0L))));
        var info = factory.GetTypeInfo(value.Type);
        if (info.Signed && info.Width is 32 or 64)
        {
            faults.Add(new(IrExceptionKind.Overflow, factory.Binary(IrBinaryOperator.AndAlso,
                    factory.Binary(IrBinaryOperator.Equal, value, Number(factory, value.Type, Bounds(info).Minimum)),
                    factory.Binary(IrBinaryOperator.Equal, right, factory.Integer(right.Type, -1L)))));
        }
        return new(factory.Binary(kind, value, right), faults.ToImmutable(), FrontendSubsetClassification.Exact);
    }

    internal static bool TryBinary(BinaryOperatorKind kind, out IrBinaryOperator result)
    {
        var supported = TryGetBinary(kind, out var semantics);
        result = supported ? semantics.IrOperator : default;
        return supported;
    }

    internal static (BigInteger Minimum, BigInteger Maximum) Bounds(IrTypeInfo type)
    {
        type = RepositoryParameterGuard.NotNull(type, nameof(type));
        return type.Signed ? (-(BigInteger.One << (type.Width - 1)), (BigInteger.One << (type.Width - 1)) - 1)
            : (BigInteger.Zero, (BigInteger.One << type.Width) - 1);
    }

    private static IrIntegerTerm Number(IrFactory factory, IrTypeId type, BigInteger value)
    {
        RepositoryParameterGuard.ThrowIfInvalid(type.IsDefault, nameof(type));
        return value > long.MaxValue ? factory.Integer(type, (ulong)value) : factory.Integer(type, (long)value);
    }
    private static TotalScalarRule Exact(IrTerm value)
    {
        return new(value, [], FrontendSubsetClassification.Exact);
    }
    private static TotalScalarRule Fail(IrFactory factory, FrontendAbstention reason)
    {
        return new(factory.Boolean(false), [], FrontendSubsetClassification.Abstain(reason));
    }
}

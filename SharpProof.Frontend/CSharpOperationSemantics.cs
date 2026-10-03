using System.Numerics;

namespace SharpProof.Frontend;

internal enum TotalOperationAdmission { Scalar, Structure, Intrinsic, Incomplete, Invalid }

internal readonly struct TotalThrow(IrExceptionKind kind, IrTerm condition)
{
    internal IrExceptionKind Kind { get; } = kind;
    internal IrTerm Condition { get; } = condition;
}

internal readonly struct TotalScalarRule(IrTerm value, ImmutableArray<TotalThrow> throws,
    FrontendSubsetClassification classification)
{
    internal IrTerm Value { get; } = value;
    internal ImmutableArray<TotalThrow> Throws { get; } = throws;
    internal FrontendSubsetClassification Classification { get; } = classification;
}

// Body and clauses use these same scalar values and local C# faults.
// Resolved Roslyn conversions determine promotions before an operator is applied.
internal static partial class CSharpOperationSemantics
{
    internal static bool IsScalar(ITypeSymbol? type)
    {
        return type != null && (type.SpecialType == SpecialType.System_Boolean || TryGetScalarInteger(type.SpecialType, out _));
    }

    internal static IrTypeId? MapType(IrFactory factory, SpecialType type)
    {
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
        var mapped = MapType(factory, type!.SpecialType)!.Value;
        return type.SpecialType == SpecialType.System_Boolean ? factory.Boolean(value is true)
            : value is ulong unsigned ? factory.Integer(mapped, unsigned)
            : factory.Integer(mapped, value == null ? 0 : Convert.ToInt64(value, CultureInfo.InvariantCulture));
    }

    internal static TotalScalarRule Apply(IrFactory factory, IOperation operation, ImmutableArray<IrTerm> operands)
    {
        if (ReferenceRule(factory, operation, operands) is { } reference)
        { return reference; }
        if (!IsScalar(operation.Type))
        {
            return Fail(factory, FrontendAbstention.UnsupportedType);
        }
        var value = operands[0];
        var target = MapType(factory, operation.Type!.SpecialType)!.Value;
        if (operation is IConversionOperation conversion)
        {
            if (conversion.OperatorMethod != null)
            {
                return Fail(factory, FrontendAbstention.UserDefinedOperator);
            }
            return ConvertInteger(factory, value, target, conversion.IsChecked);
        }
        if (operation is IUnaryOperation unary)
        {
            if (unary.OperatorMethod != null || unary.IsLifted)
            {
                return Fail(factory, unary.IsLifted ? FrontendAbstention.LiftedOperator : FrontendAbstention.UserDefinedOperator);
            }
            if (unary.OperatorKind == UnaryOperatorKind.Not && value.Type == factory.BooleanType)
            {
                return Exact(factory.Unary(IrUnaryOperator.Not, value));
            }
            if (factory.GetTypeInfo(value.Type).Kind != IrTypeKind.Integer ||
                unary.OperatorKind is not (UnaryOperatorKind.Plus or UnaryOperatorKind.Minus))
            {
                return Fail(factory, FrontendAbstention.UnsupportedOperationKind);
            }
            var promoted = value.Type == target ? value : factory.Cast(target, value);
            if (unary.OperatorKind == UnaryOperatorKind.Plus)
            { return Exact(promoted); }
            return new(factory.Unary(IrUnaryOperator.Negate, promoted), unary.IsChecked
                ? [new(IrExceptionKind.Overflow, factory.Binary(IrBinaryOperator.Equal, promoted,
                    Number(factory, target, Bounds(factory.GetTypeInfo(target)).Minimum)))] : [], FrontendSubsetClassification.Exact);
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
        if (value.Type != right.Type || !TryBinary(binary.OperatorKind, out var kind))
        {
            return Fail(factory, FrontendAbstention.UnsupportedOperationKind);
        }
        if (kind is IrBinaryOperator.Add or IrBinaryOperator.Subtract or IrBinaryOperator.Multiply)
        { return IntegerArithmetic(factory, kind, value, right, binary.IsChecked); }
        if (kind is IrBinaryOperator.Divide or IrBinaryOperator.Remainder)
        { return DivideOrRemainder(factory, kind, value, right); }
        return new(factory.Binary(kind, value, right), [], FrontendSubsetClassification.Exact);
    }

    internal static TotalScalarRule DivideOrRemainder(IrFactory factory, IrBinaryOperator kind, IrTerm value, IrTerm right)
    {
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
        return type.Signed ? (-(BigInteger.One << (type.Width - 1)), (BigInteger.One << (type.Width - 1)) - 1)
            : (BigInteger.Zero, (BigInteger.One << type.Width) - 1);
    }

    private static IrIntegerTerm Number(IrFactory factory, IrTypeId type, BigInteger value)
    {
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

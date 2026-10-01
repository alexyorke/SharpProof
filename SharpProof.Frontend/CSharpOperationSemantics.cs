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

// Candidate body and clauses use these same scalar values and local C# faults.
// Resolved Roslyn conversions determine promotions before an operator is applied.
internal static partial class CSharpOperationSemantics
{
    internal static bool IsScalar(ITypeSymbol? type)
    {
        return type?.SpecialType is SpecialType.System_Boolean or SpecialType.System_SByte or SpecialType.System_Byte or
            SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Char or
            SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_UInt64;
    }

    internal static IrTypeId? MapType(IrFactory factory, SpecialType type)
    {
        return type switch
        {
            SpecialType.System_Boolean => factory.BooleanType,
            SpecialType.System_SByte => factory.GetOrCreateIntegerType(8, true),
            SpecialType.System_Byte => factory.GetOrCreateIntegerType(8, false),
            SpecialType.System_Int16 => factory.GetOrCreateIntegerType(16, true),
            SpecialType.System_UInt16 or SpecialType.System_Char => factory.GetOrCreateIntegerType(16, false),
            SpecialType.System_Int32 => factory.GetOrCreateIntegerType(32, true),
            SpecialType.System_UInt32 => factory.GetOrCreateIntegerType(32, false),
            SpecialType.System_Int64 => factory.GetOrCreateIntegerType(64, true),
            SpecialType.System_UInt64 => factory.GetOrCreateIntegerType(64, false),
            _ => null
        };
    }

    internal static IrTerm Literal(IrFactory factory, ITypeSymbol type, object? value)
    {
        var mapped = MapType(factory, type.SpecialType)!.Value;
        return type.SpecialType == SpecialType.System_Boolean ? factory.Boolean(value is true)
            : value is ulong unsigned ? factory.Integer(mapped, unsigned)
            : factory.Integer(mapped, value == null ? 0 : Convert.ToInt64(value, CultureInfo.InvariantCulture));
    }

    internal static TotalScalarRule Apply(IrFactory factory, IOperation operation, ImmutableArray<IrTerm> operands)
    {
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
        IrBinaryOperator? mapped = kind switch
        {
            BinaryOperatorKind.Add => IrBinaryOperator.Add,
            BinaryOperatorKind.Subtract => IrBinaryOperator.Subtract,
            BinaryOperatorKind.Multiply => IrBinaryOperator.Multiply,
            BinaryOperatorKind.Divide => IrBinaryOperator.Divide,
            BinaryOperatorKind.Remainder => IrBinaryOperator.Remainder,
            BinaryOperatorKind.Equals => IrBinaryOperator.Equal,
            BinaryOperatorKind.NotEquals => IrBinaryOperator.NotEqual,
            BinaryOperatorKind.LessThan => IrBinaryOperator.LessThan,
            BinaryOperatorKind.LessThanOrEqual => IrBinaryOperator.LessThanOrEqual,
            BinaryOperatorKind.GreaterThan => IrBinaryOperator.GreaterThan,
            BinaryOperatorKind.GreaterThanOrEqual => IrBinaryOperator.GreaterThanOrEqual,
            BinaryOperatorKind.ConditionalAnd => IrBinaryOperator.AndAlso,
            BinaryOperatorKind.ConditionalOr => IrBinaryOperator.OrElse,
            _ => null
        };
        result = mapped.GetValueOrDefault();
        return mapped.HasValue;
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

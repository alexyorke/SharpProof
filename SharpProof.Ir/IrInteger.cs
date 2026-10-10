using System.Numerics;

namespace SharpProof.Ir;

// Integer payloads retain every bit of ulong without a signed intermediary.
internal readonly struct IrInteger(ulong bits, int width, bool signed)
{
    internal ulong Bits { get; } = bits;
    internal int Width { get; } = width;
    internal bool Signed { get; } = signed;
    private int StorageWidth => Width == 0 ? 64 : Width;
    internal long SignedValue => unchecked((long)(Bits << (64 - StorageWidth))) >> (64 - StorageWidth);
    internal long Int64 => Signed ? SignedValue : checked((long)Bits);
    internal BigInteger NumericValue => Signed ? new BigInteger(SignedValue) : new BigInteger(Bits);

    internal static ulong Mask(int width)
    {
        return width is 0 or 64 ? ulong.MaxValue : (1UL << width) - 1;
    }

    internal static IrInteger FromBits(IrTypeInfo type, ulong bits)
    {
        if (type.Kind != IrTypeKind.Integer)
        {
            throw new ArgumentException("An integer type is required.", nameof(type));
        }
        var width = type.Width == 0 ? 64 : type.Width;
        if ((bits & ~Mask(width)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bits), "The bits exceed the integer width.");
        }
        return new IrInteger(bits, type.Width, type.Signed);
    }

    internal static IrInteger FromNumber(IrTypeInfo type, BigInteger value)
    {
        var width = type.Width == 0 ? 64 : type.Width;
        var minimum = type.Signed ? -(BigInteger.One << (width - 1)) : BigInteger.Zero;
        var maximum = type.Signed ? (BigInteger.One << (width - 1)) - 1 : (BigInteger.One << width) - 1;
        if (value < minimum || value > maximum)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "The literal exceeds the integer range.");
        }
        return FromBits(type, (ulong)(value & Mask(width)));
    }

    internal ulong ConvertBits(int targetWidth)
    {
        return (Signed ? unchecked((ulong)SignedValue) : Bits) & Mask(targetWidth);
    }
}

internal static class IrBitVectorOperations
{
    internal static (IrScalarResultKind Kind, ulong Bits) Evaluate(
        IrBinaryOperator operation, IrInteger left, IrInteger right,
        IrExecutionSemantics semantics = IrExecutionSemantics.Legacy)
    {
        var mask = IrInteger.Mask(left.Width);
        if (operation is IrBinaryOperator.Divide or IrBinaryOperator.Remainder && right.Bits == 0)
        {
            if (semantics == IrExecutionSemantics.Legacy)
            {
                return (IrScalarResultKind.DivideByZero, 0);
            }
            return operation == IrBinaryOperator.Remainder
                ? (IrScalarResultKind.Integer, left.Bits)
                : (IrScalarResultKind.Integer, left.Signed && left.SignedValue < 0 ? 1UL : mask);
        }
        switch (operation)
        {
            case IrBinaryOperator.Add:
                return Integer(unchecked(left.Bits + right.Bits));
            case IrBinaryOperator.Subtract:
                return Integer(unchecked(left.Bits - right.Bits));
            case IrBinaryOperator.Multiply:
                return Integer(unchecked(left.Bits * right.Bits));
            case IrBinaryOperator.BitwiseAnd:
                return Integer(left.Bits & right.Bits);
            case IrBinaryOperator.ShiftLeft:
            case IrBinaryOperator.ShiftRight:
                if (left.Width is not (32 or 64))
                {
                    return (IrScalarResultKind.Unsupported, 0);
                }
                // The CLR masks the count to the operand width.
                var count = (int)(right.Bits & (ulong)(left.Width - 1));
                return Integer(operation == IrBinaryOperator.ShiftLeft ? left.Bits << count
                    : left.Signed ? unchecked((ulong)(left.SignedValue >> count)) : left.Bits >> count);
            case IrBinaryOperator.Divide:
            case IrBinaryOperator.Remainder:
                if (!left.Signed)
                {
                    return Integer(operation == IrBinaryOperator.Divide
                        ? left.Bits / right.Bits : left.Bits % right.Bits);
                }
                var signedLeft = left.SignedValue;
                var signedRight = right.SignedValue;
                // On the canonical CLR, rem shares div's minimum/-1 overflow.
                // Narrow operands are promoted to int before their result cast.
                if (left.Width is 32 or 64 && signedRight == -1 &&
                    signedLeft == (left.Width == 64 ? long.MinValue : int.MinValue))
                {
                    return semantics == IrExecutionSemantics.Legacy
                        ? (IrScalarResultKind.Overflow, 0)
                        : Integer(operation == IrBinaryOperator.Divide ? left.Bits : 0);
                }
                return Integer(unchecked((ulong)(operation == IrBinaryOperator.Divide
                    ? signedLeft / signedRight : signedLeft % signedRight)));
            case IrBinaryOperator.LessThan:
                return Boolean(left.Signed ? left.SignedValue < right.SignedValue : left.Bits < right.Bits);
            case IrBinaryOperator.LessThanOrEqual:
                return Boolean(left.Signed ? left.SignedValue <= right.SignedValue : left.Bits <= right.Bits);
            case IrBinaryOperator.GreaterThan:
                return Boolean(left.Signed ? left.SignedValue > right.SignedValue : left.Bits > right.Bits);
            case IrBinaryOperator.GreaterThanOrEqual:
                return Boolean(left.Signed ? left.SignedValue >= right.SignedValue : left.Bits >= right.Bits);
            default:
                return (IrScalarResultKind.Unsupported, 0);
        }

        (IrScalarResultKind Kind, ulong Bits) Integer(ulong bits)
        {
            return (IrScalarResultKind.Integer, bits & mask);
        }
        static (IrScalarResultKind Kind, ulong Bits) Boolean(bool value)
        {
            return (IrScalarResultKind.Boolean, value ? 1UL : 0);
        }
    }
}

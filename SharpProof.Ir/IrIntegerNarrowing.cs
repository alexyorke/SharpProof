namespace SharpProof.Ir;

// The C# integer range an IR narrowing operator targets. IR integers are
// exact; narrowing is where a C# width and overflow behaviour enter.
internal readonly struct IrIntegerNarrowing(int bits, bool signed, bool @checked)
{
    internal int Bits { get; } = bits;

    internal bool Signed { get; } = signed;

    internal bool Checked { get; } = @checked;

    internal long Minimum => Signed ? -(1L << (Bits - 1)) : 0;

    internal long Maximum => Signed ? (1L << (Bits - 1)) - 1 : (1L << Bits) - 1;

    internal string Token =>
        (Checked ? "checked(" : "unchecked(") + (Signed ? "i" : "u") + Bits + ")";

    internal bool Contains(long value)
    {
        return value >= Minimum && value <= Maximum;
    }

    // Two's-complement truncation of an exact value to this width.
    internal long Wrap(long value)
    {
        var modulus = 1L << Bits;
        var offset = (value - Minimum) % modulus;
        if (offset < 0)
        {
            offset += modulus;
        }
        return offset + Minimum;
    }

    internal static bool TryGet(IrUnaryOperator @operator, out IrIntegerNarrowing narrowing)
    {
        narrowing = @operator switch
        {
            IrUnaryOperator.CheckedSByte => new(8, true, true),
            IrUnaryOperator.CheckedByte => new(8, false, true),
            IrUnaryOperator.CheckedInt16 => new(16, true, true),
            IrUnaryOperator.CheckedUInt16 => new(16, false, true),
            IrUnaryOperator.CheckedInt32 => new(32, true, true),
            IrUnaryOperator.CheckedUInt32 => new(32, false, true),
            IrUnaryOperator.WrapSByte => new(8, true, false),
            IrUnaryOperator.WrapByte => new(8, false, false),
            IrUnaryOperator.WrapInt16 => new(16, true, false),
            IrUnaryOperator.WrapUInt16 => new(16, false, false),
            IrUnaryOperator.WrapInt32 => new(32, true, false),
            IrUnaryOperator.WrapUInt32 => new(32, false, false),
            _ => default
        };
        return narrowing.Bits != 0;
    }

    internal static IrUnaryOperator OperatorFor(int bits, bool signed, bool @checked)
    {
        return (bits, signed, @checked) switch
        {
            (8, true, true) => IrUnaryOperator.CheckedSByte,
            (8, false, true) => IrUnaryOperator.CheckedByte,
            (16, true, true) => IrUnaryOperator.CheckedInt16,
            (16, false, true) => IrUnaryOperator.CheckedUInt16,
            (32, true, true) => IrUnaryOperator.CheckedInt32,
            (32, false, true) => IrUnaryOperator.CheckedUInt32,
            (8, true, false) => IrUnaryOperator.WrapSByte,
            (8, false, false) => IrUnaryOperator.WrapByte,
            (16, true, false) => IrUnaryOperator.WrapInt16,
            (16, false, false) => IrUnaryOperator.WrapUInt16,
            (32, true, false) => IrUnaryOperator.WrapInt32,
            (32, false, false) => IrUnaryOperator.WrapUInt32,
            _ => throw new ArgumentOutOfRangeException(nameof(bits))
        };
    }
}

namespace SharpProof.Smt;

internal static class SmtNativeUtilities
{
    internal static BackendFailureReason ClassifyUnknown(string? reason)
    {
        if (reason?.IndexOf("incomplete", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return BackendFailureReason.Incomplete;
        }

        if (reason?.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return BackendFailureReason.Timeout;
        }

        if (reason?.IndexOf("resource", StringComparison.OrdinalIgnoreCase) >= 0 ||
            reason?.IndexOf("rlimit", StringComparison.OrdinalIgnoreCase) >= 0 ||
            reason?.IndexOf("max. memory", StringComparison.OrdinalIgnoreCase) >= 0 ||
            string.Equals(reason, "canceled", StringComparison.OrdinalIgnoreCase))
        {
            return BackendFailureReason.ResourceLimit;
        }

        return BackendFailureReason.InfrastructureFailure;
    }

    internal static long ComputeResourceDelta(uint before, uint after)
    {
        // StatisticsEntry exposes Z3's native rlimit counter as a uint. The
        // context counter can wrap while a backend remains alive, so subtract
        // in the counter's unsigned domain instead of allowing a negative
        // delta to undercharge the query.
        return after >= before
            ? after - before
            : (long)uint.MaxValue + 1 - before + after;
    }

    internal static uint? ReadResourceCount(Solver solver)
    {
        // Statistics is a caller-owned Z3 object holding a native reference, the
        // same as a model. A solver session outlives hundreds of
        // queries per lane, so leaving it to the finalizer accumulates.
        using var statistics = solver.Statistics;
        foreach (var entry in statistics.Entries)
        {
            if (!string.Equals(
                    entry.Key,
                    "rlimit count",
                    StringComparison.Ordinal) ||
                !entry.IsUInt)
            {
                continue;
            }

            return entry.UIntValue;
        }

        return null;
    }

    internal static void AddOwnedParameter(
        Params parameters,
        Symbol name,
        uint value)
    {
        using (name)
        {
            parameters.Add(name, value);
        }
    }

    // Store admitted integer bits or borrow a bitvector numeral until caller admission.
    internal readonly struct NativeIntegerNumeral(BitVecNum? number, int width, ulong literalBits)
    {
        internal int Width { get; } = width;
        internal ulong Bits => number is null ? literalBits : number.UInt64;
    }

    internal static NativeIntegerNumeral? ReadBitVectorNumeral(BitVecNum number)
    {
        using var sort = number.Sort;
        return sort is BitVecSort bitVector && bitVector.Size is 8 or 16 or 32 or 64
            ? new NativeIntegerNumeral(number, (int)bitVector.Size, 0) : null;
    }

    internal static NativeIntegerNumeral FromSignedIntegerLiteral(long value)
    {
        var narrow = value is >= int.MinValue and <= int.MaxValue;
        return new NativeIntegerNumeral(null, narrow ? 32 : 64,
            narrow ? unchecked((uint)value) : unchecked((ulong)value));
    }

    // The caller reuses the actual kind so literal admission adds no native getter.
    internal static IrIntegerTerm? DecodeIntegerLiteral(
        IrFactory factory, Expr expression, out Z3_decl_kind kind)
    {
        kind = expression.FuncDecl.DeclKind;
        NativeIntegerNumeral? numeral = null;
        switch (kind)
        {
            case Z3_decl_kind.Z3_OP_ANUM when expression is IntNum literal &&
                literal.BigInteger >= long.MinValue && literal.BigInteger <= long.MaxValue:
                numeral = FromSignedIntegerLiteral((long)literal.BigInteger);
                break;
            case Z3_decl_kind.Z3_OP_BNUM when expression is BitVecNum number:
                numeral = ReadBitVectorNumeral(number);
                break;
        }
        if (numeral is not { } value)
        { return null; }
        var type = factory.GetOrCreateIntegerType(value.Width, true);
        return factory.IntegerBits(type, value.Bits);
    }

    // Admit a native unsigned numeral for a CLR length without allocating a witness.
    internal static int? ReadNonnegativeInt32(Expr expression)
    {
        if (expression is not BitVecNum number || number.UInt64 > int.MaxValue)
        { return null; }
        return (int)number.UInt64;
    }

    internal static IrValue? CreateBooleanValue(IrFactory factory, Expr expression)
    {
        return expression is not BoolExpr ? null : expression.BoolValue switch
        {
            Z3_lbool.Z3_L_TRUE => factory.CreateBooleanValue(true),
            Z3_lbool.Z3_L_FALSE => factory.CreateBooleanValue(false),
            _ => null
        };
    }
}

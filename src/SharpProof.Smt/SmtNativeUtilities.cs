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
        solver = RepositoryParameterGuard.NotNull(solver, nameof(solver));

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
        name = RepositoryParameterGuard.NotNull(name, nameof(name));

        using (name)
        {
            parameters = RepositoryParameterGuard.NotNull(parameters, nameof(parameters));

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
        number = RepositoryParameterGuard.NotNull(number, nameof(number));
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
        expression = RepositoryParameterGuard.NotNull(expression, nameof(expression));
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
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
        expression = RepositoryParameterGuard.NotNull(expression, nameof(expression));
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));

        return expression is not BoolExpr ? null : expression.BoolValue switch
        {
            Z3_lbool.Z3_L_TRUE => factory.CreateBooleanValue(true),
            Z3_lbool.Z3_L_FALSE => factory.CreateBooleanValue(false),
            _ => null
        };
    }

    // Rebuild a native application from its recursively decoded children in Args order.
    // The caller owns native traversal, terminal/state admission and resource accounting.
    internal static IrTerm? ReconstructHornApplication(
        IrFactory factory, Expr expression, ImmutableArray<IrTerm?> operands)
    {
        expression = RepositoryParameterGuard.NotNull(expression, nameof(expression));
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
        if (operands.Any(operand => operand == null))
        { return null; }
        var terms = operands.Select(operand => operand!).ToImmutableArray();
        var kind = expression.FuncDecl.DeclKind;
        return kind switch
        {
            Z3_decl_kind.Z3_OP_AND => Fold(factory, IrBinaryOperator.AndAlso, terms),
            Z3_decl_kind.Z3_OP_OR => Fold(factory, IrBinaryOperator.OrElse, terms),
            Z3_decl_kind.Z3_OP_NOT when terms.Length == 1 => factory.Unary(IrUnaryOperator.Not, terms[0]),
            Z3_decl_kind.Z3_OP_IMPLIES when terms.Length == 2 =>
                factory.Binary(IrBinaryOperator.OrElse, factory.Unary(IrUnaryOperator.Not, terms[0]), terms[1]),
            Z3_decl_kind.Z3_OP_EQ when terms.Length == 2 => Compare(factory, IrBinaryOperator.Equal, terms[0], terms[1], null),
            Z3_decl_kind.Z3_OP_DISTINCT when terms.Length == 2 => Compare(factory, IrBinaryOperator.NotEqual, terms[0], terms[1], null),
            Z3_decl_kind.Z3_OP_ITE when terms.Length == 3 && terms[0].Type == factory.BooleanType => Conditional(factory, terms[0], terms[1], terms[2]),
            Z3_decl_kind.Z3_OP_ADD or Z3_decl_kind.Z3_OP_BADD => Fold(factory, IrBinaryOperator.Add, terms),
            Z3_decl_kind.Z3_OP_MUL or Z3_decl_kind.Z3_OP_BMUL => Fold(factory, IrBinaryOperator.Multiply, terms),
            Z3_decl_kind.Z3_OP_SUB or Z3_decl_kind.Z3_OP_BSUB when kind == Z3_decl_kind.Z3_OP_SUB || terms.Length == 2 =>
                Fold(factory, IrBinaryOperator.Subtract, terms),
            Z3_decl_kind.Z3_OP_UMINUS or Z3_decl_kind.Z3_OP_BNEG when terms.Length == 1 =>
                factory.Unary(IrUnaryOperator.Negate, terms[0]),
            Z3_decl_kind.Z3_OP_LE or Z3_decl_kind.Z3_OP_SLEQ or Z3_decl_kind.Z3_OP_ULEQ when terms.Length == 2 =>
                Compare(factory, IrBinaryOperator.LessThanOrEqual, terms[0], terms[1], ComparisonSignedness(kind)),
            Z3_decl_kind.Z3_OP_LT or Z3_decl_kind.Z3_OP_SLT or Z3_decl_kind.Z3_OP_ULT when terms.Length == 2 =>
                Compare(factory, IrBinaryOperator.LessThan, terms[0], terms[1], ComparisonSignedness(kind)),
            Z3_decl_kind.Z3_OP_GE or Z3_decl_kind.Z3_OP_SGEQ or Z3_decl_kind.Z3_OP_UGEQ when terms.Length == 2 =>
                Compare(factory, IrBinaryOperator.GreaterThanOrEqual, terms[0], terms[1], ComparisonSignedness(kind)),
            Z3_decl_kind.Z3_OP_GT or Z3_decl_kind.Z3_OP_SGT or Z3_decl_kind.Z3_OP_UGT when terms.Length == 2 =>
                Compare(factory, IrBinaryOperator.GreaterThan, terms[0], terms[1], ComparisonSignedness(kind)),
            _ => null
        };
    }

    private static bool? ComparisonSignedness(Z3_decl_kind kind)
    {
        return kind switch
        {
            Z3_decl_kind.Z3_OP_SLEQ or Z3_decl_kind.Z3_OP_SLT or Z3_decl_kind.Z3_OP_SGEQ or Z3_decl_kind.Z3_OP_SGT => true,
            Z3_decl_kind.Z3_OP_ULEQ or Z3_decl_kind.Z3_OP_ULT or Z3_decl_kind.Z3_OP_UGEQ or Z3_decl_kind.Z3_OP_UGT => false,
            _ => null
        };
    }

    internal static IrTerm? Fold(IrFactory factory, IrBinaryOperator @operator, ImmutableArray<IrTerm> terms)
    {
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
        if (terms.Length == 0)
        { return null; }
        var result = terms[0];
        foreach (var term in terms.Skip(1))
        {
            var (left, right) = Unify(factory, result, term);
            result = factory.Binary(@operator, left, right);
        }
        return result;
    }

    // Bitvector comparisons choose their signedness; IR comparisons take
    // it from their operands' type.
    private static IrTerm Compare(IrFactory factory, IrBinaryOperator @operator, IrTerm left, IrTerm right, bool? signed)
    {
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
        left = RepositoryParameterGuard.NotNull(left, nameof(left));
        right = RepositoryParameterGuard.NotNull(right, nameof(right));
        (left, right) = Unify(factory, left, right);
        if (signed is { } kind && factory.GetTypeInfo(left.Type) is { Kind: IrTypeKind.Integer } info && info.Signed != kind)
        {
            var type = factory.GetOrCreateIntegerType(info.Width, kind);
            (left, right) = (factory.Cast(type, left), factory.Cast(type, right));
        }
        return factory.Binary(@operator, left, right);
    }

    private static IrTerm Conditional(IrFactory factory, IrTerm condition, IrTerm whenTrue, IrTerm whenFalse)
    {
        condition = RepositoryParameterGuard.NotNull(condition, nameof(condition));
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
        whenFalse = RepositoryParameterGuard.NotNull(whenFalse, nameof(whenFalse));
        whenTrue = RepositoryParameterGuard.NotNull(whenTrue, nameof(whenTrue));
        var (left, right) = Unify(factory, whenTrue, whenFalse);
        return factory.Conditional(condition, left, right);
    }

    // A literal takes the type of the term it meets.
    private static (IrTerm Left, IrTerm Right) Unify(IrFactory factory, IrTerm left, IrTerm right)
    {
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
        left = RepositoryParameterGuard.NotNull(left, nameof(left));
        right = RepositoryParameterGuard.NotNull(right, nameof(right));
        return left is IrIntegerTerm && right is not IrIntegerTerm ? (Like(factory, left, right.Type), right) : (left, Like(factory, right, left.Type));
    }

    private static IrTerm Like(IrFactory factory, IrTerm term, IrTypeId type)
    { factory = RepositoryParameterGuard.NotNull(factory, nameof(factory)); term = RepositoryParameterGuard.NotNull(term, nameof(term)); RepositoryParameterGuard.ThrowIfInvalid(type.IsDefault, nameof(type)); return term.Type == type || factory.GetTypeInfo(type).Kind != IrTypeKind.Integer ? term : factory.Cast(type, term); }

    internal static ImmutableArray<IrValue> WithArrayObservations(
        IrFactory factory, IrTypeId elementType, ImmutableArray<IrValue> elements,
        ImmutableDictionary<int, IrValue> observed)
    {
        RepositoryParameterGuard.ThrowIfInvalid(elementType.IsDefault, nameof(elementType));
        RepositoryParameterGuard.ThrowIfInvalid(elements.IsDefault, nameof(elements));
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
        observed = RepositoryParameterGuard.NotNull(observed, nameof(observed));
        var result = elements.ToBuilder();
        foreach (var entry in observed)
        {
            if (entry.Key < 0 || entry.Key >= elements.Length)
            { throw new UnsupportedIrEncodingException(); }
            var value = entry.Value.Type == elementType ? entry.Value
                : factory.CreateIntegerValueFromBits(elementType, entry.Value.IntegerBits);
            result[entry.Key] = value;
        }
        return result.ToImmutable();
    }

    // This explicitly in-place API retains partial writes on failure, as tested
    // by ArrayValueOverlayTests. Production witness construction uses copies.
    internal static void ApplyArrayObservations(IrFactory factory, IrTypeId elementType,
        IrValue[] elements, ImmutableArray<KeyValuePair<int, IrValue>> observed)
    {
        RepositoryParameterGuard.ThrowIfInvalid(elementType.IsDefault, nameof(elementType));
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
        RepositoryParameterGuard.RequireInitialized(observed.IsDefault, nameof(observed), "Array must be initialized.");
        foreach (var entry in observed)
        {
            if (entry.Key < 0 || entry.Key >= elements.Length)
            { throw new UnsupportedIrEncodingException(); }
            elements[entry.Key] = entry.Value.Type == elementType ? entry.Value
                : factory.CreateIntegerValueFromBits(elementType, entry.Value.IntegerBits);
        }
    }
}

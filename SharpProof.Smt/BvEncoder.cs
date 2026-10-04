namespace SharpProof.Smt;

internal sealed partial class BvEncoder(Context context, IrFactory factory, Z3ExpressionOwner owner)
{
    private readonly Dictionary<IrId, Expr> _encoded = [];
    private readonly Dictionary<IrVarId, Expr> _variables = [];

    internal void ValidateQuery(VerificationQuery query, SmtQueryResourceMeter meter, CancellationToken cancellationToken)
    {
        var depths = new Dictionary<IrId, int>();
        foreach (var assumption in query.Assumptions)
        {
            SmtEncodingDepth.Validate(assumption.Predicate, depths, meter, cancellationToken);
        }
        SmtEncodingDepth.Validate(query.Goal.Predicate, depths, meter, cancellationToken);
        foreach (var variable in query.ModelVariables)
        {
            GetVariable(variable, meter);
        }
    }

    internal Expr GetVariable(IrVarId variable, SmtQueryResourceMeter meter)
    {
        meter.Consume();
        if (_variables.TryGetValue(variable, out var existing))
        {
            return existing;
        }
        var type = factory.GetTypeInfo(factory.GetVariableInfo(variable).Type);
        var name = "v" + _variables.Count.ToString(CultureInfo.InvariantCulture);
        Expr expression = type.Kind == IrTypeKind.Boolean ? owner.Own(context.MkBoolConst(name))
            : IsInteger(type) ? owner.Own(context.MkBVConst(name, (uint)type.Width))
            : IsReference(type) ? owner.Own(context.MkConst(name, ReferenceSort))
            : throw new UnsupportedIrEncodingException();
        _variables.Add(variable, expression);
        if (type.Kind == IrTypeKind.String)
        { EncodeEmptyStringIdentity(expression, meter); }
        return expression;
    }

    internal BoolExpr EncodeBoolean(IrTerm term, SmtQueryResourceMeter meter)
    {
        return Encode(term, meter) as BoolExpr ?? throw new UnsupportedIrEncodingException();
    }

    private Expr Encode(IrTerm term, SmtQueryResourceMeter meter)
    {
        meter.PollCancellation();
        if (_encoded.TryGetValue(term.Id, out var existing))
        {
            return existing;
        }
        meter.Consume();
        var expression = term switch
        {
            IrBooleanTerm boolean => owner.Own(boolean.Value ? context.MkTrue() : context.MkFalse()),
            IrIntegerTerm integer when IsInteger(factory.GetTypeInfo(integer.Type)) =>
                owner.Own(context.MkBV(integer.Bits, (uint)factory.GetTypeInfo(integer.Type).Width)),
            IrVariableTerm variable => GetVariable(variable.Variable, meter),
            IrNullTerm => NullReference,
            IrStringTerm text => EncodeStringLiteral(text, meter),
            IrEmptyArrayTerm empty => EncodeEmptyArray(empty, meter),
            IrLengthTerm length => EncodeLength(Encode(length.Value, meter), meter),
            IrSequenceAccessTerm access => EncodeArrayAccess(access, meter),
            IrUnaryTerm unary => EncodeUnary(unary, meter),
            IrBinaryTerm binary => EncodeBinary(binary, meter),
            IrConditionalTerm conditional => owner.Own(context.MkITE(EncodeBoolean(conditional.Condition, meter),
                Encode(conditional.WhenTrue, meter), Encode(conditional.WhenFalse, meter))),
            IrCastTerm cast => EncodeCast(cast, meter),
            IrOpaqueTerm field when IrFieldSites.IsFieldRead(factory, field) => EncodeFieldRead(field, meter),
            _ => throw new UnsupportedIrEncodingException()
        };
        _encoded.Add(term.Id, expression);
        return expression;
    }

    private Expr EncodeUnary(IrUnaryTerm unary, SmtQueryResourceMeter meter)
    {
        var operand = Encode(unary.Operand, meter);
        return unary.Operator switch
        {
            IrUnaryOperator.Not when operand is BoolExpr boolean => owner.Own(context.MkNot(boolean)),
            IrUnaryOperator.Negate when operand is BitVecExpr integer => owner.Own(context.MkBVNeg(integer)),
            _ => throw new UnsupportedIrEncodingException()
        };
    }

    private Expr EncodeBinary(IrBinaryTerm binary, SmtQueryResourceMeter meter)
    {
        var left = Encode(binary.Left, meter);
        var right = Encode(binary.Right, meter);
        switch (binary.Operator)
        {
            case IrBinaryOperator.StringConcat:
                return EncodeStringConcat(binary, left, right, meter);
            case IrBinaryOperator.StringEquals:
                return EncodeStringEquals(left, right, meter);
            case IrBinaryOperator.Equal:
                return owner.Own(context.MkEq(left, right));
            case IrBinaryOperator.NotEqual:
                return owner.Own(context.MkNot(owner.Own(context.MkEq(left, right))));
            case IrBinaryOperator.AndAlso when left is BoolExpr a && right is BoolExpr b:
                return owner.Own(context.MkAnd(a, b));
            case IrBinaryOperator.OrElse when left is BoolExpr a && right is BoolExpr b:
                return owner.Own(context.MkOr(a, b));
        }
        if (left is not BitVecExpr x || right is not BitVecExpr y)
        {
            throw new UnsupportedIrEncodingException();
        }
        if (binary.Left is IrIntegerTerm { Bits: 0 } && binary.Operator is IrBinaryOperator.Divide or IrBinaryOperator.Remainder)
        {
            var width = factory.GetTypeInfo(binary.Left.Type).Width;
            var zero = owner.Own(context.MkBV(0, (uint)width));
            if (binary.Operator == IrBinaryOperator.Remainder)
            { return zero; }
            var mask = width == 64 ? ulong.MaxValue : (1UL << width) - 1;
            return owner.Own(context.MkITE(owner.Own(context.MkEq(y, zero)),
                owner.Own(context.MkBV(mask, (uint)width)), zero));
        }
        var signed = factory.GetTypeInfo(binary.Left.Type).Signed;
        return owner.Own(binary.Operator switch
        {
            IrBinaryOperator.Add => (Expr)context.MkBVAdd(x, y),
            IrBinaryOperator.Subtract => context.MkBVSub(x, y),
            IrBinaryOperator.Multiply => context.MkBVMul(x, y),
            IrBinaryOperator.Divide => signed ? context.MkBVSDiv(x, y) : context.MkBVUDiv(x, y),
            IrBinaryOperator.Remainder => signed ? context.MkBVSRem(x, y) : context.MkBVURem(x, y),
            IrBinaryOperator.LessThan => signed ? context.MkBVSLT(x, y) : context.MkBVULT(x, y),
            IrBinaryOperator.LessThanOrEqual => signed ? context.MkBVSLE(x, y) : context.MkBVULE(x, y),
            IrBinaryOperator.GreaterThan => signed ? context.MkBVSGT(x, y) : context.MkBVUGT(x, y),
            IrBinaryOperator.GreaterThanOrEqual => signed ? context.MkBVSGE(x, y) : context.MkBVUGE(x, y),
            _ => throw new UnsupportedIrEncodingException()
        });
    }

    private Expr EncodeCast(IrCastTerm cast, SmtQueryResourceMeter meter)
    {
        var source = factory.GetTypeInfo(cast.Operand.Type);
        var target = factory.GetTypeInfo(cast.Type);
        // All references share one sort: widening to object and casts between
        // class types keep the reference itself.
        if (cast.Type == factory.ObjectType && source.Kind is IrTypeKind.Reference or IrTypeKind.String or IrTypeKind.Sequence ||
            source.Kind == IrTypeKind.Reference && target.Kind == IrTypeKind.Reference)
        { return Encode(cast.Operand, meter); }
        if (!IsInteger(source) || !IsInteger(target))
        {
            throw new UnsupportedIrEncodingException();
        }
        var value = (BitVecExpr)Encode(cast.Operand, meter);
        if (source.Width == target.Width)
        {
            return value;
        }
        return owner.Own(target.Width < source.Width ? context.MkExtract((uint)target.Width - 1, 0, value)
            : source.Signed ? context.MkSignExt((uint)(target.Width - source.Width), value)
            : context.MkZeroExt((uint)(target.Width - source.Width), value));
    }

    internal static IrValue? CreateValue(IrFactory factory, IrTypeId type, Expr expression)
    {
        var info = factory.GetTypeInfo(type);
        if (info.Kind == IrTypeKind.Boolean)
        {
            return SmtNativeUtilities.CreateBooleanValue(factory, expression);
        }
        if (!IsInteger(info) || expression is not BitVecNum integer)
        {
            return null;
        }
        using var sort = integer.Sort;
        return sort is BitVecSort bitVector && bitVector.Size == info.Width
            ? factory.CreateIntegerValueFromBits(type, integer.UInt64) : null;
    }

    private static bool IsInteger(IrTypeInfo type)
    {
        return type.Kind == IrTypeKind.Integer && type.Width is 8 or 16 or 32 or 64;
    }
}

internal static class SmtEncodingDepth
{
    internal static void Validate(IrTerm root, Dictionary<IrId, int> maximumDepths,
        SmtQueryResourceMeter meter, CancellationToken cancellationToken)
    {
        var pending = new Stack<(IrTerm Term, int Depth)>();
        var children = new Stack<IrTerm>();
        pending.Push((root, 1));
        while (pending.Count != 0)
        {
            meter.Consume();
            cancellationToken.ThrowIfCancellationRequested();
            var (term, depth) = pending.Pop();
            if (depth > 256)
            {
                throw new UnsupportedIrEncodingException();
            }
            if (maximumDepths.TryGetValue(term.Id, out var previous) && previous >= depth)
            {
                continue;
            }
            maximumDepths[term.Id] = depth;
            children.Clear();
            IrTraversal.PushChildren(term, children);
            while (children.Count != 0)
            {
                pending.Push((children.Pop(), depth + 1));
            }
        }
    }
}

namespace SharpProof.Smt;

internal sealed partial class BvEncoder(Context context, IrFactory factory, Z3ExpressionOwner owner, bool enableInvariantRelations = false)
{
    private readonly Dictionary<IrId, Expr> _encoded = (owner is null ? throw new System.ArgumentNullException(nameof(owner)) : (factory is null ? throw new System.ArgumentNullException(nameof(factory)) : (context is null ? throw new System.ArgumentNullException(nameof(context)) : [])));
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
        meter = RepositoryParameterGuard.NotNull(meter, nameof(meter));
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
        // A zero-length string input need not be the interned "" literal:
        // the CLR can allocate distinct empty strings (e.g. string.Copy("")).
        _variables.Add(variable, expression);
        return expression;
    }

    internal BoolExpr EncodeBoolean(IrTerm term, SmtQueryResourceMeter meter)
    {
        return Encode(term, meter) as BoolExpr ?? throw new UnsupportedIrEncodingException();
    }

    private Expr Encode(IrTerm term, SmtQueryResourceMeter meter)
    {
        meter = RepositoryParameterGuard.NotNull(meter, nameof(meter));
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
            IrUnaryTerm or IrBinaryTerm => EncodeOperator(term, meter),
            IrConditionalTerm conditional => owner.Own(context.MkITE(EncodeBoolean(conditional.Condition, meter),
                Encode(conditional.WhenTrue, meter), Encode(conditional.WhenFalse, meter))),
            IrCastTerm cast => EncodeCast(cast, meter),
            IrOpaqueTerm field when IrFieldSites.IsFieldRead(factory, field) => EncodeFieldRead(field, meter),
            IrOpaqueTerm relation when Relations != null && IrInvariantRelations.IsRelation(factory, relation) =>
                EncodeRelation(relation, meter),
            _ => throw new UnsupportedIrEncodingException()
        };
        _encoded.Add(term.Id, expression);
        return expression;
    }

    // Only a Horn search encodes unknown invariants, as uninterpreted relations.
    internal ImmutableDictionary<IrMemberId, FuncDecl>? Relations { get; private set; } =
        enableInvariantRelations ? ImmutableDictionary<IrMemberId, FuncDecl>.Empty : null;
    internal ImmutableList<KeyValuePair<IrMemberId, FuncDecl>> RelationDeclarations { get; private set; } =
        ImmutableList<KeyValuePair<IrMemberId, FuncDecl>>.Empty;

    private Expr EncodeRelation(IrOpaqueTerm relation, SmtQueryResourceMeter meter)
    {
        meter = RepositoryParameterGuard.NotNull(meter, nameof(meter));
        relation = RepositoryParameterGuard.NotNull(relation, nameof(relation));
        var arguments = relation.Arguments.Select(argument => argument is null ? throw new System.ArgumentNullException(nameof(argument)) : (Encode(argument, meter))).ToArray();
        var relations = Relations ?? throw new InvalidOperationException("Invariant relations require Relations to be set.");
        if (!relations.TryGetValue(relation.Member, out var declaration))
        {
            declaration = context.MkFuncDecl("invariant" + relation.Member.Value.ToString(CultureInfo.InvariantCulture),
                [.. arguments.Select(argument => argument is null ? throw new System.ArgumentNullException(nameof(argument)) : (argument.Sort))], context.BoolSort);
            Relations = relations.Add(relation.Member, declaration);
            RelationDeclarations = RelationDeclarations.Add(new(relation.Member, declaration));
        }
        return owner.Own(context.MkApp(declaration, arguments));
    }

    private Expr EncodeOperator(IrTerm term, SmtQueryResourceMeter meter)
    {
        meter = RepositoryParameterGuard.NotNull(meter, nameof(meter));
        term = RepositoryParameterGuard.NotNull(term, nameof(term));
        if (term is IrUnaryTerm unary)
        {
            var operand = Encode(unary.Operand, meter);
            if (unary.Operator == IrUnaryOperator.Not && operand is BoolExpr boolean)
            { return owner.Own(context.MkNot(boolean)); }
            if (unary.Operator == IrUnaryOperator.Negate && operand is BitVecExpr integer)
            { return owner.Own(context.MkBVNeg(integer)); }
        }
        else if (term is IrBinaryTerm binary)
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
            if (left is BitVecExpr x && right is BitVecExpr y)
            {
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
                if (binary.Operator is IrBinaryOperator.ShiftLeft or IrBinaryOperator.ShiftRight)
                {
                    var width = factory.GetTypeInfo(binary.Left.Type).Width;
                    if (width is 32 or 64)
                    {
                        // The CLR masks the count to the operand width.
                        var count = owner.Own(context.MkBVAND(y, owner.Own(context.MkBV(width - 1, (uint)width))));
                        return owner.Own(binary.Operator == IrBinaryOperator.ShiftLeft ? context.MkBVSHL(x, count)
                            : signed ? context.MkBVASHR(x, count) : context.MkBVLSHR(x, count));
                    }
                }
                else
                {
                    switch (binary.Operator)
                    {
                        case IrBinaryOperator.Add:
                            return owner.Own(context.MkBVAdd(x, y));
                        case IrBinaryOperator.Subtract:
                            return owner.Own(context.MkBVSub(x, y));
                        case IrBinaryOperator.Multiply:
                            return owner.Own(context.MkBVMul(x, y));
                        case IrBinaryOperator.BitwiseAnd:
                            return owner.Own(context.MkBVAND(x, y));
                        case IrBinaryOperator.BitwiseOr:
                            return owner.Own(context.MkBVOR(x, y));
                        case IrBinaryOperator.BitwiseXor:
                            return owner.Own(context.MkBVXOR(x, y));
                        case IrBinaryOperator.Divide:
                            return owner.Own(signed ? context.MkBVSDiv(x, y) : context.MkBVUDiv(x, y));
                        case IrBinaryOperator.Remainder:
                            return owner.Own(signed ? context.MkBVSRem(x, y) : context.MkBVURem(x, y));
                        case IrBinaryOperator.LessThan:
                            return owner.Own(signed ? context.MkBVSLT(x, y) : context.MkBVULT(x, y));
                        case IrBinaryOperator.LessThanOrEqual:
                            return owner.Own(signed ? context.MkBVSLE(x, y) : context.MkBVULE(x, y));
                        case IrBinaryOperator.GreaterThan:
                            return owner.Own(signed ? context.MkBVSGT(x, y) : context.MkBVUGT(x, y));
                        case IrBinaryOperator.GreaterThanOrEqual:
                            return owner.Own(signed ? context.MkBVSGE(x, y) : context.MkBVUGE(x, y));
                    }
                }
            }
        }
        // Wrong operand classes, unsupported shifts and unknown operators share
        // one refusal; no rejected operator receives an alternative meaning.
        throw new UnsupportedIrEncodingException();
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
        var value = Encode(cast.Operand, meter) as BitVecExpr ?? throw new UnsupportedIrEncodingException();
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
        RepositoryParameterGuard.ThrowIfInvalid(type.IsDefault, nameof(type));
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
        var info = factory.GetTypeInfo(type);
        if (info.Kind == IrTypeKind.Boolean)
        {
            return SmtNativeUtilities.CreateBooleanValue(factory, expression);
        }
        if (!IsInteger(info) || expression is not BitVecNum integer)
        {
            return null;
        }
        var numeral = SmtNativeUtilities.ReadBitVectorNumeral(integer);
        return numeral is { } value && value.Width == info.Width
            ? factory.CreateIntegerValueFromBits(type, value.Bits) : null;
    }

    private static bool IsInteger(IrTypeInfo type)
    {
        type = RepositoryParameterGuard.NotNull(type, nameof(type));
        return type.Kind == IrTypeKind.Integer && type.Width is 8 or 16 or 32 or 64;
    }
}

internal static class SmtEncodingDepth
{
    internal static void Validate(IrTerm root, Dictionary<IrId, int> maximumDepths,
        SmtQueryResourceMeter meter, CancellationToken cancellationToken)
    {
        maximumDepths = RepositoryParameterGuard.NotNull(maximumDepths, nameof(maximumDepths));
        meter = RepositoryParameterGuard.NotNull(meter, nameof(meter));
        root = RepositoryParameterGuard.NotNull(root, nameof(root));
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

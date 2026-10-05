namespace SharpProof.Smt;

internal sealed partial class BvEncoder
{
    private Sort? _referenceSort;
    private FuncDecl? _length;
    private Expr? _nullReference;
    private readonly Dictionary<IrTypeId, Expr> _emptyArrays = [];
    private readonly Dictionary<IrStringId, Expr> _stringLiterals = [];
    internal List<BoolExpr> ReferenceFacts { get; } = [];

    private Expr EncodeStringLiteral(IrStringTerm term, SmtQueryResourceMeter meter)
    {
        if (_stringLiterals.TryGetValue(term.Value, out var existing))
        { return existing; }
        var value = owner.Own(context.MkConst("text" + term.Value.Value.ToString(CultureInfo.InvariantCulture), ReferenceSort));
        ReferenceFacts.Add(owner.Own(context.MkNot(owner.Own(context.MkEq(value, NullReference)))));
        ReferenceFacts.Add(owner.Own(context.MkEq(EncodeLength(value, meter),
            owner.Own(context.MkBV(factory.GetString(term.Value).Length, 32)))));
        foreach (var literal in _stringLiterals.Values)
        {
            meter.Consume();
            ReferenceFacts.Add(owner.Own(context.MkNot(owner.Own(context.MkEq(value, literal)))));
        }
        _stringLiterals.Add(term.Value, value);
        if (_text != null)
        { AddLiteralText(value, factory.GetString(term.Value), meter); }
        return value;
    }

    private void EncodeEmptyStringIdentity(Expr value, SmtQueryResourceMeter meter)
    {
        var empty = EncodeStringLiteral(factory.String(""), meter);
        var zeroLength = owner.Own(context.MkEq(EncodeLength(value, meter), owner.Own(context.MkBV(0, 32))));
        ReferenceFacts.Add(owner.Own(context.MkOr(owner.Own(context.MkEq(value, NullReference)),
            owner.Own(context.MkNot(zeroLength)), owner.Own(context.MkEq(value, empty)))));
    }

    private Expr EncodeStringConcat(IrBinaryTerm term, Expr left, Expr right, SmtQueryResourceMeter meter)
    {
        // Empty operands preserve CLR aliases. Two nonempty operands make a
        // fresh non-null string; once a query reads content, its content is
        // the operands' content in order and its length their total. SAT
        // still requires replay.
        var value = owner.Own(context.MkConst("concat" + term.Id.Value.ToString(CultureInfo.InvariantCulture), ReferenceSort));
        ReferenceFacts.Add(owner.Own(context.MkNot(owner.Own(context.MkEq(value, NullReference)))));
        var empty = EncodeStringLiteral(factory.String(""), meter);
        RegisterConcatenation(value, left, right, meter);
        var leftEmpty = owner.Own(context.MkEq(EncodeLength(left, meter), owner.Own(context.MkBV(0, 32))));
        var rightEmpty = owner.Own(context.MkEq(EncodeLength(right, meter), owner.Own(context.MkBV(0, 32))));
        return owner.Own(context.MkITE(leftEmpty, owner.Own(context.MkITE(rightEmpty, empty, right)),
            owner.Own(context.MkITE(rightEmpty, left, value))));
    }

    private Expr EncodeEmptyArray(IrEmptyArrayTerm term, SmtQueryResourceMeter meter)
    {
        if (_emptyArrays.TryGetValue(term.Type, out var existing))
        { return existing; }
        var value = owner.Own(context.MkConst("empty" + term.Type.Value.ToString(CultureInfo.InvariantCulture), ReferenceSort));
        ReferenceFacts.Add(owner.Own(context.MkNot(owner.Own(context.MkEq(value, NullReference)))));
        ReferenceFacts.Add(owner.Own(context.MkEq(EncodeLength(value, meter), owner.Own(context.MkBV(0, 32)))));
        _emptyArrays.Add(term.Type, value);
        return value;
    }

    private Sort ReferenceSort => _referenceSort ??= context.MkUninterpretedSort("Ref");
    private Expr NullReference => _nullReference ??= owner.Own(context.MkConst("nullRef", ReferenceSort));

    private static bool IsReference(IrTypeInfo type)
    {
        return type.Kind is IrTypeKind.Reference or IrTypeKind.Sequence or IrTypeKind.String;
    }

    private Expr EncodeLength(Expr value, SmtQueryResourceMeter meter)
    {
        meter.Consume();
        if (_length == null)
        {
            // Every C# length spans 0..Int32.MaxValue. Encoding that intrinsic
            // range in the result sort adds no input cap or proof premise.
            using var result = context.MkBitVecSort(31);
            _length = context.MkFuncDecl("len", ReferenceSort, result);
        }
        var length = owner.Own(context.MkZeroExt(1, (BitVecExpr)owner.Own(context.MkApp(_length, value))));
        return owner.Own(context.MkITE(owner.Own(context.MkEq(value, NullReference)), owner.Own(context.MkBV(0, 32)), length));
    }

    internal Dictionary<IrVarId, IrValue>? DecodeModel(VerificationQuery query, Model model, SmtQueryResourceMeter meter)
    {
        var values = new Dictionary<IrVarId, IrValue>();
        var aliases = new Dictionary<(IrTypeId Type, string Token), IrValue>();
        var certifiedTypes = new Dictionary<string, IrTypeId>(StringComparer.Ordinal);
        foreach (var empty in _emptyArrays)
        {
            meter.Consume();
            using var evaluated = model.Evaluate(empty.Value, true);
            aliases.Add((empty.Key, evaluated.ToString()), factory.CreateEmptyArrayValue(empty.Key));
        }
        var identities = new Dictionary<string, object>(StringComparer.Ordinal);
        string? emptyStringToken = null;
        foreach (var literal in _stringLiterals)
        {
            meter.Consume();
            using var evaluated = model.Evaluate(literal.Value, true);
            var token = evaluated.ToString();
            var text = factory.GetString(literal.Key);
            aliases.Add((factory.StringType, token), factory.CreateStringValue(text));
            if (text.Length == 0)
            { emptyStringToken = token; }
        }
        var observations = DecodeArrayObservations(model, meter);
        // Strings and arrays decode first, so a reference that shares a token
        // with one (an object-typed view of it) keeps it as its identity, as
        // a Total cast to object does.
        var shared = new Dictionary<string, object>(StringComparer.Ordinal);
        using var nullValue = model.Evaluate(NullReference, true);
        foreach (var variable in query.ModelVariables.OrderBy(variable =>
            factory.GetTypeInfo(factory.GetVariableInfo(variable).Type).Kind == IrTypeKind.Reference))
        {
            meter.Consume();
            using var evaluated = model.Evaluate(GetVariable(variable, meter), true);
            if (Decode(factory.GetVariableInfo(variable).Type, evaluated) is not { } value)
            { return null; }
            values.Add(variable, value);
        }
        return values;

        // An object decodes once per model token; its fields, references
        // included, decode after it is registered, so cycles terminate.
        IrValue? Decode(IrTypeId type, Expr evaluated)
        {
            var info = factory.GetTypeInfo(type);
            if (!IsReference(info))
            { return CreateValue(factory, type, evaluated); }
            if (evaluated.Equals(nullValue))
            { return factory.CreateNullValue(type); }
            var token = evaluated.ToString();
            if (factory.IsClosedSealedReferenceType(type))
            {
                meter.Consume();
                if (certifiedTypes.TryGetValue(token, out var previous) && previous != type)
                { throw new UnsupportedIrEncodingException(); }
                certifiedTypes[token] = type;
            }
            if (aliases.TryGetValue((type, token), out var value))
            { return value; }
            if (info.Kind == IrTypeKind.Reference)
            {
                if (!identities.TryGetValue(token, out var identity))
                {
                    if (shared.TryGetValue(token, out var view))
                    { identities.Add(token, identity = view); }
                    else
                    {
                        var state = new IrObjectState();
                        identities.Add(token, identity = state);
                        FillObjectState(state, evaluated, model, meter, Decode);
                    }
                }
                value = factory.CreateReferenceValue(type, identity);
            }
            else
            {
                using var length = model.Evaluate(EncodeLength(evaluated, meter), true);
                if (length is not BitVecNum number || number.UInt64 > int.MaxValue)
                { return null; }
                var count = (int)number.UInt64;
                // Charge and check cancellation before allocating any witness data.
                // Refusing a large SAT witness never narrows the proof input domain.
                meter.Consume(count + 1L);
                if (info.Kind == IrTypeKind.String && count == 0)
                {
                    // CLR construction canonicalizes empty strings. Never
                    // replay distinct Ref tokens as that same concrete object.
                    if (emptyStringToken != null && emptyStringToken != token)
                    { throw new UnsupportedIrEncodingException(); }
                    emptyStringToken = token;
                }
                var text = info.Kind == IrTypeKind.String ? DecodeText(evaluated, model, meter) : null;
                value = info.Kind == IrTypeKind.String
                    ? factory.CreateStringValue(text?.Length == count ? text : new string('\0', count))
                    : DecodeArrayWitness(type, count, token, observations);
                if (value == null)
                { return null; }
                if (!shared.ContainsKey(token))
                { shared.Add(token, info.Kind == IrTypeKind.String ? value.String : value); }
            }
            // A cyclic object may already have aliased itself while its
            // fields decoded.
            aliases[(type, token)] = value;
            return value;
        }
    }

    private IrValue DefaultValue(IrTypeId type)
    {
        var info = factory.GetTypeInfo(type);
        return info.Kind == IrTypeKind.Boolean ? factory.CreateBooleanValue(false)
            : IsInteger(info) ? factory.CreateIntegerValue(type, 0L)
            : IsReference(info) ? factory.CreateNullValue(type)
            : throw new UnsupportedIrEncodingException();
    }

    internal void DisposeReferences()
    {
        foreach (var element in _elements.Values)
        { element.Dispose(); }
        _length?.Dispose();
        DisposeStrings();
        DisposeFields();
        _referenceSort?.Dispose();
    }
}

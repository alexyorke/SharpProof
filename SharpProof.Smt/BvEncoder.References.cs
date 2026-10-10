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
        foreach (var literal in _stringLiterals.Values.Concat(_emptyArrays.Values))
        {
            meter.Consume();
            AddDistinctReferenceFact(value, literal);
        }
        _stringLiterals.Add(term.Value, value);
        if (_text != null)
        { AddLiteralText(value, factory.GetString(term.Value), meter); }
        return value;
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
        foreach (var previous in _emptyArrays)
        {
            meter.Consume();
            if (HaveDistinctScalarElements(term.Type, previous.Key))
            { AddDistinctReferenceFact(value, previous.Value); }
        }
        foreach (var literal in _stringLiterals.Values)
        {
            meter.Consume();
            AddDistinctReferenceFact(value, literal);
        }
        _emptyArrays.Add(term.Type, value);
        return value;
    }

    // Distinct closed scalar T values have separate Array.Empty<T> caches.
    // Do not infer identity separation from nominal array names or from
    // nullable annotations that may describe the same runtime element type.
    private bool HaveDistinctScalarElements(IrTypeId left, IrTypeId right)
    {
        var first = factory.GetTypeInfo(left).ElementType!.Value;
        var second = factory.GetTypeInfo(right).ElementType!.Value;
        return first != second &&
            factory.GetTypeInfo(first).Kind is IrTypeKind.Boolean or IrTypeKind.Integer or IrTypeKind.String &&
            factory.GetTypeInfo(second).Kind is IrTypeKind.Boolean or IrTypeKind.Integer or IrTypeKind.String;
    }

    private void AddDistinctReferenceFact(Expr left, Expr right)
    {
        ReferenceFacts.Add(owner.Own(context.MkNot(owner.Own(context.MkEq(left, right)))));
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
        var shared = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var empty in _emptyArrays)
        {
            meter.Consume();
            using var evaluated = model.Evaluate(empty.Value, true);
            var token = evaluated.ToString();
            var value = factory.CreateEmptyArrayValue(empty.Key);
            CertifyType(token, empty.Key);
            aliases.Add((empty.Key, token), value);
            if (!shared.ContainsKey(token))
            { shared.Add(token, value); }
        }
        var identities = new Dictionary<string, object>(StringComparer.Ordinal);
        string? emptyStringToken = null;
        foreach (var literal in _stringLiterals)
        {
            meter.Consume();
            using var evaluated = model.Evaluate(literal.Value, true);
            var token = evaluated.ToString();
            var text = factory.GetString(literal.Key);
            CertifyType(token, factory.StringType);
            aliases.Add((factory.StringType, token), factory.CreateStringValue(text));
            if (!shared.ContainsKey(token))
            { shared.Add(token, text); }
            if (text.Length == 0)
            { emptyStringToken = token; }
        }
        var observations = DecodeArrayObservations(model, meter);
        // Decode built-ins, then nominal views, then object views. A nominal
        // view cannot borrow a built-in identity without assignability evidence.
        // Object views use the resulting identity; replay validates any repair.
        using var nullValue = model.Evaluate(NullReference, true);
        foreach (var variable in query.ModelVariables.OrderBy(variable =>
        {
            var type = factory.GetVariableInfo(variable).Type;
            return factory.GetTypeInfo(type).Kind != IrTypeKind.Reference ? 0 : type == factory.ObjectType ? 2 : 1;
        }))
        {
            meter.Consume();
            using var evaluated = model.Evaluate(GetVariable(variable, meter), true);
            if (Decode(factory.GetVariableInfo(variable).Type, evaluated) is not { } value)
            { return null; }
            values.Add(variable, value);
        }
        return values;

        void CertifyType(string token, IrTypeId type)
        {
            if (certifiedTypes.TryGetValue(token, out var previous) && previous != type)
            { throw new UnsupportedIrEncodingException(); }
            certifiedTypes[token] = type;
        }

        // An object decodes once per model token; its fields, references
        // included, decode after it is registered, so cycles terminate.
        IrValue? Decode(IrTypeId type, Expr evaluated)
        {
            var info = factory.GetTypeInfo(type);
            if (!IsReference(info))
            { return CreateValue(factory, type, evaluated); }
            if (evaluated.Equals(nullValue))
            { return factory.CreateNullValue(type); }
            var modelToken = evaluated.ToString();
            var token = modelToken;
            // Split an uncertified nominal alias from a built-in. If the query
            // needs that alias, concrete replay rejects the repaired witness;
            // an unrelated field-write witness can still replay successfully.
            if (info.Kind == IrTypeKind.Reference && type != factory.ObjectType &&
                certifiedTypes.TryGetValue(token, out var runtimeType) &&
                factory.GetTypeInfo(runtimeType).Kind is IrTypeKind.String or IrTypeKind.Sequence)
            { token = "nominal:" + token; }
            if (factory.IsClosedSealedReferenceType(type))
            {
                meter.Consume();
                CertifyType(token, type);
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
                        if (token != modelToken)
                        { identities[modelToken] = state; }
                        FillObjectState(state, evaluated, model, meter, Decode);
                    }
                }
                value = factory.CreateReferenceValue(type, identity);
            }
            else
            {
                // Incompatible built-in views get separate concrete values.
                // Compatible integer arrays retain their shared storage identity;
                // replay checks every concrete witness.
                if (certifiedTypes.TryGetValue(token, out var previous) && previous != type &&
                    factory.GetTypeInfo(previous).Kind == IrTypeKind.Reference)
                { throw new UnsupportedIrEncodingException(); }
                if (!certifiedTypes.ContainsKey(token))
                { certifiedTypes.Add(token, type); }
                using var length = model.Evaluate(EncodeLength(evaluated, meter), true);
                if (length is not BitVecNum number || number.UInt64 > int.MaxValue)
                { return null; }
                var count = (int)number.UInt64;
                // Charge and check cancellation before allocating any witness data.
                // Refusing a large SAT witness never narrows the proof input domain.
                meter.Consume(count + 1L);
                // The CLR can hold empty strings other than the interned "".
                // Never replay distinct Ref tokens as that same concrete object.
                var distinctEmpty = info.Kind == IrTypeKind.String && count == 0 &&
                    emptyStringToken != null && emptyStringToken != token;
                if (info.Kind == IrTypeKind.String && count == 0 && !distinctEmpty)
                { emptyStringToken = token; }
                var text = info.Kind == IrTypeKind.String ? DecodeText(evaluated, model, meter) : null;
                value = info.Kind == IrTypeKind.String
                    ? factory.CreateStringValue(distinctEmpty ? DistinctEmptyString()
                        : text?.Length == count ? text : new string('\0', count))
                    : DecodeArrayWitness(type, count, token, observations);
                if (value == null)
                { return null; }
                if (value.Kind == IrValueKind.Sequence && shared.TryGetValue(token, out var storage) &&
                    storage is IrValue { Kind: IrValueKind.Sequence } array &&
                    IrArrayStorage.CompatibleIntegerViews(factory, array.Type, type))
                { value = IrValue.WithSequenceIdentity(value, array); }
                if (!shared.ContainsKey(token))
                { shared.Add(token, info.Kind == IrTypeKind.String ? value.String : value); }
            }
            // A cyclic object may already have aliased itself while its
            // fields decoded.
            aliases[(type, token)] = value;
            return value;
        }
    }

    // A fresh zero-length instance, as string.Copy("") produces on the CLR.
    private static string DistinctEmptyString()
    {
#pragma warning disable CS0618
        return string.Copy(string.Empty);
#pragma warning restore CS0618
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

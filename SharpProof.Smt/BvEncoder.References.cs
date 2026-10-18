namespace SharpProof.Smt;

internal sealed partial class BvEncoder
{
    private Sort? _referenceSort;
    private FuncDecl? _length;
    private Expr? _nullReference;
    private ImmutableDictionary<IrTypeId, Expr> _emptyArrays = ImmutableDictionary<IrTypeId, Expr>.Empty;
    private ImmutableList<KeyValuePair<IrTypeId, Expr>> _emptyArrayOrder = ImmutableList<KeyValuePair<IrTypeId, Expr>>.Empty;
    private ImmutableDictionary<IrStringId, Expr> _stringLiterals = ImmutableDictionary<IrStringId, Expr>.Empty;
    private ImmutableList<KeyValuePair<IrStringId, Expr>> _stringLiteralOrder = ImmutableList<KeyValuePair<IrStringId, Expr>>.Empty;
    internal ImmutableList<BoolExpr> ReferenceFacts { get; private set; } = ImmutableList<BoolExpr>.Empty;

    private void AddReferenceFact(BoolExpr fact)
    {
        fact = RepositoryParameterGuard.NotNull(fact, nameof(fact));
        ReferenceFacts = ReferenceFacts.Add(fact);
    }

    // Witness lengths are charged when decoded. Prefer small string/array
    // models without restricting the unbounded query used for proofs.
    internal BoolExpr? SmallReferenceBound(VerificationQuery query, SmtQueryResourceMeter meter)
    {
        meter = RepositoryParameterGuard.NotNull(meter, nameof(meter));
        query = RepositoryParameterGuard.NotNull(query, nameof(query));
        var bounds = ImmutableList<BoolExpr>.Empty;
        foreach (var variable in query.ModelVariables)
        {
            meter.Consume();
            if (factory.GetTypeInfo(factory.GetVariableInfo(variable).Type).Kind is IrTypeKind.String or IrTypeKind.Sequence)
            {
                bounds = bounds.Add(owner.Own(context.MkBVULE((BitVecExpr)EncodeLength(GetVariable(variable, meter), meter),
                    owner.Own(context.MkBV(MaximumSmallReference, 32)))));
            }
        }
        return bounds.Count == 0 ? null : owner.Own(context.MkAnd(bounds.ToArray()));
    }

    private const int MaximumSmallReference = 64;

    private Expr EncodeStringLiteral(IrStringTerm term, SmtQueryResourceMeter meter)
    {
        meter = RepositoryParameterGuard.NotNull(meter, nameof(meter));
        if (_stringLiterals.TryGetValue(term.Value, out var existing))
        { return existing; }
        var value = owner.Own(context.MkConst("text" + term.Value.Value.ToString(CultureInfo.InvariantCulture), ReferenceSort));
        AddReferenceFact(owner.Own(context.MkNot(owner.Own(context.MkEq(value, NullReference)))));
        AddReferenceFact(owner.Own(context.MkEq(EncodeLength(value, meter),
            owner.Own(context.MkBV(factory.GetString(term.Value).Length, 32)))));
        foreach (var literal in _stringLiteralOrder.Select(static literal => literal.Value).Concat(_emptyArrayOrder.Select(static empty => empty.Value)))
        {
            meter.Consume();
            AddDistinctReferenceFact(value, literal);
        }
        _stringLiterals = _stringLiterals.AddUnique(term.Value, value);
        _stringLiteralOrder = _stringLiteralOrder.Add(new(term.Value, value));
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
        left = RepositoryParameterGuard.NotNull(left, nameof(left));
        meter = RepositoryParameterGuard.NotNull(meter, nameof(meter));
        right = RepositoryParameterGuard.NotNull(right, nameof(right));
        term = RepositoryParameterGuard.NotNull(term, nameof(term));
        var value = owner.Own(context.MkConst("concat" + term.Id.Value.ToString(CultureInfo.InvariantCulture), ReferenceSort));
        AddReferenceFact(owner.Own(context.MkNot(owner.Own(context.MkEq(value, NullReference)))));
        var empty = EncodeStringLiteral(factory.String(""), meter);
        RegisterConcatenation(value, left, right, meter);
        var leftEmpty = owner.Own(context.MkEq(EncodeLength(left, meter), owner.Own(context.MkBV(0, 32))));
        var rightEmpty = owner.Own(context.MkEq(EncodeLength(right, meter), owner.Own(context.MkBV(0, 32))));
        return owner.Own(context.MkITE(leftEmpty, owner.Own(context.MkITE(rightEmpty, empty, right)),
            owner.Own(context.MkITE(rightEmpty, left, value))));
    }

    private Expr EncodeEmptyArray(IrEmptyArrayTerm term, SmtQueryResourceMeter meter)
    {
        // EmptyArray terms are canonical per type; Encode's successful identity
        // cache has already handled any previously completed construction.
        meter = RepositoryParameterGuard.NotNull(meter, nameof(meter));
        term = RepositoryParameterGuard.NotNull(term, nameof(term));
        var value = owner.Own(context.MkConst("empty" + term.Type.Value.ToString(CultureInfo.InvariantCulture), ReferenceSort));
        AddReferenceFact(owner.Own(context.MkNot(owner.Own(context.MkEq(value, NullReference)))));
        AddReferenceFact(owner.Own(context.MkEq(EncodeLength(value, meter), owner.Own(context.MkBV(0, 32)))));
        foreach (var previous in _emptyArrayOrder)
        {
            meter.Consume();
            if (HaveDistinctScalarElements(term.Type, previous.Key))
            { AddDistinctReferenceFact(value, previous.Value); }
        }
        foreach (var literal in _stringLiteralOrder.Select(static literal => literal.Value))
        {
            meter.Consume();
            AddDistinctReferenceFact(value, literal);
        }
        _emptyArrays = _emptyArrays.AddUnique(term.Type, value);
        _emptyArrayOrder = _emptyArrayOrder.Add(new(term.Type, value));
        return value;
    }

    // Distinct closed scalar T values have separate Array.Empty<T> caches.
    // Do not infer identity separation from nominal array names or from
    // nullable annotations that may describe the same runtime element type.
    private bool HaveDistinctScalarElements(IrTypeId left, IrTypeId right)
    {
        var first = factory.GetTypeInfo(left).ElementType ?? throw new UnsupportedIrEncodingException();
        var second = factory.GetTypeInfo(right).ElementType ?? throw new UnsupportedIrEncodingException();
        return first != second &&
            factory.GetTypeInfo(first).Kind is IrTypeKind.Boolean or IrTypeKind.Integer or IrTypeKind.String &&
            factory.GetTypeInfo(second).Kind is IrTypeKind.Boolean or IrTypeKind.Integer or IrTypeKind.String;
    }

    private void AddDistinctReferenceFact(Expr left, Expr right)
    {
        left = RepositoryParameterGuard.NotNull(left, nameof(left));
        right = RepositoryParameterGuard.NotNull(right, nameof(right));
        AddReferenceFact(owner.Own(context.MkNot(owner.Own(context.MkEq(left, right)))));
    }

    private Sort ReferenceSort => _referenceSort ??= context.MkUninterpretedSort("Ref");
    private Expr NullReference => _nullReference ??= owner.Own(context.MkConst("nullRef", ReferenceSort));

    private static bool IsReference(IrTypeInfo type)
    {
        type = RepositoryParameterGuard.NotNull(type, nameof(type));
        return type.Kind is IrTypeKind.Reference or IrTypeKind.Sequence or IrTypeKind.String;
    }

    private Expr EncodeLength(Expr value, SmtQueryResourceMeter meter)
    {
        meter = RepositoryParameterGuard.NotNull(meter, nameof(meter));
        value = RepositoryParameterGuard.NotNull(value, nameof(value));
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

    internal ImmutableDictionary<IrVarId, IrValue> DecodeModel(VerificationQuery query, Model model, SmtQueryResourceMeter meter)
    {
        meter = RepositoryParameterGuard.NotNull(meter, nameof(meter));
        model = RepositoryParameterGuard.NotNull(model, nameof(model));
        query = RepositoryParameterGuard.NotNull(query, nameof(query));
        var values = ImmutableDictionary<IrVarId, IrValue>.Empty;
        var aliases = ImmutableDictionary<(IrTypeId Type, string Token), IrValue>.Empty;
        var certifiedTypes = ImmutableDictionary.Create<string, IrTypeId>(StringComparer.Ordinal);
        var shared = ImmutableDictionary.Create<string, object>(StringComparer.Ordinal);
        foreach (var empty in _emptyArrayOrder)
        {
            meter.Consume();
            using var evaluated = model.Evaluate(empty.Value, true);
            var token = evaluated.ToString();
            var value = factory.CreateEmptyArrayValue(empty.Key);
            CertifyType(token, empty.Key);
            aliases = aliases.AddUnique((empty.Key, token), value);
            if (!shared.ContainsKey(token))
            { shared = shared.AddUnique(token, value); }
        }
        var identities = ImmutableDictionary.Create<string, object>(StringComparer.Ordinal);
        string? emptyStringToken = null;
        foreach (var literal in _stringLiteralOrder)
        {
            meter.Consume();
            using var evaluated = model.Evaluate(literal.Value, true);
            var token = evaluated.ToString();
            var text = factory.GetString(literal.Key);
            CertifyType(token, factory.StringType);
            aliases = aliases.AddUnique((factory.StringType, token), factory.CreateStringValue(text));
            if (!shared.ContainsKey(token))
            { shared = shared.AddUnique(token, text); }
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
            RepositoryParameterGuard.ThrowIfInvalid(variable.IsDefault, nameof(variable));
            var type = factory.GetVariableInfo(variable).Type;
            return factory.GetTypeInfo(type).Kind != IrTypeKind.Reference ? 0 : type == factory.ObjectType ? 2 : 1;
        }))
        {
            meter.Consume();
            using var evaluated = model.Evaluate(GetVariable(variable, meter), true);
            var value = Decode(factory.GetVariableInfo(variable).Type, evaluated);
            values = values.AddUnique(variable, value);
        }
        return values;

        void CertifyType(string token, IrTypeId type)
        {
            if (certifiedTypes.TryGetValue(token, out var previous) && previous != type)
            { throw new UnsupportedIrEncodingException(); }
            certifiedTypes = certifiedTypes.SetItem(token, type);
        }

        // An object decodes once per model token; its fields, references
        // included, decode after it is registered, so cycles terminate.
        IrValue Decode(IrTypeId type, Expr evaluated)
        {
            var info = factory.GetTypeInfo(type);
            if (!IsReference(info))
            { return CreateValue(factory, type, evaluated)!; }
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
                    { identities = identities.AddUnique(token, identity = view); }
                    else
                    {
                        var state = new IrObjectState();
                        identities = identities.AddUnique(token, identity = state);
                        if (token != modelToken)
                        { identities = identities.SetItem(modelToken, state); }
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
                { certifiedTypes = certifiedTypes.AddUnique(token, type); }
                using var length = model.Evaluate(EncodeLength(evaluated, meter), true);
                // Completed EncodeLength values are BV32 numerals with a zero high bit.
                var count = SmtNativeUtilities.ReadNonnegativeInt32(length)!.Value;
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
                if (value.Kind == IrValueKind.Sequence && shared.TryGetValue(token, out var storage) &&
                    storage is IrValue { Kind: IrValueKind.Sequence } array &&
                    IrArrayStorage.CompatibleIntegerViews(factory, array.Type, type))
                { value = IrValue.WithSequenceIdentity(value, array); }
                if (!shared.ContainsKey(token))
                { shared = shared.AddUnique(token, info.Kind == IrTypeKind.String ? value.String : value); }
            }
            // A cyclic object may already have aliased itself while its
            // fields decoded.
            aliases = aliases.SetItem((type, token), value);
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
        RepositoryParameterGuard.ThrowIfInvalid(type.IsDefault, nameof(type));
        var info = factory.GetTypeInfo(type);
        return info.Kind == IrTypeKind.Boolean ? factory.CreateBooleanValue(false)
            : IsInteger(info) ? factory.CreateIntegerValue(type, 0L)
            : IsReference(info) ? factory.CreateNullValue(type)
            : throw new UnsupportedIrEncodingException();
    }

    internal void DisposeReferences()
    {
        foreach (var element in _elementOrder)
        { element.Dispose(); }
        _length?.Dispose();
        DisposeStrings();
        DisposeFields();
        _referenceSort?.Dispose();
    }
}

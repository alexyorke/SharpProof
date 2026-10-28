namespace SharpProof.Smt;

internal sealed partial class BvEncoder
{
    private Sort? _referenceSort;
    private FuncDecl? _length;
    private FuncDecl? _nominalWitnessTags;
    private Expr? _nullReference;
    private readonly ImmutableDictionary<IrTypeId, Expr>.Builder _emptyArrays = ImmutableDictionary.CreateBuilder<IrTypeId, Expr>();
    private readonly ImmutableList<KeyValuePair<IrTypeId, Expr>>.Builder _emptyArrayOrder = ImmutableList.CreateBuilder<KeyValuePair<IrTypeId, Expr>>();
    private readonly ImmutableDictionary<IrStringId, Expr>.Builder _stringLiterals = ImmutableDictionary.CreateBuilder<IrStringId, Expr>();
    private readonly ImmutableList<KeyValuePair<IrStringId, Expr>>.Builder _stringLiteralOrder = ImmutableList.CreateBuilder<KeyValuePair<IrStringId, Expr>>();
    private readonly ImmutableList<BoolExpr>.Builder _referenceFacts = ImmutableList.CreateBuilder<BoolExpr>();
    internal ImmutableList<BoolExpr> ReferenceFacts => _referenceFacts.ToImmutable();

    private void AddReferenceFact(BoolExpr fact)
    {
        fact = RepositoryParameterGuard.NotNull(fact, nameof(fact));
        _referenceFacts.Add(fact);
    }

    // Witness sizes are charged when decoded. Prefer small string/array/collection
    // models without restricting the unbounded query used for proofs.
    internal BoolExpr? SmallReferenceBound(VerificationQuery query, SmtQueryResourceMeter meter)
    {
        meter = RepositoryParameterGuard.NotNull(meter, nameof(meter));
        query = RepositoryParameterGuard.NotNull(query, nameof(query));
        var bounds = ImmutableArray.CreateBuilder<BoolExpr>();
        foreach (var variable in query.ModelVariables)
        {
            meter.Consume();
            if (factory.GetTypeInfo(factory.GetVariableInfo(variable).Type).Kind is IrTypeKind.String or IrTypeKind.Sequence)
            {
                bounds.Add(owner.Own(context.MkBVULE((BitVecExpr)EncodeLength(GetVariable(variable, meter), meter),
                    owner.Own(context.MkBV(MaximumSmallReference, 32)))));
            }
        }
        foreach (var field in _fieldOrder.ToImmutable())
        {
            meter.Consume();
            var member = factory.GetMemberInfo(field.Key);
            if (member.ParameterTypes.IsEmpty && member.ReturnType == factory.IntegerType && factory.GetString(member.Name) is IrFieldSites.Prefix + "list:count" or IrFieldSites.Prefix + "queue:count" or IrFieldSites.Prefix + "stack:count" or IrFieldSites.Prefix + "hash-set:count" or IrFieldSites.Prefix + "dictionary:count")
            {
                foreach (var observed in _fieldReads[field.Key].ToImmutable())
                {
                    meter.Consume();
                    var count = owner.Own(context.MkApp(field.Value, observed.Owner));
                    var size = factory.GetString(member.Name) is IrFieldSites.Prefix + "hash-set:count" or IrFieldSites.Prefix + "dictionary:count"
                        ? (BitVecExpr)owner.Own(context.MkITE(owner.Own(context.MkBVSLT((BitVecExpr)count, owner.Own(context.MkBV(0, 32)))),
                            owner.Own(context.MkBVNeg((BitVecExpr)count)), count))
                        : owner.Own(context.MkBVAND((BitVecExpr)count, owner.Own(context.MkBV(int.MaxValue, 32))));
                    bounds.Add(owner.Own(context.MkBVULE(size, owner.Own(context.MkBV(MaximumSmallReference, 32)))));
                }
            }
        }
        return bounds.Count == 0 ? null : owner.Own(context.MkAnd(bounds.ToImmutable()));
    }

    internal BoolExpr? NominalReferenceBound(VerificationQuery query, SmtQueryResourceMeter meter)
    {
        query = RepositoryParameterGuard.NotNull(query, nameof(query));
        meter = RepositoryParameterGuard.NotNull(meter, nameof(meter));
        var bounds = ImmutableArray.CreateBuilder<BoolExpr>();
        // This preference follows a rejected SAT model. A
        // bounded set of nominal types covers every requested view, rather
        // than sampling SSA values whose later aliases can evade the sample.
        // Compatible types may share tags; uncertain required aliases can
        // still abstain. These are never proof premises.
        var groups = ImmutableDictionary.CreateBuilder<IrTypeId, int>();
        var groupOrder = ImmutableArray.CreateBuilder<KeyValuePair<IrTypeId, int>>();
        foreach (var field in _fieldOrder.ToImmutable())
        {
            meter.Consume();
            AddType(factory.GetMemberInfo(field.Key).ReturnType);
        }
        foreach (var variable in query.ModelVariables)
        {
            meter.Consume();
            AddType(factory.GetVariableInfo(variable).Type);
        }
        var orderedGroups = groupOrder.ToImmutable();
        if (groups.Count != 0)
        {
            if (_nominalWitnessTags == null)
            {
                using var result = context.MkBitVecSort(MaximumNominalWitnessTypes);
                _nominalWitnessTags = context.MkFreshFuncDecl("nominal_witness_tags", [ReferenceSort], result);
            }
            var masks = ImmutableArray.CreateBuilder<ulong>(groups.Count);
            masks.Count = groups.Count;
            foreach (var first in orderedGroups)
            {
                var mask = 1UL << first.Value;
                foreach (var second in orderedGroups)
                {
                    meter.Consume();
                    if (!KnownNominalCompatibility(first.Key, second.Key, meter))
                    { mask |= 1UL << second.Value; }
                }
                masks[first.Value] = mask;
            }
            var maskSnapshot = masks.MoveToImmutable();
            foreach (var field in _fieldOrder.ToImmutable())
            {
                meter.Consume();
                if (!groups.TryGetValue(factory.GetMemberInfo(field.Key).ReturnType, out var group))
                { continue; }
                foreach (var observed in _fieldReads[field.Key].ToImmutable())
                {
                    meter.Consume();
                    var application = observed.Key == null ? context.MkApp(field.Value, observed.Owner)
                        : context.MkApp(field.Value, observed.Owner, observed.Key);
                    AddMembership(group, owner.Own(application));
                }
            }
            foreach (var variable in query.ModelVariables)
            {
                meter.Consume();
                if (groups.TryGetValue(factory.GetVariableInfo(variable).Type, out var group))
                { AddMembership(group, GetVariable(variable, meter)); }
            }

            void AddMembership(int group, Expr value)
            {
                RepositoryParameterGuard.ThrowIfInvalid(group < 0 || group >= maskSnapshot.Length, nameof(group));
                value = RepositoryParameterGuard.NotNull(value, nameof(value));
                meter.Consume();
                var tags = (BitVecExpr)owner.Own(context.MkApp(_nominalWitnessTags, value));
                var masked = owner.Own(context.MkBVAND(tags, owner.Own(context.MkBV(maskSnapshot[group], MaximumNominalWitnessTypes))));
                bounds.Add(owner.Own(context.MkOr(owner.Own(context.MkEq(value, NullReference)),
                    owner.Own(context.MkEq(masked, owner.Own(context.MkBV(1UL << group, MaximumNominalWitnessTypes)))))));
            }
        }
        return bounds.Count == 0 ? null : owner.Own(context.MkAnd(bounds.ToImmutable()));

        void AddType(IrTypeId type)
        {
            RepositoryParameterGuard.ThrowIfInvalid(type.IsDefault, nameof(type));
            meter.Consume();
            if (groups.Count < MaximumNominalWitnessTypes && type != factory.ObjectType &&
                factory.GetTypeInfo(type).Kind == IrTypeKind.Reference && !groups.ContainsKey(type))
            {
                var group = groups.Count;
                groups.AddUnique(type, group);
                groupOrder.Add(new(type, group));
            }
        }
    }
    private bool KnownNominalCompatibility(IrTypeId first, IrTypeId second, SmtQueryResourceMeter meter)
    {
        RepositoryParameterGuard.ThrowIfInvalid(first.IsDefault, nameof(first));
        RepositoryParameterGuard.ThrowIfInvalid(second.IsDefault, nameof(second));
        meter = RepositoryParameterGuard.NotNull(meter, nameof(meter));
        return first == second || IsBase(first, second) || IsBase(second, first) ||
            factory.IsInterfaceReferenceType(first) && factory.IsInterfaceReferenceType(second) ||
            factory.GetImplementedReferenceInterfaces(first).Contains(second) || factory.GetImplementedReferenceInterfaces(second).Contains(first);

        bool IsBase(IrTypeId ancestor, IrTypeId descendant)
        {
            RepositoryParameterGuard.ThrowIfInvalid(ancestor.IsDefault, nameof(ancestor));
            RepositoryParameterGuard.ThrowIfInvalid(descendant.IsDefault, nameof(descendant));
            for (IrTypeId? current = descendant; current.HasValue && factory.TryGetClosedClassReferenceBase(current.Value, out var parent); current = parent)
            {
                meter.Consume();
                if (current == ancestor)
                { return true; }
            }
            return false;
        }
    }

    // A type-group cap, independent of the small string/array/count size.
    private const int MaximumNominalWitnessTypes = 64;
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
        foreach (var literal in _stringLiteralOrder.ToImmutable().Select(static literal => literal.Value).Concat(_emptyArrayOrder.ToImmutable().Select(static empty => empty.Value)))
        {
            meter.Consume();
            AddDistinctReferenceFact(value, literal);
        }
        _stringLiterals.AddUnique(term.Value, value);
        _stringLiteralOrder.Add(new(term.Value, value));
        if (_text != null)
        { EnsureText(meter); }
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
        foreach (var previous in _emptyArrayOrder.ToImmutable())
        {
            meter.Consume();
            if (HaveDistinctScalarElements(term.Type, previous.Key))
            { AddDistinctReferenceFact(value, previous.Value); }
        }
        foreach (var literal in _stringLiteralOrder.ToImmutable().Select(static literal => literal.Value))
        {
            meter.Consume();
            AddDistinctReferenceFact(value, literal);
        }
        _emptyArrays.AddUnique(term.Type, value);
        _emptyArrayOrder.Add(new(term.Type, value));
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
        var values = ImmutableDictionary.CreateBuilder<IrVarId, IrValue>();
        var aliases = ImmutableDictionary.CreateBuilder<(IrTypeId Type, string Token), IrValue>();
        var certifiedTypes = ImmutableDictionary.CreateBuilder<string, IrTypeId>(StringComparer.Ordinal);
        var shared = ImmutableDictionary.CreateBuilder<string, object>(StringComparer.Ordinal);
        var classTypes = ImmutableDictionary.CreateBuilder<string, ImmutableArray<IrTypeId>.Builder>(StringComparer.Ordinal);
        var cacheTypes = ImmutableDictionary.CreateBuilder<string, ImmutableHashSet<IrTypeId>.Builder>(StringComparer.Ordinal);
        // Preserve observed framework cache aliases in every SSA/object view.
        // Unavailable observations stay symbolic and cannot confirm a witness.
        foreach (var cache in _defaultComparerOrder.ToImmutable())
        {
            meter.Consume();
            if (ConcreteOpaqueValueProvider?.Invoke(cache.Term) is not { } observed)
            { continue; }
            if (observed.Type != cache.Type || observed.Kind != IrValueKind.Reference)
            { throw new UnsupportedIrEncodingException(); }
            using var evaluated = model.Evaluate(cache.Value, true);
            var token = evaluated.ToString();
            if (shared.TryGetValue(token, out var previous) && !ReferenceEquals(previous, observed.Reference))
            { throw new UnsupportedIrEncodingException(); }
            shared[token] = observed.Reference!;
            aliases[(cache.Type, token)] = observed;
            if (!cacheTypes.TryGetValue(token, out var cacheViews))
            { cacheTypes.Add(token, cacheViews = ImmutableHashSet.CreateBuilder<IrTypeId>()); }
            cacheViews.Add(cache.Type);
            if (cache.Type != factory.ObjectType)
            {
                if (!classTypes.TryGetValue(token, out var types))
                { classTypes.Add(token, types = ImmutableArray.CreateBuilder<IrTypeId>()); }
                if (!types.Contains(cache.Type))
                { types.Add(cache.Type); }
            }
        }
        foreach (var empty in _emptyArrayOrder.ToImmutable())
        {
            meter.Consume();
            using var evaluated = model.Evaluate(empty.Value, true);
            var token = evaluated.ToString();
            var value = factory.CreateEmptyArrayValue(empty.Key);
            CertifyType(token, empty.Key);
            aliases.AddUnique((empty.Key, token), value);
            if (!shared.ContainsKey(token))
            { shared.AddUnique(token, value); }
        }
        var identities = ImmutableDictionary.CreateBuilder<string, object>(StringComparer.Ordinal);
        string? emptyStringToken = null;
        foreach (var literal in _stringLiteralOrder.ToImmutable())
        {
            meter.Consume();
            using var evaluated = model.Evaluate(literal.Value, true);
            var token = evaluated.ToString();
            var text = factory.GetString(literal.Key);
            CertifyType(token, factory.StringType);
            aliases.AddUnique((factory.StringType, token), factory.CreateStringValue(text));
            if (!shared.ContainsKey(token))
            { shared.AddUnique(token, text); }
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
            values.AddUnique(variable, value);
        }
        return values.ToImmutable();

        void CertifyType(string token, IrTypeId type)
        {
            if (certifiedTypes.TryGetValue(token, out var previous) && previous != type)
            { throw new UnsupportedIrEncodingException(); }
            certifiedTypes[token] = type;
        }

        bool IsClassBase(IrTypeId ancestor, IrTypeId descendant)
        {
            RepositoryParameterGuard.ThrowIfInvalid(ancestor.IsDefault, nameof(ancestor));
            RepositoryParameterGuard.ThrowIfInvalid(descendant.IsDefault, nameof(descendant));
            for (IrTypeId? current = descendant; current.HasValue && factory.TryGetClosedClassReferenceBase(current.Value, out var parent); current = parent)
            {
                meter.Consume();
                if (current == ancestor)
                { return true; }
            }
            return false;
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
            if (info.Kind == IrTypeKind.Reference && cacheTypes.TryGetValue(token, out var caches))
            {
                foreach (var declared in caches)
                {
                    meter.Consume();
                    if (!IrOpaqueCallSite.IsDefaultComparerValueView(factory, declared, type))
                    { throw new UnsupportedIrEncodingException(); }
                }
            }
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
            // Distinct nominal views need positive runtime compatibility. Missing
            // certificates, including legacy and open generic views, cannot
            // make an impossible alias into an authoritative counterexample.
            if (info.Kind == IrTypeKind.Reference && type != factory.ObjectType)
            {
                if (!classTypes.TryGetValue(token, out var previousTypes))
                { classTypes.AddUnique(token, previousTypes = ImmutableArray.CreateBuilder<IrTypeId>()); }
                foreach (var previous in previousTypes)
                {
                    meter.Consume();
                    if (type != previous && !IsClassBase(type, previous) && !IsClassBase(previous, type) &&
                        !(factory.IsInterfaceReferenceType(type) && factory.IsInterfaceReferenceType(previous)) &&
                        !factory.GetImplementedReferenceInterfaces(type).Contains(previous) && !factory.GetImplementedReferenceInterfaces(previous).Contains(type))
                    { throw new UnsupportedIrEncodingException(); }
                }
                if (!previousTypes.Contains(type))
                { previousTypes.Add(type); }
            }
            if (aliases.TryGetValue((type, token), out var value))
            { return value; }
            if (info.Kind == IrTypeKind.Reference)
            {
                if (!identities.TryGetValue(token, out var identity))
                {
                    if (shared.TryGetValue(token, out var view))
                    { identities.AddUnique(token, identity = view); }
                    else
                    {
                        var state = new IrObjectState();
                        identities.AddUnique(token, identity = state);
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
                { certifiedTypes.AddUnique(token, type); }
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
                { shared.AddUnique(token, info.Kind == IrTypeKind.String ? value.String : value); }
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
        RepositoryParameterGuard.ThrowIfInvalid(type.IsDefault, nameof(type));
        var info = factory.GetTypeInfo(type);
        return info.Kind == IrTypeKind.Boolean ? factory.CreateBooleanValue(false)
            : IsInteger(info) ? factory.CreateIntegerValue(type, 0L)
            : IsReference(info) ? factory.CreateNullValue(type)
            : throw new UnsupportedIrEncodingException();
    }

    internal void DisposeReferences()
    {
        foreach (var element in _elementOrder.ToImmutable())
        { element.Dispose(); }
        _length?.Dispose();
        _nominalWitnessTags?.Dispose();
        DisposeStrings();
        DisposeFields();
        _referenceSort?.Dispose();
    }
}

namespace SharpProof.Smt;

// An instance field is a function from an object to the field's value as the
// callable entered; stores are expressed over it by the verification
// condition.
internal sealed partial class BvEncoder
{
    private readonly Dictionary<IrMemberId, FuncDecl> _fields = [];
    private readonly Dictionary<IrMemberId, Dictionary<IrId, (IrOpaqueTerm Read, Expr Owner, Expr? Key)>> _fieldReads = [];

    private Expr EncodeFieldRead(IrOpaqueTerm read, SmtQueryResourceMeter meter)
    {
        meter = RepositoryParameterGuard.NotNull(meter, nameof(meter));
        var receiver = Encode(read.Receiver ?? throw new System.IO.InvalidDataException("An instance field read has no receiver."), meter);
        if (!_fields.TryGetValue(read.Member, out var field))
        {
            var type = factory.GetTypeInfo(read.Type);
            using Sort? ownedSort = IsReference(type) ? null
                : type.Kind == IrTypeKind.Boolean ? context.MkBoolSort()
                : IsInteger(type) ? context.MkBitVecSort((uint)type.Width)
                : throw new UnsupportedIrEncodingException();
            var result = ownedSort ?? ReferenceSort;
            if (read.Arguments.IsEmpty)
            { field = context.MkFuncDecl("field" + read.Member.Value.ToString(CultureInfo.InvariantCulture), ReferenceSort, result); }
            else
            {
                var key = factory.GetTypeInfo(read.Arguments[0].Type);
                if (!IsInteger(key))
                { throw new UnsupportedIrEncodingException(); }
                using var keySort = context.MkBitVecSort((uint)key.Width);
                field = context.MkFuncDecl("field" + read.Member.Value.ToString(CultureInfo.InvariantCulture), [ReferenceSort, keySort], result);
            }
            _fields.Add(read.Member, field);
        }
        var keyValue = read.Arguments.IsEmpty ? null : Encode(read.Arguments[0], meter);
        if (!_fieldReads.TryGetValue(read.Member, out var observations))
        { _fieldReads.Add(read.Member, observations = []); }
        observations[read.Id] = (read, receiver, keyValue);
        return owner.Own(keyValue == null ? context.MkApp(field, receiver) : context.MkApp(field, receiver, keyValue));
    }

    // Fills entry fields and only the keyed cells observed for this owner.
    private void FillObjectState(IrObjectState state, Expr reference, Model model, SmtQueryResourceMeter meter,
        Func<IrTypeId, Expr, IrValue?> decode)
    {
        decode = RepositoryParameterGuard.NotNull(decode, nameof(decode));
        meter = RepositoryParameterGuard.NotNull(meter, nameof(meter));
        model = RepositoryParameterGuard.NotNull(model, nameof(model));
        reference = RepositoryParameterGuard.NotNull(reference, nameof(reference));
        state = RepositoryParameterGuard.NotNull(state, nameof(state));
        foreach (var field in _fields.ToArray())
        {
            meter.Consume();
            var observations = new List<(IrOpaqueTerm Read, Expr Owner, Expr? Key)>();
            foreach (var observed in _fieldReads[field.Key].Values)
            {
                meter.Consume();
                using var observedOwner = model.Evaluate(observed.Owner, true);
                if (observedOwner.Equals(reference))
                { observations.Add(observed); }
            }
            // Model completion does not establish a field on an unrelated owner.
            // Leave unobserved entry storage unavailable to concrete replay.
            if (observations.Count == 0)
            { continue; }
            var member = factory.GetMemberInfo(field.Key);
            if (member.ParameterTypes.IsEmpty)
            {
                using var application = context.MkApp(field.Value, reference);
                using var value = model.Evaluate(application, true);
                if (decode(member.ReturnType, value) is { } decoded)
                {
                    // Charge witness size without narrowing universal proofs.
                    // HashSet/Dictionary callback state can have negative Count;
                    // bound its magnitude instead of masking away the sign bit.
                    if (observations.Count != 0 && decoded.Kind == IrValueKind.Integer && factory.GetString(member.Name) is IrFieldSites.Prefix + "list:count" or IrFieldSites.Prefix + "queue:count" or IrFieldSites.Prefix + "stack:count" or IrFieldSites.Prefix + "hash-set:count" or IrFieldSites.Prefix + "dictionary:count")
                    {
                        var size = factory.GetString(member.Name) is IrFieldSites.Prefix + "hash-set:count" or IrFieldSites.Prefix + "dictionary:count"
                            ? System.Numerics.BigInteger.Abs(decoded.IntegerNumericValue) : decoded.IntegerBits & int.MaxValue;
                        meter.Consume((long)size + 1L);
                    }
                    state.Fields[field.Key] = decoded;
                }
                continue;
            }
            // Reconstruct only cells actually queried for this owner and key.
            // An unobserved key remains unavailable to replay, never a default.
            foreach (var observed in observations)
            {
                meter.Consume();
                using var key = model.Evaluate(observed.Key!, true);
                using var application = context.MkApp(field.Value, reference, key);
                using var value = model.Evaluate(application, true);
                if (decode(observed.Read.Arguments[0].Type, key) is { } decodedKey &&
                    decode(member.ReturnType, value) is { } decodedValue)
                { state.WithKeyedField(field.Key, decodedKey, decodedValue); }
            }
        }
    }

    private void DisposeFields()
    {
        foreach (var field in _fields.Values)
        { field.Dispose(); }
    }
}

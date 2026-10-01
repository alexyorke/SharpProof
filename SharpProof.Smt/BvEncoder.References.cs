namespace SharpProof.Smt;

internal sealed partial class BvEncoder
{
    private Sort? _referenceSort;
    private FuncDecl? _length;
    private Expr? _nullReference;
    private readonly Dictionary<IrTypeId, Expr> _emptyArrays = [];
    internal List<BoolExpr> ReferenceFacts { get; } = [];

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
        foreach (var empty in _emptyArrays)
        {
            meter.Consume();
            using var evaluated = model.Evaluate(empty.Value, true);
            aliases.Add((empty.Key, evaluated.ToString()), factory.CreateEmptyArrayValue(empty.Key));
        }
        var identities = new Dictionary<string, object>(StringComparer.Ordinal);
        string? emptyStringToken = null;
        var observations = DecodeArrayObservations(model, meter);
        foreach (var variable in query.ModelVariables)
        {
            meter.Consume();
            var type = factory.GetVariableInfo(variable).Type;
            var info = factory.GetTypeInfo(type);
            using var evaluated = model.Evaluate(GetVariable(variable, meter), true);
            IrValue? value;
            if (!IsReference(info))
            {
                value = CreateValue(factory, type, evaluated);
            }
            else
            {
                using var nullValue = model.Evaluate(NullReference, true);
                if (evaluated.Equals(nullValue))
                {
                    values.Add(variable, factory.CreateNullValue(type));
                    continue;
                }
                var token = evaluated.ToString();
                if (!aliases.TryGetValue((type, token), out value))
                {
                    if (info.Kind == IrTypeKind.Reference)
                    {
                        if (!identities.TryGetValue(token, out var identity))
                        { identities.Add(token, identity = new object()); }
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
                        value = info.Kind == IrTypeKind.String ? factory.CreateStringValue(new string('\0', count))
                            : DecodeArrayWitness(type, count, token, observations);
                    }
                    aliases.Add((type, token), value);
                }
            }
            if (value == null)
            { return null; }
            values.Add(variable, value);
        }
        return values;
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
        _referenceSort?.Dispose();
    }
}

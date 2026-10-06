namespace SharpProof.Smt;

internal sealed partial class BvEncoder
{
    private readonly Dictionary<IrTypeId, FuncDecl> _elements = [];
    private readonly HashSet<IrSequenceAccessTerm> _arrayAccesses = [];

    private Expr EncodeArrayAccess(IrSequenceAccessTerm access, SmtQueryResourceMeter meter)
    {
        var info = factory.GetTypeInfo(access.Type);
        if (info.Kind != IrTypeKind.Boolean && !IsInteger(info))
        { throw new UnsupportedIrEncodingException(); }
        var array = Encode(access.Sequence, meter);
        var index = (BitVecExpr)Encode(access.Index, meter);
        var storageType = ArrayStorageType(access.Type);
        if (!_elements.TryGetValue(storageType, out var element))
        {
            using var indexSort = context.MkBitVecSort(32);
            using Sort valueSort = info.Kind == IrTypeKind.Boolean ? context.MkBoolSort() : context.MkBitVecSort((uint)info.Width);
            element = context.MkFuncDecl("element" + _elements.Count.ToString(CultureInfo.InvariantCulture), [ReferenceSort, indexSort], valueSort);
            _elements.Add(storageType, element);
        }
        var length = (BitVecExpr)EncodeLength(array, meter);
        var valid = owner.Own(context.MkAnd(owner.Own(context.MkNot(owner.Own(context.MkEq(array, NullReference)))),
            owner.Own(context.MkBVSGE(index, owner.Own(context.MkBV(0, 32)))), owner.Own(context.MkBVSLT(index, length))));
        Expr empty = info.Kind == IrTypeKind.Boolean ? owner.Own(context.MkFalse()) : owner.Own(context.MkBV(0, (uint)info.Width));
        _arrayAccesses.Add(access);
        return owner.Own(context.MkITE(valid, owner.Own(context.MkApp(element, array, index)), empty));
    }

    private Dictionary<(IrTypeId Type, string Token), Dictionary<int, IrValue>> DecodeArrayObservations(Model model, SmtQueryResourceMeter meter)
    {
        var observations = new Dictionary<(IrTypeId Type, string Token), Dictionary<int, IrValue>>();
        foreach (var access in _arrayAccesses)
        {
            meter.Consume();
            using var array = model.Evaluate(Encode(access.Sequence, meter), true);
            using var index = model.Evaluate(Encode(access.Index, meter), true);
            using var length = model.Evaluate(EncodeLength(array, meter), true);
            if (index is not BitVecNum number || number.UInt64 > int.MaxValue ||
                length is not BitVecNum size || number.UInt64 >= size.UInt64)
            { continue; }
            using var value = model.Evaluate(Encode(access, meter), true);
            var decoded = CreateValue(factory, access.Type, value) ?? throw new UnsupportedIrEncodingException();
            var key = (ArrayStorageType(access.Type), array.ToString());
            if (!observations.TryGetValue(key, out var elements))
            { observations.Add(key, elements = []); }
            elements[(int)number.UInt64] = decoded;
        }
        return observations;
    }

    // Signedness changes the numeric interpretation, not the array's bits.
    private IrTypeId ArrayStorageType(IrTypeId type)
    {
        var info = factory.GetTypeInfo(type);
        return info.Kind == IrTypeKind.Integer ? factory.GetOrCreateIntegerType(info.Width, false) : type;
    }

    private IrValue DecodeArrayWitness(IrTypeId type, int count, string token,
        Dictionary<(IrTypeId Type, string Token), Dictionary<int, IrValue>> observations)
    {
        var elementType = factory.GetTypeInfo(type).ElementType!.Value;
        var elements = Enumerable.Repeat(DefaultValue(elementType), count).ToArray();
        if (observations.TryGetValue((ArrayStorageType(elementType), token), out var observed))
        {
            foreach (var entry in observed)
            {
                elements[entry.Key] = entry.Value.Type == elementType ? entry.Value
                    : factory.CreateIntegerValueFromBits(elementType, entry.Value.IntegerBits);
            }
        }
        return factory.CreateSequenceValue(type, elements);
    }
}

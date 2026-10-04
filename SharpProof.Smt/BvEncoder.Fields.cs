namespace SharpProof.Smt;

// An instance field is a function from an object to the field's value as the
// callable entered; stores are expressed over it by the verification
// condition.
internal sealed partial class BvEncoder
{
    private readonly Dictionary<IrMemberId, FuncDecl> _fields = [];

    private Expr EncodeFieldRead(IrOpaqueTerm read, SmtQueryResourceMeter meter)
    {
        var receiver = Encode(read.Receiver!, meter);
        if (!_fields.TryGetValue(read.Member, out var field))
        {
            var type = factory.GetTypeInfo(read.Type);
            if (IsReference(type))
            { field = context.MkFuncDecl("field" + read.Member.Value.ToString(CultureInfo.InvariantCulture), ReferenceSort, ReferenceSort); }
            else
            {
                using Sort sort = type.Kind == IrTypeKind.Boolean ? context.MkBoolSort()
                    : IsInteger(type) ? context.MkBitVecSort((uint)type.Width)
                    : throw new UnsupportedIrEncodingException();
                field = context.MkFuncDecl("field" + read.Member.Value.ToString(CultureInfo.InvariantCulture), ReferenceSort, sort);
            }
            _fields.Add(read.Member, field);
        }
        return owner.Own(context.MkApp(field, receiver));
    }

    // Fills an object of the model with every encoded field's entry value.
    private void FillObjectState(IrObjectState state, Expr reference, Model model, SmtQueryResourceMeter meter,
        Func<IrTypeId, Expr, IrValue?> decode)
    {
        foreach (var field in _fields.ToArray())
        {
            meter.Consume();
            using var application = context.MkApp(field.Value, reference);
            using var value = model.Evaluate(application, true);
            if (decode(factory.GetMemberInfo(field.Key).ReturnType, value) is { } decoded)
            { state.Fields[field.Key] = decoded; }
        }
    }

    private void DisposeFields()
    {
        foreach (var field in _fields.Values)
        { field.Dispose(); }
    }
}

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
            using Sort sort = type.Kind == IrTypeKind.Boolean ? context.MkBoolSort()
                : IsInteger(type) ? context.MkBitVecSort((uint)type.Width)
                : throw new UnsupportedIrEncodingException();
            field = context.MkFuncDecl("field" + read.Member.Value.ToString(CultureInfo.InvariantCulture), ReferenceSort, sort);
            _fields.Add(read.Member, field);
        }
        return owner.Own(context.MkApp(field, receiver));
    }

    // An object of the model with every encoded field's entry value.
    private IrObjectState ObjectState(Expr reference, Model model, SmtQueryResourceMeter meter)
    {
        var state = new IrObjectState();
        foreach (var field in _fields)
        {
            meter.Consume();
            using var application = context.MkApp(field.Value, reference);
            using var value = model.Evaluate(application, true);
            if (CreateValue(factory, factory.GetMemberInfo(field.Key).ReturnType, value) is { } decoded)
            { state.Fields[field.Key] = decoded; }
        }
        return state;
    }

    private void DisposeFields()
    {
        foreach (var field in _fields.Values)
        { field.Dispose(); }
    }
}

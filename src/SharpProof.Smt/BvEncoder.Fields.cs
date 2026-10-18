namespace SharpProof.Smt;

// An instance field is a function from an object to the field's value as the
// callable entered; stores are expressed over it by the verification
// condition.
internal sealed partial class BvEncoder
{
    private readonly Dictionary<IrMemberId, FuncDecl> _fields = [];

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
        return owner.Own(read.Arguments.IsEmpty ? context.MkApp(field, receiver)
            : context.MkApp(field, receiver, Encode(read.Arguments[0], meter)));
    }

    // Fills an object of the model with every encoded field's entry value.
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
            // Entry keyed contents are not reconstructed from a finite sample.
            // Replay must decline any witness that needs such an unknown cell.
            if (!factory.GetMemberInfo(field.Key).ParameterTypes.IsEmpty)
            { continue; }
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

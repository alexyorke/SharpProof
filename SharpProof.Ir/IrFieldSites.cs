namespace SharpProof.Ir;

// A field is a member whose name has this prefix. Its pure opaque
// application to a receiver denotes the field's value as the callable entered.
internal static class IrFieldSites
{
    internal const string Prefix = "field:";

    internal static bool IsField(IrFactory factory, IrMemberId member)
    { return factory.GetString(factory.GetMemberInfo(member).Name).StartsWith(Prefix, StringComparison.Ordinal); }

    internal static bool IsFieldRead(IrFactory factory, IrTerm term)
    {
        return term is IrOpaqueTerm { Purity: IrOpaquePurity.Pure, Receiver: not null, Arguments.IsEmpty: true } opaque &&
            IsField(factory, opaque.Member);
    }
}

// The identity of an object in a counterexample, with its fields' values as
// the callable entered.
public sealed class IrObjectState
{
    internal Dictionary<IrMemberId, IrValue> Fields { get; } = [];

    public IrObjectState WithField(IrMemberId field, IrValue value)
    {
        Fields[field] = ArgumentNullGuard.NotNull(value, nameof(value));
        return this;
    }
}

// What an execution has stored: array contents and object fields, both by
// identity.
internal sealed class IrHeap
{
    private readonly Dictionary<IrValue, Dictionary<int, IrValue>> _elements = new(IrValueIdentity.Instance);
    private readonly HashSet<IrValue> _freshArrays = new(IrValueIdentity.Instance);
    internal Dictionary<(object Identity, IrMemberId Field), IrValue> Fields { get; } = new(FieldKey.Instance);
    internal bool ConsumedApproximation { get; private set; }
    private bool UnknownContents { get; set; }

    internal void Invalidate()
    {
        UnknownContents = true;
        _elements.Clear();
        Fields.Clear();
        _freshArrays.Clear();
    }

    internal void RegisterFreshArray(IrValue sequence)
    { _freshArrays.Add(sequence); }

    internal void StoreElement(IrValue sequence, int index, IrValue value)
    {
        if (!_elements.TryGetValue(sequence, out var elements))
        { _elements.Add(sequence, elements = []); }
        elements[index] = value;
    }

    internal bool TryReadElement(IrValue sequence, int index, out IrValue value)
    {
        if (_elements.TryGetValue(sequence, out var elements) && elements.TryGetValue(index, out value!))
        { return true; }
        if (!UnknownContents || _freshArrays.Contains(sequence))
        { value = sequence.Elements[index]; return true; }
        ConsumedApproximation = true;
        value = null!;
        return false;
    }

    internal bool TryReadField(IrValue owner, IrMemberId field, out IrValue value)
    {
        if (Fields.TryGetValue((owner.Reference, field), out value!))
        { return true; }
        if (UnknownContents)
        { ConsumedApproximation = true; value = null!; return false; }
        if (owner.Reference is IrObjectState entry && entry.Fields.TryGetValue(field, out value!))
        { return true; }
        value = null!;
        return false;
    }

    private sealed class FieldKey : IEqualityComparer<(object Identity, IrMemberId Field)>
    {
        internal static FieldKey Instance { get; } = new();
        public bool Equals((object Identity, IrMemberId Field) x, (object Identity, IrMemberId Field) y)
        { return ReferenceEquals(x.Identity, y.Identity) && x.Field == y.Field; }
        public int GetHashCode((object Identity, IrMemberId Field) obj)
        { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj.Identity) * 31 + obj.Field.GetHashCode(); }
    }
}

// Arrays are compared by identity, as the CLR compares references.
internal sealed class IrValueIdentity : IEqualityComparer<IrValue>
{
    internal static IrValueIdentity Instance { get; } = new();
    public bool Equals(IrValue? x, IrValue? y) { return ReferenceEquals(x, y); }
    public int GetHashCode(IrValue obj) { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj); }
}

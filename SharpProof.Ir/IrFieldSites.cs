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
    internal Dictionary<IrValue, IrValue[]> Elements { get; } = new(IrValueIdentity.Instance);
    internal Dictionary<(object Identity, IrMemberId Field), IrValue> Fields { get; } = new(FieldKey.Instance);
    internal bool IsEmpty => Elements.Count == 0 && Fields.Count == 0;

    internal void StoreElement(IrValue sequence, int index, IrValue value)
    {
        if (!Elements.TryGetValue(sequence, out var elements))
        { Elements.Add(sequence, elements = [.. sequence.Elements]); }
        elements[index] = value;
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

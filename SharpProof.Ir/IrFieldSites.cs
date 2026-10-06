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

// Storage compatibility of compiler-owned primitive array types.
internal static class IrArrayStorage
{
    internal static bool CompatibleIntegerViews(IrFactory factory, IrTypeId left, IrTypeId right)
    {
        var first = factory.GetTypeInfo(left);
        var second = factory.GetTypeInfo(right);
        if (first.Kind != IrTypeKind.Sequence || second.Kind != IrTypeKind.Sequence ||
            first.ElementType is not { } firstElement || second.ElementType is not { } secondElement)
        { return false; }
        var x = factory.GetTypeInfo(firstElement);
        var y = factory.GetTypeInfo(secondElement);
        // CLR signed/unsigned integer views share bits. Char arrays remain
        // nominally distinct despite their UInt16 scalar representation.
        return x.Kind == IrTypeKind.Integer && y.Kind == IrTypeKind.Integer &&
            x.Width is 8 or 16 or 32 or 64 && x.Width == y.Width &&
            !factory.GetString(first.Name).EndsWith("::System.Char[]", StringComparison.Ordinal) &&
            !factory.GetString(second.Name).EndsWith("::System.Char[]", StringComparison.Ordinal);
    }
}

// What an execution has stored: array contents and object fields, both by
// identity.
internal sealed class IrHeap
{
    private readonly Dictionary<IrValue, Dictionary<int, IrValue>> _elements = new(IrValueIdentity.Instance);
    private readonly HashSet<IrValue> _freshArrays = new(IrValueIdentity.Instance);
    private readonly HashSet<object> _freshObjects = new(ObjectKey.Instance);
    internal Dictionary<(object Identity, IrMemberId Field), IrValue> Fields { get; } = new(FieldKey.Instance);
    internal bool ConsumedApproximation { get; private set; }
    private bool UnknownContents { get; set; }

    internal void Invalidate()
    {
        UnknownContents = true;
        _elements.Clear();
        Fields.Clear();
        _freshArrays.Clear();
        _freshObjects.Clear();
    }

    internal void RegisterFreshArray(IrValue sequence)
    { _freshArrays.Add(sequence.SequenceIdentity); }

    internal void RegisterFreshObject(IrValue owner)
    { _freshObjects.Add(owner.Reference); }

    internal void StoreElement(IrValue sequence, int index, IrValue value)
    {
        var identity = sequence.SequenceIdentity;
        if (!_elements.TryGetValue(identity, out var elements))
        { _elements.Add(identity, elements = []); }
        elements[index] = value;
    }

    internal bool TryReadElement(IrValue sequence, int index, out IrValue value)
    {
        if (_elements.TryGetValue(sequence.SequenceIdentity, out var elements) && elements.TryGetValue(index, out value!))
        { return true; }
        if (!UnknownContents || _freshArrays.Contains(sequence.SequenceIdentity))
        { value = sequence.Elements[index]; return true; }
        ConsumedApproximation = true;
        value = null!;
        return false;
    }

    internal bool TryReadField(IrFactory factory, IrValue owner, IrMemberId field, out IrValue value)
    {
        if (Fields.TryGetValue((owner.Reference, field), out value!))
        { return true; }
        if (_freshObjects.Contains(owner.Reference))
        {
            var type = factory.GetMemberInfo(field).ReturnType;
            var info = factory.GetTypeInfo(type);
            if (info.Kind == IrTypeKind.Boolean)
            { value = factory.CreateBooleanValue(false); return true; }
            if (info.Kind == IrTypeKind.Integer)
            { value = factory.CreateIntegerValueFromBits(type, 0); return true; }
        }
        if (UnknownContents)
        { ConsumedApproximation = true; value = null!; return false; }
        if (owner.Reference is IrObjectState entry && entry.Fields.TryGetValue(field, out value!))
        { return true; }
        value = null!;
        return false;
    }

    private sealed class ObjectKey : IEqualityComparer<object>
    {
        internal static ObjectKey Instance { get; } = new();
        public new bool Equals(object? x, object? y) { return ReferenceEquals(x, y); }
        public int GetHashCode(object obj) { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj); }
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

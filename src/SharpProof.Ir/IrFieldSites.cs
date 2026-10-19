namespace SharpProof.Ir;

// A field is a member whose name has this prefix. Its pure opaque
// application to a receiver denotes the field's value as the callable entered.
internal static class IrFieldSites
{
    internal const string Prefix = "field:";

    internal static bool IsField(IrFactory factory, IrMemberId member)
    { factory = RepositoryParameterGuard.NotNull(factory, nameof(factory)); RepositoryParameterGuard.ThrowIfInvalid(member.IsDefault, nameof(member)); return factory.GetString(factory.GetMemberInfo(member).Name).StartsWith(Prefix, StringComparison.Ordinal); }

    internal static bool IsFieldRead(IrFactory factory, IrTerm term)
    {
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
        term = RepositoryParameterGuard.NotNull(term, nameof(term));
        return term is IrOpaqueTerm { Purity: IrOpaquePurity.Pure, Receiver: not null, Arguments.Length: <= 1 } opaque &&
            IsField(factory, opaque.Member) &&
            (opaque.Arguments.IsEmpty || factory.GetTypeInfo(opaque.Arguments[0].Type).Kind == IrTypeKind.Integer);
    }
}

// The identity of an object in a counterexample, with its fields' values as
// the callable entered.
internal sealed class IrObjectState
{
    internal Dictionary<IrMemberId, IrValue> Fields { get; } = [];

    internal Dictionary<(IrMemberId Field, IrTypeId Type, ulong Key), IrValue> KeyedFields { get; } = [];

    internal IrObjectState WithKeyedField(IrMemberId field, IrValue key, IrValue value)
    {
        RepositoryParameterGuard.ThrowIfInvalid(field.IsDefault, nameof(field));
        key = ArgumentNullGuard.NotNull(key, nameof(key));
        value = ArgumentNullGuard.NotNull(value, nameof(value));
        if (key.Kind != IrValueKind.Integer)
        { throw new ArgumentException("A keyed field requires an integer key.", nameof(key)); }
        KeyedFields[(field, key.Type, key.IntegerBits)] = value;
        return this;
    }

    internal bool TryRead(IrMemberId field, IrValue? key, out IrValue value)
    {
        RepositoryParameterGuard.ThrowIfInvalid(field.IsDefault, nameof(field));
        return key == null ? Fields.TryGetValue(field, out value!)
            : KeyedFields.TryGetValue((field, key.Type, key.IntegerBits), out value!);
    }

    public IrObjectState WithField(IrMemberId field, IrValue value)
    {
        RepositoryParameterGuard.ThrowIfInvalid(field.IsDefault, nameof(field));
        Fields[field] = ArgumentNullGuard.NotNull(value, nameof(value));
        return this;
    }
}

// Storage compatibility of compiler-owned primitive array types.
internal static class IrArrayStorage
{
    internal static bool CompatibleIntegerViews(IrFactory factory, IrTypeId left, IrTypeId right)
    {
        RepositoryParameterGuard.ThrowIfInvalid(left.IsDefault, nameof(left));
        RepositoryParameterGuard.ThrowIfInvalid(right.IsDefault, nameof(right));
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
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
    private readonly Dictionary<object, Dictionary<(IrMemberId Field, IrTypeId Type, ulong Key), IrValue>> _keyedFields = new(ObjectKey.Instance);
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
        _keyedFields.Clear();
        _freshArrays.Clear();
        _freshObjects.Clear();
    }

    internal void RegisterFreshArray(IrValue sequence)
    { sequence = RepositoryParameterGuard.NotNull(sequence, nameof(sequence)); _freshArrays.Add(sequence.SequenceIdentity); }

    internal void RegisterFreshObject(IrValue owner)
    { owner = RepositoryParameterGuard.NotNull(owner, nameof(owner)); _freshObjects.Add(owner.Reference); }

    // Allocation identity is sufficient to reject a purity counterexample:
    // publishing this receiver already requires a separate observable effect.
    internal bool IsFreshReceiver(IrValue value)
    {
        value = RepositoryParameterGuard.NotNull(value, nameof(value));
        return value.Kind switch
        {
            IrValueKind.Sequence => _freshArrays.Contains(value.SequenceIdentity),
            IrValueKind.Reference => _freshObjects.Contains(value.Reference),
            _ => false
        };
    }

    internal void StoreElement(IrValue sequence, int index, IrValue value)
    {
        RepositoryParameterGuard.RequireInitialized(index < 0, nameof(index), "Value must be initialized.");

        sequence = RepositoryParameterGuard.NotNull(sequence, nameof(sequence));
        value = RepositoryParameterGuard.NotNull(value, nameof(value));
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

    internal void StoreKeyedField(IrValue owner, IrMemberId field, IrValue key, IrValue value)
    {
        owner = RepositoryParameterGuard.NotNull(owner, nameof(owner));
        key = RepositoryParameterGuard.NotNull(key, nameof(key));
        value = RepositoryParameterGuard.NotNull(value, nameof(value));
        RepositoryParameterGuard.ThrowIfInvalid(field.IsDefault, nameof(field));
        if (!_keyedFields.TryGetValue(owner.Reference, out var cells))
        { _keyedFields.Add(owner.Reference, cells = []); }
        cells[(field, key.Type, key.IntegerBits)] = value;
    }

    internal bool TryReadField(IrFactory factory, IrValue owner, IrMemberId field, out IrValue value, IrValue? key = null)
    {
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
        if (key != null && _keyedFields.TryGetValue(owner.Reference, out var cells) &&
            cells.TryGetValue((field, key.Type, key.IntegerBits), out value!))
        { return true; }
        if (key == null && Fields.TryGetValue((owner.Reference, field), out value!))
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
        if (owner.Reference is IrObjectState entry && entry.TryRead(field, key, out value!))
        { return true; }
        value = null!;
        return false;
    }

    private sealed class ObjectKey : IEqualityComparer<object>
    {
        internal static ObjectKey Instance { get; } = new();
        public new bool Equals(object? x, object? y) { return ReferenceEquals(x, y); }
        public int GetHashCode(object obj) { obj = RepositoryParameterGuard.NotNull(obj, nameof(obj)); return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj); }
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
    public int GetHashCode(IrValue obj) { obj = RepositoryParameterGuard.NotNull(obj, nameof(obj)); return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj); }
}

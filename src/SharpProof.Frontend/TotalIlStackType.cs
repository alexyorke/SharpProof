namespace SharpProof.Frontend;

// An opaque value from the admitted CLI stack domain. Callers cannot supply a
// category tag or an encoded width; producers use fixed type/opcode factories.
internal readonly struct TotalIlStackType : IEquatable<TotalIlStackType>
{
    private const int BooleanCompatibleI4Value = -32;
    private const int Integer32Value = 32;
    private const int Integer64Value = 64;
    private const int ObjectReferenceValue = 128;
    private const int StringReferenceValue = 129;
    private const int NullReferenceValue = 130;
    private readonly int _value;

    private TotalIlStackType(int value)
    { _value = value; }

    internal bool IsValid => _value != 0;
    internal bool IsBooleanCompatibleI4 => _value == BooleanCompatibleI4Value;
    internal bool IsNullReference => _value == NullReferenceValue;
    internal bool IsReference => _value is ObjectReferenceValue or StringReferenceValue or NullReferenceValue;
    // This is only an integer-stack width. References and null have width zero.
    internal int NumericWidth => _value is BooleanCompatibleI4Value or Integer32Value ? 32
        : _value == Integer64Value ? 64 : 0;

    internal static TotalIlStackType BooleanCompatibleI4 => new(BooleanCompatibleI4Value);
    internal static TotalIlStackType Integer32 => new(Integer32Value);
    internal static TotalIlStackType Integer64 => new(Integer64Value);
    internal static TotalIlStackType NullFromIl => new(NullReferenceValue);

    internal static bool TryFromTypeSymbol(ITypeSymbol? type, out TotalIlStackType shape)
    {
        if (type is not null)
        { return TryFromSpecialType(type.SpecialType, out shape); }
        shape = default;
        return false;
    }

    internal static bool TryFromSpecialType(SpecialType type, out TotalIlStackType shape)
    {
        shape = MapSpecialType(type);
        return shape.IsValid;
    }

    // Precondition: the same special type has already passed Supported during
    // whole-body local admission. This preserves the former no-failure Ldloc map.
    internal static TotalIlStackType FromSupportedSpecialType(SpecialType type)
    { return MapSpecialType(type); }

    private static TotalIlStackType MapSpecialType(SpecialType type)
    {
        if (type == SpecialType.System_Boolean)
        { return BooleanCompatibleI4; }
        if (type == SpecialType.System_Object)
        { return new(ObjectReferenceValue); }
        if (type == SpecialType.System_String)
        { return new(StringReferenceValue); }
        return NumericWidthFor(type) switch
        {
            32 => Integer32,
            64 => Integer64,
            _ => default
        };
    }


    internal static TotalIlStackType FromInt32Constant(long value)
    { return value is 0 or 1 ? BooleanCompatibleI4 : Integer32; }

    internal TotalIlStackType WidenBooleanCompatibleI4()
    { return IsBooleanCompatibleI4 ? Integer32 : this; }

    internal bool TryJoin(TotalIlStackType other, out TotalIlStackType joined)
    {
        if (Equals(other))
        { joined = this; return true; }
        if (IsNullReference && other.IsReference)
        { joined = other; return true; }
        if (other.IsNullReference && IsReference)
        { joined = this; return true; }
        if (NumericWidth == 32 && other.NumericWidth == 32)
        { joined = WidenBooleanCompatibleI4(); return true; }
        joined = default;
        return false;
    }

    internal static bool CompatibleReferences(TotalIlStackType left, TotalIlStackType right)
    {
        return left.IsReference && right.IsReference &&
            (left.Equals(right) || left.IsNullReference || right.IsNullReference);
    }

    internal bool Accepts(ITypeSymbol? type)
    { return type is not null && Accepts(type.SpecialType); }

    internal bool Accepts(SpecialType type)
    {
        if (type is SpecialType.System_Object or SpecialType.System_String)
        {
            var expected = type == SpecialType.System_Object ? ObjectReferenceValue : StringReferenceValue;
            return _value == expected || IsNullReference;
        }
        if (type == SpecialType.System_Boolean)
        { return IsBooleanCompatibleI4; }
        var expectedWidth = NumericWidthFor(type);
        return _value != 0 && expectedWidth != 0 && NumericWidth == expectedWidth;
    }

    internal IrTypeId StorageType(IrFactory factory)
    {
        ArgumentNullGuard.NotNull(factory, nameof(factory));
        return _value switch
        {
            ObjectReferenceValue or NullReferenceValue => factory.ObjectType,
            StringReferenceValue => factory.StringType,
            BooleanCompatibleI4Value or Integer32Value => factory.GetOrCreateIntegerType(32, true),
            Integer64Value => factory.GetOrCreateIntegerType(64, true),
            _ => factory.GetOrCreateIntegerType(Math.Abs(_value), true)
        };
    }

    // Current scalar support is the fixed 8/16/32/64-bit C# integer catalogue.
    // CIL normalizes 8/16/32 to I4 and retains I8 as 64 bits.
    internal static int NumericWidthFor(SpecialType type)
    {
        return type == SpecialType.System_Boolean ? 32 :
            CSharpOperationSemantics.TryGetScalarInteger(type, out var integer)
                ? Math.Max(32, integer.BitWidth)
                : 0;
    }

    public bool Equals(TotalIlStackType other)
    { return _value == other._value; }
    public override bool Equals(object? obj)
    { return obj is TotalIlStackType other && Equals(other); }
    public override int GetHashCode()
    { return _value; }
}

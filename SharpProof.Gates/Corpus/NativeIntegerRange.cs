using Microsoft.CodeAnalysis;

namespace SharpProof.Gates.Corpus;

internal static class NativeIntegerRange
{
    internal static bool FitsRuntimeType(Type type, System.Numerics.BigInteger numeric)
    {
        return type == typeof(sbyte) ? numeric >= sbyte.MinValue && numeric <= sbyte.MaxValue :
            type == typeof(byte) ? numeric >= byte.MinValue && numeric <= byte.MaxValue :
            type == typeof(short) ? numeric >= short.MinValue && numeric <= short.MaxValue :
            type == typeof(ushort) || type == typeof(char) ? numeric >= ushort.MinValue && numeric <= ushort.MaxValue :
            type == typeof(int) ? numeric >= int.MinValue && numeric <= int.MaxValue :
            type == typeof(uint) ? numeric >= uint.MinValue && numeric <= uint.MaxValue :
            type == typeof(long) ? numeric >= long.MinValue && numeric <= long.MaxValue :
            type == typeof(ulong) ? numeric >= ulong.MinValue && numeric <= ulong.MaxValue :
            false;
    }

    // Non-integral special types fall through to the switch's null arm.
    internal static bool FitsCompilerType(SpecialType type, System.Numerics.BigInteger numeric)
    {
        return type switch
        {
            SpecialType.System_SByte => numeric >= sbyte.MinValue && numeric <= sbyte.MaxValue,
            SpecialType.System_Byte => numeric >= byte.MinValue && numeric <= byte.MaxValue,
            SpecialType.System_Int16 => numeric >= short.MinValue && numeric <= short.MaxValue,
            SpecialType.System_UInt16 or SpecialType.System_Char => numeric >= ushort.MinValue && numeric <= ushort.MaxValue,
            SpecialType.System_Int32 => numeric >= int.MinValue && numeric <= int.MaxValue,
            SpecialType.System_UInt32 => numeric >= uint.MinValue && numeric <= uint.MaxValue,
            SpecialType.System_Int64 => numeric >= long.MinValue && numeric <= long.MaxValue,
            SpecialType.System_UInt64 => numeric >= ulong.MinValue && numeric <= ulong.MaxValue,
            _ => true,
        };
    }
}

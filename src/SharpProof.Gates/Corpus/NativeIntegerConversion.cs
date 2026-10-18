using System.Numerics;
using Microsoft.CodeAnalysis;

namespace SharpProof.Gates.Corpus;

internal static class NativeIntegerConversion
{
    internal static bool TryRuntimeValue(Type type, BigInteger numeric, out object? result)
    {
        result = null;
        // Explicit BigInteger conversions throw on overflow; an out-of-range
        // witness value is not concrete for this parameter type.
        if (!NativeIntegerRange.FitsRuntimeType(type, numeric))
        { return false; }
        result = type == typeof(sbyte) ? (object)(sbyte)numeric : type == typeof(byte) ? (byte)numeric :
            type == typeof(short) ? (short)numeric : type == typeof(ushort) ? (ushort)numeric :
            type == typeof(char) ? (char)(ushort)numeric : type == typeof(int) ? (int)numeric :
            type == typeof(uint) ? (uint)numeric : type == typeof(long) ? (long)numeric :
            type == typeof(ulong) ? (ulong)numeric : null;
        return result != null;
    }

    internal static bool TryCompilerValue(ITypeSymbol type, BigInteger numeric, out object? result)
    {
        // Explicit BigInteger conversions throw on overflow; an out-of-range
        // witness value is not concrete for this parameter type.
        if (!NativeIntegerRange.FitsCompilerType(type.SpecialType, numeric))
        { result = null; return false; }
        result = type.SpecialType switch
        {
            SpecialType.System_SByte => (sbyte)numeric,
            SpecialType.System_Byte => (byte)numeric,
            SpecialType.System_Int16 => (short)numeric,
            SpecialType.System_UInt16 => (ushort)numeric,
            SpecialType.System_Char => (char)(ushort)numeric,
            SpecialType.System_Int32 => (int)numeric,
            SpecialType.System_UInt32 => (uint)numeric,
            SpecialType.System_Int64 => (long)numeric,
            SpecialType.System_UInt64 => (ulong)numeric,
            _ => null
        };
        return result != null;
    }
}

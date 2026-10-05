// golden-scenario: vc-shadow
using SharpProof.Attributes;
public static class Subject
{
    public static int AAdd(int x)
    {
        Contract.Requires(x == int.MaxValue);
        Contract.Ensures(Contract.Result<int>() == 0);
        try { return checked(x + 1); } catch (System.OverflowException) { return 0; }
    }
    public static uint BSubtract(uint x)
    {
        Contract.Requires(x == 0U);
        Contract.Ensures(Contract.Result<uint>() == 2U);
        try { return checked(x - 1U); } catch (System.OverflowException) { return 2U; }
    }
    public static ulong CMultiply(ulong x)
    {
        Contract.Requires(x == ulong.MaxValue);
        Contract.Ensures(Contract.Result<ulong>() == 42UL && x == Contract.Old(x));
        try { checked { x *= 2UL; } return 0UL; } catch (System.OverflowException) { return 42UL; }
    }
    public static int DNegate(int x)
    {
        Contract.Requires(x == int.MinValue);
        Contract.Ensures(Contract.Result<int>() == int.MinValue + 1);
        try { return checked(-x++); } catch (System.OverflowException) { return x; }
    }
    public static byte ENarrow(byte x)
    {
        Contract.Requires(x == byte.MaxValue);
        Contract.Ensures(Contract.Result<byte>() == 0 && x == byte.MaxValue);
        try { checked { x++; } return x; } catch (System.OverflowException) { return 0; }
    }
    public static int FUnsafeClause(int x)
    {
        Contract.Requires(x == int.MaxValue);
        Contract.Ensures(checked(x + 1) == unchecked(x + 1));
        return x;
    }
    public static long GNoNormalReturn(long x)
    {
        Contract.Requires(x == long.MinValue);
        Contract.Ensures(false);
        return checked(x * -1L);
    }
    public static int HNormalReturn(int x)
    {
        Contract.Ensures(Contract.Result<int>() == checked(Contract.Old(x) + 1));
        Contract.Ensures(Contract.Result<int>() == Contract.Old(x));
        return checked(x + 1);
    }
    public static int ICapturedCompound(int x)
    {
        Contract.Requires(x == 1073741824);
        Contract.Ensures(Contract.Result<int>() == 1073741825 && x == 7);
        Contract.Ensures(Contract.Result<int>() == 7);
        try { try { return checked(x += x++); } catch (System.OverflowException) { return x; } }
        finally { x = 7; }
    }
}

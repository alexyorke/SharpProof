// golden-scenario: vc-shadow
using SharpProof.Attributes;
public static class Subject
{
    public static int AScalar(int x)
    {
        Contract.Requires(x == 0);
        Contract.Ensures(Contract.Result<int>() == 1);
        Contract.Ensures(Contract.Result<int>() == 0);
        return Increment(x);
    }
    public static int BCapturedArguments(int x)
    {
        Contract.Requires(x == 3);
        Contract.Ensures(Contract.Result<int>() == 33 && x == 4);
        Contract.Ensures(Contract.Result<int>() == x * 11);
        return Pack(first: x, second: x++);
    }
    public static ulong CFullUlongDefault(ulong x)
    {
        Contract.Requires(x == 18446744073709551615UL);
        Contract.Ensures(Contract.Result<ulong>() == 0UL);
        Contract.Ensures(Contract.Result<ulong>() == 1UL);
        return Add(x);
    }
    public static int DLoopFrames(int x)
    {
        Contract.Requires(x == 3);
        Contract.Ensures(Contract.Result<int>() == x);
        Contract.Ensures(Contract.Result<int>() == Contract.Old(x));
        for (int i = 0; i < 2; i++) x = Increment(x);
        return x;
    }
    private static int Increment(int value) { return value + 1; }
    private static int Pack(int first, int second) { return first * 10 + second; }
    private static ulong Add(ulong value, ulong step = 1UL) { return unchecked(value + step); }
}

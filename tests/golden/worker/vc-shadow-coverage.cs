// golden-scenario: vc-shadow
using SharpProof.Attributes;
public static class Subject {
    public static ulong AUlong(ulong x) {
        Contract.Requires(x == 18446744073709551615UL);
        Contract.Ensures(Contract.Result<ulong>() == 0UL);
        return unchecked(x + 1UL);
    }
    public static int BAssume(int x) {
        Contract.Ensures(Contract.Result<int>() > 0);
        Contract.Assume(x > 0);
        return x;
    }
    public static int CLoop(int x) {
        Contract.Ensures(Contract.Result<int>() == x);
        while (x < 0) x++;
        return x;
    }
    public static int DCall(int x) {
        Contract.Ensures(Contract.Result<int>() == x);
        return System.Math.Abs(x);
    }
}

// golden-scenario: vc-shadow
using SharpProof.Attributes;
public static class Subject {
    public static int AIdentity(int x, int unused) {
        Contract.Ensures(Contract.Result<int>() == Contract.Old(x));
        return x;
    }
    public static int BImpossible(int x) {
        Contract.Requires(x > 0); Contract.Requires(x < 0);
        Contract.Ensures(Contract.Result<int>() == 7);
        return x;
    }
    public static int CAllThrow(int d) {
        Contract.Requires(d == 0);
        Contract.Ensures(Contract.Result<int>() == 7);
        return 10 / d;
    }
    public static int DGuarded(int d) {
        Contract.Requires(d == 0 || d == 1);
        Contract.Ensures(d == 0 ? Contract.Result<int>() == 0 : Contract.Result<int>() == 10 / d);
        return 10 / d;
    }
    public static int EUnsafe(int d) {
        Contract.Ensures(Contract.Result<int>() / d == 0);
        return 0;
    }
}

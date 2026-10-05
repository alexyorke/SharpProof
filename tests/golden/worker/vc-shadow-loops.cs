// golden-scenario: vc-shadow
using SharpProof.Attributes;
public static class Subject {
    public static int ALoop(int x) {
        Contract.Ensures(Contract.Result<int>() == x);
        Contract.Ensures(Contract.Result<int>() == Contract.Old(x));
        while (x < 3) x++; return x;
    }
    public static int BDo(int x) {
        Contract.Requires(x == 0);
        Contract.Ensures(Contract.Result<int>() == x);
        Contract.Ensures(Contract.Result<int>() == Contract.Old(x));
        do { x++; } while (x < 3); return x;
    }
    public static int CNested(int x) {
        Contract.Requires(x == 0);
        Contract.Ensures(Contract.Result<int>() == x);
        Contract.Ensures(Contract.Result<int>() == Contract.Old(x));
        for (int i = 0; i < 1; i++) { for (int j = 0; j < 2; j++) x++; } return x;
    }
    public static ulong DWrap(ulong x) {
        Contract.Requires(x == ulong.MaxValue);
        Contract.Ensures(Contract.Result<ulong>() == 0UL);
        Contract.Ensures(Contract.Result<ulong>() == Contract.Old(x));
        do { unchecked { x++; } } while (x != 0UL); return x;
    }
    public static int EBeyond(int x) {
        Contract.Requires(x == 0);
        Contract.Ensures(Contract.Result<int>() == x);
        Contract.Ensures(Contract.Result<int>() == 0);
        while (x < 6) x++; return x;
    }
    public static int FInfinite(int x) {
        Contract.Ensures(false); while (true) x++;
    }
    public static int GAssume(int x, int y) {
        Contract.Assume(y > 0); Contract.Ensures(Contract.Result<int>() > 0);
        while (x < 3) x++; return y;
    }
}

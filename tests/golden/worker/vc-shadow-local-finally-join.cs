// golden-scenario: vc-shadow
using SharpProof.Attributes;
public static class Subject
{
    public static int AUninitialized(int d)
    {
        Contract.Ensures(Contract.Result<int>() == 10 / Contract.Old(d));
        int y;
        try { y = 10 / d; } finally { d = 1; }
        return y;
    }
    public static int BInitialized(int d)
    {
        Contract.Ensures(Contract.Result<int>() == 10 / Contract.Old(d));
        int y = 0;
        try { y = 10 / d; } finally { d = 1; }
        return y;
    }
    public static int CAssume(int d)
    {
        Contract.Assume(d == 2);
        Contract.Ensures(Contract.Result<int>() == 5);
        int y;
        try { y = 10 / d; } finally { d = 1; }
        return y;
    }
    public static int DGoto(int d)
    {
        Contract.Ensures(Contract.Result<int>() == 10 / Contract.Old(d));
        int y;
        goto L;
        d = 2;
    L:
        try { y = 10 / d; } finally { d = 1; }
        return y;
    }
}

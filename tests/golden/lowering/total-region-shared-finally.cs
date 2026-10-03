// golden-mode: Total
// golden-contracts: true
using SharpProof.Attributes;
public static class Subject
{
    public static int Target(int x, bool choose)
    {
        Contract.Assume(x == 3);
        Contract.Ensures(choose ? Contract.Result<int>() == Contract.Old(x) : Contract.Result<int>() == 7);
        try { if (choose) return x; x++; }
        finally { x = 7; }
        return x;
    }
}

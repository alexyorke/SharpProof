// golden-mode: Total
// golden-contracts: true
using SharpProof.Attributes;
public static class Subject
{
    public static int Target(int x)
    {
        Contract.Requires(x == 1073741824);
        Contract.Ensures(Contract.Result<int>() == checked(Contract.Old(x) + 1) && x == 7);
        try
        {
            try { return checked(x += x++); }
            catch (System.OverflowException) { return x; }
        }
        finally { x = 7; }
    }
}

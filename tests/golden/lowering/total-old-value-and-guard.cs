// golden-mode: Total
// golden-contracts: true
using SharpProof.Attributes;
internal static class Subject
{
    public static int Target(int x, int d)
    {
        Contract.Ensures(Contract.Old(x / d) == 2);
        d = 0;
        return x;
    }
}

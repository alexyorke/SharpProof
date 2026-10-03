// golden-mode: Total
// golden-contracts: true
using SharpProof.Attributes;
internal static class Subject
{
    public static int Target(int d)
    {
        Contract.Ensures(Contract.Result<int>() / d == Contract.Result<int>() / d);
        return 0;
    }
}

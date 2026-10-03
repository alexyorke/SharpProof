// golden-mode: Total
// golden-contracts: true
using SharpProof.Attributes;
internal static class Subject
{
    public static int Target(int x)
    {
        Contract.Requires(x > 0);
        Contract.Ensures(Contract.Result<int>() == Contract.Old(x) && x == Contract.Result<int>());
        x = unchecked(x + 1);
        return x;
    }
}

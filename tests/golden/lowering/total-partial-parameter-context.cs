// golden-mode: Total
// golden-contracts: true
using SharpProof.Attributes;
internal static partial class Subject
{
    public static partial int Target(int x);
    public static partial int Target(int x)
    {
        Contract.Requires(x > 0);
        return x;
    }
}

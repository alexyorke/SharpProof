// golden-mode: Total
// golden-contracts: true
// golden-context: constructed
using SharpProof.Attributes;
internal static class Subject
{
    public static int Target<T>(int x)
    {
        Contract.Requires(x > 0);
        return x;
    }
}

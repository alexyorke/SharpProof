// golden-mode: Total
// golden-contracts: true
using SharpProof.Attributes;
internal static class Subject
{
    public static long Target(long divisor)
    {
        Contract.Ensures(Contract.Result<long>() / divisor == 0L);
        return 0L;
    }
}

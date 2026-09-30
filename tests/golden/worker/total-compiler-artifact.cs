// golden-scenario: total-artifact
using SharpProof.Attributes;
public static class Subject {
    public static ulong Target(ulong x) {
        Contract.Requires(x == 18446744073709551615UL);
        Contract.Ensures(Contract.Result<ulong>() == 0UL);
        return unchecked(x + 1UL);
    }
}

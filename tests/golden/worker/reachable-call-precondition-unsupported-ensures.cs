// golden-scenario: reachable-source
// golden-native-call-preconditions: true
#undef SHARPPROOF_CONTRACTS
using SharpProof.Attributes;
public static class Subject
{
    [ZeroAllocations]
    public static int Root() => Helper(1);
    private static int Helper(int value)
    {
        Contract.Requires(value > 0);
        Contract.Ensures(System.DateTime.Now.Ticks > value);
        return value;
    }
}

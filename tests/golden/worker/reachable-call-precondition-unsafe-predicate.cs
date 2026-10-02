// golden-scenario: reachable-source
// golden-native-call-preconditions: true
#undef SHARPPROOF_CONTRACTS
using SharpProof.Attributes;
public static class Subject
{
    [ZeroAllocations]
    public static int Root() => Helper(0);
    private static int Helper(int value)
    {
        Contract.Requires(10 / value > 1);
        return 1;
    }
}

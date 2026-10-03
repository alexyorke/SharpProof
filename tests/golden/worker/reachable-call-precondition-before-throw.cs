// golden-scenario: reachable-source
// golden-native-call-preconditions: true
using SharpProof.Attributes;
public static class Subject
{
    [ZeroAllocations]
    public static int Root() => 1 / Helper(0);
    private static int Helper(int value)
    {
        Contract.Requires(value > 0);
        return value;
    }
}

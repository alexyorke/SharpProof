// golden-scenario: reachable-source
// golden-native-call-preconditions: true
using SharpProof.Attributes;
public static class Subject
{
    [ZeroAllocations]
    public static int Root()
    {
        int sum = 0;
        for (int i = 0; i < 2; i++)
            sum += Helper(i);
        return sum;
    }
    private static int Helper(int value)
    {
        Contract.Requires(value > 0);
        return value;
    }
}

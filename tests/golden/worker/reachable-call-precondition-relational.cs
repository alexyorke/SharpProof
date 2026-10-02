// golden-scenario: reachable-source
// golden-native-call-preconditions: true
using SharpProof.Attributes;
public static class Subject
{
    [ZeroAllocations]
    public static int Root(int left, int right)
    {
        if (left >= right)
            return 0;
        return Helper(left, right);
    }
    private static int Helper(int left, int right)
    {
        Contract.Requires(left < right);
        return left;
    }
}

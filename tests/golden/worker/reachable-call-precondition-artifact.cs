// golden-scenario: reachable-source
// golden-total-call-preconditions: true
using SharpProof.Attributes;

static class Subject
{
    [ZeroAllocations]
    public static int Root(int x) => Helper(x) + Helper(x);

    static int Helper(int value)
    {
        Contract.Requires(value > 0);
        return value;
    }
}

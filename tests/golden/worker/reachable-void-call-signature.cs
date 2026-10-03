// golden-scenario: reachable-source
// golden-mutation: void-call-return
using SharpProof.Attributes;

static class Subject
{
    [ZeroAllocations]
    public static int Root()
    {
        Keep();
        Helper();
        return 1;
    }

    static void Keep() { }
    static void Helper() { }
}

// golden-scenario: reachable-source
using SharpProof.Attributes;

static class Subject
{
    [ZeroAllocations]
    public static int Root(System.IDisposable value)
    {
        using var resource = value;
        return 1;
    }
}

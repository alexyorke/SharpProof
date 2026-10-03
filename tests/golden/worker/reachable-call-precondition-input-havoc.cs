// golden-scenario: reachable-source
// golden-native-input-havoc: true
// Probe owned IR: Input havoc, overwrite Current with 42, Input havoc again.
// Original Entry is 1; call, allocation, write and exception witnesses survive.
using SharpProof.Attributes;
public static class Subject
{
    [ZeroAllocations]
    public static int Root(int value) => value;
}

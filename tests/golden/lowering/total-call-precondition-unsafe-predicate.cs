// golden-mode: Total
// golden-inline-source: true
// golden-contracts: true
// golden-call-precondition-input: 0
using SharpProof.Attributes;

public static class Subject
{
    public static int Target(int x) => Helper(x);

    static int Helper(int value)
    {
        Contract.Requires(10 / value > 1);
        return 20 / value;
    }
}

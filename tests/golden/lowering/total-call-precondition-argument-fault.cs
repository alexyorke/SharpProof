// golden-mode: Total
// golden-inline-source: true
// golden-contracts: true
// golden-call-precondition-input: 0
using SharpProof.Attributes;

public static class Subject
{
    public static int Target(int x) => Helper(10 / x);

    static int Helper(int value)
    {
        Contract.Requires(value > 0);
        return 1;
    }
}

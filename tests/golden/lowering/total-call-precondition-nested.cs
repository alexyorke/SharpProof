// golden-mode: Total
// golden-inline-source: true
// golden-contracts: true
// golden-call-precondition-input: 0
using SharpProof.Attributes;

public static class Subject
{
    public static int Target(int x) => Outer(x);

    static int Outer(int value)
    {
        Contract.Requires(value >= 0);
        return Inner(value);
    }

    static int Inner(int value)
    {
        Contract.Requires(value > 0);
        new object();
        return value;
    }
}

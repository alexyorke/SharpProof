// golden-mode: Total
// golden-inline-source: true
// golden-contracts: true
// golden-call-precondition-input: 3
using SharpProof.Attributes;

public static class Subject
{
    public static int Target(int x) => Helper(second: x++, first: x);

    static int Helper(int first, int second)
    {
        Contract.Requires(first == second);
        new object();
        return first * 10 + second;
    }
}

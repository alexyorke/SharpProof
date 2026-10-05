// golden-mode: Total
// golden-inline-source: true
// golden-contracts: true
// golden-call-precondition-input: 0
using SharpProof.Attributes;

public static class Subject
{
    public static int Target(int x)
    {
        while (x < 2)
        {
            Helper(x);
            x++;
        }
        return x;
    }

    static int Helper(int value)
    {
        Contract.Requires(value > 0);
        new object();
        return value;
    }
}

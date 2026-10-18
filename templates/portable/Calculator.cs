using SharpProof.Attributes;

namespace SharpProofStarter;

public static class Calculator
{
    public static int Positive(int value)
    {
        Contract.Requires(value > 0);
        return value;
    }

    // Change 1 to 0 and rebuild to see warning SP0027.
    public static int Example() => Positive(1);
}

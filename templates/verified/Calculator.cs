using SharpProof.Attributes;

namespace SharpProofStarter;

public static class Calculator
{
    public static int Increment(int value)
    {
        Contract.Requires(value >= 0 && value < int.MaxValue);
        Contract.Ensures(Contract.Result<int>() == Contract.Old(value) + 1);
        return checked(value + 1);
    }
}

// golden-scenario: identity
using SharpProof.Attributes;

public static class Subject
{
    public static int Target(int value)
    {
        Contract.Requires(value >= 0 && value <= 10);
        Contract.Ensures(Contract.Result<int>() == value);
        return value;
    }
}

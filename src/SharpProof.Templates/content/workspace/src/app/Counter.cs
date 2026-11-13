using SharpProof.Attributes;

namespace SharpProofStarter;

public static class Counter
{
    public static int Next(int value)
    {
        Contract.Requires(value >= 0 && value < int.MaxValue);
        Contract.Ensures(Contract.Result<int>() == Contract.Old(value) + 1);
        return checked(value + 1);
    }

    // To see a failure, uncomment the next line and build. The call breaks the
    // precondition on Next: the analyzer reports SP0027, and a verifying build
    // also refutes the call with SP0051 and fails.
    // public static int Broken() => Next(-1);
}

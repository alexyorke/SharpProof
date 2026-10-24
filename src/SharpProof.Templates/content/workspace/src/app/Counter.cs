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

    // Deliberately emits portable diagnostic SP0027. Change -1 to 0 to fix it.
    public static int DemonstrateDiagnostic() => Next(-1);
}

// Retires the legacy diamond-join regression through typed public verification.
using SharpProof.Attributes;
public static class Subject
{
    public static int Target(bool condition) { Contract.Ensures(Contract.Result<int>() == 7); if (condition) { } else { } return 7; }
}

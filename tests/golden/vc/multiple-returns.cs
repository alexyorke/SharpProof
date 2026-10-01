// Retires the legacy multiple-returns regression through typed public verification.
using SharpProof.Attributes;
public static class Subject
{
    public static int Target(bool condition) { Contract.Ensures(Contract.Result<int>() == (condition ? 1 : 2)); if (condition) return 1; return 2; }
}

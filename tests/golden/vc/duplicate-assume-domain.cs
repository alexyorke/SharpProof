// Retires the legacy duplicate-assume-domain regression through typed public verification.
using SharpProof.Attributes;
public static class Subject
{
    public static int Target(int value) { Contract.Assume(value >= int.MinValue); Contract.Ensures(Contract.Result<int>() >= int.MinValue); return value; }
}

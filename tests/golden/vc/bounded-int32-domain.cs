// Retires the legacy bounded-int32-domain regression through typed public verification.
using SharpProof.Attributes;
public static class Subject
{
    public static int Target(int value) { Contract.Ensures(Contract.Result<int>() >= int.MinValue && Contract.Result<int>() <= int.MaxValue); return value; }
}

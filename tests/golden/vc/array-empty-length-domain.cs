// Retires the legacy array-empty-length-domain regression through typed public verification.
using SharpProof.Attributes;
public static class Subject
{
    public static int Target() { Contract.Ensures(Contract.Result<int>() >= int.MinValue && Contract.Result<int>() <= int.MaxValue); return System.Array.Empty<int>().Length; }
}

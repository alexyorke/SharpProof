// Retires the legacy full-int64-domain regression through typed public verification.
using SharpProof.Attributes;
public static class Subject
{
    public static long Target(long value) { Contract.Ensures(Contract.Result<long>() >= long.MinValue && Contract.Result<long>() <= long.MaxValue); return value; }
}

// Retires the legacy branch-fault regression through typed public verification.
using SharpProof.Attributes;
public static class Subject
{
    public static long Target(long divisor) { Contract.Ensures(divisor != 0L); if (1L / divisor > 0L) return 7L; return 7L; }
}

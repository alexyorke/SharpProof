// Retires the legacy call-argument-fault regression through typed public verification.
using SharpProof.Attributes;
public static class Subject
{
    public static int Target(int divisor) { Contract.Ensures(divisor != 0); return System.Math.Abs(1 / divisor); }
}

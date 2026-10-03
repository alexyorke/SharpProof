// Retires the legacy normal-completion-domain regression through typed public verification.
using SharpProof.Attributes;
public static class Subject
{
    public static int Target([InRange(0, 0)] int divisor) { Contract.Ensures(false); return 1 / divisor; }
}

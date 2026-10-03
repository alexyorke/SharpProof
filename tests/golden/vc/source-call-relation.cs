// Retires the legacy source-call-relation regression through typed public verification.
using SharpProof.Attributes;
public static class Subject
{
    private static int Increment(int value) => unchecked(value + 1); public static int Target(int value) { Contract.Ensures(Contract.Result<int>() == unchecked(value + 1)); return Increment(value); }
}

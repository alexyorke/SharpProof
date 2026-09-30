// golden-scenario: replay-guard-unresolved
// A Total contract guard with no concrete value cannot authorize a refutation.
using SharpProof.Attributes;
public static class Subject
{
    public static void Target(int value) { Contract.Ensures(value >= 0); }
}

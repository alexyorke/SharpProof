// golden-scenario: replay-spec-result-unbound
// The directive selects a Total IR replay boundary scenario until typed lowering is available.
// Entry value is -1; generic provider value is -1 (or 2 for input-bound).
using SharpProof.Attributes;

public static class Subject
{
    public static void Target(int value) { Contract.Ensures(value >= 0); }
}

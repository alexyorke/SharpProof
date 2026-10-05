// Retires the legacy nested-reassignment regression through typed public verification.
using SharpProof.Attributes;
public static class Subject
{
    public static int Target(bool outer, bool inner) { Contract.Ensures(Contract.Result<int>() == (outer ? (inner ? 1 : 2) : 3)); int value = 0; if (outer) { if (inner) value = 1; else value = 2; } else value = 3; return value; }
}

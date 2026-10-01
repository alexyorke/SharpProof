// Retires the legacy receiver-fault regression through typed public verification.
using SharpProof.Attributes;
public static class Subject
{
    public static int Target(string value) { Contract.Ensures(value != null); return value.Length; }
}

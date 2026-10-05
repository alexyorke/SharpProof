// Retires the legacy sequential-framework-calls regression through typed public verification.
using SharpProof.Attributes;
public static class Subject
{
    public static int Target() { Contract.Ensures(Contract.Result<int>() == 0); var first = System.Array.Empty<int>(); var second = System.Array.Empty<int>(); return first.Length + second.Length; }
}

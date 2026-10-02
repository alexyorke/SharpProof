// vc-corrupt-spec-identity
using SharpProof.Attributes;
public static class Subject {
    public static int[] Target() {
        Contract.Ensures(Contract.Result<int[]>() != null);
        return System.Array.Empty<int>();
    }
}

// golden-scenario: vc-shadow-reference
using SharpProof.Attributes;
public static class Subject {
    public static int Target(string text, int[] array, object unused) {
        Contract.Requires(text != null && text.Length == 3 && array != null && array.Length == 3);
        Contract.Ensures(Contract.Result<int>() == text.Length && array.Length == text.Length);
        Contract.Ensures(Contract.Result<int>() == 0);
        return Length(text);
    }
    private static int Length(string value) { return value.Length; }
}

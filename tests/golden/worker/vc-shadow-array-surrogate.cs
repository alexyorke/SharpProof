// golden-scenario: vc-shadow-reference
using SharpProof.Attributes;
public static class Subject {
    public static char Target(char[] x) {
        Contract.Requires(x != null && x.Length == 1 && x[0] == '\ud800');
        Contract.Ensures(Contract.Result<char>() == '\ud800');
        Contract.Ensures(Contract.Result<char>() == '\ud801');
        return Read(x);
    }
    private static char Read(char[] value) { return value[0]; }
}

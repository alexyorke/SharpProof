// vc-depth-limit: 1
using SharpProof.Attributes;
public static class Subject {
    public static int Target(int value) {
        Contract.Requires(value > 0);
        Contract.Ensures(true);
        return value;
    }
}

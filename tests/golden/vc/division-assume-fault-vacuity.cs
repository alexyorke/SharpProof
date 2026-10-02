using SharpProof.Attributes;
public static class Subject {
    public static int Target(int value) {
        Contract.Requires(value == 0); Contract.Assume(1 / value == 1 / value); Contract.Ensures(false);
        return 1 / value;
    }
}

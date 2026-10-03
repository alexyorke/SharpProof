using SharpProof.Attributes;
public static class Subject {
    public static int Target(int value) {
        Contract.Requires(value != 0); Contract.Ensures(true);
        return 1 / value;
    }
}

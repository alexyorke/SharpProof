using SharpProof.Attributes;
public static class Subject {
    public static int Target(int value) {
        Contract.Assume(false); Contract.Ensures(false);
        return value;
    }
}

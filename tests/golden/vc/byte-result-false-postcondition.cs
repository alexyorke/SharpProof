using SharpProof.Attributes;
public static class Subject {
    public static byte Target() {
        Contract.Ensures(false);
        return byte.MaxValue;
    }
}

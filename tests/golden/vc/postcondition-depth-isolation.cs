// vc-depth-limit: 3
using SharpProof.Attributes;
public static class Subject {
    public static int Target(int value) {
        Contract.Ensures(Contract.Result<int>() == value);
        Contract.Ensures(value > 0 && value > 1 && value > 2 && value > 3);
        return value;
    }
}

// golden-scenario: vc-shadow-meter-boundary
using SharpProof.Attributes;
public static class Subject {
    public static int Target(int x, int unused) {
        Contract.Ensures(Contract.Result<int>() == Contract.Old(x));
        return x;
    }
}

// golden-scenario: vc-assume-placement
using SharpProof.Attributes;
public static class Subject {
    public static int Target(int x) {
        Contract.Assume(x == 7);
        Contract.Ensures(Contract.Result<int>() == 7);
        x = 0;
        return x;
    }
}

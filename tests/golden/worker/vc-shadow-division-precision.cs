// golden-scenario: vc-shadow
using SharpProof.Attributes;
public static class Subject {
    public static int Constant(int d) {
        Contract.Ensures(Contract.Result<int>() == 10 / d);
        return 10 / d;
    }
    public static int Variable(int x, int d) {
        Contract.Requires(d != 0);
        Contract.Ensures(Contract.Result<int>() == x / d);
        return x / d;
    }
}

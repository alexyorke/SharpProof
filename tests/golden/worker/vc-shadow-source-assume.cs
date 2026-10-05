// golden-scenario: vc-shadow
using SharpProof.Attributes;
public static class Subject {
    public static int AFilterThenMutation(int x) {
        Contract.Assume(x == 7);
        Contract.Ensures(Contract.Result<int>() == 8 && Contract.Old(x) == 7);
        x++;
        return x;
    }
    public static int BContradictoryFilter(int x) {
        Contract.Assume(x > 0);
        Contract.Assume(x < 0);
        Contract.Ensures(Contract.Result<int>() == 42);
        return x;
    }
    public static int CWrongReturn(int x) {
        Contract.Assume(x == 7);
        Contract.Ensures(Contract.Result<int>() == 7);
        x = 0;
        return x;
    }
    public static int DUndefinedFilter(int x) {
        Contract.Requires(x == 0);
        Contract.Assume(10 / x == 10 / x);
        Contract.Ensures(Contract.Result<int>() == 42);
        return x;
    }
}

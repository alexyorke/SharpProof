// golden-scenario: artifact-passive-enrollment
using SharpProof.Attributes;
public static class Subject {
    public static int Target(int x, bool choose, int unused) {
        Contract.Requires(x >= -10);
        Contract.Ensures(Contract.Result<int>() == (choose ? unchecked(Contract.Old(x) + 1) : unchecked(Contract.Old(x) - 1)) && x == Contract.Result<int>());
        Contract.Ensures(Contract.Result<int>() == Contract.Old(x));
        if (choose) x = unchecked(x + 1); else x = unchecked(x - 1);
        return x;
    }
}

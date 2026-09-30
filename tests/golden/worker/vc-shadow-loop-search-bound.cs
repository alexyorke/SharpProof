// golden-scenario: vc-shadow
using SharpProof.Attributes;
public static class Subject { public static int Target(int x) {
    Contract.Requires(x == 0);
    Contract.Ensures(Contract.Result<int>() == x);
    Contract.Ensures(Contract.Result<int>() == 0);
    while (x < 6) x++; return x;
} }

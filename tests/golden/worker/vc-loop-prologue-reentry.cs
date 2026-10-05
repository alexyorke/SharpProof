// golden-scenario: vc-loop-prologue-reentry
using SharpProof.Attributes;
public static class Subject { public static int Target(int x) {
    Contract.Assume(x >= 0); Contract.Ensures(Contract.Result<int>() == x);
    while (x < 3) x++; return x;
} }

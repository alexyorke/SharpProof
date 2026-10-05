// golden-mode: Total
// golden-contracts: true
using SharpProof.Attributes;
public static class Subject {
    public static int Target(int x, int y) {
        Contract.Assume(y > 0);
        Contract.Ensures(Contract.Result<int>() == x && y > 0);
        for (int i = 0; i < 3; i++) {
            if (i == 1) continue;
            x++;
            if (x == 0) break;
        }
        return x;
    }
}

// golden-scenario: total-claim-results
using SharpProof.Attributes;

public static class Subject
{
    public static ulong Wide(ulong x)
    {
        Contract.Requires(x == ulong.MaxValue);
        Contract.Ensures(Contract.Result<ulong>() == 0UL);
        return x;
    }

    public static string Empty(string x)
    {
        Contract.Requires(x != null && x.Length == 0);
        Contract.Ensures(false);
        return x;
    }

    public static int Contradictory(int x)
    {
        Contract.Requires(x > 0 && x < 0);
        Contract.Ensures(false);
        return x;
    }

    public static int Never(int x)
    {
        Contract.Ensures(false);
        throw null!;
    }

    public static int Bounded(int x)
    {
        Contract.Requires(x == 0);
        Contract.Ensures(Contract.Result<int>() == 0);
        while (x < 6) x++;
        return x;
    }
}

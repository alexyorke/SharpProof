using SharpProof.Attributes;
public static class Loops
{
    public static int CountUp(int n)
    {
        Contract.Requires(n >= 0);
        Contract.Ensures(Contract.Result<int>() == n);
        var i = 0;
        while (i < n) i++;
        return i;
    }
    [DoesNotThrow]
    public static int Sum([NotNull] int[] values)
    {
        var total = 0;
        for (var i = 0; i < values.Length; i++) total += values[i];
        return total;
    }
}
public sealed class Counter
{
    private int count;
    public int SetThenRead()
    {
        Contract.Ensures(Contract.Result<int>() == 7);
        count = 7;
        return count;
    }
}

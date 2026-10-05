// golden-mode: Total
// golden-inline-source: true
public static class Subject
{
    public static int Target(int x)
    {
        try { return Callee(x); }
        catch (System.DivideByZeroException) when (++x > 0) { return 7; }
        catch (System.OverflowException) { return x; }
    }
    private static int Callee(int value)
    {
        try { return 10 / value; }
        finally { value = checked((byte)(value + 256)); }
    }
}

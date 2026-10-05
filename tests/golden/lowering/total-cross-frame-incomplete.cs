// golden-mode: Total
// golden-inline-source: true
public static class Subject
{
    public static int Target(int x)
    {
        try { return Callee(x); }
        catch (System.DivideByZeroException) when (x > 0) { return x; }
    }
    private static int Callee(int value)
    {
        value++;
        return System.Math.Abs(value);
    }
}

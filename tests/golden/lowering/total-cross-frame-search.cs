// golden-mode: Total
// golden-inline-source: true
public static class Subject
{
    public static int Target(int x)
    {
        try { return Callee(x); }
        catch (System.DivideByZeroException) when (++x > 0 && Filter(x - 1)) { return 7; }
        catch (System.DivideByZeroException) { return x; }
        finally { x += 10; }
    }
    private static int Callee(int value)
    {
        try { return 10 / value; }
        finally { value++; }
    }
    private static bool Filter(int value)
    {
        try { return More(value); }
        finally { value++; }
    }
    private static bool More(int value)
    {
        try { return value > 0; }
        finally { value = checked((byte)(value + 256)); }
    }
}

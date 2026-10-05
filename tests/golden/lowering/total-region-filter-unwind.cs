// golden-mode: Total
public static class Subject
{
    public static int Target(int x)
    {
        try
        {
            try { return 10 / x; }
            finally { x = x * 10 + 2; }
        }
        catch (System.DivideByZeroException) when ((x = x * 10 + 1) > 0)
        { return x * 10 + 3; }
    }
}

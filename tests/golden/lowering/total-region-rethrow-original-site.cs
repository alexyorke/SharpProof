// golden-mode: Total
public static class Subject
{
    public static int Target(int x)
    {
        try { if (x == 0) return 10 / x; return 20 / (x - 1); }
        catch (System.DivideByZeroException)
        {
            try { x = checked((byte)(unchecked(x + 256))); }
            catch (System.OverflowException) { }
            throw;
        }
    }
}

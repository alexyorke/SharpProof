// golden-mode: Total
public static class Subject
{
    public static int Target(int x)
    {
        int i = 0;
        while (i < 2)
        {
            try { if (i == 0) return 10 / x; return 20 / (x - 1); }
            catch (System.DivideByZeroException)
            {
                try { x = checked((byte)(x + 256)); }
                catch (System.OverflowException) { }
                if (i++ == 1) throw;
            }
            finally { x = 1; }
        }
        return 9;
    }
}

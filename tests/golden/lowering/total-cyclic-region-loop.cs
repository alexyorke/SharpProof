// golden-mode: Total
public static class Subject
{
    public static int Target(int x)
    {
        int i = 0;
        while (i < 2)
        {
            try { x = 10 / x; }
            catch (System.DivideByZeroException) { x = 1; }
            finally { i++; }
        }
        return x;
    }
}

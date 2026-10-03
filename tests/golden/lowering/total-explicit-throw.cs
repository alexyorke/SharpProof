// golden-mode: Total
// golden-inline-source: true
// `throw new X(...)` allocates a core exception and raises an Explicit
// exception whose site names X's hierarchy; the handler is chosen by type.
public static class C
{
    public static int Target(bool flag)
    {
        try
        {
            if (flag) throw new System.InvalidOperationException("failed");
            return 1;
        }
        catch (System.InvalidOperationException)
        {
            return 0;
        }
    }
}

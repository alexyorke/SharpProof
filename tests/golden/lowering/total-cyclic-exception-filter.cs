// golden-mode: Total
public static class Subject
{
    public static int Target(int x)
    {
        try { try { return 10 / x; } finally { x = 10 / x; } }
        catch (System.Exception) when (x == 0) { return 1; }
    }
}

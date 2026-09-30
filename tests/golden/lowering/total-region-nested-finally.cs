// golden-mode: Total
public static class Subject
{
    public static int Target(int x)
    {
        try { try { return x; } finally { x = x * 10 + 1; } }
        finally { x = x * 10 + 2; }
    }
}

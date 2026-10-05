// golden-mode: Total
// golden-inline-source: true
public static class Subject
{
    public static int Target(int x) { return Pack(first: x, second: x++); }
    private static int Pack(int first, int second)
    {
        try { return first * 10 + second; }
        finally { first = 99; }
    }
}

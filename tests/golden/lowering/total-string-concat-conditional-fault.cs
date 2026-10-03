// golden-mode: Total
// golden-inline-source: true
public static class C
{
    private static string Pass(string text, int value) => text;

    public static int Target(int x)
    {
        return ((x == 0 ? "a" : "b") + "c" + (x == 0 ? Pass("d", 10 / x) : "e")).Length;
    }
}

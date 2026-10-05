// golden-mode: Total
// golden-inline-source: true
public static class C
{
    public static int Target(int x)
    {
        return Helper(x);
    }

    private static int Helper(int value)
    {
        new object();
        value++;
        return value;
    }
}

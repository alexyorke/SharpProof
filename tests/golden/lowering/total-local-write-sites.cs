// golden-mode: Total
public static class C
{
    public static int Target(int x)
    {
        var value = x;
        value++;
        value += 2;
        x = value;
        new object();
        return x;
    }
}

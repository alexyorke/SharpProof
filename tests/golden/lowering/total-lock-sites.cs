// golden-mode: Total
public static class C
{
    public static int Target(object gate, int x)
    {
        lock (gate)
        {
            x++;
        }
        return x;
    }
}

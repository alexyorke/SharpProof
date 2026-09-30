// golden-mode: Total
internal static class Subject
{
    public static int Target(int x)
    {
        x += x++;
        return x;
    }
}

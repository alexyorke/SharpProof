// golden-mode: Total
internal static class Subject
{
    public static int Target(int x)
    {
        ref readonly int r = ref x;
        x++;
        return r;
    }
}

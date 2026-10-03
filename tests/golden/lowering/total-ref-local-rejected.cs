// golden-mode: Total
internal static class Subject
{
    public static int Target(int x)
    {
        ref int r = ref x;
        r++;
        return x;
    }
}

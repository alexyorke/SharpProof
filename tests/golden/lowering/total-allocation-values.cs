// golden-mode: Total
public static class C
{
    public static bool Target(object input, int x)
    {
        object first = 10 / x;
        object second = new object();
        return first != null && first != input && first != second;
    }
}

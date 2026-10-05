// golden-mode: Total
public static class C
{
    public static bool Target<T>(T value)
    {
        if (value is sbyte) return true;
        if (value is int) return true;
        return false;
    }
}

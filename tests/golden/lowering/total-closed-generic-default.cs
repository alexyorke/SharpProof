// golden-mode: Total
public static class C
{
    public static int Target<T>()
    {
        T ignored = default(T);
        return 7;
    }
}

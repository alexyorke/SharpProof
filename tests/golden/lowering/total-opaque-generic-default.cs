// golden-mode: Total
// golden-opaque-calls: true
public static class C
{
    public static int Target<T>()
    {
        T ignored = default(T);
        return 7;
    }
}

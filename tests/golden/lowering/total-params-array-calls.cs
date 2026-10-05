// golden-mode: Total
// golden-inline-source: true
// golden-framework-models: true
public static class C
{
    public static int Target()
    {
        return Count() + Count(1, 2);
    }
    private static int Count(params int[] values)
    {
        return values.Length;
    }
}

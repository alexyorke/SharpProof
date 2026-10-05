// golden-mode: Total
// golden-inline-source: true
public static class C
{
    public static int Target(int length)
    {
        try
        {
            int[] values = new int[length];
            return values.Length;
        }
        catch (System.OverflowException)
        {
            return -1;
        }
    }
}

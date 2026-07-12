#nullable enable
public static class UnsafeSuppression
{
    public static int Length()
    {
        string? value = null;
        return value!.Length;
    }
}

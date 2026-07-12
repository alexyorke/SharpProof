#nullable enable
public static class UnnecessarySuppression
{
    public static int Length(string value) => value!.Length;
}

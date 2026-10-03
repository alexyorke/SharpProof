// golden-mode: Total
public static class Subject
{
    public static int Target(int x)
    {
        try { return 10 / x; }
        catch (System.DivideByZeroException) { return 7; }
    }
}
namespace System { public sealed class DivideByZeroException : Exception { } }

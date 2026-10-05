// golden-mode: Total
// golden-opaque-calls: true
// Metadata calls without a model are opaque: an effect-site call, then an
// approximated exception flag and result. A catch-all handles the unknown
// exception.
public static class C
{
    public static int Target(System.Version version, int value)
    {
        try
        {
            var ignored = version.GetHashCode() + System.Environment.TickCount;
            return value;
        }
        catch
        {
            return version.Major;
        }
    }
}

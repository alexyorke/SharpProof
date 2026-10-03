// golden-mode: Total
// golden-opaque-calls: true
// Struct values are opaque: their calls are opaque calls without a null check.
public static class C
{
    public static int Target(System.DateTime time, int value)
    {
        var copy = time;
        var ignored = copy.AddTicks(1).Ticks;
        return value;
    }
}

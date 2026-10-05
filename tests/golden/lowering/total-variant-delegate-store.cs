// golden-mode: Total
// golden-opaque-calls: true
public static class Subject
{
    public static void Target(System.Func<object>[] values, System.Func<object> item)
    {
        values[0] = item;
    }
}

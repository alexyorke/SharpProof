// golden-mode: Total
// golden-framework-models: true
using System;
internal static class Subject
{
    public static int Target(string left, string right, int divisor)
    {
        var empty = Array.Empty<int>();
        var combined = string.Concat(1 / divisor == 0 ? "" : left, right);
        return empty != null && empty.Length == 0 && combined != null ? 1 : 0;
    }
}

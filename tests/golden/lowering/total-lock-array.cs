// golden-mode: Total
// golden-opaque-calls: true
// A lock on a fresh array allocates the array, then synchronizes on it.
public static class C
{
    public static void Target()
    {
        lock (new object[1]) { }
        return;
    }
}

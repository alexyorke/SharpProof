// golden-mode: Total
// golden-inline-source: true
public static class Subject
{
    public static int Target(int x) => Increment(x++) + Increment(x++);
    private static int Increment(int value) => value + 1;
}

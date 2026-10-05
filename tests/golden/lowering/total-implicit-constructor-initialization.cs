// golden-mode: Total
// golden-opaque-calls: true
public sealed class Bomb
{
    static Bomb() { throw new System.InvalidOperationException(); }
}
public static class Subject
{
    public static object Target() => new Bomb();
}

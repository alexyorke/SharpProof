// golden-mode: Total
// golden-opaque-calls: true
public sealed class Box { public int Value; }
public static class Subject
{
    public static int Target()
    {
        Box a = new Box(); a.Value = 3;
        Box b = new Box(); b.Value = 5;
        Box original = a;
        a.Value += (a = b).Value;
        return original.Value * 10 + b.Value;
    }
}

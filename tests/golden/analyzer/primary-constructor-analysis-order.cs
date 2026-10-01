using SharpProof.Attributes;

public static class Guard
{
    public static int Positive(int value)
    {
        Contract.Requires(value > 0);
        return value;
    }
}

public class Base
{
    public Base(int value) { }
}

public sealed class Derived(int marker) : Base(
    double.NaN switch { < 0.0 => 0, _ => Guard.Positive(-1) }) { }

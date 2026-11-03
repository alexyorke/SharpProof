// golden-scenario: call-precondition-claims
using SharpProof.Attributes;

public static class Subject
{
    public static int Positive(int x)
    {
        Contract.Requires(x > 0);
        return x;
    }

    public static int Literal() => Positive(-1);

    public static int Forward(int value) => Positive(value);

    public static int Established(int value)
    {
        Contract.Requires(value > 1);
        return Positive(value);
    }

    public static int Guarded(int value) => value > 0 ? Positive(value) : 0;

    public static int Unbounded() => Positive(System.Environment.TickCount);

    public static int Contradictory(int value)
    {
        Contract.Requires(value > 0 && value < 0);
        return Positive(-1);
    }
}

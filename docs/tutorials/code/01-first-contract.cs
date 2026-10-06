using SharpProof.Attributes;
public static class FirstContract
{
    public static int Positive(int value)
    {
        Contract.Requires(value > 0);
        return value;
    }
    public static int GoodCall() => Positive(1);
    public static int ClosedRange([InRange(0, 10)] int value) => value;
    public static int GoodRange() => ClosedRange(5);
}

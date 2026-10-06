using SharpProof.Attributes;
public static class Outcomes
{
    public static long Proven(long value)
    {
        Contract.Ensures(Contract.Result<long>() == value);
        return value;
    }
    public static long Refuted(long value)
    {
        Contract.Ensures(Contract.Result<long>() > value);
        return value;
    }
    public static volatile int Shared;
    [DoesNotThrow]
    public static int Unknown() => Shared;
}

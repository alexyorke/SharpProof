using SharpProof.Attributes;
public static class Diagnostics
{
    public static int Positive(int value)
    {
        Contract.Requires(value > 0);
        return value;
    }
    public static int BadCall() => Positive(0);
    [ZeroAllocations]
    public static object Allocates() => new object();
    [ZeroAllocations]
    public static int DelegateCall()
    {
        System.Func<int> value = () => 1;
        return value();
    }
}

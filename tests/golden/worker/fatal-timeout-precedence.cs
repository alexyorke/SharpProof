// golden-scenario: fatal-timeout-precedence
using SharpProof.Attributes;
internal static class Subject
{
    public static long A(long value)
    {
        Contract.Ensures(Contract.Result<long>() == value);
        return value;
    }
    public static long B(long value)
    {
        Contract.Ensures(Contract.Result<long>() == value);
        return value;
    }
}

using System.Diagnostics;

namespace SharpProof.Attributes;

public static class Contract
{
    public const string ConditionalSymbol = "SHARPPROOF_CONTRACTS";

    [Conditional(ConditionalSymbol)]
    public static void Requires(bool condition)
    {
    }

    [Conditional(ConditionalSymbol)]
    public static void Ensures(bool condition)
    {
    }

    [Conditional(ConditionalSymbol)]
    public static void Assume(bool condition)
    {
    }

    public static T Result<T>()
    {
        throw new InvalidOperationException("Contract.Result<T>() is valid only inside Contract.Ensures(...).");
    }

    public static T Old<T>(T value)
    {
        throw new InvalidOperationException("Contract.Old(...) is valid only inside Contract.Ensures(...).");
    }
}

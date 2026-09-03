using System.Diagnostics;

namespace SharpProof.Attributes;

/// <summary>Provides compiler-bound static contract clauses and contract expressions.</summary>
public static class Contract
{
    /// <summary>Names the conditional-compilation symbol that emits contract statement calls.</summary>
    public const string ConditionalSymbol = "SHARPPROOF_CONTRACTS";

    /// <summary>Declares a precondition in the direct contract prologue.</summary>
    /// <param name="condition">The condition required on entry.</param>
    [Conditional(ConditionalSymbol)]
    public static void Requires(bool condition)
    {
    }

    /// <summary>Declares a postcondition in the direct contract prologue.</summary>
    /// <param name="condition">The condition required on normal return.</param>
    [Conditional(ConditionalSymbol)]
    public static void Ensures(bool condition)
    {
    }

    /// <summary>Declares explicit user-supplied proof evidence in the direct contract prologue.</summary>
    /// <param name="condition">The condition to assume for static analysis.</param>
    [Conditional(ConditionalSymbol)]
    public static void Assume(bool condition)
    {
    }

    /// <summary>Represents the normal return value inside a postcondition.</summary>
    /// <typeparam name="T">The callable return type.</typeparam>
    /// <returns>A static-analysis placeholder for the normal return value.</returns>
    public static T Result<T>()
    {
        throw new InvalidOperationException("Contract.Result<T>() is valid only inside Contract.Ensures(...).");
    }

    /// <summary>Represents the entry value of an expression inside a postcondition.</summary>
    /// <typeparam name="T">The expression value type.</typeparam>
    /// <param name="value">The expression whose entry value is requested.</param>
    /// <returns>A static-analysis placeholder for the entry value.</returns>
    public static T Old<T>(T value)
    {
        throw new InvalidOperationException("Contract.Old(...) is valid only inside Contract.Ensures(...).");
    }
}

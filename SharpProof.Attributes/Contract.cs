using System.Diagnostics;

namespace SharpProof.Attributes;

/// <summary>Provides compiler-bound static contract clauses and contract expressions.</summary>
/// <remarks>
/// These clauses describe static proof obligations; they do not perform runtime validation.
/// Put clauses at the beginning of the callable body, before executable statements.
/// </remarks>
public static class Contract
{
    /// <summary>Names the reserved conditional-compilation symbol used by contract binding.</summary>
    /// <remarks>Do not define this symbol. SharpProof rejects it because contract calls must not execute at runtime.</remarks>
    public const string ConditionalSymbol = "SHARPPROOF_CONTRACTS";

    /// <summary>Declares a precondition in the direct contract prologue.</summary>
    /// <param name="condition">The condition required on entry.</param>
    /// <remarks>This is a static requirement on callers, not a runtime argument check.</remarks>
    [Conditional(ConditionalSymbol)]
    public static void Requires(bool condition)
    {
    }

    /// <summary>Declares a postcondition in the direct contract prologue.</summary>
    /// <param name="condition">The condition required on normal return.</param>
    /// <remarks>
    /// Describes normal returns only; it does not prove termination or absence of exceptions.
    /// Use <see cref="Result{T}"/> and <see cref="Old{T}(T)"/> only inside this clause.
    /// </remarks>
    [Conditional(ConditionalSymbol)]
    public static void Ensures(bool condition)
    {
    }

    /// <summary>Declares explicit user-supplied proof evidence in the direct contract prologue.</summary>
    /// <param name="condition">The condition to assume for static analysis.</param>
    /// <remarks>This adds trusted evidence, not a proven fact. It can make verification conditional on an incorrect assumption.</remarks>
    [Conditional(ConditionalSymbol)]
    public static void Assume(bool condition)
    {
    }

    /// <summary>Represents the normal return value inside a postcondition.</summary>
    /// <typeparam name="T">The callable return type.</typeparam>
    /// <returns>A static-analysis placeholder for the normal return value.</returns>
    /// <remarks>Valid only inside <see cref="Ensures"/>. T must match the enclosing callable's return type. Throws if executed.</remarks>
    public static T Result<T>()
    {
        throw new InvalidOperationException("Contract.Result<T>() is valid only inside Contract.Ensures(...).");
    }

    /// <summary>Represents the entry value of an expression inside a postcondition.</summary>
    /// <typeparam name="T">The expression value type.</typeparam>
    /// <param name="value">The expression whose entry value is requested.</param>
    /// <returns>A static-analysis placeholder for the entry value.</returns>
    /// <remarks>Valid only inside <see cref="Ensures"/>. Do not nest Old or Result inside Old. Throws if executed.</remarks>
    public static T Old<T>(/* SPINT001: all values valid - Placeholder always rejects runtime use. */ T value /* SPINT001: all values valid - generic values are forwarded or serialized, including null when the type permits it */)
    {
        throw new InvalidOperationException("Contract.Old(...) is valid only inside Contract.Ensures(...).");
    }
}

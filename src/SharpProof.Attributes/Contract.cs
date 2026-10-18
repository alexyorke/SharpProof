using System.Diagnostics;

namespace SharpProof.Attributes;

/// <summary>Provides compiler-bound static contract clauses and contract expressions.</summary>
/// <remarks>
/// These clauses describe static proof obligations; they do not perform runtime validation.
/// Put clauses at the beginning of the callable body, before executable statements.
/// The C# compiler normally omits the entire conditional clause call, including argument evaluation.
/// Expressions must fit the supported contract model; valid C# syntax alone does not guarantee proof support.
/// </remarks>
/// <example><code>
/// public static int Increment(int value)
/// {
///     Contract.Requires(value &gt;= 0 &amp;&amp; value &lt; int.MaxValue);
///     Contract.Ensures(Contract.Result&lt;int&gt;() == Contract.Old(value) + 1);
///     return checked(value + 1);
/// }
/// </code></example>
public static class Contract
{
    /// <summary>Names the reserved conditional-compilation symbol used by contract binding.</summary>
    /// <remarks>Do not define this symbol. SharpProof rejects it because contract calls must not execute at runtime.</remarks>
    public const string ConditionalSymbol = "SHARPPROOF_CONTRACTS";

    /// <summary>Declares a precondition in the direct contract prologue.</summary>
    /// <param name="condition">The condition required on entry.</param>
    /// <remarks>
    /// Use a direct call in the contiguous contract prologue, for example <c>Contract.Requires(value &gt; 0)</c>.
    /// This restricts the callee's entry domain and creates modeled caller obligations; it is not a runtime argument check.
    /// The call and its argument evaluation are normally compiler-elided. Keep executable input validation where required.
    /// </remarks>
    [Conditional(ConditionalSymbol)]
    public static void Requires(bool condition)
    {
    }

    /// <summary>Declares a postcondition in the direct contract prologue.</summary>
    /// <param name="condition">The condition required on normal return.</param>
    /// <remarks>
    /// Describes normal returns only; it does not prove termination or absence of exceptions.
    /// Use <see cref="Result{T}"/> and <see cref="Old{T}(T)"/> only inside this clause.
    /// Use a direct call in the contiguous prologue, for example <c>Contract.Ensures(Contract.Result&lt;int&gt;() == 1)</c>.
    /// The condition must bind to the admitted model; side effects or unsupported expressions do not create proof evidence.
    /// </remarks>
    [Conditional(ConditionalSymbol)]
    public static void Ensures(bool condition)
    {
    }

    /// <summary>Declares explicit user-supplied proof evidence in the direct contract prologue.</summary>
    /// <param name="condition">The condition to assume for static analysis.</param>
    /// <remarks>
    /// Use a direct call in the contiguous contract prologue. This adds user-supplied evidence, not a proven fact.
    /// An incorrect assumption can hide reachable failures or make a proof vacuous. The worker records it and applies the
    /// configured assumption policy; the default strict policy rejects declared user assumptions.
    /// </remarks>
    [Conditional(ConditionalSymbol)]
    public static void Assume(bool condition)
    {
    }

    /// <summary>Represents the normal return value inside a postcondition.</summary>
    /// <typeparam name="T">The callable return type.</typeparam>
    /// <returns>A static-analysis placeholder for the normal return value.</returns>
    /// <remarks>Valid only inside <see cref="Ensures"/>. T must match the enclosing callable's return type. Throws if executed.</remarks>
    /// <exception cref="InvalidOperationException">The expression is executed at runtime.</exception>
    public static T Result<T>()
    {
        throw new InvalidOperationException("Contract.Result<T>() is valid only inside Contract.Ensures(...).");
    }

    /// <summary>Represents the entry value of an expression inside a postcondition.</summary>
    /// <typeparam name="T">The expression value type.</typeparam>
    /// <param name="value">The expression whose entry value is requested.</param>
    /// <returns>A static-analysis placeholder for the entry value.</returns>
    /// <remarks>Valid only inside <see cref="Ensures"/>. Do not nest Old or Result inside Old. Throws if executed.</remarks>
    /// <exception cref="InvalidOperationException">The expression is executed at runtime.</exception>
    public static T Old<T>(/* SPINT001: all values valid - Placeholder always rejects runtime use. */ T value /* SPINT001: all values valid - generic values are forwarded or serialized, including null when the type permits it */)
    {
        throw new InvalidOperationException("Contract.Old(...) is valid only inside Contract.Ensures(...).");
    }
}

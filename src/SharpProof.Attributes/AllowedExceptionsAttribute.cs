namespace SharpProof.Attributes;

/// <summary>Declares the exception types that a member may let escape.</summary>
/// <remarks>
/// Use exception types, for example <c>[AllowedExceptions(typeof(ArgumentException))]</c>.
/// An empty allowance admits no escaping exception types. Null elements or unrelated types are invalid contract data.
/// This selects a modeled synchronous-exception obligation; it does not install catch handlers or prove termination.
/// </remarks>
[AttributeUsage(SharpProofAttributeTargets.Contract, Inherited = false,
    AllowMultiple = true)]
public sealed class AllowedExceptionsAttribute : Attribute
{
    /// <summary>Creates an escaping-exception allowance.</summary>
    /// <param name="exceptionTypes">The allowed exception types.</param>
    /// <exception cref="ArgumentNullException"><paramref name="exceptionTypes"/> is null.</exception>
    public AllowedExceptionsAttribute(params Type[] exceptionTypes)
    {
        ExceptionTypes = exceptionTypes ?? throw new ArgumentNullException(nameof(exceptionTypes));
    }

    /// <summary>Gets the allowed exception types.</summary>
    /// <value>The allowed exception types.</value>
    /// <remarks>Type validity is checked by SharpProof; listing a type is not evidence that the body satisfies the allowance.</remarks>
    public Type[] ExceptionTypes { get; }
}

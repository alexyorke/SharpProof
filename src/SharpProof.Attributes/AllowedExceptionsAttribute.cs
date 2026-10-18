namespace SharpProof.Attributes;

/// <summary>Declares the exception types that a member may let escape.</summary>
[AttributeUsage(SharpProofAttributeTargets.Contract, Inherited = false,
    AllowMultiple = true)]
public sealed class AllowedExceptionsAttribute : Attribute
{
    /// <summary>Creates an escaping-exception allowance.</summary>
    /// <param name="exceptionTypes">The allowed exception types.</param>
    public AllowedExceptionsAttribute(params Type[] exceptionTypes)
    {
        ExceptionTypes = exceptionTypes ?? throw new ArgumentNullException(nameof(exceptionTypes));
    }

    /// <summary>Gets the allowed exception types.</summary>
    /// <value>The allowed exception types.</value>
    public Type[] ExceptionTypes { get; }
}

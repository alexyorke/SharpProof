namespace SharpProof.Attributes;

/// <summary>Declares the exception types that a member may let escape.</summary>
[AttributeUsage(SharpProofAttributeTargets.Contract, Inherited = false,
    AllowMultiple = true)]
public sealed class AllowedExceptionsAttribute(params Type[] exceptionTypes) : Attribute
{
    /// <summary>Creates an escaping-exception allowance.</summary>
    /// <param name="exceptionTypes">The allowed exception types.</param>
    public Type[] ExceptionTypes { get; } = exceptionTypes ?? throw new ArgumentNullException(nameof(exceptionTypes));
}

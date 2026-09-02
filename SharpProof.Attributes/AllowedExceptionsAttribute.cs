namespace SharpProof.Attributes;
[AttributeUsage(SharpProofAttributeTargets.Contract, Inherited = false,
    AllowMultiple = true)]
public sealed class AllowedExceptionsAttribute(params Type[] exceptionTypes) : Attribute
{
    public Type[] ExceptionTypes { get; } = exceptionTypes ?? throw new ArgumentNullException(nameof(exceptionTypes));
}

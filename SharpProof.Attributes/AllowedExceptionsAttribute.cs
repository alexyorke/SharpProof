namespace SharpProof.Attributes;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Property, Inherited = false,
    AllowMultiple = true)]
public sealed class AllowedExceptionsAttribute(params Type[] exceptionTypes) : Attribute {
    public Type[] ExceptionTypes { get; } = exceptionTypes ?? throw new ArgumentNullException(nameof(exceptionTypes));
}

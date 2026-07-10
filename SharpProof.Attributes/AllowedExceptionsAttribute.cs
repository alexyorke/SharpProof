using System;

namespace SharpProof.Attributes;

[AttributeUsage(AttributeTargets.All, Inherited = false, AllowMultiple = true)]
public sealed class AllowedExceptionsAttribute : Attribute
{
    public AllowedExceptionsAttribute(params Type[] exceptionTypes)
    {
        ExceptionTypes = exceptionTypes ?? throw new ArgumentNullException(nameof(exceptionTypes));
    }

    public Type[] ExceptionTypes { get; }
}
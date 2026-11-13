namespace System.Diagnostics.CodeAnalysis;

[Microsoft.CodeAnalysis.Embedded]
[AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
internal sealed class NotNullWhenAttribute(bool returnValue) : Attribute
{
    public bool ReturnValue { get; } = returnValue;
}

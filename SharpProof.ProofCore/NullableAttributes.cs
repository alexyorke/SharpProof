namespace System.Diagnostics.CodeAnalysis;
[AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
internal sealed class NotNullWhenAttribute(bool returnValue) : Attribute {
    public bool ReturnValue { get; } = returnValue;
}
[AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
internal sealed class MaybeNullWhenAttribute(bool returnValue) : Attribute {
    public bool ReturnValue { get; } = returnValue;
}
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property, AllowMultiple = true, Inherited = false)]
internal sealed class MemberNotNullWhenAttribute(bool returnValue, params string[] members) : Attribute {
    public bool ReturnValue { get; } = returnValue;
    public string[] Members { get; } = members;
}

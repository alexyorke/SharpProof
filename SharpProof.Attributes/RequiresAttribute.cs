namespace SharpProof.Attributes;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor, Inherited = false, AllowMultiple = true)]
public sealed class RequiresAttribute(string condition) : Attribute {
    public string Condition { get; } = condition ?? throw new ArgumentNullException(nameof(condition));
}

namespace SharpProof.Attributes;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor, Inherited = false, AllowMultiple = true)]
public sealed class RequiresAttribute : Attribute {
    public RequiresAttribute(string condition) {
        Condition = condition ?? throw new ArgumentNullException(nameof(condition));
    }

    public string Condition { get; }
}

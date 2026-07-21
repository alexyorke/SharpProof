namespace SharpProof.Attributes;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Property, Inherited = false, AllowMultiple = true)]
public sealed class EnsuresAttribute : Attribute {
    public EnsuresAttribute(string condition) {
        Condition = condition ?? throw new ArgumentNullException(nameof(condition));
    }

    public string Condition { get; }
}

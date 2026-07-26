namespace SharpProof.Attributes;
[Obsolete("String contracts are legacy syntax. Use Contract.Ensures with a compiler-bound Boolean expression.")]
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Property, Inherited = false,
    AllowMultiple = true)]
public sealed class EnsuresAttribute(string condition) : Attribute {
    public string Condition { get; } = condition ?? throw new ArgumentNullException(nameof(condition));
}

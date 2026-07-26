namespace SharpProof.Attributes;
[Obsolete("String contracts are legacy syntax. Use Contract.Requires with a compiler-bound Boolean expression.")]
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor, Inherited = false, AllowMultiple = true)]
public sealed class RequiresAttribute(string condition) : Attribute {
    public string Condition { get; } = condition ?? throw new ArgumentNullException(nameof(condition));
}

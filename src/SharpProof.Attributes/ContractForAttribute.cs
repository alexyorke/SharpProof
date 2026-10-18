namespace SharpProof.Attributes;

/// <summary>Associates a static contract companion class with its target type.</summary>
/// <remarks>
/// Place this on a static companion distinct from the target interface or class. An instance target member uses a static
/// companion member with an explicit first receiver parameter; remaining parameter/ref kinds, generic constraints,
/// nullability and return type must match the target overload. The relationship must be acyclic.
/// Binding uses compiler symbols after generators run. It declares a target contract; it does not prove arbitrary implementations satisfy it.
/// Valid direct clauses on the target take precedence over companion clauses rather than combining with them.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ContractForAttribute : Attribute
{
    /// <summary>Creates a contract-companion association.</summary>
    /// <param name="targetType">The interface or class described by the companion.</param>
    /// <exception cref="ArgumentNullException"><paramref name="targetType"/> is null.</exception>
    public ContractForAttribute(Type targetType)
    {
        TargetType = targetType ?? throw new ArgumentNullException(nameof(targetType));
    }

    /// <summary>Gets the type described by the companion.</summary>
    /// <value>The target type.</value>
    public Type TargetType { get; }
}

namespace SharpProof.Attributes;

/// <summary>Declares an effect summary for a member boundary; summaries are partial and nondeterministic by default.</summary>
[AttributeUsage(
    SharpProofAttributeTargets.Contract,
    AllowMultiple = true,
    Inherited = false)]
public sealed class EffectContractAttribute : Attribute
{
    /// <summary>Creates an effect summary.</summary>
    /// <param name="effects">The declared effect flags.</param>
    public EffectContractAttribute(SharpProofEffect effects)
    {
        Effects = effects;
    }

    /// <summary>Gets the declared effect flags.</summary>
    /// <value>The declared effects.</value>
    public SharpProofEffect Effects { get; }

    /// <summary>Gets or sets the ambient capabilities used by the member.</summary>
    /// <value>The used capabilities; the default is <c>None</c>.</value>
    public SharpProofCapability Capabilities
    {
        get; set;
    }

    /// <summary>Gets or sets the exception types that may escape.</summary>
    /// <value>The escaping exception types; the default is an empty array.</value>
    public Type[] ThrownExceptions { get; set; } = [];

    /// <summary>Gets or sets whether the member is deterministic under its declared inputs and ambient reads.</summary>
    /// <value>True when the summary declares deterministic behavior; the default is false.</value>
    public bool IsDeterministic
    {
        get; set;
    }

    /// <summary>Gets or sets whether the declaration is a complete effect summary.</summary>
    /// <value>True when omitted effects are asserted absent; the default is false.</value>
    public bool Complete
    {
        get; set;
    }

    /// <summary>Gets or sets whether the reviewed boundary is certified to have no preconditions.</summary>
    /// <value>True only when callers may use the summary without a separate precondition certificate; the default is false.</value>
    public bool PreconditionFree
    {
        get; set;
    }
}

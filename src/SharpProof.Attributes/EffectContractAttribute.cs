namespace SharpProof.Attributes;

/// <summary>Declares an effect summary for a member boundary; summaries are partial and nondeterministic by default.</summary>
/// <remarks>
/// Combine known <see cref="SharpProofEffect"/> flags with bitwise OR. Each flag is independent; declaring throws does not
/// imply allocation or native interop. A partial declaration does not certify omitted effects are absent.
/// A reviewed external boundary requires an admitted complete summary and explicit trust; these are recorded assumptions,
/// not a proof of the external implementation. Metadata use additionally needs <see cref="PreconditionFree"/> certification.
/// </remarks>
[AttributeUsage(
    SharpProofAttributeTargets.Contract,
    AllowMultiple = true,
    Inherited = false)]
public sealed class EffectContractAttribute : Attribute
{
    /// <summary>Creates an effect summary.</summary>
    /// <param name="effects">The declared effect flags.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="effects"/> contains an unknown flag bit.</exception>
    public EffectContractAttribute(SharpProofEffect effects)
    {
        const SharpProofEffect known = SharpProofEffect.ReadsReceiverState |
            SharpProofEffect.ReadsArgumentState | SharpProofEffect.ReadsCapturedState |
            SharpProofEffect.ReadsStaticState | SharpProofEffect.ReadsAmbientState |
            SharpProofEffect.WritesReceiverState | SharpProofEffect.WritesArgumentState |
            SharpProofEffect.WritesCapturedState | SharpProofEffect.WritesStaticState |
            SharpProofEffect.WritesAmbientState | SharpProofEffect.Allocates |
            SharpProofEffect.Throws | SharpProofEffect.Synchronizes |
            SharpProofEffect.UsesNondeterminism | SharpProofEffect.UsesNativeCode |
            SharpProofEffect.UsesReflection;
        if ((effects & ~known) != SharpProofEffect.None)
        {
            throw new ArgumentOutOfRangeException(nameof(effects));
        }

        Effects = effects;
    }

    /// <summary>Gets the declared effect flags.</summary>
    /// <value>The declared effects.</value>
    /// <remarks>None means no flags were declared; it establishes no absence guarantee while <see cref="Complete"/> is false.</remarks>
    public SharpProofEffect Effects { get; }

    /// <summary>Gets or sets the ambient capabilities used by the member.</summary>
    /// <value>The used capabilities; the default is <c>None</c>.</value>
    /// <remarks>Use only known flags. These describe boundary effects, not permission granted to the running process.</remarks>
    public SharpProofCapability Capabilities
    {
        get; set;
    }

    /// <summary>Gets or sets the exception types that may escape.</summary>
    /// <value>The escaping exception types; the default is an empty array.</value>
    /// <remarks>Entries must denote admitted exception types; arbitrary types or null elements are invalid contract data.</remarks>
    public Type[] ThrownExceptions { get; set; } = [];

    /// <summary>Gets or sets whether the member is deterministic under its declared inputs and ambient reads.</summary>
    /// <value>True when the summary declares deterministic behavior; the default is false.</value>
    /// <remarks>This is an explicit stronger boundary assertion. A partial or trusted summary does not imply determinism.</remarks>
    public bool IsDeterministic
    {
        get; set;
    }

    /// <summary>Gets or sets whether the declaration is a complete effect summary.</summary>
    /// <value>True when omitted effects are asserted absent; the default is false.</value>
    /// <remarks>Set this only when review covers every tracked effect. It does not supply a return-value relation or verify the implementation.</remarks>
    public bool Complete
    {
        get; set;
    }

    /// <summary>Gets or sets whether the reviewed boundary is certified to have no preconditions.</summary>
    /// <value>True only when callers may use the summary without a separate precondition certificate; the default is false.</value>
    /// <remarks>
    /// For metadata boundaries, true explicitly certifies that no source-only precondition envelope was lost during emit.
    /// Do not set it merely to make an unsupported call pass; review the boundary's actual input requirements first.
    /// </remarks>
    public bool PreconditionFree
    {
        get; set;
    }
}

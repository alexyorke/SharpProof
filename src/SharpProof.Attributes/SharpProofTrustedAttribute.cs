namespace SharpProof.Attributes;

/// <summary>Marks an explicitly reviewed trust boundary that still requires a complete contract.</summary>
/// <remarks>
/// This records an assumption; it does not verify the implementation or supply missing effect facts.
/// A reviewed external boundary also needs a complete admitted <see cref="EffectContractAttribute"/>.
/// Give an auditable reason identifying what was reviewed and its evidence. The default strict assumption policy rejects
/// declared trusted boundaries even when a particular claim does not mark the declaration as used.
/// </remarks>
/// <example>A useful rationale is <c>"Reviewed native ABI and complete effects against the vendor specification; review tracked in issue 42."</c>.</example>
[AttributeUsage(
    SharpProofAttributeTargets.Declaration,
    Inherited = false)]
public sealed class SharpProofTrustedAttribute : Attribute
{
    /// <summary>Creates a documented trust declaration.</summary>
    /// <param name="reason">The nonempty trust rationale.</param>
    /// <exception cref="ArgumentException"><paramref name="reason"/> is null, empty or whitespace.</exception>
    public SharpProofTrustedAttribute(string reason)
    {
        Reason = SharpProofAttributeValidation.RequireReason(
            reason,
            "A trust reason is required.");
    }

    /// <summary>Gets the trust rationale.</summary>
    /// <value>The trust rationale.</value>
    /// <remarks>The text documents the review; a nonempty string alone is not evidence that the declared boundary is sound.</remarks>
    public string Reason
    {
        get;
    }
}

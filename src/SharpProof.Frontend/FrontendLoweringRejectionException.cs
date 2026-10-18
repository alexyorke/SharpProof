namespace SharpProof.Frontend;

// Only bounded frontend refusals use this control flow. Callers retain the
// operation identity and choose their existing expression or CFG rejection.
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1032", Justification = "Internal bounded lowering control flow.")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1064", Justification = "Internal bounded lowering control flow never escapes the lowering session.")]
internal class FrontendLoweringRejectionException(FrontendAbstention reason) : Exception
{
    internal FrontendAbstention Reason { get; } = reason;
}

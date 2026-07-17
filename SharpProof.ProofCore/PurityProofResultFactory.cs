using SharpProof.ProofCore.Purity;

namespace SharpProof.ProofCore.Smt;

internal static class PurityProofResultFactory
{
    internal static PurityProofResult Unknown(string reason)
    {
        return new PurityProofResult(
            PurityProofOutcome.Unknown,
            new ProofCheckInfo(false, Feasibility.Unknown),
            new ProofCheckInfo(false, Feasibility.Unknown),
            reason);
    }
}

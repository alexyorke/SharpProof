using SharpProof.Ir;
using FiniteDomainPreparedFormula = SharpProof.Fuzz.FiniteDomainSmtDifferentialOracle.FiniteDomainPreparedFormula;

namespace SharpProof.Fuzz;

internal static class FiniteDomainCampaignAdmission
{
    internal static FiniteDomainPreparedFormula RequireDefinedFormula(
        IrFactory factory, IrTerm formula, CancellationToken cancellationToken)
    {
        if (FiniteDomainSmtDifferentialOracle.TryPrepareForCampaign(
                factory,
                formula,
                cancellationToken,
                out var prepared))
        {
            return prepared!;
        }

        throw new InvalidOperationException(
            "The generated total finite-domain formula could not be prepared.");
    }
}

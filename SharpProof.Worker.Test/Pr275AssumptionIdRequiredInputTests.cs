using NUnit.Framework;
using SharpProof.Ir;
using SharpProof.Smt;
using SharpProof.Verify;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class Pr275AssumptionIdRequiredInputTests
{
    [Test]
    public async Task NativeEmptyProofCoreRequiresAnAssumptionIdMap()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var query = new VerificationQuery(factory, [],
            new Goal(factory, factory.Boolean(true), ProofDiagnosticKind.Postcondition,
                new SourceLocationId(0)));
        using var session = new CallableSolverSession(factory, new IrSmtBackendOptions());
        var outcome = await new ProofKernel(session).VerifyAsync(query, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(outcome, Is.TypeOf<ProvenOutcome>());
        Assert.That(session.ConsumedResourceCount, Is.GreaterThan(0));

        var core = ((ProvenOutcome)outcome).Core;
        Assert.That(core.IsDefault, Is.False);
        Assert.That(core, Is.Empty);
        var assumptionIds = new Dictionary<ProofJustification, string>();
        Assert.That(CallableProofCore.AssumptionIds(core, assumptionIds), Is.Empty);

        var error = Assert.Throws<ArgumentNullException>((Action)(() =>
            CallableProofCore.AssumptionIds(core, null!)));
        Assert.That(error!.ParamName, Is.EqualTo("assumptionIds"));
    }
}

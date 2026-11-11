using NUnit.Framework;
using SharpProof.Ir;
using SharpProof.Smt;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
internal sealed class NativeBackendSemanticsAdmissionTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task NativeLaneChecksQueries(bool predicate)
    {
        // A backend admission test, independent of compiler preparation and
        // source claim publication.
        var factory = new IrFactory();
        var site = factory.CreateOperation("native-semantics-admission");
        var query = new VerificationQuery(factory, [], new Goal(factory,
            factory.Boolean(predicate), ProofDiagnosticKind.Precondition, new SourceLocationId(site.Value)));
        using var backend = new NativeCallableBackend(new IrSmtBackendOptions(WorkerBudgets.DefaultQueryRlimit));
        var result = await backend.CheckAsync(query, TestContext.CurrentContext.CancellationToken);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(
                predicate ? BackendCheckStatus.Unsatisfiable : BackendCheckStatus.Satisfiable));
            Assert.That(result.FailureReason, Is.EqualTo(BackendFailureReason.None));
            Assert.That(backend.ConsumedResourceCount, Is.GreaterThan(0));
        }
    }
}

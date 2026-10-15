using NUnit.Framework;
using SharpProof.Ir;
using SharpProof.Smt;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class NativeBackendSemanticsAdmissionTests
{
    [TestCase(IrExecutionSemantics.Legacy, false)]
    [TestCase(IrExecutionSemantics.Legacy, true)]
    [TestCase(IrExecutionSemantics.Total, false)]
    [TestCase(IrExecutionSemantics.Total, true)]
    public async Task NativeLaneAdmitsTotalQueriesAndRejectsLegacyQueriesBeforeSpending(
        IrExecutionSemantics semantics, bool predicate)
    {
        // Both factories and queries are legal maintained API inputs. The
        // native Worker lane supports Total; this is a backend admission test,
        // independent of compiler preparation and source claim publication.
        var factory = new IrFactory(semantics);
        var site = factory.CreateOperation("native-semantics-admission");
        var query = new VerificationQuery(factory, [], new Goal(factory,
            factory.Boolean(predicate), ProofDiagnosticKind.Precondition, new SourceLocationId(site.Value)));
        using var backend = new NativeCallableBackend(new IrSmtBackendOptions(WorkerBudgets.DefaultQueryRlimit));
        var result = await backend.CheckAsync(query, CancellationToken.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(semantics == IrExecutionSemantics.Legacy
                ? BackendCheckStatus.Unknown
                : predicate ? BackendCheckStatus.Unsatisfiable : BackendCheckStatus.Satisfiable));
            Assert.That(result.FailureReason, Is.EqualTo(semantics == IrExecutionSemantics.Legacy
                ? BackendFailureReason.UnsupportedEncoding : BackendFailureReason.None));
            if (semantics == IrExecutionSemantics.Legacy)
            {
                Assert.That(result.Model, Is.Null);
                Assert.That(result.UnsatCore, Is.Empty);
                Assert.That(backend.ConsumedResourceCount, Is.Zero);
            }
            else
            { Assert.That(backend.ConsumedResourceCount, Is.GreaterThan(0)); }
        }
    }
}

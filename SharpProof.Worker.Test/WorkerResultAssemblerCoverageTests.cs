using System.IO;
using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class WorkerResultAssemblerCoverageTests
{
    [Test]
    public void ApplyRequestContextRejectsResponseWithoutSummary()
    {
        var response = new WorkerVerifyResponse { Summary = null! };

        Assert.That(
            (Action)(() => WorkerResultAssembler.ApplyRequestContext(
                response,
                "request-hash",
                new WorkerBudgets(),
                new WorkerVersionSummary(),
                WorkerCacheStatus.Disabled,
                0)),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void ProtocolErrorWithNullCodeDoesNotProjectRunState()
    {
        var projected = WorkerResultAssembler.TryProjectRunState(
            [], [],
            [new WorkerProtocolError { Code = null!, Message = "missing code" }],
            out var status,
            out var failure);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(projected, Is.False);
            Assert.That(status, Is.EqualTo(WorkerRunStatus.Unspecified));
            Assert.That(failure, Is.EqualTo(WorkerRunFailureReason.Unspecified));
        }
    }
}

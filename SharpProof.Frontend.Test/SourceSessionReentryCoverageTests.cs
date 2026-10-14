using System.Numerics;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class SourceSessionReentryCoverageTests
{
    [Test]
    public void DuplicateActiveTargetAbstainsWithoutRemovingTheOwningSessionFrame()
    {
        using var subject = TypedProgramSubject.Create("int Target(int value) => value + 1;");
        Assert.That(subject.Invoke([17]), Is.EqualTo(18));
        var session = new TotalSourceCallSession(subject.Compilation, null, null, default);
        Assert.That(session.Enter(subject.Context.Target), Is.True);
        try
        {
            var lowering = new RoslynTotalProgramLowerer(subject.Context, default, session).Lower(subject.Graph);
            Assert.That(lowering.IsExact, Is.False);
            Assert.That(lowering.Classification.Abstention,
                Is.EqualTo(FrontendAbstention.UnsupportedControlFlow));
            Assert.That(lowering.ConstructionLimitExceeded, Is.False);
            Assert.That(session.IsActive(subject.Context.Target), Is.True);
            Assert.That(session.Enter(subject.Context.Target), Is.False);
        }
        finally
        {
            session.Leave(subject.Context.Target);
        }
        Assert.That(session.IsActive(subject.Context.Target), Is.False);
        var control = new RoslynTotalProgramLowerer(subject.Context, default, session).Lower(subject.Graph);
        Assert.That(control.IsExact, Is.True);
        Assert.That(session.IsActive(subject.Context.Target), Is.False);
        Assert.That(session.ConstructionLimitExceeded, Is.False);
        var execution = subject.Execute(control, [17]);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(execution.ConsumedApproximation, Is.False);
        Assert.That(execution.ReturnValue!.IntegerNumericValue, Is.EqualTo(new BigInteger(18)));
    }
}

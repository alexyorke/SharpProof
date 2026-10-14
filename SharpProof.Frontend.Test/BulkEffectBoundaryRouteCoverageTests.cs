using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class BulkEffectBoundaryRouteCoverageTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void DiscardedFieldIncrementRetainsRejectedReceiverConversion(bool missing)
    {
        using var subject = TypedProgramSubject.Create(
            "int Target(object value) { try { ((Box)value).Value++; return 17; } catch (System.NullReferenceException) { return 19; } }",
            "public sealed class Box { public int Value; }");
        if (missing)
        { Assert.That(subject.Invoke([null!]), Is.EqualTo(19)); }
        else
        { Assert.That(subject.Invoke([new object()]), Is.InstanceOf<InvalidCastException>()); }
        var lowering = new RoslynProgramLowerer(subject.Factory).LowerCandidate(subject.Graph, subject.Context,
            static _ => true, null, default, approximateElementReads: true);
        Assert.That(lowering.IsExact, Is.False);
        Assert.That(lowering.Classification.Abstention, Is.EqualTo(FrontendAbstention.ConversionMayChangeValue));
        Assert.That(lowering.ConstructionLimitExceeded, Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SourceAndDispatchedSettersRetainRejectedAssignedUnboxing(bool dispatched)
    {
        var declaration = "public class Box { public " + (dispatched ? "virtual " : "") + "int Value { set { } } }";
        using var subject = TypedProgramSubject.Create(
            "int Target(object value) { var box = new Box(); box.Value = (int)value; return 17; }",
            declaration);
        Assert.That(subject.Invoke([23]), Is.EqualTo(17));
        Assert.That(subject.Invoke(["word"]), Is.InstanceOf<InvalidCastException>());
        var lowering = subject.LowerSourceCalls(false, opaqueCalls: true);
        Assert.That(lowering.IsExact, Is.False);
        Assert.That(lowering.Classification.Abstention, Is.EqualTo(FrontendAbstention.ConversionMayChangeValue));
        Assert.That(lowering.ConstructionLimitExceeded, Is.False);
        using var supported = TypedProgramSubject.Create(
            "int Target(int value) { var box = new Box(); box.Value = value; return 17; }",
            declaration);
        Assert.That(supported.Invoke([23]), Is.EqualTo(17));
        Assert.That(supported.LowerSourceCalls(false, opaqueCalls: true).IsExact, Is.True);
    }

    [Test]
    public void InlinedInstanceCallRetainsRejectedReceiverDowncast()
    {
        using var subject = TypedProgramSubject.Create(
            "int Target(object value) => ((Box)value).Read();",
            "public sealed class Box { public int Read() => 17; }");
        Assert.That(subject.Invoke([new object()]), Is.InstanceOf<InvalidCastException>());
        Assert.That(subject.Invoke([null!]), Is.InstanceOf<NullReferenceException>());
        var lowering = subject.LowerSourceCalls();
        Assert.That(lowering.IsExact, Is.False);
        Assert.That(lowering.Classification.Abstention, Is.EqualTo(FrontendAbstention.ConversionMayChangeValue));
        Assert.That(lowering.ConstructionLimitExceeded, Is.False);
        using var supported = TypedProgramSubject.Create(
            "int Target() { var box = new Box(); return box.Read(); }",
            "public sealed class Box { public int Read() => 17; }");
        Assert.That(supported.Invoke([]), Is.EqualTo(17));
        var exact = supported.LowerSourceCalls();
        Assert.That(exact.IsExact, Is.True);
        var replay = supported.Execute(exact, []);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(17)));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ActualSourceCallHonorsTheShadowPreservationDecision(bool preserve)
    {
        using var subject = TypedProgramSubject.Create("int Target(int value) => Helper(value); static int Helper(int value) => value + 1;");
        Assert.That(subject.Invoke([16]), Is.EqualTo(17));
        var shadow = new RoslynProgramLowerer(subject.Factory).LowerShadowSourceBody(subject.Graph, subject.Context,
            _ => preserve, default);
        Assert.That(shadow.IsShadowCallSkeleton, Is.True);
        Assert.That(shadow.Classification.IsExact, Is.EqualTo(preserve));
        Assert.That(shadow.PreservedSourceCalls.Count, Is.EqualTo(preserve ? 1 : 0));
        Assert.That(shadow.ConstructionLimitExceeded, Is.False);
        var lowering = subject.LowerSourceCalls();
        Assert.That(lowering.IsExact, Is.True);
        var replay = subject.Execute(lowering, [16]);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(17)));
    }
}

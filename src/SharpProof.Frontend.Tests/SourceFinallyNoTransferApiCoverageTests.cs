using Microsoft.CodeAnalysis.FlowAnalysis;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class SourceFinallyNoTransferApiCoverageTests
{
    [Test]
    public void ActualUnconditionalLoopRetainsItsFinallyRegionAndExactCompilerLowering()
    {
        // This successfully emitted source has no terminating CLR path; inspect only its compiler API.
        using var subject = TypedProgramSubject.Create(
            "void Target() { try { for (;;) { } } finally { throw new System.Exception(); } }");
        Assert.That(subject.Graph.Blocks.Last().Kind, Is.EqualTo(BasicBlockKind.Exit));
        Assert.That(subject.Graph.Blocks.Last().IsReachable, Is.False);
        Assert.That(subject.Graph.Blocks.Any(static block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.IsReachable &&
            block.ConditionKind == ControlFlowConditionKind.None &&
            ReferenceEquals(block.FallThroughSuccessor?.Destination, block))), Is.True);
        var pending = new Stack<ControlFlowRegion>();
        pending.Push(subject.Graph.Root);
        var hasFinally = false;
        while (pending.Count != 0)
        {
            var region = pending.Pop();
            hasFinally |= region.Kind == ControlFlowRegionKind.Finally;
            foreach (var child in region.NestedRegions)
            {
                pending.Push(child);
            }
        }
        Assert.That(hasFinally, Is.True);
        var lowered = subject.Lower();
        Assert.That(lowered.IsExact, Is.True);
        Assert.That(lowered.ConstructionLimitExceeded, Is.False);

        using var control = TypedProgramSubject.Create(
            "int Target() { try { try { return 17; } finally { throw new System.Exception(); } } catch (System.Exception) { return 23; } }");
        Assert.That(control.Invoke([]), Is.EqualTo(23));
        var program = control.Lower();
        Assert.That(program.IsExact, Is.True);
        Assert.That(program.ConstructionLimitExceeded, Is.False);
        var replay = control.Execute(program, []);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(23)));
    }
}

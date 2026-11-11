using System.Numerics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class ConditionalFinallyCompletionLoopTests
{
    [TestCase(0, 3)]
    [TestCase(-1, -2)]
    [TestCase(24, 613)]
    public void RealDoWhileFinallyCompletionPreservesBothLoopEdges(int input, int expected)
    {
        using var subject = TypedProgramSubject.Create("""
            int Target(int value) {
                try { value += 1; }
                finally {
                    do { value = value * 5 - 2; }
                    while (value == 123);
                }
                return value;
            }
            """);
        Assert.That(subject.Context.Target.Parameters.Single().Type.SpecialType, Is.EqualTo(SpecialType.System_Int32));
        Assert.That(subject.Context.HasScalarSignature, Is.True);
        Assert.That(subject.Context.OwnsBody(subject.Graph.OriginalOperation), Is.True);
        Assert.That(subject.Graph.OriginalOperation.SemanticModel!.Compilation, Is.SameAs(subject.Compilation));
        var loop = subject.Graph.OriginalOperation.Descendants().OfType<IWhileLoopOperation>().Single();
        Assert.That(loop.ConditionIsTop, Is.False);
        var completion = subject.Graph.Blocks.Single(block => block.IsReachable &&
            block.ConditionKind != ControlFlowConditionKind.None &&
            block.FallThroughSuccessor?.Semantics == ControlFlowBranchSemantics.StructuredExceptionHandling);
        Assert.That(completion.EnclosingRegion.Kind, Is.EqualTo(ControlFlowRegionKind.Finally));
        var fall = completion.FallThroughSuccessor!;
        Assert.That(fall.Source, Is.SameAs(completion));
        Assert.That(fall.Destination, Is.Null);
        Assert.That(fall.FinallyRegions, Is.Empty);
        var back = completion.ConditionalSuccessor!;
        Assert.That(back.Source, Is.SameAs(completion));
        Assert.That(back.Semantics, Is.EqualTo(ControlFlowBranchSemantics.Regular));
        Assert.That(back.Destination, Is.Not.Null);
        var destination = back.Destination!;
        Assert.That(destination.Ordinal, Is.LessThanOrEqualTo(completion.Ordinal));
        Assert.That(subject.Graph.Blocks[destination.Ordinal], Is.SameAs(destination));
        Assert.That(destination.EnclosingRegion, Is.SameAs(completion.EnclosingRegion));
        Assert.That(destination.IsReachable, Is.True);
        Assert.That(back.FinallyRegions, Is.Empty);
        Assert.That(subject.Invoke([input]), Is.EqualTo(expected));
        var lowered = subject.Lower();
        Assert.That(lowered.IsExact, Is.True);
        Assert.That(lowered.Classification.Abstention, Is.EqualTo(FrontendAbstention.None));
        Assert.That(lowered.ConstructionLimitExceeded, Is.False);
        var replay = subject.Execute(lowered, [input]);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new BigInteger(expected)));
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(replay.ApproximationVariables, Is.Empty);
    }
}

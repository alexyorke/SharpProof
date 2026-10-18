using System.Numerics;
using Microsoft.CodeAnalysis.FlowAnalysis;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class PlainRegularCfgEdgeApiCoverageTests
{
    [TestCase(false, 17)]
    [TestCase(true, 18)]
    public void ActualOwnedBranchesKeepPlainRegularAdmission(bool withFinally, int expected)
    {
        var source = withFinally
            ? "int Target() { int value = 1; try { value = 17; goto Done; } finally { value++; } Done: return value; }"
            : "int Target() => 17;";
        using var subject = TypedProgramSubject.Create(source);
        Assert.That(subject.Invoke([]), Is.EqualTo(expected));
        Assert.That(subject.Graph.OriginalOperation.SemanticModel!.Compilation, Is.SameAs(subject.Compilation));
        var edges = subject.Graph.Blocks.Select(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.FallThroughSuccessor))
            .Where(branch => branch != null).Select(branch => branch!).ToArray();
        var plain = edges.First(branch => branch is null ? throw new System.ArgumentNullException(nameof(branch)) : (branch.Semantics == ControlFlowBranchSemantics.Regular &&
            branch.FinallyRegions.IsEmpty && branch.Destination != null));
        Assert.That(subject.Graph.Blocks[plain.Source.Ordinal], Is.SameAs(plain.Source));
        Assert.That(subject.Graph.Blocks[plain.Destination!.Ordinal], Is.SameAs(plain.Destination));
        Assert.That(RoslynCfgReachability.TryGetPlainRegularDestination(plain, out var destination), Is.True);
        Assert.That(destination, Is.SameAs(plain.Destination));
        var returned = edges.Single(branch => branch is null ? throw new System.ArgumentNullException(nameof(branch)) : (branch.Semantics == ControlFlowBranchSemantics.Return));
        Assert.That(subject.Graph.Blocks[returned.Source.Ordinal], Is.SameAs(returned.Source));
        Assert.That(RoslynCfgReachability.TryGetPlainRegularDestination(returned, out destination), Is.False);
        Assert.That(destination, Is.Null);
        if (withFinally)
        {
            var unwind = edges.Single(branch => branch is null ? throw new System.ArgumentNullException(nameof(branch)) : (branch.Semantics == ControlFlowBranchSemantics.Regular &&
                !branch.FinallyRegions.IsEmpty && branch.Destination != null));
            Assert.That(subject.Graph.Blocks[unwind.Source.Ordinal], Is.SameAs(unwind.Source));
            Assert.That(subject.Graph.Blocks[unwind.Destination!.Ordinal], Is.SameAs(unwind.Destination));
            Assert.That(RoslynCfgReachability.TryGetPlainRegularDestination(unwind, out destination), Is.False);
            Assert.That(destination, Is.Null);
        }
        var exit = subject.Graph.Blocks.Single(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Kind == BasicBlockKind.Exit));
        Assert.That(exit.FallThroughSuccessor, Is.Null);
        Assert.That(RoslynCfgReachability.TryGetPlainRegularDestination(exit.FallThroughSuccessor, out destination), Is.False);
        Assert.That(destination, Is.Null);
        var lowered = subject.LowerSourceCalls();
        Assert.That(lowered.IsExact, Is.True, lowered.Classification.Abstention.ToString());
        Assert.That(lowered.ConstructionLimitExceeded, Is.False);
        var replay = subject.Execute(lowered, []);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new BigInteger(expected)));
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(replay.ApproximationVariables, Is.Empty);
    }
}

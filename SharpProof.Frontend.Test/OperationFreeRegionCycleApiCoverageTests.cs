using System.Numerics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class OperationFreeRegionCycleApiCoverageTests
{
    [Test]
    public void OperationFreeCycleWithRegionLocalIsConservativelyRejected()
    {
        using var subject = TypedProgramSubject.Create("""
            int Target(int x) {
                try {
                    goto Loop;
                    int unused = 0;
                Loop:
                    goto Loop;
                }
                finally { x++; }
            }
            """);
        var regions = new Stack<ControlFlowRegion>();
        regions.Push(subject.Graph.Root);
        var allRegions = new List<ControlFlowRegion>();
        while (regions.TryPop(out var region))
        {
            allRegions.Add(region);
            foreach (var child in region.NestedRegions)
            {
                regions.Push(child);
            }
        }
        Assert.That(allRegions.Any(region => region.Kind == ControlFlowRegionKind.TryAndFinally), Is.True);
        var local = allRegions.SelectMany(region => region.Locals).Single(local => local.Name == "unused");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(local.Type.SpecialType, Is.EqualTo(SpecialType.System_Int32));
            Assert.That(local.RefKind, Is.EqualTo(RefKind.None));
            Assert.That(local.IsConst, Is.False);
            Assert.That(subject.Graph.OriginalOperation.SemanticModel!.Compilation, Is.SameAs(subject.Compilation));
        }
        var block = subject.Graph.Blocks[0];
        var visited = new HashSet<BasicBlock>();
        while (visited.Add(block))
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(block.Operations, Is.Empty);
                Assert.That(block.BranchValue, Is.Null);
                Assert.That(block.ConditionKind, Is.EqualTo(ControlFlowConditionKind.None));
                Assert.That(block.FallThroughSuccessor!.Semantics, Is.EqualTo(ControlFlowBranchSemantics.Regular));
                Assert.That(block.FallThroughSuccessor.FinallyRegions, Is.Empty);
                Assert.That(block.FallThroughSuccessor.Destination, Is.Not.Null);
            }
            block = block.FallThroughSuccessor!.Destination!;
        }
        Assert.That(block.Ordinal, Is.GreaterThan(0));
        var lowering = subject.Lower();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(lowering.IsExact, Is.False);
            Assert.That(lowering.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedControlFlow));
            Assert.That(lowering.ConstructionLimitExceeded, Is.False);
        }
        // The infinite source is compiled and inspected only, never executed.
        using var control = TypedProgramSubject.Create("""
            int Target(int x) {
                try { int unused = 0; return x + unused; }
                finally { x++; }
            }
            """);
        Assert.That(control.Invoke([17]), Is.EqualTo(17));
        var exact = control.Lower();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exact.IsExact, Is.True);
            Assert.That(exact.ConstructionLimitExceeded, Is.False);
            Assert.That(exact.Classification.Abstention, Is.EqualTo(FrontendAbstention.None));
        }
        var replay = control.Execute(exact, [17]);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
            Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new BigInteger(17)));
            Assert.That(replay.ConsumedApproximation, Is.False);
            Assert.That(replay.ApproximationVariables, Is.Empty);
        }
    }
}

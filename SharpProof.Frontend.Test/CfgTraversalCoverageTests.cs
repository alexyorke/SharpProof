using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class CfgTraversalCoverageTests
{
    [Test]
    public void ReachabilityExcludesDeadStatementsAndPreservesBlockOrder()
    {
        using var subject = TypedProgramSubject.Create(
            "int Target(int x) { if (false) x = 99; return x; }");
        var reachable = RoslynCfgReachability.ReachableBlocks(subject.Graph, TestContext.CurrentContext.CancellationToken).ToArray();
        Assert.That(reachable.Select(block => block.Ordinal),
            Is.EqualTo(subject.Graph.Blocks.Where(block => block.IsReachable)
                .Select(block => block.Ordinal)));
        Assert.That(subject.Graph.Blocks.Any(block => !block.IsReachable), Is.True);
        Assert.That(reachable.SelectMany(block => block.Operations)
            .Any(operation => operation.Syntax.ToString().Contains("99", StringComparison.Ordinal)), Is.False);
    }

    [TestCase("int Target(int x) { try { return 10 / x; } catch (System.Exception) { return 7; } }")]
    [TestCase("int Target(int x) { try { return 10 / x; } finally { x++; } }")]
    [TestCase("int Target(int x) { try { return 10 / x; } catch (System.Exception) when (x == 0) { return 7; } }")]
    [TestCase("int Target(int x) { try { try { return 10 / x; } catch (System.DivideByZeroException) { return 3; } } catch (System.Exception) { return 7; } }")]
    public void ExceptionalTraversalVisitsLexicalHandlersOnce(string source)
    {
        using var subject = TypedProgramSubject.Create(source);
        var block = FaultingBlock(subject.Graph);
        var successors = RoslynCfgReachability.ExceptionalSuccessors(subject.Graph, block, TestContext.CurrentContext.CancellationToken).ToArray();
        Assert.That(successors, Is.Not.Empty);
        Assert.That(successors.Select(candidate => candidate.Ordinal), Is.Unique);
        Assert.That(successors.All(candidate => candidate.EnclosingRegion.Kind is
            ControlFlowRegionKind.Catch or ControlFlowRegionKind.Filter or
            ControlFlowRegionKind.Finally), Is.True);
        Assert.That(RoslynCfgReachability.ExceptionalSuccessors(subject.Graph,
            subject.Graph.Blocks[0], TestContext.CurrentContext.CancellationToken), Is.Empty);
    }

    [Test]
    public void TraversalsObserveCancellationBeforeYielding()
    {
        using var subject = TypedProgramSubject.Create(
            "int Target(int x) { try { return 10 / x; } catch (System.Exception) { return 7; } }");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var block = FaultingBlock(subject.Graph);
        Assert.Throws<OperationCanceledException>((Action)(() =>
            _ = RoslynCfgReachability.ReachableBlocks(subject.Graph, cancellation.Token).ToArray()));
        Assert.Throws<OperationCanceledException>((Action)(() =>
            _ = RoslynCfgReachability.ExceptionalSuccessors(subject.Graph, block, cancellation.Token).ToArray()));
    }
    private static BasicBlock FaultingBlock(ControlFlowGraph graph)
    {
        return graph.Blocks.Single(block => block.Operations
            .Any(operation => operation.DescendantsAndSelf().OfType<IBinaryOperation>()
                .Any(binary => binary.OperatorKind == BinaryOperatorKind.Divide)) ||
            block.BranchValue?.DescendantsAndSelf().OfType<IBinaryOperation>()
                .Any(binary => binary.OperatorKind == BinaryOperatorKind.Divide) == true);
    }
}

using System.Collections.Immutable;
using NUnit.Framework;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class Pr275CfgRequiredInputTests
{
    [Test]
    public void ReachabilityRequiresTheGraphWhenEnumerationStarts()
    {
        using var subject = TypedProgramSubject.Create("int Target(int x) => x + 1;");
        Assert.That(subject.Invoke([6]), Is.EqualTo(7));
        Assert.That(RoslynCfgReachability.ReachableBlocks(subject.Graph, TestContext.CurrentContext.CancellationToken).ToImmutableArray(),
            Is.EqualTo(subject.Graph.Blocks.Where(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.IsReachable)).ToImmutableArray()));
        var missing = RoslynCfgReachability.ReachableBlocks(null!, TestContext.CurrentContext.CancellationToken);
        var exception = Assert.Throws<ArgumentNullException>((Action)(() => _ = missing.ToImmutableArray()));
        Assert.That(exception!.ParamName, Is.EqualTo("graph"));
    }

    [Test]
    public void ExceptionalTraversalRequiresTheGraphWhenEnumerationStarts()
    {
        using var subject = TypedProgramSubject.Create("int Target(int x) => x + 1;");
        Assert.That(subject.Invoke([6]), Is.EqualTo(7));
        var block = subject.Graph.Blocks[0];
        Assert.That(RoslynCfgReachability.ExceptionalSuccessors(subject.Graph, block, TestContext.CurrentContext.CancellationToken), Is.Empty);
        var missing = RoslynCfgReachability.ExceptionalSuccessors(null!, block, TestContext.CurrentContext.CancellationToken);
        var exception = Assert.Throws<ArgumentNullException>((Action)(() => _ = missing.ToImmutableArray()));
        Assert.That(exception!.ParamName, Is.EqualTo("graph"));
    }

    [Test]
    public void ExceptionalTraversalRequiresTheBlockWhenEnumerationStarts()
    {
        using var subject = TypedProgramSubject.Create("int Target(int x) => x + 1;");
        Assert.That(subject.Invoke([6]), Is.EqualTo(7));
        Assert.That(RoslynCfgReachability.ExceptionalSuccessors(subject.Graph, subject.Graph.Blocks[0], TestContext.CurrentContext.CancellationToken), Is.Empty);
        var missing = RoslynCfgReachability.ExceptionalSuccessors(subject.Graph, null!, TestContext.CurrentContext.CancellationToken);
        var exception = Assert.Throws<ArgumentNullException>((Action)(() => _ = missing.ToImmutableArray()));
        Assert.That(exception!.ParamName, Is.EqualTo("block"));
    }
}

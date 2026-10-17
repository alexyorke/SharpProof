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
        Assert.That(RoslynCfgReachability.ReachableBlocks(subject.Graph, CancellationToken.None).ToArray(),
            Is.EqualTo(subject.Graph.Blocks.Where(block => block.IsReachable).ToArray()));
        var missing = RoslynCfgReachability.ReachableBlocks(null!, CancellationToken.None);
        var exception = Assert.Throws<ArgumentNullException>((Action)(() => _ = missing.ToArray()));
        Assert.That(exception!.ParamName, Is.EqualTo("graph"));
    }

    [Test]
    public void ExceptionalTraversalRequiresTheGraphWhenEnumerationStarts()
    {
        using var subject = TypedProgramSubject.Create("int Target(int x) => x + 1;");
        Assert.That(subject.Invoke([6]), Is.EqualTo(7));
        var block = subject.Graph.Blocks[0];
        Assert.That(RoslynCfgReachability.ExceptionalSuccessors(subject.Graph, block, CancellationToken.None), Is.Empty);
        var missing = RoslynCfgReachability.ExceptionalSuccessors(null!, block, CancellationToken.None);
        var exception = Assert.Throws<ArgumentNullException>((Action)(() => _ = missing.ToArray()));
        Assert.That(exception!.ParamName, Is.EqualTo("graph"));
    }

    [Test]
    public void ExceptionalTraversalRequiresTheBlockWhenEnumerationStarts()
    {
        using var subject = TypedProgramSubject.Create("int Target(int x) => x + 1;");
        Assert.That(subject.Invoke([6]), Is.EqualTo(7));
        Assert.That(RoslynCfgReachability.ExceptionalSuccessors(subject.Graph, subject.Graph.Blocks[0], CancellationToken.None), Is.Empty);
        var missing = RoslynCfgReachability.ExceptionalSuccessors(subject.Graph, null!, CancellationToken.None);
        var exception = Assert.Throws<ArgumentNullException>((Action)(() => _ = missing.ToArray()));
        Assert.That(exception!.ParamName, Is.EqualTo("block"));
    }
}

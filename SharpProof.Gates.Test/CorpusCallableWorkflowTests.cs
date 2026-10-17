using NUnit.Framework;
using SharpProof.Gates.Corpus;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class CorpusCallableWorkflowTests
{
    [TestCase("public sealed class C { }",
        "Corpus case ordinary-cardinality produced 0 public-method semantic outcomes; exactly one is required.")]
    [TestCase("""
        using SharpProof.Attributes;
        public static class C {
            [DoesNotThrow] public static int First() => 1;
            [DoesNotThrow] public static int Second() => 2;
        }
        """, "Corpus case ordinary-cardinality has 2 public verified callables.")]
    public void CorpusCaseRequiresExactlyOneOrdinaryPublicCallable(string source, string expected)
    {
        System.ArgumentNullException.ThrowIfNull(expected);
        System.ArgumentNullException.ThrowIfNull(source);
        var item = new CorpusCase("ordinary-cardinality", "ordinary-cardinality",
            CorpusVariant.Baseline, "effects", CorpusVerdict.Proven, CorpusSupport.Supported, source);

        var error = Assert.ThrowsAsync<InvalidOperationException>((Func<Task>)(async () =>
            await CorpusGate.ObserveCaseAsync(item, TestContext.CurrentContext.CancellationToken)));

        Assert.That(error!.Message, Is.EqualTo(expected));
    }
}

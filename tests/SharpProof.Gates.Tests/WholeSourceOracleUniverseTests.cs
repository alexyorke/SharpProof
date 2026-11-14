using NUnit.Framework;
using SharpProof.Gates.Corpus;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class WholeSourceOracleUniverseTests
{
    [Test]
    public void WholeSourceCoversEverySelectedMethodOfThatSourceOnly()
    {
        var document = OpenSourceCorpusCatalog.Load(RepositoryLayout.FindRoot());
        var selected = document.Methods.Where(static method => method.SourceId == "nodatime")
            .Select(static method => method.DeclarationSha256).ToHashSet(StringComparer.Ordinal);

        var whole = NativeEffectOracleGate.ExpandToWholeSource(document, "nodatime", TestContext.CurrentContext.CancellationToken);

        Assert.That(whole.Methods.Select(static method => method.SourceId), Is.All.EqualTo("nodatime"));
        Assert.That(whole.Files.Select(static file => file.SourceId), Is.All.EqualTo("nodatime"));
        Assert.That(whole.Methods.Select(static method => method.Id), Is.Unique);
        Assert.That(whole.Methods.Select(static method => method.DeclarationSha256), Is.Unique);
        Assert.That(whole.Methods.Length, Is.GreaterThan(selected.Count));
        Assert.That(selected.IsSubsetOf(whole.Methods.Select(static method => method.DeclarationSha256)), Is.True);
    }

    [Test]
    public void UnknownSourceIsRejected()
    {
        var document = OpenSourceCorpusCatalog.Load(RepositoryLayout.FindRoot());

        Assert.Throws<ArgumentException>((Action)(() =>
            NativeEffectOracleGate.ExpandToWholeSource(document, "missing", TestContext.CurrentContext.CancellationToken)));
    }
}

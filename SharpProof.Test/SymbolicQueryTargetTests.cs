using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Symbolic;

namespace SharpProof.Test;

[TestFixture]
public sealed class SharpProofTargetTests
{
    [Test]
    public void QueryOptions_TrimsImpliedConditions()
    {
        var options = new SymbolicQueryOptions(impliedConditions: new[] { " value > 0 ", "\t", "\nother < 10\r\n" });

        Assert.That(options.ImpliedConditions, Is.EqualTo(new[] { "value > 0", "other < 10" }));
    }

    [Test]
    public void QueryOptions_NullReferenceEntry_Throws()
    {
        var exception =
            Assert.Throws<ArgumentException>(() => new SymbolicQueryOptions(new MetadataReference[] { null! }));

        Assert.That(exception!.ParamName, Is.EqualTo("references"));
    }

    [Test]
    public void SourceCompilation_NullReferenceEntry_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() => SymbolicSourceCompilation.Create(
            "public sealed class Sample { }",
            "Sample.cs",
            "Sample.cs",
            "Sample",
            new MetadataReference[] { null! },
            CancellationToken.None));

        Assert.That(exception!.ParamName, Is.EqualTo("references"));
    }

}

using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Symbolic;

namespace SharpProof.Test;

[TestFixture]
public sealed class SharpProofTargetTests {
    [Test]
    public void SourceCompilation_NullReferenceEntry_Throws() {
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

using NUnit.Framework;
using SharpProof.TestSupport;

namespace SharpProof.Meta.Analyzers.Test;

[TestFixture]
public sealed class DiagnosticDescriptorCatalogTests
{
    [Test]
    public void RuntimeDescriptorsMatchTheAuthoritativeCatalog()
    {
        DiagnosticDescriptorCatalogAssertions.AssertOutput(
            "metaAnalyzer",
            typeof(SharpProofSoundnessAnalyzer).Assembly);
    }
}

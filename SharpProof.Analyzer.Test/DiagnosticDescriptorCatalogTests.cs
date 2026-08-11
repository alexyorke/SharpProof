using NUnit.Framework;
using SharpProof.TestSupport;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class DiagnosticDescriptorCatalogTests
{
    [Test]
    public void RuntimeDescriptorsMatchTheAuthoritativeCatalog()
    {
        DiagnosticDescriptorCatalogAssertions.AssertOutput(
            "analyzer",
            typeof(SharpProofAnalyzerEngine).Assembly);
    }
}

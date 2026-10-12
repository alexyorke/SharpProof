using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class AnalyzerCoreActivationCoverageTests
{
    [Test]
    public void DefaultEngineRejectsMissingAnalysisContext()
    {
        var engine = new SharpProofAnalyzerEngine();
        Assert.Throws<ArgumentNullException>((Action)(() => engine.RegisterActions(null!)));
    }
}

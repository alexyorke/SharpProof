using NUnit.Framework;
using SharpProof.Contracts;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class AnalyzerCoreCatalogCoverageTests
{
    [Test]
    public void InvalidOldSignatureNamesTheRequiredValueArgument()
    {
        var description = AnalyzerDiagnosticCatalog.DescribeIntrinsicViolation(
            ContractBindingFailure.InvalidIntrinsicSignature, isOld: true);
        Assert.That(description.Argument, Is.EqualTo("<signature>"));
        Assert.That(description.Reason, Does.Contain("exactly one value argument"));
    }

    [Test]
    public void ProducerRejectsAProvenOutcomeWithAnUnsupportedContractReason()
    {
        Assert.Throws<InvalidOperationException>((Action)(() => EffectEvaluationProducerTupleCatalog.Require(
            EffectEvaluationOutcome.Proven, EffectEvaluationReason.UnsupportedContract,
            EffectEvaluationCertainty.CompleteMayEffectSummary)));
    }
}

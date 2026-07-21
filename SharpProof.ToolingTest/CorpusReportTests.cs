using SharpProof.Tools.CorpusReport;
using NUnit.Framework;

namespace SharpProof.Test;

public sealed class CorpusReportTests {
    [Test]
    public void AggregatesEffectVerdictsCapabilitiesAndUnknownBoundaries() {
        const string sarif = """
            {
              "runs": [{ "results": [{
                "ruleId": "SP0002",
                "message": { "text": "contract not verified" },
                "properties": {
                  "sharpproof.effects.category": "unresolved_call",
                  "sharpproof.effects.flags": "Unknown, Calls",
                  "sharpproof.effects.capabilities": "Native",
                  "sharpproof.explain.proof_status": "unknown",
                  "sharpproof.explain.unknown_reason": "metadata_unavailable",
                  "sharpproof.baseline.symbol": "External.Api()"
                }
              }] }]
            }
            """;

        var report = Create(sarif);

        Assert.Multiple(() => {
            Assert.That(report.SchemaVersion, Is.EqualTo("2.0"));
            Assert.That(report.EnforcePureFailureCount, Is.EqualTo(1));
            Assert.That(report.EffectCategories["unresolved_call"], Is.EqualTo(1));
            Assert.That(report.EffectFlags.Keys, Does.Contain("Unknown"));
            Assert.That(report.CapabilityFlags["Native"], Is.EqualTo(1));
            Assert.That(report.DerivedVerdicts["unknown"], Is.EqualTo(1));
            Assert.That(report.UnknownBoundaries.Single().Value, Is.EqualTo("External.Api()"));
        });
    }

    [Test]
    public void AggregatesStructuredExceptionEvidenceWithoutPurityCatalogs() {
        const string sarif = """
            {
              "runs": [{ "results": [{
                "ruleId": "SP0010",
                "properties": {
                  "sharpproof.exceptions.types": "System.InvalidOperationException",
                  "sharpproof.exceptions.categories": "explicit_throw",
                  "sharpproof.exceptions.sources": "Test.Throw()"
                }
              }] }]
            }
            """;

        var report = Create(sarif);

        Assert.That(report.ExceptionDiagnosticCount, Is.EqualTo(1));
        Assert.That(report.ExceptionSources.Single(), Is.EqualTo(new RankedItem("Test.Throw()", 1)));
        Assert.That(report.Diagnostics.Single().ExceptionTypes,
            Is.EqualTo("System.InvalidOperationException"));
    }

    private static CorpusReportSummary Create(string sarif) {
        var path = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid() + ".sarif");
        try {
            File.WriteAllText(path, sarif);
            return SarifCorpusReport.CreateFromSarifFiles([new SarifCorpusInput("input", path)]);
        }
        finally {
            File.Delete(path);
        }
    }
}

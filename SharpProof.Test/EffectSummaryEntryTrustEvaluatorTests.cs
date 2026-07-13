using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Test;

[TestFixture]
public sealed class EffectSummaryEntryTrustEvaluatorTests
{
    [Test]
    public void AssemblyMismatch_IsRejectedAndReportedWithTheSharedReason()
    {
        var reporter = new EffectSummaryCompatibilityReporter();

        var trusted = EffectSummaryEntryTrustEvaluator.IsTrusted(
            new SummaryAssemblyIdentity("Expected.Assembly", "hash", "mvid"),
            null,
            new SummaryMethodIdentity("0x06000001", "body"),
            new ActualAssemblyIdentity("Actual.Assembly", "hash", "mvid"),
            new ActualMethodIdentity("0x06000001", "body"),
            false,
            reporter,
            "additional.SharpProof.EffectSummary.json",
            "Test.Method()");

        var issue = reporter.GetIssues().Single();
        Assert.Multiple(() =>
        {
            Assert.That(trusted, Is.False);
            Assert.That(issue.Path, Is.EqualTo("additional.SharpProof.EffectSummary.json"));
            Assert.That(issue.ReasonCode, Is.EqualTo("effect_summary_assembly_name_mismatch"));
        });
    }

    [Test]
    public void BuiltInMetadataTokenFallback_IsTheOnlyMethodHashException()
    {
        var assemblyIdentity = new SummaryAssemblyIdentity("Test.Assembly", "hash", "mvid");
        var actualAssemblyIdentity = new ActualAssemblyIdentity("Test.Assembly", "hash", "mvid");
        var methodIdentity = new SummaryMethodIdentity("0x06000001", "old-body");
        var actualMethodIdentity = new ActualMethodIdentity("0x06000001", "new-body");
        var reporter = new EffectSummaryCompatibilityReporter();

        var builtInTrusted = EffectSummaryEntryTrustEvaluator.IsTrusted(
            assemblyIdentity,
            null,
            methodIdentity,
            actualAssemblyIdentity,
            actualMethodIdentity,
            true,
            reporter,
            "built-in.json",
            "Test.Method()");
        var additionalTrusted = EffectSummaryEntryTrustEvaluator.IsTrusted(
            assemblyIdentity,
            null,
            methodIdentity,
            actualAssemblyIdentity,
            actualMethodIdentity,
            false,
            reporter,
            "additional.json",
            "Test.Method()");

        Assert.Multiple(() =>
        {
            Assert.That(builtInTrusted, Is.True);
            Assert.That(additionalTrusted, Is.False);
            Assert.That(reporter.GetIssues().Single().ReasonCode,
                Is.EqualTo("effect_summary_method_body_hash_mismatch"));
        });
    }

    [Test]
    public void TrustMetadata_PreservesSourcePriorityPolicyAndCompatibilityReporting()
    {
        var assemblyIdentity = new SummaryAssemblyIdentity("Test.Assembly", "hash", "mvid");
        var actualAssemblyIdentity = new ActualAssemblyIdentity("Test.Assembly", "hash", "mvid");
        var methodIdentity = new SummaryMethodIdentity("0x06000001", "old-body");
        var actualMethodIdentity = new ActualMethodIdentity("0x06000001", "new-body");
        var reporter = new EffectSummaryCompatibilityReporter();
        var builtIn = new EffectSummaryEntryTrustMetadata(
            assemblyIdentity,
            methodIdentity,
            null,
            sourcePriority: 0,
            builtInSourcePriority: 0,
            additionalSourcePriority: 1,
            sourcePath: "built-in.json",
            compatibilityReporter: reporter);
        var additional = new EffectSummaryEntryTrustMetadata(
            assemblyIdentity,
            methodIdentity,
            null,
            sourcePriority: 1,
            builtInSourcePriority: 0,
            additionalSourcePriority: 1,
            sourcePath: "additional.json",
            compatibilityReporter: reporter);

        Assert.Multiple(() =>
        {
            Assert.That(builtIn.IsTrustedFor(actualAssemblyIdentity, actualMethodIdentity, "Test.Method()"), Is.True);
            Assert.That(additional.IsTrustedFor(actualAssemblyIdentity, actualMethodIdentity, "Test.Method()"),
                Is.False);
            Assert.That(reporter.GetIssues().Single().Path, Is.EqualTo("additional.json"));
            Assert.That(reporter.GetIssues().Single().ReasonCode,
                Is.EqualTo("effect_summary_method_body_hash_mismatch"));
        });
    }
}

using NUnit.Framework;
using SharpProof.Analyzer.Configuration;
using SharpProof.Testing;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class AnalyzerConfigurationUnitTests
{
    [Test]
    public void TreeConfigurationDistinguishesRedundantAndLocalValues()
    {
        var tree = new DictionaryAnalyzerConfigOptions(
            ("sharpproof_profile", "strict"));
        var sameGlobal = new DictionaryAnalyzerConfigOptions(
            ("sharpproof_profile", "STRICT"));
        var differentGlobal = new DictionaryAnalyzerConfigOptions(
            ("sharpproof_profile", "advisory"));

        var redundant =
            AnalyzerConfiguration.GetInvalidTreeConfigurationValues(
                tree,
                sameGlobal);
        var local =
            AnalyzerConfiguration.GetInvalidTreeConfigurationValues(
                tree,
                differentGlobal);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(redundant, Is.Empty);
            Assert.That(local, Has.Length.EqualTo(1));
            Assert.That(
                local[0].Key,
                Is.EqualTo("sharpproof_profile"));
            Assert.That(local[0].Value, Is.EqualTo("strict"));
            Assert.That(
                local[0].Reason,
                Does.Contain("compilation-global"));
        }
    }

    [Test]
    public void ConflictingGlobalAliasesFailClosed()
    {
        var configuration = AnalyzerConfiguration.FromOptions(
            new DictionaryAnalyzerConfigOptionsProvider(
                new DictionaryAnalyzerConfigOptions(
                    ("sharpproof_profile", "strict"),
                    ("build_property.SharpProofProfile", "advisory"))));

        Assert.That(configuration.Profile, Is.EqualTo(SharpProofProfile.Off));
        Assert.That(configuration.InvalidConfigurationValues, Has.Length.EqualTo(1));
        Assert.That(
            configuration.InvalidConfigurationValues[0].Reason,
            Does.Contain("aliases disagree"));
    }

    [Test]
    public void InvalidCurrentOptionDoesNotHideRetiredMode()
    {
        var configuration = AnalyzerConfiguration.FromOptions(
            new DictionaryAnalyzerConfigOptionsProvider(
                new DictionaryAnalyzerConfigOptions(
                    ("sharpproof_profile", "everything"),
                    ("sharpproof_mode", "effects"))));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(configuration.Profile, Is.EqualTo(SharpProofProfile.Off));
            Assert.That(
                configuration.InvalidConfigurationValues.Select(
                    static invalid => invalid.Key),
                Is.EqualTo(["sharpproof_profile", "sharpproof_mode"]));
            Assert.That(
                configuration.InvalidConfigurationValues.Select(
                    static invalid => invalid.Value),
                Is.EqualTo(["everything", "effects"]));
        }
    }

    [Test]
    public void ConflictingTreeAliasesCannotHideBehindMatchingGlobalValue()
    {
        var tree = new DictionaryAnalyzerConfigOptions(
            ("sharpproof_profile", "advisory"),
            ("build_property.SharpProofProfile", "strict"));
        var global = new DictionaryAnalyzerConfigOptions(
            ("sharpproof_profile", "advisory"));

        var invalid = AnalyzerConfiguration.GetInvalidTreeConfigurationValues(
            tree,
            global);

        Assert.That(invalid, Has.Length.EqualTo(1));
        Assert.That(invalid[0].Reason, Does.Contain("aliases disagree"));
    }

    [Test]
    public void SemanticOutcomeOrderingIsExhaustiveAndValidated()
    {
        Assert.That(
            AnalyzerSemanticOutcomes.Combine(
                AnalyzerSemanticOutcome.Suppressed,
                AnalyzerSemanticOutcome.Proven),
            Is.EqualTo(AnalyzerSemanticOutcome.Suppressed));
        Assert.Throws<ArgumentOutOfRangeException>(
            (Action)(() =>
                _ = AnalyzerSemanticOutcomes.Combine(
                    (AnalyzerSemanticOutcome)int.MaxValue,
                    AnalyzerSemanticOutcome.Proven)));
    }

}

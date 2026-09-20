using NUnit.Framework;
using SharpProof.Analyzer.Configuration;
using SharpProof.Testing;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class AnalyzerConfigurationPackageDefaultTests
{
    [Test]
    public void AnalyzerConfigurationOverridesPackageProfileAndFeatureDefaults()
    {
        var configuration = AnalyzerConfiguration.FromOptions(
            new DictionaryAnalyzerConfigOptionsProvider(
                new DictionaryAnalyzerConfigOptions(
                    ("build_property.SharpProofProfile", "advisory"),
                    ("build_property.SharpProofFeatures", "all"),
                    ("sharpproof_profile", "strict"),
                    ("sharpproof_features", "contracts"))));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(configuration.Profile, Is.EqualTo(SharpProofProfile.Strict));
            Assert.That(configuration.Features, Is.EqualTo(SharpProofFeatures.Contracts));
            Assert.That(configuration.InvalidConfigurationValues, Is.Empty);
        }
    }

    [Test]
    public void TreeAnalyzerConfigurationOverridesPackageProfileDefault()
    {
        var global = new DictionaryAnalyzerConfigOptions(
            ("build_property.SharpProofProfile", "advisory"),
            ("build_property.SharpProofFeatures", "all"));
        var tree = new DictionaryAnalyzerConfigOptions(
            ("sharpproof_profile", "strict"));

        var invalid = AnalyzerConfiguration.GetInvalidTreeConfigurationValues(
            tree,
            global);

        Assert.That(invalid, Is.Empty);
    }

    [Test]
    public void ExplicitMsBuildProfileStillConflictsWithAnalyzerConfiguration()
    {
        var configuration = AnalyzerConfiguration.FromOptions(
            new DictionaryAnalyzerConfigOptionsProvider(
                new DictionaryAnalyzerConfigOptions(
                    ("build_property.SharpProofProfile", "off"),
                    ("build_property.SharpProofFeatures", "all"),
                    ("sharpproof_profile", "strict"))));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(configuration.Profile, Is.EqualTo(SharpProofProfile.Off));
            Assert.That(configuration.InvalidConfigurationValues, Has.Length.EqualTo(1));
            Assert.That(
                configuration.InvalidConfigurationValues[0].Reason,
                Does.Contain("aliases disagree"));
        }
    }
}

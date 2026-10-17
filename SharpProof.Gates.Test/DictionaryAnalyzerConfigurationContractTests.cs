using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using NUnit.Framework;
using SharpProof.Analyzer.Configuration;
using SharpProof.Testing;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class DictionaryAnalyzerConfigurationContractTests
{
    [Test]
    public void TupleOptionsParseMixedCaseKeysAndValuesAndReportMissingKeys()
    {
        var options = new DictionaryAnalyzerConfigOptions(
            ("SHARPPROOF_PROFILE", " StRiCt "),
            ("SharpProof_Features", " EfFeCtS "));
        var provider = new DictionaryAnalyzerConfigOptionsProvider(options);
        var configuration = AnalyzerConfiguration.FromOptions(provider);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(configuration.Profile, Is.EqualTo(SharpProofProfile.Strict));
            Assert.That(configuration.Features, Is.EqualTo(SharpProofFeatures.Effects));
            Assert.That(configuration.InvalidConfigurationValues, Is.Empty);
            Assert.That(options.TryGetValue("sharpproof_profile", out var profile), Is.True);
            Assert.That(profile, Is.EqualTo(" StRiCt "));
            Assert.That(options.TryGetValue("missing_option", out var missing), Is.False);
            Assert.That(missing, Is.Empty);
            Assert.That(provider.GlobalOptions, Is.SameAs(options));
        }
    }

    [Test]
    public void DictionaryProviderKeepsGlobalConfigurationOutOfFileOptions()
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["build_property.SharpProofProfile"] = "strict",
            ["build_property.SharpProofFeatures"] = "contracts"
        };
        var provider = new DictionaryAnalyzerConfigOptionsProvider(values);
        var configuration = AnalyzerConfiguration.FromOptions(provider);
        var tree = CSharpSyntaxTree.ParseText("public class C { }", path: "input.cs");
        var additional = new ConfigurationAdditionalText();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(configuration.Profile, Is.EqualTo(SharpProofProfile.Strict));
            Assert.That(configuration.Features, Is.EqualTo(SharpProofFeatures.Contracts));
            Assert.That(configuration.InvalidConfigurationValues, Is.Empty);
            Assert.That(provider.GlobalOptions.TryGetValue(
                "BUILD_PROPERTY.SHARPPROOFFEATURES", out var features), Is.True);
            Assert.That(features, Is.EqualTo("contracts"));
            Assert.That(provider.GetOptions(tree), Is.SameAs(DictionaryAnalyzerConfigOptions.Empty));
            Assert.That(provider.GetOptions(additional), Is.SameAs(DictionaryAnalyzerConfigOptions.Empty));
            Assert.That(provider.GetOptions(tree).TryGetValue(
                "build_property.SharpProofProfile", out var treeProfile), Is.False);
            Assert.That(treeProfile, Is.Empty);
            Assert.That(provider.GetOptions(additional).TryGetValue(
                "build_property.SharpProofFeatures", out var fileFeatures), Is.False);
            Assert.That(fileFeatures, Is.Empty);
        }
    }

    [TestCase(true)]
    [TestCase(false)]
    public void FileOptionSharingPreservesGlobalIdentityAndConfiguration(bool globalForFiles)
    {
        var options = new DictionaryAnalyzerConfigOptions(
            ("sharpproof_profile", "strict"),
            ("sharpproof_features", "effects"));
        var provider = new DictionaryAnalyzerConfigOptionsProvider(options, globalForFiles);
        var tree = CSharpSyntaxTree.ParseText("public class C { }", path: "input.cs");
        var additional = new ConfigurationAdditionalText();
        var expected = globalForFiles ? options : DictionaryAnalyzerConfigOptions.Empty;
        var treeOptions = provider.GetOptions(tree);
        var additionalOptions = provider.GetOptions(additional);
        var configuration = AnalyzerConfiguration.FromOptions(provider);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(provider.GlobalOptions, Is.SameAs(options));
            Assert.That(treeOptions, Is.SameAs(expected));
            Assert.That(additionalOptions, Is.SameAs(expected));
            Assert.That(treeOptions.TryGetValue("sharpproof_features", out var treeFeatures),
                Is.EqualTo(globalForFiles));
            Assert.That(treeFeatures, Is.EqualTo(globalForFiles ? "effects" : string.Empty));
            Assert.That(additionalOptions.TryGetValue("sharpproof_features", out var fileFeatures),
                Is.EqualTo(globalForFiles));
            Assert.That(fileFeatures, Is.EqualTo(globalForFiles ? "effects" : string.Empty));
            Assert.That(configuration.Profile, Is.EqualTo(SharpProofProfile.Strict));
            Assert.That(configuration.Features, Is.EqualTo(SharpProofFeatures.Effects));
            Assert.That(configuration.InvalidConfigurationValues, Is.Empty);
            Assert.That(additional.Path, Is.EqualTo("configuration.txt"));
            Assert.That(additional.GetText(TestContext.CurrentContext.CancellationToken).ToString(), Is.EqualTo("ordinary additional input"));
        }
    }

    private sealed class ConfigurationAdditionalText : AdditionalText
    {
        private readonly SourceText _text = SourceText.From("ordinary additional input");

        public override string Path => "configuration.txt";

        public override SourceText GetText(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _text;
        }
    }
}

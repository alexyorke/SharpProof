using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;
using SharpProof.Analyzer.Configuration;
using SharpProof.Analyzer.Engine;

namespace SharpProof.Test;

[TestFixture]
public class ImpurityCatalogAccessorTests
{
    [Test]
    public void SetterOnlyProperty_UsesExistingSetterAccessorForConfiguredPurity()
    {
        var tree = CSharpSyntaxTree.ParseText(@"
public sealed class Target
{
    public int Value { set { } }
}");
        var compilation = CSharpCompilation.Create(
            "CatalogProbe",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var property = compilation.GetTypeByMetadataName("Target")!.GetMembers("Value")
            .OfType<IPropertySymbol>().Single();

        using (UseKnownPureConfiguration("Target.Value.get"))
            Assert.That(ImpurityCatalog.IsKnownPureBCLMember(property, compilation), Is.False);

        using (UseKnownPureConfiguration("Target.Value.set"))
            Assert.That(ImpurityCatalog.IsKnownPureBCLMember(property, compilation), Is.True);
    }

    private static IDisposable UseKnownPureConfiguration(string signature)
    {
        var provider = new TestAnalyzerConfigOptionsProvider(
            ImmutableDictionary<string, string>.Empty.Add("sharpproof_known_pure_methods", signature));
        var configuration = AnalyzerConfiguration.FromOptions(
            new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty, provider));
        return ImpurityCatalog.UseConfiguredOverrides(configuration);
    }

    private sealed class TestAnalyzerConfigOptionsProvider : AnalyzerConfigOptionsProvider
    {
        private readonly AnalyzerConfigOptions _options;

        public TestAnalyzerConfigOptionsProvider(ImmutableDictionary<string, string> options)
        {
            _options = new DictionaryAnalyzerConfigOptions(options);
        }

        public override AnalyzerConfigOptions GlobalOptions => _options;
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => _options;
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => _options;
    }

    private sealed class DictionaryAnalyzerConfigOptions : AnalyzerConfigOptions
    {
        private readonly ImmutableDictionary<string, string> _options;

        public DictionaryAnalyzerConfigOptions(ImmutableDictionary<string, string> options)
        {
            _options = options;
        }

        public override bool TryGetValue(string key, out string value)
        {
            if (_options.TryGetValue(key, out var configuredValue))
            {
                value = configuredValue;
                return true;
            }

            value = string.Empty;
            return false;
        }
    }
}

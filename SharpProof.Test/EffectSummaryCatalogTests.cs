using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Test;

[TestFixture]
public sealed class EffectSummaryCatalogTests
{
    [Test]
    public void AdditionalCatalogCreation_DoesNotReplaceOrMutateTheBuiltInCatalog()
    {
        var emptyOptions = GeneratedPurityTestSupport.CreateAnalyzerOptions(ImmutableArray<AdditionalText>.Empty);
        var builtIn = EffectSummaryCatalog.FromOptions(emptyOptions, default);
        var withAdditional = EffectSummaryCatalog.FromOptions(
            GeneratedPurityTestSupport.CreateAnalyzerOptions(ImmutableArray.Create<AdditionalText>(
                new AnalyzerTestHost.InMemoryAdditionalText(
                    "Empty.SharpProof.EffectSummary.json",
                    "{ \"SchemaVersion\": 5 }"))),
            default);
        var builtInAfterAdditional = EffectSummaryCatalog.FromOptions(emptyOptions, default);

        Assert.Multiple(() =>
        {
            Assert.That(withAdditional, Is.Not.SameAs(builtIn));
            Assert.That(builtInAfterAdditional, Is.SameAs(builtIn));
        });
    }
}

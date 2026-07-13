using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Test;

[TestFixture]
public sealed class EffectSummaryCatalogEntryMapTests
{
    [Test]
    public void CloneAddAndFreeze_PreserveGroupsWithoutMutatingTheSource()
    {
        var source = ImmutableDictionary<string, ImmutableArray<(string Symbol, string Value)>>.Empty
            .WithComparers(StringComparer.Ordinal)
            .Add("A", ImmutableArray.Create(("A", "first")));

        var mutable = EffectSummaryCatalogEntryMap.Clone(source);
        EffectSummaryCatalogEntryMap.Add(
            mutable,
            new[] { (Symbol: "A", Value: "second"), (Symbol: "B", Value: "third") },
            static entry => entry.Symbol);
        var frozen = EffectSummaryCatalogEntryMap.Freeze(mutable);

        Assert.Multiple(() =>
        {
            Assert.That(source["A"].Select(static entry => entry.Value), Is.EqualTo(new[] { "first" }));
            Assert.That(frozen["A"].Select(static entry => entry.Value),
                Is.EqualTo(new[] { "first", "second" }));
            Assert.That(frozen["B"].Select(static entry => entry.Value), Is.EqualTo(new[] { "third" }));
            Assert.That(frozen.KeyComparer, Is.SameAs(StringComparer.Ordinal));
        });
    }
}

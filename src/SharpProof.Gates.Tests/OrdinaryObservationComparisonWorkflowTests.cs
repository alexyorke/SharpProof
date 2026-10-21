using System.Collections.Immutable;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Gates.Corpus;
using SharpProof.Worker.Protocol;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class OrdinaryObservationComparisonWorkflowTests
{
    [TestCase(true, true)]
    [TestCase(true, false, TestName = "ActualCacheSecondVersionDifference")]
    [TestCase(false, true)]
    [TestCase(false, false)]
    public async Task ActualVersionObservationsKeepTheCacheComparison(bool firstMatches, bool secondMatches)
    {
        var expected = await ObserveVersionAsync("CacheTarget", allocate: false);
        var first = await ObserveVersionAsync("CacheTarget", allocate: !firstMatches);
        var second = await ObserveVersionAsync("CacheTarget", allocate: !secondMatches);

        var failures = CorpusObservationComparison.CompareCached(expected, first, second);

        Assert.That(failures, Is.EqualTo(firstMatches && secondMatches
            ? ImmutableArray<string>.Empty
            : [$"Cache replay changed {expected.CaseId}."]));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ActualVersionObservationsKeepTheConcurrentComparisonAndOrder(bool changeVersions)
    {
        var expectedAlpha = await ObserveVersionAsync("AlphaTarget", allocate: false);
        var expectedZulu = await ObserveVersionAsync("ZuluTarget", allocate: false);
        var actualZulu = await ObserveVersionAsync("ZuluTarget", allocate: changeVersions);
        var actualAlpha = await ObserveVersionAsync("AlphaTarget", allocate: changeVersions);
        var expected = ImmutableArray.Create(expectedZulu, expectedAlpha).ToImmutableDictionary(
            static observation => observation is null ? throw new System.ArgumentNullException(nameof(observation)) : (observation.CaseId), StringComparer.Ordinal);

        var failures = CorpusObservationComparison.CompareConcurrent(expected, [actualZulu, actualAlpha]);

        Assert.That(failures, Is.EqualTo(changeVersions
            ? ImmutableArray.Create(
                $"Concurrent replay changed {expectedAlpha.CaseId}.",
                $"Concurrent replay changed {expectedZulu.CaseId}.")
            : ImmutableArray<string>.Empty));
    }

    private static async Task<CorpusObservation> ObserveVersionAsync(string className, bool allocate)
    {
        System.ArgumentNullException.ThrowIfNull(className);
        var source = $$"""
            public static class {{className}} {
                [SharpProof.Attributes.ZeroAllocations]
                public static int Target(int value) {
                    {{(allocate ? "_ = new object();" : "")}}
                    return value;
                }
            }
            """;
        var compilation = AnalyzerGateHost.CreateCompilation(source, "OrdinaryObservationVersion");
        Assert.That(compilation.GetDiagnostics().Where(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        var symbol = compilation.GetTypeByMetadataName(className)!.GetMembers("Target").OfType<IMethodSymbol>().Single();
        var identity = symbol.GetDocumentationCommentId()!;
        Assert.That(identity, Is.EqualTo($"M:{className}.Target(System.Int32)"));

        using var image = NativeSourceEmission.EmitImage(compilation, "Observation version did not compile: ", TestContext.CurrentContext.CancellationToken);
        var context = new AssemblyLoadContext("ordinary-observation-version-" + Guid.NewGuid().ToString("N"), isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(image);
            var method = assembly.GetType(className, throwOnError: true)!.GetMethod("Target")!;
            var target = method.CreateDelegate<Func<int, int>>();
            Assert.That(target(37), Is.EqualTo(37));
            Assert.That(AllocationIlOracle.HasPotentialAllocation(method), Is.EqualTo(allocate));
        }
        finally
        {
            context.Unload();
        }

        var verdict = allocate ? CorpusVerdict.Refuted : CorpusVerdict.Proven;
        var item = new CorpusCase(identity, identity, CorpusVariant.Baseline, "effects",
            verdict, CorpusSupport.Supported, source);
        var observation = await CorpusGate.ObserveCaseAsync(item, TestContext.CurrentContext.CancellationToken);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(observation.CaseId, Is.EqualTo(identity));
            Assert.That(observation.Verdict, Is.EqualTo(verdict));
            Assert.That(observation.NativeOutcome, Is.EqualTo(allocate ? WorkerClaimOutcome.Refuted : WorkerClaimOutcome.Proven));
            Assert.That(observation.Diagnostics.IsDefaultOrEmpty, Is.EqualTo(!allocate));
        }
        await TestContext.Out.WriteLineAsync("actual-version: " + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))) +
            " " + observation.ToCanonicalLine() + " native=" + observation.NativeOutcome);
        return observation;
    }
}

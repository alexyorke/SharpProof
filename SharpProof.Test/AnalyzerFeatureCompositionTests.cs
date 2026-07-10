using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Test
{
    [TestFixture]
    [Parallelizable(ParallelScope.Children)]
    public sealed class AnalyzerFeatureCompositionTests
    {
        [Test]
        public void FeatureDependencies_ExpandEnsuresToPurityCore()
        {
            Assert.That(new SharpProofAnalyzer().Features, Is.EqualTo(AnalyzerFeatures.All));
            Assert.That(
                new SharpProofAnalyzer(AnalyzerFeatures.Ensures).Features,
                Is.EqualTo(AnalyzerFeatures.Ensures | AnalyzerFeatures.PurityCore));
        }

        [Test]
        public async Task EnsuresScope_MatchesFullAnalyzerAndExcludesUnrelatedFeatures()
        {
            const string source = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [ZeroAllocations]
    [Ensures(""result > 0"")]
    public int Run()
    {
        _ = new object();
        return 0;
    }
}";
            var references = AnalyzerTestHost.GetMinimalFrameworkReferences();
            var fullDiagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(
                source,
                frameworkReferences: references);
            var ensuresDiagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(
                source,
                frameworkReferences: references,
                analyzerFeatures: AnalyzerFeatures.Ensures);

            Assert.That(
                fullDiagnostics.Select(static diagnostic => diagnostic.Id),
                Does.Contain(SharpProofDiagnostics.AllocationInZeroAllocationMethodId));
            var fullEnsures = AnalyzerTestHost.SingleDiagnostic(
                fullDiagnostics,
                SharpProofDiagnostics.EnsuresNotProvenId);
            var scopedEnsures = AnalyzerTestHost.SingleDiagnostic(
                ensuresDiagnostics,
                SharpProofDiagnostics.EnsuresNotProvenId);

            Assert.That(
                ensuresDiagnostics.Select(static diagnostic => diagnostic.Id),
                Is.EqualTo(new[] { SharpProofDiagnostics.EnsuresNotProvenId }));
            AssertEquivalent(fullEnsures, scopedEnsures);
        }

        private static void AssertEquivalent(Diagnostic expected, Diagnostic actual)
        {
            Assert.Multiple(() =>
            {
                Assert.That(actual.Id, Is.EqualTo(expected.Id));
                Assert.That(actual.Severity, Is.EqualTo(expected.Severity));
                Assert.That(actual.GetMessage(), Is.EqualTo(expected.GetMessage()));
                Assert.That(actual.Location.SourceSpan, Is.EqualTo(expected.Location.SourceSpan));
                Assert.That(
                    actual.AdditionalLocations.Select(static location => location.SourceSpan),
                    Is.EqualTo(expected.AdditionalLocations.Select(static location => location.SourceSpan)));
                Assert.That(actual.Properties, Is.EquivalentTo(expected.Properties));
            });
        }
    }
}

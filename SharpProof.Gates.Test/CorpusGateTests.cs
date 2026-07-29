using System.Text;
using NUnit.Framework;
using SharpProof.Gates.Corpus;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class CorpusGateTests
{
    [Test]
    public void GeneratorHasDocumentedMetamorphicCoverage()
    {
        var cases = CorpusCatalog.CreateCases();
        var synthetic = cases.Where(static item =>
            item.Origin == CorpusOrigin.SyntheticMetamorphic).ToArray();
        var openSource = cases.Where(static item =>
            item.Origin == CorpusOrigin.OpenSource).ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(synthetic, Has.Length.EqualTo(280));
            Assert.That(
                synthetic.Select(static item => item.SeedId).Distinct().Count(),
                Is.EqualTo(28));
            Assert.That(
                synthetic.Select(static item => item.Variant).Distinct(),
                Is.EquivalentTo(Enum.GetValues<CorpusVariant>()));
            Assert.That(
                openSource.Length,
                Is.InRange(
                    OpenSourceCorpusCatalog.MinimumMethodCount,
                    OpenSourceCorpusCatalog.MaximumMethodCount));
            Assert.That(
                openSource.Select(static item => item.ProvenanceId)
                    .Distinct(StringComparer.Ordinal).Count(),
                Is.EqualTo(openSource.Length));
            Assert.That(
                cases.Select(static item => item.Id).Distinct().Count(),
                Is.EqualTo(cases.Length));
        }
    }

    [Test]
    public void OpenSourceManifestHasPinnedLicensedProvenance()
    {
        var root = RepositoryLayout.FindRoot();
        var document = OpenSourceCorpusCatalog.Load(root);
        var selectedFileCount = document.Methods
            .Select(static method => method.Path)
            .Distinct(StringComparer.Ordinal)
            .Count();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(document.SchemaVersion, Is.EqualTo(1));
            Assert.That(document.Sources, Has.Length.EqualTo(1));
            Assert.That(document.Sources[0].Repository, Is.EqualTo(
                "https://github.com/aalhour/C-Sharp-Algorithms"));
            Assert.That(document.Sources[0].Commit, Has.Length.EqualTo(40));
            Assert.That(document.Sources[0].LicenseSpdx, Is.EqualTo("MIT"));
            Assert.That(document.Methods, Has.Length.EqualTo(200));
            Assert.That(
                selectedFileCount,
                Is.GreaterThanOrEqualTo(
                    OpenSourceCorpusCatalog.MinimumSourceFileCount));
            Assert.That(
                document.Methods.Select(static method =>
                    method.DeclarationSha256).Distinct(StringComparer.Ordinal)
                    .Count(),
                Is.EqualTo(document.Methods.Length));
        }
    }

    [Test]
    public async Task AnalyzerMatchesCanonicalCorpusAndReplayModes()
    {
        var root = RepositoryLayout.FindRoot();

        var result = await CorpusGate.RunAsync(root);

        Assert.That(
            result.Failures,
            Is.Empty,
            string.Join(Environment.NewLine, result.Failures));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Passed, Is.True);
            Assert.That(result.CaseCount, Is.EqualTo(480));
            Assert.That(result.BaseCaseCount, Is.EqualTo(228));
            Assert.That(result.OpenSourceMethodCount, Is.EqualTo(200));
            Assert.That(result.OpenSourceFileCount, Is.EqualTo(87));
            Assert.That(result.SyntheticSeedCount, Is.EqualTo(28));
            Assert.That(result.SupportedCaseCount, Is.EqualTo(171));
            Assert.That(
                result.IntentionallyUnsupportedCaseCount,
                Is.EqualTo(309));
            Assert.That(result.SupportedUnknownCount, Is.Zero);
            Assert.That(result.UnknownCount, Is.EqualTo(299));
            Assert.That(result.SilentUnknownCount, Is.EqualTo(10));
            Assert.That(result.TotalUnknownCount, Is.EqualTo(309));
            Assert.That(
                result.UnknownReasons
                    .ToDictionary(
                        static item => item.Reason,
                        static item => item.Count),
                Is.EquivalentTo(
                    new Dictionary<string, int>(StringComparer.Ordinal)
                    {
                        ["SP0002"] = 28,
                        ["SP0016"] = 20,
                        ["SP0045"] = 30,
                        ["SP0045+SP0046"] = 10,
                        ["SP0046"] = 30,
                        ["SP0047"] = 181,
                        ["silent-unclassified"] = 10
                    }));
            Assert.That(
                result.UnknownRate,
                Is.EqualTo(result.UnknownCount / (double)result.CaseCount));
            Assert.That(
                result.SilentUnknownRate,
                Is.EqualTo(
                    result.SilentUnknownCount / (double)result.CaseCount));
            Assert.That(
                result.TotalUnknownRate,
                Is.EqualTo(
                    result.TotalUnknownCount / (double)result.CaseCount));
            Assert.That(result.CacheReplayCount, Is.GreaterThan(0));
            Assert.That(result.ConcurrentReplayCount, Is.GreaterThan(0));
        }
    }

    [Test]
    public void SnapshotCapturesSemanticOutcomeAndCanonicalDiagnostics()
    {
        var root = RepositoryLayout.FindRoot();
        var lines = File.ReadAllLines(
            Path.Combine(
                root,
                "SharpProof.Gates",
                "Corpus",
                "expected.canonical.snapshot"));
        var refuted = lines.Single(static line =>
            line.StartsWith("C02.baseline|", StringComparison.Ordinal));
        var refutedParts = refuted.Split('|');
        var diagnosticParts = refutedParts[3].Split('@');
        var message = Encoding.UTF8.GetString(
            Convert.FromBase64String(diagnosticParts[3]));
        var silentUnknown = lines.Single(static line =>
            line.StartsWith("C06.baseline|", StringComparison.Ordinal))
            .Split('|');
        var openSource = lines.Single(static line =>
            line.StartsWith("OSS0001.baseline|", StringComparison.Ordinal))
            .Split('|');

        using (Assert.EnterMultipleScope())
        {
            Assert.That(refutedParts, Has.Length.EqualTo(4));
            Assert.That(refutedParts[1], Is.EqualTo("Refuted"));
            Assert.That(refutedParts[2], Is.EqualTo("Refuted"));
            Assert.That(diagnosticParts, Has.Length.EqualTo(4));
            Assert.That(diagnosticParts[0], Is.EqualTo("SP0027"));
            Assert.That(diagnosticParts[1], Is.EqualTo("Warning"));
            Assert.That(
                diagnosticParts[2],
                Does.StartWith("input.cs:"));
            Assert.That(
                message,
                Is.EqualTo(
                    "Call to 'Positive' violates precondition 'false'"));
            Assert.That(silentUnknown[1], Is.EqualTo("SilentUnknown"));
            Assert.That(silentUnknown[2], Is.EqualTo("Unknown"));
            Assert.That(silentUnknown[3], Is.Empty);
            Assert.That(openSource, Has.Length.EqualTo(4));
            Assert.That(openSource[1], Is.EqualTo("Unknown"));
            Assert.That(openSource[2], Is.EqualTo("Abstained"));
            Assert.That(openSource[3], Does.StartWith("SP0047@Warning@"));
        }
    }
}

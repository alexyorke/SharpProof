using System.Globalization;
using System.IO;
using System.Text.Json;
using NUnit.Framework;
using SharpProof.Gates.Corpus;
using SharpProof.Gates.Performance;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class ProgramTests
{
    [Test]
    public void SourceLinkMappingsMustCoverEveryPortablePdbDocument()
    {
        const string expected = "https://raw.githubusercontent.com/alexyorke/SharpProof/" +
            "0123456789012345678901234567890123456789/*";

        Assert.DoesNotThrow((Action)(() => SharpProofSymbolPackageValidator.ValidateSourceLinkCoverage(
            "test.pdb",
            ["/_/src/One.cs", "/_/src/Two.cs"],
            [("/_/*", expected)],
            expected)));
        Assert.DoesNotThrow((Action)(() => SharpProofSymbolPackageValidator.ValidateSourceLinkCoverage(
            "test.pdb",
            ["/_/src/One.cs", "/_/src/Two.cs"],
            [("/_/*", expected), ("/_/src/*", expected)],
            expected)));
        Assert.Throws<InvalidDataException>((Action)(() => SharpProofSymbolPackageValidator.ValidateSourceLinkCoverage(
            "test.pdb",
            ["/_/src/One.cs"],
            [("/x/*", expected)],
            expected)));
        Assert.Throws<InvalidDataException>((Action)(() => SharpProofSymbolPackageValidator.ValidateSourceLinkCoverage(
            "test.pdb",
            ["/_/src/One.cs"],
            [("/_/*", expected), ("/unused/*", expected)],
            expected)));
    }

    [Test]
    public async Task AllGateReportsInfrastructureFailureWhenCorpusPhaseCrashes()
    {
        var performanceCalled = false;
        var originalOutput = Console.Out;
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        Console.SetOut(output);
        try
        {
            var exitCode = await Program.RunAllAsync(
                "unused",
                static _ => Task.FromException<CorpusGateResult>(
                    new InvalidOperationException("synthetic corpus failure")),
                _ =>
                {
                    performanceCalled = true;
                    return Task.FromResult(PassingPerformance());
                });

            Assert.That(exitCode, Is.EqualTo(Program.GateInfrastructureFailureExitCode));
            Assert.That(performanceCalled, Is.False);
            using var document = JsonDocument.Parse(output.ToString());
            Assert.That(document.RootElement.GetProperty("corpus").ValueKind,
                Is.EqualTo(JsonValueKind.Null));
            Assert.That(
                document.RootElement.GetProperty("failure").GetProperty("phase")
                    .GetString(),
                Is.EqualTo("corpus"));
        }
        finally
        {
            Console.SetOut(originalOutput);
        }
    }

    [Test]
    public async Task AllGateReturnsPartialCodeAndPreservesCompletedCorpus()
    {
        var originalOutput = Console.Out;
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        Console.SetOut(output);
        try
        {
            var exitCode = await Program.RunAllAsync(
                "unused",
                static _ => Task.FromResult(PassingCorpus()),
                static _ => Task.FromException<PerformanceGateResult>(
                    new InvalidOperationException("synthetic performance failure")));

            Assert.That(exitCode, Is.EqualTo(Program.GatePartialFailureExitCode));
            using var document = JsonDocument.Parse(output.ToString());
            Assert.That(document.RootElement.GetProperty("corpus").GetProperty("Passed")
                .GetBoolean(), Is.True);
            Assert.That(document.RootElement.GetProperty("performance").ValueKind,
                Is.EqualTo(JsonValueKind.Null));
            Assert.That(
                document.RootElement.GetProperty("failure").GetProperty("phase")
                    .GetString(),
                Is.EqualTo("performance"));
        }
        finally
        {
            Console.SetOut(originalOutput);
        }
    }

    private static CorpusGateResult PassingCorpus()
    {
        return new CorpusGateResult(
            Passed: true,
            CaseCount: 1,
            BaseCaseCount: 1,
            OpenSourceMethodCount: 0,
            SupportedOpenSourceMethodCount: 0,
            OpenSourceFileCount: 0,
            SyntheticSeedCount: 1,
            VariantCount: 1,
            DiagnosticCount: 0,
            SupportedCaseCount: 1,
            IntentionallyUnsupportedCaseCount: 0,
            SupportedUnknownCount: 0,
            UnknownCount: 0,
            SilentUnknownCount: 0,
            TotalUnknownCount: 0,
            UnknownRate: 0,
            SilentUnknownRate: 0,
            TotalUnknownRate: 0,
            CacheReplayCount: 0,
            ConcurrentReplayCount: 0,
            UnknownReasons: [],
            AllowedDegradations: [],
            Failures: []);
    }

    private static PerformanceGateResult PassingPerformance()
    {
        return new PerformanceGateResult(
            Passed: true,
            Warmups: 0,
            Samples: 0,
            PackageBuildEstimatorVersion: "test",
            PackageBuildSdk: new PackageBuildSdkIdentity("test", "test", "test", new string('a', 64)),
            PackageBuildSamples: [],
            OrderBalancedRatios: [],
            UnannotatedAdvisoryAnalyzerDriverRunCount: 0,
            UnannotatedAdvisoryAnalysisSessionCreateCount: 0,
            UnannotatedAdvisoryApiSpecCreateCount: 0,
            UnannotatedAdvisoryEffectAnalysisCreateCount: 0,
            OrderBalancedMedianRatio: 0,
            RawMedianRatio: 0,
            BaselineFirstMedianRatio: 0,
            UnannotatedAdvisoryFirstMedianRatio: 0,
            RawP95Ratio: 0,
            BaselineRetainedBytes: 0,
            UnannotatedAdvisoryRetainedBytes: 0,
            RetainedMemoryRatio: 0,
            RetainedMemoryIncreaseMiB: 0,
            EnabledRetainedCompilationCount: 0,
            EnabledRetainedMemoryIncreaseMiB: 0,
            IdeEdits: 0,
            IdeEditP95Milliseconds: 0,
            IdeEditMaximumMilliseconds: 0,
            IdeDiagnosticReplayFailureCount: 0,
            CancellationP95Milliseconds: 0,
            ForcedTerminationMilliseconds: 0,
            Failures: []);
    }
}

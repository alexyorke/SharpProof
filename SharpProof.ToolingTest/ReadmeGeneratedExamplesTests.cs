using NUnit.Framework;

namespace SharpProof.Test
{
    [TestFixture]
    public sealed class ReadmeGeneratedExamplesTests
    {
        [ReadmeExample("purity-clock")]
        [Test]
        public async Task PurityAnalyzerExample_MatchesSnapshot()
        {
            const string exampleId = "purity-clock";
            var source = ReadmeExampleFixture.LoadExampleSource(exampleId);
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(
                source,
                sourcePath: ReadmeExampleFixture.GetRelativeExamplePath(exampleId, "input.cs"));

            var formatted = ReadmeExampleFixture.FormatDiagnostics(diagnostics);

            ReadmeExampleFixture.AssertOutputMatchesSnapshot(exampleId, formatted);
        }

        [ReadmeExample("zero-allocations")]
        [Test]
        public async Task ZeroAllocationsAnalyzerExample_MatchesSnapshot()
        {
            const string exampleId = "zero-allocations";
            var source = ReadmeExampleFixture.LoadExampleSource(exampleId);
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(
                source,
                sourcePath: ReadmeExampleFixture.GetRelativeExamplePath(exampleId, "input.cs"));

            var formatted = ReadmeExampleFixture.FormatDiagnostics(diagnostics);

            ReadmeExampleFixture.AssertOutputMatchesSnapshot(exampleId, formatted);
        }

        [ReadmeExample("capabilities-console")]
        [Test]
        public async Task CapabilitiesCliExample_MatchesSnapshot()
        {
            const string exampleId = "capabilities-console";
            var result = await SymbolicCliTestHost.RunOutOfProcessAsync(
                "--file",
                ReadmeExampleFixture.GetRelativeExamplePath(exampleId, "input.cs"),
                "--line",
                "7",
                "--capabilities");

            Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
            ReadmeExampleFixture.AssertOutputMatchesSnapshot(exampleId, result.StandardOutput);
        }

        [ReadmeExample("invariants-positive")]
        [Test]
        public async Task InvariantsCliExample_MatchesSnapshot()
        {
            const string exampleId = "invariants-positive";
            var result = await SymbolicCliTestHost.RunOutOfProcessAsync(
                "--file",
                ReadmeExampleFixture.GetRelativeExamplePath(exampleId, "input.cs"),
                "--line",
                "7",
                "--column",
                "13",
                "--check-reachability",
                "--implies",
                "value > 0");

            Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
            ReadmeExampleFixture.AssertOutputMatchesSnapshot(exampleId, result.StandardOutput);
        }

        [ReadmeExample("runtime-hazard-divide-by-zero")]
        [Test]
        public async Task RuntimeHazardCliExample_MatchesSnapshot()
        {
            const string exampleId = "runtime-hazard-divide-by-zero";
            var result = await SymbolicCliTestHost.RunOutOfProcessAsync(
                "--file",
                ReadmeExampleFixture.GetRelativeExamplePath(exampleId, "input.cs"),
                "--line",
                "7",
                "--runtime-hazards");

            Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
            ReadmeExampleFixture.AssertOutputMatchesSnapshot(exampleId, result.StandardOutput);
        }

        [ReadmeExample("complexity-linear")]
        [Test]
        public async Task ComplexityCliExample_MatchesSnapshot()
        {
            const string exampleId = "complexity-linear";
            var result = await SymbolicCliTestHost.RunOutOfProcessAsync(
                "--file",
                ReadmeExampleFixture.GetRelativeExamplePath(exampleId, "input.cs"),
                "--line",
                "10",
                "--complexity");

            Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
            ReadmeExampleFixture.AssertOutputMatchesSnapshot(exampleId, result.StandardOutput);
        }

        [Test]
        public async Task GeneratedReadme_IsUpToDate()
        {
            var result = await ReadmeExampleFixture.RunReadmeGeneratorAsync(verifyOnly: true);

            Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardOutput + result.StandardError);
        }
    }
}

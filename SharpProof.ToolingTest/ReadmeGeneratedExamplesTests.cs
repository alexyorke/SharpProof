using NUnit.Framework;

namespace SharpProof.Test
{
    [TestFixture]
    public sealed class ReadmeGeneratedExamplesTests
    {
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

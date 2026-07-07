using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace SharpProof.Test
{
    [TestFixture]
    [Parallelizable(ParallelScope.Children)]
    public sealed class ReadmeGeneratedExamplesTests
    {
        private const string BclFallbackFixtureSource = @"
namespace System.Experimental
{
    public static class NumericFacts
    {
        public static int Normalize(int value)
        {
            return value < 0 ? -value : value;
        }
    }
}
";

        [ReadmeExample("purity-clock")]
        [Test]
        public async Task PurityAnalyzerExample_MatchesSnapshot()
        {
            await VerifyAnalyzerExampleAsync("purity-clock");
        }

        [ReadmeExample("sp0003-misplaced-enforce-pure")]
        [Test]
        public async Task Sp0003_MisplacedEnforcePureExample_MatchesSnapshot()
        {
            await VerifyAnalyzerExampleAsync("sp0003-misplaced-enforce-pure");
        }

        [ReadmeExample("sp0004-missing-enforce-pure")]
        [Test]
        public async Task Sp0004_MissingEnforcePureExample_MatchesSnapshot()
        {
            await VerifyAnalyzerExampleAsync("sp0004-missing-enforce-pure");
        }

        [ReadmeExample("sp0005-conflicting-purity-attributes")]
        [Test]
        public async Task Sp0005_ConflictingPurityAttributesExample_MatchesSnapshot()
        {
            await VerifyAnalyzerExampleAsync("sp0005-conflicting-purity-attributes");
        }

        [ReadmeExample("sp0006-allow-sync-without-purity")]
        [Test]
        public async Task Sp0006_AllowSynchronizationWithoutPurityExample_MatchesSnapshot()
        {
            await VerifyAnalyzerExampleAsync("sp0006-allow-sync-without-purity");
        }

        [ReadmeExample("sp0007-misplaced-allow-synchronization")]
        [Test]
        public async Task Sp0007_MisplacedAllowSynchronizationExample_MatchesSnapshot()
        {
            await VerifyAnalyzerExampleAsync("sp0007-misplaced-allow-synchronization");
        }

        [ReadmeExample("sp0008-redundant-allow-synchronization")]
        [Test]
        public async Task Sp0008_RedundantAllowSynchronizationExample_MatchesSnapshot()
        {
            await VerifyAnalyzerExampleAsync("sp0008-redundant-allow-synchronization");
        }

        [ReadmeExample("sp0009-purity-explanation")]
        [Test]
        public async Task Sp0009_PurityExplanationExample_MatchesSnapshot()
        {
            await VerifyAnalyzerExampleAsync(
                "sp0009-purity-explanation",
                ImmutableDictionary<string, string>.Empty.Add("sharpproof_emit_explanations", "true"));
        }

        [ReadmeExample("sp0010-exception-summary")]
        [Test]
        public async Task Sp0010_ExceptionSummaryExample_MatchesSnapshot()
        {
            await VerifyAnalyzerExampleAsync(
                "sp0010-exception-summary",
                ImmutableDictionary<string, string>.Empty.Add("sharpproof_runtime_hazard_mode", "summaries"));
        }

        [ReadmeExample("runtime-hazard-divide-by-zero")]
        [Test]
        public async Task RuntimeHazardCliExample_MatchesSnapshot()
        {
            const string exampleId = "runtime-hazard-divide-by-zero";
            await VerifyCliExampleAsync(
                exampleId,
                "--file",
                ReadmeExampleFixture.GetRelativeExamplePath(exampleId, "input.cs"),
                "--line",
                "7",
                "--runtime-hazards");
        }

        [ReadmeExample("sp0012-bcl-fallback-guess")]
        [Test]
        public async Task Sp0012_BclFallbackGuessExample_MatchesSnapshot()
        {
            using var fixture = CreateMetadataOnlyAssemblyFixture("System.FallbackSdk", BclFallbackFixtureSource);
            await VerifyAnalyzerExampleAsync(
                "sp0012-bcl-fallback-guess",
                ImmutableDictionary<string, string>.Empty.Add("sharpproof_report_bcl_fallback_guesses", "true"),
                additionalMetadataReferences: ImmutableArray.Create(fixture.Reference));
        }

        [ReadmeExample("zero-allocations")]
        [Test]
        public async Task ZeroAllocationsAnalyzerExample_MatchesSnapshot()
        {
            await VerifyAnalyzerExampleAsync("zero-allocations");
        }

        [ReadmeExample("sp0014-misplaced-zero-allocations")]
        [Test]
        public async Task Sp0014_MisplacedZeroAllocationsExample_MatchesSnapshot()
        {
            await VerifyAnalyzerExampleAsync("sp0014-misplaced-zero-allocations");
        }

        [ReadmeExample("sp0015-capability-violation")]
        [Test]
        public async Task Sp0015_CapabilityViolationExample_MatchesSnapshot()
        {
            await VerifyAnalyzerExampleAsync("sp0015-capability-violation");
        }

        [ReadmeExample("sp0016-capability-unknown")]
        [Test]
        public async Task Sp0016_CapabilityUnknownExample_MatchesSnapshot()
        {
            await VerifyAnalyzerExampleAsync("sp0016-capability-unknown");
        }

        [ReadmeExample("sp0017-misplaced-capabilities")]
        [Test]
        public async Task Sp0017_MisplacedAllowedCapabilitiesExample_MatchesSnapshot()
        {
            await VerifyAnalyzerExampleAsync("sp0017-misplaced-capabilities");
        }

        [ReadmeExample("sp0018-ensures-failing-return")]
        [Test]
        public async Task Sp0018_EnsuresNotProvenExample_MatchesSnapshot()
        {
            await VerifyAnalyzerExampleAsync("sp0018-ensures-failing-return");
        }

        [ReadmeExample("sp0019-ensures-unsupported")]
        [Test]
        public async Task Sp0019_EnsuresUnsupportedExample_MatchesSnapshot()
        {
            await VerifyAnalyzerExampleAsync("sp0019-ensures-unsupported");
        }

        [ReadmeExample("sp0020-misplaced-ensures")]
        [Test]
        public async Task Sp0020_MisplacedEnsuresExample_MatchesSnapshot()
        {
            await VerifyAnalyzerExampleAsync("sp0020-misplaced-ensures");
        }

        [ReadmeExample("sp0021-complexity-exceeded")]
        [Test]
        public async Task Sp0021_ComplexityExceededExample_MatchesSnapshot()
        {
            await VerifyAnalyzerExampleAsync("sp0021-complexity-exceeded");
        }

        [ReadmeExample("sp0022-complexity-unknown")]
        [Test]
        public async Task Sp0022_ComplexityUnknownExample_MatchesSnapshot()
        {
            await VerifyAnalyzerExampleAsync("sp0022-complexity-unknown");
        }

        [ReadmeExample("sp0023-misplaced-expected-complexity")]
        [Test]
        public async Task Sp0023_MisplacedExpectedComplexityExample_MatchesSnapshot()
        {
            await VerifyAnalyzerExampleAsync("sp0023-misplaced-expected-complexity");
        }

        [ReadmeExample("capabilities-console")]
        [Test]
        public async Task CapabilitiesCliExample_MatchesSnapshot()
        {
            const string exampleId = "capabilities-console";
            await VerifyCliExampleAsync(
                exampleId,
                "--file",
                ReadmeExampleFixture.GetRelativeExamplePath(exampleId, "input.cs"),
                "--line",
                "7",
                "--capabilities");
        }

        [ReadmeExample("symbolic-unknown-dynamic")]
        [Test]
        public async Task SymbolicUnknownCapabilitiesCliExample_MatchesSnapshot()
        {
            const string exampleId = "symbolic-unknown-dynamic";
            await VerifyCliExampleAsync(
                exampleId,
                "--file",
                ReadmeExampleFixture.GetRelativeExamplePath(exampleId, "input.cs"),
                "--line",
                "5",
                "--capabilities");
        }

        [ReadmeExample("invariants-positive")]
        [Test]
        public async Task InvariantsCliExample_MatchesSnapshot()
        {
            const string exampleId = "invariants-positive";
            await VerifyCliExampleAsync(
                exampleId,
                "--file",
                ReadmeExampleFixture.GetRelativeExamplePath(exampleId, "input.cs"),
                "--line",
                "7",
                "--column",
                "13",
                "--check-reachability",
                "--implies",
                "value > 0");
        }

        [ReadmeExample("complexity-linear")]
        [Test]
        public async Task ComplexityCliExample_MatchesSnapshot()
        {
            const string exampleId = "complexity-linear";
            await VerifyCliExampleAsync(
                exampleId,
                "--file",
                ReadmeExampleFixture.GetRelativeExamplePath(exampleId, "input.cs"),
                "--line",
                "10",
                "--complexity");
        }

        [Test]
        public async Task GeneratedExamplePages_AreUpToDate()
        {
            if (string.Equals(Environment.GetEnvironmentVariable("SHARPPROOF_REGENERATE_EXAMPLE_OUTPUTS"), "1", StringComparison.Ordinal) ||
                string.Equals(Environment.GetEnvironmentVariable("SHARPPROOF_REGENERATE_EXAMPLE_OUTPUTS"), "true", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Ignore("Skipping generated-page verification while regenerating example snapshots.");
            }

            var result = await ReadmeExampleFixture.RunReadmeGeneratorAsync(verifyOnly: true);

            Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardOutput + result.StandardError);
        }

        [Test]
        public void DiagnosticExampleManifest_CoversEveryPublicRule()
        {
            var manifestPath = Path.Combine(
                ReadmeExampleFixture.GetRepositoryRoot(),
                "docs",
                "readme-examples",
                "diagnostic-examples.json");
            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var actualIds = document.RootElement
                .EnumerateArray()
                .Select(example => example.GetProperty("DiagnosticId").GetString())
                .Where(static id => !string.IsNullOrWhiteSpace(id))
                .Cast<string>()
                .ToHashSet(StringComparer.Ordinal);

            var expectedIds = Enumerable.Range(2, 22)
                .Select(index => "SP" + index.ToString("0000"))
                .ToHashSet(StringComparer.Ordinal);

            Assert.That(actualIds, Is.SupersetOf(expectedIds));
        }

        private static async Task VerifyAnalyzerExampleAsync(
            string exampleId,
            ImmutableDictionary<string, string>? globalOptions = null,
            ImmutableArray<MetadataReference>? additionalMetadataReferences = null)
        {
            var source = ReadmeExampleFixture.LoadExampleSource(exampleId);
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(
                source,
                globalOptions,
                allowUnsafe: false,
                additionalFiles: null,
                sourcePath: ReadmeExampleFixture.GetRelativeExamplePath(exampleId, "input.cs"),
                autoEnableEffectSummaryJsonForAdditionalFiles: true,
                frameworkReferences: null,
                additionalMetadataReferences: additionalMetadataReferences,
                compilationName: "ReadmeExample_" + exampleId.Replace('-', '_'));

            var formatted = ReadmeExampleFixture.FormatDiagnostics(diagnostics);
            ReadmeExampleFixture.AssertOutputMatchesSnapshot(exampleId, formatted);
        }

        private static async Task VerifyCliExampleAsync(string exampleId, params string[] arguments)
        {
            var result = await SymbolicCliTestHost.RunAsync(arguments);

            Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
            ReadmeExampleFixture.AssertOutputMatchesSnapshot(exampleId, result.StandardOutput);
        }

        private static MetadataOnlyAssemblyFixture CreateMetadataOnlyAssemblyFixture(
            string assemblyName,
            string source)
        {
            var tempDirectory = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                assemblyName + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);

            var assemblyPath = Path.Combine(tempDirectory, assemblyName + ".dll");
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                assemblyName,
                new[] { syntaxTree },
                AnalyzerTestHost.GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var emitResult = compilation.Emit(assemblyPath);
            Assert.That(
                emitResult.Success,
                Is.True,
                string.Join(Environment.NewLine, emitResult.Diagnostics.Select(diagnostic => diagnostic.ToString())));

            return new MetadataOnlyAssemblyFixture(tempDirectory, assemblyPath);
        }

        private sealed class MetadataOnlyAssemblyFixture : IDisposable
        {
            public MetadataOnlyAssemblyFixture(string directoryPath, string assemblyPath)
            {
                DirectoryPath = directoryPath;
                Reference = MetadataReference.CreateFromFile(assemblyPath);
            }

            public string DirectoryPath { get; }

            public MetadataReference Reference { get; }

            public void Dispose()
            {
                if (Directory.Exists(DirectoryPath))
                {
                    Directory.Delete(DirectoryPath, recursive: true);
                }
            }
        }
    }
}

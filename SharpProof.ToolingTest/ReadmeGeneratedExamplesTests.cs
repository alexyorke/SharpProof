using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Test;

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
            ImmutableArray.Create(fixture.Reference));
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

    [ReadmeExample("sp0024-invalid-contract-argument")]
    [Test]
    public async Task Sp0024_InvalidContractArgumentExample_MatchesSnapshot()
    {
        await VerifyAnalyzerExampleAsync("sp0024-invalid-contract-argument");
    }

    [ReadmeExample("sp0025-invalid-analyzer-configuration")]
    [Test]
    public async Task Sp0025_InvalidAnalyzerConfigurationExample_MatchesSnapshot()
    {
        await VerifyAnalyzerExampleAsync(
            "sp0025-invalid-analyzer-configuration",
            ImmutableDictionary<string, string>.Empty.Add("sharpproof_smt_mode", "turbo"));
    }

    [ReadmeExample("sp0032-invalid-analyzer-input")]
    [Test]
    public async Task Sp0032_InvalidAnalyzerInputExample_MatchesSnapshot()
    {
        await VerifyAnalyzerExampleAsync(
            "sp0032-invalid-analyzer-input",
            additionalFiles: ImmutableArray.Create<AdditionalText>(
                new AnalyzerTestHost.InMemoryAdditionalText(
                    "SharpProof.EffectSummary.json",
                    "{ invalid json")));
    }

    [ReadmeExample("sp0033-unknown-runtime-hazard")]
    [Test]
    public async Task Sp0033_UnknownRuntimeHazardExample_MatchesSnapshot()
    {
        await VerifyAnalyzerExampleAsync(
            "sp0033-unknown-runtime-hazard",
            ImmutableDictionary<string, string>.Empty
                .Add("sharpproof_runtime_hazard_mode", "unknowns")
                .Add("sharpproof_suggest_missing_enforce_pure", "false"));
    }

    [ReadmeExample("sp0034-suggest-zero-allocations")]
    [Test]
    public async Task Sp0034_SuggestZeroAllocationsExample_MatchesSnapshot()
    {
        await VerifyAnalyzerExampleAsync(
            "sp0034-suggest-zero-allocations",
            GetInferredSuggestionOptions("zero-allocations"));
    }

    [ReadmeExample("sp0035-suggest-capabilities")]
    [Test]
    public async Task Sp0035_SuggestCapabilitiesExample_MatchesSnapshot()
    {
        await VerifyAnalyzerExampleAsync(
            "sp0035-suggest-capabilities",
            GetInferredSuggestionOptions("capabilities"));
    }

    [ReadmeExample("sp0036-suggest-complexity")]
    [Test]
    public async Task Sp0036_SuggestComplexityExample_MatchesSnapshot()
    {
        await VerifyAnalyzerExampleAsync(
            "sp0036-suggest-complexity",
            GetInferredSuggestionOptions("complexity"));
    }

    [ReadmeExample("sp0037-suggest-exception-contract")]
    [Test]
    public async Task Sp0037_SuggestExceptionContractExample_MatchesSnapshot()
    {
        await VerifyAnalyzerExampleAsync(
            "sp0037-suggest-exception-contract",
            GetInferredSuggestionOptions("exceptions"));
    }

    [ReadmeExample("sp0038-suggest-ensures")]
    [Test]
    public async Task Sp0038_SuggestEnsuresExample_MatchesSnapshot()
    {
        await VerifyAnalyzerExampleAsync(
            "sp0038-suggest-ensures",
            GetInferredSuggestionOptions("ensures"));
    }

    [ReadmeExample("sp0039-suggest-requires")]
    [Test]
    public async Task Sp0039_SuggestRequiresExample_MatchesSnapshot()
    {
        await VerifyAnalyzerExampleAsync(
            "sp0039-suggest-requires",
            GetInferredSuggestionOptions("requires"));
    }

    [ReadmeExample("sp0040-trusted-boundary-review")]
    [Test]
    public async Task Sp0040_TrustedBoundaryReviewExample_MatchesSnapshot()
    {
        await VerifyAnalyzerExampleAsync(
            "sp0040-trusted-boundary-review",
            ImmutableDictionary<string, string>.Empty
                .Add("sharpproof_suggest_missing_enforce_pure", "false")
                .Add("sharpproof_trusted_boundary_review_mode", "used")
                .Add(
                    "sharpproof_known_pure_methods",
                    "spm1|VHJ1c3RlZEJvdW5kYXJ5|b3JkaW5hcnk=|VmFsdWU=|0|1|bm9uZQ==|" +
                    "bmFtZWQ6U3lzdGVtLkludDMy|bm9uZQ==|bmFtZWQ6U3lzdGVtLkludDMy"));
    }

    [ReadmeExample("common-bug-diagnostics")]
    [Test]
    public async Task CommonBugDiagnosticExamples_MatchSnapshotAndCoverEveryRule()
    {
        var diagnostics = await VerifyAnalyzerExampleAsync(
            "common-bug-diagnostics",
            analyzerFeatures: AnalyzerFeatures.CommonBugs);
        var actualIds = diagnostics.Select(static diagnostic => diagnostic.Id).ToHashSet(StringComparer.Ordinal);
        var expectedIds = Enumerable.Range(48, 29)
            .Select(static number => $"SP{number:0000}")
            .ToArray();

        Assert.That(actualIds, Is.SupersetOf(expectedIds));
    }

    [ReadmeExample("sp0026-unrecognized-attribute-identity")]
    [Test]
    public async Task Sp0026_UnrecognizedAttributeIdentityExample_MatchesSnapshot()
    {
        await VerifyAnalyzerExampleAsync("sp0026-unrecognized-attribute-identity");
    }

    [ReadmeExample("sp0027-requires-not-proven")]
    [Test]
    public async Task Sp0027_RequiresNotProvenExample_MatchesSnapshot()
    {
        await VerifyAnalyzerExampleAsync("sp0027-requires-not-proven");
    }

    [ReadmeExample("sp0028-requires-unsupported")]
    [Test]
    public async Task Sp0028_RequiresUnsupportedExample_MatchesSnapshot()
    {
        await VerifyAnalyzerExampleAsync("sp0028-requires-unsupported");
    }

    [ReadmeExample("sp0029-misplaced-requires")]
    [Test]
    public async Task Sp0029_MisplacedRequiresExample_MatchesSnapshot()
    {
        await VerifyAnalyzerExampleAsync("sp0029-misplaced-requires");
    }

    [ReadmeExample("sp0030-exception-contract-violation")]
    [Test]
    public async Task Sp0030_ExceptionContractViolationExample_MatchesSnapshot()
    {
        await VerifyAnalyzerExampleAsync("sp0030-exception-contract-violation");
    }

    [ReadmeExample("sp0031-misplaced-exception-contract")]
    [Test]
    public async Task Sp0031_MisplacedExceptionContractExample_MatchesSnapshot()
    {
        await VerifyAnalyzerExampleAsync("sp0031-misplaced-exception-contract");
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
        if (string.Equals(Environment.GetEnvironmentVariable("SHARPPROOF_REGENERATE_EXAMPLE_OUTPUTS"), "1",
                StringComparison.Ordinal) ||
            string.Equals(Environment.GetEnvironmentVariable("SHARPPROOF_REGENERATE_EXAMPLE_OUTPUTS"), "true",
                StringComparison.OrdinalIgnoreCase))
            Assert.Ignore("Skipping generated-page verification while regenerating example snapshots.");

        var result = await ReadmeExampleFixture.RunReadmeGeneratorAsync(true);

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
        var actualIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var example in document.RootElement.EnumerateArray())
        {
            if (example.TryGetProperty("DiagnosticId", out var diagnosticId) &&
                !string.IsNullOrWhiteSpace(diagnosticId.GetString()))
                actualIds.Add(diagnosticId.GetString()!);
            if (example.TryGetProperty("DiagnosticIds", out var diagnosticIds))
                foreach (var id in diagnosticIds.EnumerateArray())
                    if (!string.IsNullOrWhiteSpace(id.GetString()))
                        actualIds.Add(id.GetString()!);
        }

        var expectedIds = new SharpProof.Analyzer.SharpProofAnalyzer().SupportedDiagnostics
            .Select(static descriptor => descriptor.Id)
            .ToHashSet(StringComparer.Ordinal);

        Assert.That(actualIds, Is.SupersetOf(expectedIds));
    }

    private static async Task<ImmutableArray<Diagnostic>> VerifyAnalyzerExampleAsync(
        string exampleId,
        ImmutableDictionary<string, string>? globalOptions = null,
        ImmutableArray<MetadataReference>? additionalMetadataReferences = null,
        ImmutableArray<AdditionalText>? additionalFiles = null,
        AnalyzerFeatures analyzerFeatures = AnalyzerFeatures.All)
    {
        var source = ReadmeExampleFixture.LoadExampleSource(exampleId);
        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(
            source,
            globalOptions,
            false,
            additionalFiles,
            ReadmeExampleFixture.GetRelativeExamplePath(exampleId, "input.cs"),
            true,
            null,
            additionalMetadataReferences: additionalMetadataReferences,
            concurrentAnalysis: true,
            compilationName: "ReadmeExample_" + exampleId.Replace('-', '_'),
            analyzerFeatures: analyzerFeatures);

        var formatted = ReadmeExampleFixture.FormatDiagnostics(diagnostics);
        ReadmeExampleFixture.AssertOutputMatchesSnapshot(exampleId, formatted);
        return diagnostics;
    }

    private static ImmutableDictionary<string, string> GetInferredSuggestionOptions(string kind)
    {
        return ImmutableDictionary<string, string>.Empty
            .Add("sharpproof_suggest_missing_enforce_pure", "false")
            .Add("sharpproof_suggest_inferred_contracts", "true")
            .Add("sharpproof_suggest_inferred_contracts_scope", "all")
            .Add("sharpproof_suggest_inferred_contracts_kinds", kind)
            .Add("sharpproof_suggest_inferred_contracts_minimum_confidence", "high");
    }

    private static async Task VerifyCliExampleAsync(string exampleId, params string[] arguments)
    {
        var result = await SymbolicCliTestHost.RunOutOfProcessAsync(arguments);

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
            if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, true);
        }
    }
}

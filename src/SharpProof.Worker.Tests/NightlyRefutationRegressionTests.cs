using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.CompilerArtifact;
using SharpProof.CompilerCollector;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class NightlyRefutationRegressionTests
{
    [Test]
    public async Task ResultDependentEnsuresOnUnconditionalThrowIsVacuous()
    {
        // Derived from nightly RefutedNotObserved seed 2059086 (source SHA-256
        // 42035bae3b03600cd7522d5a5d8b8742b7caa2f5aaae2890021e11a6735741cf).
        const string source = """
            using System;
            using SharpProof.Attributes;
            public static class C
            {
                public static int Target(int x)
                {
                    Contract.Requires(x > 0);
                    Contract.Ensures(!(true || Contract.Result<int>() == 0));
                    throw new InvalidOperationException();
                }
            }
            """;

        // Compile the same method for the CLR with contracts erased, as the
        // nightly runtime oracle does. Both admitted values throw; the source
        // has no normal-return statement, so this holds for all admitted x.
        var runtime = TestCompilation.CreateExact("NightlyRefutationRuntime",
            [("Subject.cs", "#undef SHARPPROOF_CONTRACTS\n" + source)]);
        TestCompilation.AssertNoErrors(runtime);
        using var image = new MemoryStream();
        var emission = runtime.Emit(image);
        Assert.That(emission.Success, Is.True, string.Join("\n", emission.Diagnostics));
        image.Position = 0;
        var assembly = System.Reflection.Assembly.Load(image.ToArray());
        var target = assembly.GetType("C")!.GetMethod("Target")!;
        foreach (var x in ImmutableArray.Create(1, 2))
        {
            var exception = Assert.Throws<System.Reflection.TargetInvocationException>(
                new Action(() => { _ = target.Invoke(null, [x]); }));
            Assert.That(exception!.InnerException, Is.TypeOf<InvalidOperationException>());
        }

        var result = await RunExactNightlyPipeline(source);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven),
            "An Ensures claim cannot be refuted without a modeled normal return.");
        Assert.That(result.Vacuity, Is.EqualTo(WorkerVacuityKind.NoModeledNormalReturn));
    }
    [Test]
    public async Task NearbyNormalReturnPostconditionsKeepTheirMeaning()
    {
        const string template = """
            using SharpProof.Attributes;
            public static class C
            {
                public static int Target(int x)
                {
                    Contract.Requires(x > 0);
                    Contract.Ensures(POSTCONDITION);
                    return x;
                }
            }
            """;
        var invalid = await RunExactNightlyPipeline(template.Replace("POSTCONDITION",
            "!(true || Contract.Result<int>() == 0)", StringComparison.Ordinal));
        Assert.That(invalid.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted),
            "A normal return really violates the false postcondition.");

        var valid = await RunExactNightlyPipeline(template.Replace("POSTCONDITION",
            "true || Contract.Result<int>() == 0", StringComparison.Ordinal));
        Assert.That(valid.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven),
            "A true postcondition holds on the same normal return.");
    }
    private static async Task<WorkerClaimResult> RunExactNightlyPipeline(string source)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        var tree = CSharpSyntaxTree.ParseText(source,
            new CSharpParseOptions(LanguageVersion.CSharp12, preprocessorSymbols: []),
            "/project/Subject.cs", Encoding.UTF8);
        var compilation = CSharpCompilation.Create("NightlyRefutationRegression",
            [tree], TestMetadataReferences.WithSharpProof,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                nullableContextOptions: NullableContextOptions.Enable));
        Assert.That(compilation.GetDiagnostics().Where(d => d is null ? throw new System.ArgumentNullException(nameof(d)) : (d.Severity == DiagnosticSeverity.Error)), Is.Empty);
        using var temp = new TempDirectory("sharpproof-nightly-regression-");
        var manifestPath = Path.Combine(temp.FullName, "manifest.json");
        var optionsBuilder = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.OrdinalIgnoreCase);
        optionsBuilder["build_property._SharpProofCompilerManifestPath"] = manifestPath;
        optionsBuilder["build_property._SharpProofCompilationTargetFramework"] = "net9.0";
        optionsBuilder["build_property._SharpProofProjectDirectory"] = "/project";
        optionsBuilder["build_property.SharpProofVerifyMaximumExpressionDepth"] =
            WorkerBudgets.DefaultMaximumExpressionDepth.ToString(CultureInfo.InvariantCulture);
        optionsBuilder["build_property.SharpProofProfile"] = "advisory";
        optionsBuilder["build_property.SharpProofFeatures"] = "all";
        optionsBuilder["build_property.SharpProofVerifyPolicy"] = "advisory";
        optionsBuilder["build_property.SharpProofAssumptionPolicy"] = "allow";
        var options = optionsBuilder.ToImmutable();
        var exceptions = ImmutableArray.CreateBuilder<string>();
        var analyzerOptions = new AnalyzerOptions([], new OptionsProvider(options));
        var withAnalyzers = compilation.WithAnalyzers(
            [new SharpProofAnalyzer(), new FinalCompilationCollectorAnalyzer()],
            new CompilationWithAnalyzersOptions(analyzerOptions,
                (exception, analyzer, _) => { System.ArgumentNullException.ThrowIfNull(exception); System.ArgumentNullException.ThrowIfNull(analyzer); lock (exceptions) { exceptions.Add(analyzer.GetType().Name + ": " + exception); } },
                concurrentAnalysis: false, logAnalyzerExecutionTime: false));
        var diagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync();
        Assert.That(exceptions.ToImmutable(), Is.Empty);
        Assert.That(diagnostics.Where(d => d is null ? throw new System.ArgumentNullException(nameof(d)) : (d.Id is "AD0001" or "SP0049" or "SP0025")), Is.Empty);
        Assert.That(File.Exists(manifestPath), Is.True);
        var artifact = CompilerManifestArtifactJson.Deserialize(await File.ReadAllTextAsync(manifestPath), cancellationToken: TestContext.CurrentContext.CancellationToken);
        using var project = new ShadowTestProject(artifact);
        artifact = project.Artifact;
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, TestContext.CurrentContext.CancellationToken);
        Assert.That(response.Errors, Is.Empty);
        return response.ClaimResults.Single(result =>
            result is null ? throw new System.ArgumentNullException(nameof(result)) : (artifact.Manifest.Claims.Any(claim => claim is null ? throw new System.ArgumentNullException(nameof(claim)) : (claim.ClaimId == result.ClaimId &&
                claim.CallableId.Contains("Target", StringComparison.Ordinal)))));
    }

    private sealed class OptionsProvider(ImmutableDictionary<string, string> values) : AnalyzerConfigOptionsProvider
    {
        private readonly AnalyzerConfigOptions _options = new DictionaryOptions((values ?? throw new System.ArgumentNullException(nameof(values))));
        public override AnalyzerConfigOptions GlobalOptions => _options;
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree)
        {
            System.ArgumentNullException.ThrowIfNull(tree);
            return _options;
        }
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile)
        {
            System.ArgumentNullException.ThrowIfNull(textFile);
            return _options;
        }
    }

    private sealed class DictionaryOptions(ImmutableDictionary<string, string> values) : AnalyzerConfigOptions
    {
        private readonly ImmutableDictionary<string, string> _guarded_values = values ?? throw new System.ArgumentNullException(nameof(values));
        public override bool TryGetValue(string key, out string value)
        {
            System.ArgumentNullException.ThrowIfNull(key);
            return _guarded_values.TryGetValue(key, out value!);
        }
    }
}

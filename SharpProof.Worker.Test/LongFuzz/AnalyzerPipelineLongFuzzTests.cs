using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.CompilerArtifact;
using SharpProof.CompilerCollector;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test.LongFuzz;

// Each seed generates one method with one claim and runs it through the
// shipped analyzer, the final-compilation collector, the worker and the CLR.
// Findings: escaped analyzer exceptions (AD0001/SP0049/SP0025), invalid
// manifests or worker responses, analyzer/worker disagreement, a Proven claim
// that the CLR falsifies, and a Refuted claim that no tested input witnesses.
[TestFixture]
[Explicit("Long-running fuzz campaign; select with TestCategory=LongFuzz.")]
[Category(LongFuzzSession.Category)]
[NonParallelizable]
public sealed class AnalyzerPipelineLongFuzzTests
{
    private const string Harness = "analyzer-pipeline";
    private static readonly int[] s_ints = [0, 1, 2, 3, -1, 5, 100, int.MaxValue, int.MinValue];
    private static readonly int[]?[] s_arrays = [null, [], [3, -1, 0]];
    private static readonly string?[] s_strings = [null, "", "ab", "abc"];

    [Test]
    [Order(0)]
    public async Task GeneratedClaimsAgreeWithAnalyzerWorkerAndRuntime()
    {
        var session = new LongFuzzSession(Harness, harnessIndex: 0, harnessCount: 2);
        foreach (var seed in session.Seeds())
        {
            var (source, mode, claim) = new ContractClaimProgramGenerator(new Random(seed)).Program();
            session.Count("mode:" + mode);
            CaseResult result;
            try
            {
                result = await RunCase(source, mode, session.Count);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                result = new CaseResult("", "", "");
                result.Problems.Add(("HarnessException", LongFuzzHit.Error, exception.ToString()));
            }
            foreach (var (kind, severity, details) in result.Problems)
            {
                session.RecordHit(new LongFuzzHit(kind, severity, seed, claim, result.Outcome, result.Reason, result.Runtime,
                    details, source));
            }
        }
        var summary = session.Finish();
        Assert.That(session.Cases, Is.Not.Zero, "The campaign must run at least one case: " + summary);
    }

    private sealed record CaseResult(string Outcome, string Reason, string Runtime)
    {
        internal List<(string Kind, string Severity, string Details)> Problems { get; } = [];
    }

    private static async Task<CaseResult> RunCase(string source, string mode, Action<string> count)
    {
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp12, preprocessorSymbols: []),
            "/project/Subject.cs", Encoding.UTF8);
        var compilation = CSharpCompilation.Create("Fuzz_" + Guid.NewGuid().ToString("N"), [tree], TestMetadataReferences.WithSharpProof,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release,
                nullableContextOptions: NullableContextOptions.Enable));
        if (compilation.GetDiagnostics().Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            count("compile-error");
            return new CaseResult("", "", "");
        }
        count("compiled");
        var problems = new List<(string, string, string)>();
        using var temp = new TempDirectory("sharpproof-longfuzz-");
        var manifestPath = Path.Combine(temp.FullName, "manifest.json");
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["build_property._SharpProofCompilerManifestPath"] = manifestPath,
            ["build_property._SharpProofCompilationTargetFramework"] = "net9.0",
            ["build_property._SharpProofProjectDirectory"] = "/project",
            ["build_property.SharpProofVerifyMaximumExpressionDepth"] =
                WorkerBudgets.DefaultMaximumExpressionDepth.ToString(CultureInfo.InvariantCulture),
            ["build_property.SharpProofProfile"] = "advisory",
            ["build_property.SharpProofFeatures"] = "all",
            ["build_property.SharpProofVerifyPolicy"] = "advisory",
            ["build_property.SharpProofAssumptionPolicy"] = "allow"
        };
        var exceptions = new ConcurrentBag<string>();
        var withAnalyzers = compilation.WithAnalyzers([new SharpProofAnalyzer(), new FinalCompilationCollectorAnalyzer()],
            new CompilationWithAnalyzersOptions(new AnalyzerOptions([], new OptionsProvider(options)),
                (exception, analyzer, _) => exceptions.Add(analyzer.GetType().Name + ": " + exception), concurrentAnalysis: false,
                logAnalyzerExecutionTime: false));
        var diagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync();
        foreach (var exception in exceptions)
        {
            problems.Add(("AnalyzerException", LongFuzzHit.Error, exception));
        }
        foreach (var diagnostic in diagnostics)
        {
            count("diag:" + diagnostic.Id);
            if (diagnostic.Id is "AD0001" or "SP0049" or "SP0025")
            {
                problems.Add(("AnalyzerDiagnostic-" + diagnostic.Id, LongFuzzHit.Error, diagnostic.ToString()));
            }
        }
        WorkerClaimOutcome? outcome = null;
        var reason = "";
        if (File.Exists(manifestPath))
        {
            count("manifest");
            try
            {
                var artifact = CompilerManifestArtifactJson.Deserialize(await File.ReadAllTextAsync(manifestPath));
                using var project = new ShadowTestProject(artifact);
                using var worker = SharpProofWorker.Create(project.Request.Budgets);
                var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
                if (response.Errors.Length > 0)
                {
                    problems.Add(("WorkerErrors", LongFuzzHit.Error,
                        string.Join(" | ", response.Errors.Select(error => error.Code + ":" + error.Message))));
                }
                if (!WorkerProtocolJson.Validate(response).IsValid)
                {
                    problems.Add(("WorkerResponseInvalid", LongFuzzHit.Error, "The worker response failed protocol validation."));
                }
                var claims = response.ClaimResults.Where(result => artifact.Manifest.Claims.Any(claim =>
                    claim.ClaimId == result.ClaimId && claim.CallableId.Contains("Target", StringComparison.Ordinal))).ToArray();
                if (claims.Length == 1)
                {
                    outcome = claims[0].Outcome;
                    reason = claims[0].Reason.ToString();
                    count("outcome:" + outcome + (outcome == WorkerClaimOutcome.Unknown ? ":" + reason : ""));
                }
                else
                {
                    count("target-claims:" + claims.Length);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                problems.Add(("WorkerException", LongFuzzHit.Error, exception.ToString()));
            }
        }
        else
        {
            count("no-manifest");
        }

        if (outcome == WorkerClaimOutcome.Proven)
        {
            foreach (var diagnostic in diagnostics.Where(diagnostic => diagnostic.Id is "SP0030" or "SP0013"))
            {
                problems.Add(("AnalyzerWorkerDisagree", LongFuzzHit.Error, diagnostic.ToString()));
            }
        }

        var runtime = "";
        if (outcome is WorkerClaimOutcome.Proven or WorkerClaimOutcome.Refuted)
        {
            var observation = RuntimeOracle.Run(source, mode);
            runtime = observation.Error ?? observation.Violation ?? "no violation on " + observation.Inputs + " inputs";
            if (observation.Error != null)
            {
                problems.Add(("RuntimeHarness", LongFuzzHit.Error, observation.Error));
            }
            else if (outcome == WorkerClaimOutcome.Proven && observation.Violation != null)
            {
                problems.Add(("FalseProven", LongFuzzHit.Error, observation.Violation));
            }
            else if (outcome == WorkerClaimOutcome.Refuted && observation.Violation == null)
            {
                count("refuted-not-observed");
                problems.Add(("RefutedNotObserved", LongFuzzHit.Warning,
                    "No tested input violates the claim the worker refuted (" + observation.Inputs + " inputs)."));
            }
        }
        var caseResult = new CaseResult(outcome?.ToString() ?? "None", reason, runtime);
        caseResult.Problems.AddRange(problems);
        return caseResult;
    }

    private sealed record Observation(string? Violation, string? Error, int Inputs);

    // Executes the Release-compiled method on a fixed input grid. EnforcePure
    // counts writes to State through a property, so a net-zero write sequence
    // such as State--; State++ is still observed as a write.
    private static class RuntimeOracle
    {
        private const string StateField = "public static int State;";
        private const string TrackedState =
            "private static int s_state; public static int StateWrites; " +
            "public static int State { get { return s_state; } set { s_state = value; StateWrites++; } }";

        internal static Observation Run(string source, string mode)
        {
            var trackWrites = mode == ContractClaimProgramGenerator.EnforcePure &&
                !source.Contains("ref State", StringComparison.Ordinal);
            var runtimeSource = trackWrites ? source.Replace(StateField, TrackedState, StringComparison.Ordinal) : source;
            var tree = CSharpSyntaxTree.ParseText(runtimeSource, new CSharpParseOptions(LanguageVersion.CSharp12));
            var compilation = CSharpCompilation.Create("FuzzRt_" + Guid.NewGuid().ToString("N"), [tree],
                TestMetadataReferences.WithSharpProof,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release,
                    nullableContextOptions: NullableContextOptions.Enable));
            using var image = new MemoryStream();
            var emit = compilation.Emit(image);
            if (!emit.Success)
            {
                return new(null, "emit failed " + string.Join(";", emit.Diagnostics), 0);
            }
            var context = new AssemblyLoadContext("longfuzz", isCollectible: true);
            try
            {
                image.Position = 0;
                var type = context.LoadFromStream(image).GetType("C")!;
                return Execute(type, mode, trackWrites);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return new(null, exception.ToString(), 0);
            }
            finally
            {
                context.Unload();
            }
        }

        private static Observation Execute(Type type, string mode, bool trackWrites)
        {
            var target = type.GetMethod("Target", BindingFlags.Public | BindingFlags.Static)!;
            var pre = type.GetMethod("Pre", BindingFlags.Public | BindingFlags.Static)!;
            var post = type.GetMethod("Post", BindingFlags.Public | BindingFlags.Static)!;
            var other = type.GetField("Other")!;
            var writes = type.GetField("StateWrites");
            var stateField = type.GetField("State");
            var stateProperty = type.GetProperty("State");
            int GetState()
            {
                return (int)(stateField?.GetValue(null) ?? stateProperty!.GetValue(null))!;
            }
            void Reset()
            {
                if (stateField != null)
                {
                    stateField.SetValue(null, 0);
                }
                else
                {
                    stateProperty!.SetValue(null, 0);
                }
                writes?.SetValue(null, 0);
                other.SetValue(null, 0);
            }
            var inputs = 0;
            foreach (var x in s_ints)
            {
                foreach (var y in s_ints)
                {
                    foreach (var a in s_arrays)
                    {
                        foreach (var s in s_strings)
                        {
                            Reset();
                            if (!(bool)pre.Invoke(null, [x, y, a?.ToArray(), s])!)
                            {
                                continue;
                            }
                            inputs++;
                            object?[] args = [x, y, a?.ToArray(), s];
                            object? result = null;
                            Exception? thrown = null;
                            Reset();
                            try
                            {
                                result = target.Invoke(null, args);
                            }
                            catch (TargetInvocationException exception)
                            {
                                thrown = exception.InnerException;
                            }
                            var input = "x=" + x + " y=" + y + " a=" + (a == null ? "null" : "[" + string.Join(",", a) + "]") +
                                " s=" + (s == null ? "null" : "\"" + s + "\"");
                            var violation = Check(mode, thrown, result, args, a, input, GetState(), (int)other.GetValue(null)!,
                                trackWrites ? (int)writes!.GetValue(null)! : 0, () => Measure(target, x, y, a, s, Reset), post, x, y);
                            if (violation != null)
                            {
                                return new(violation, null, inputs);
                            }
                        }
                    }
                }
            }
            return new(null, null, inputs);
        }

        private static string? Check(string mode, Exception? thrown, object? result, object?[] args, int[]? original, string input,
            int state, int other, int stateWrites, Func<long> measure, MethodInfo post, int x, int y)
        {
            switch (mode)
            {
                case ContractClaimProgramGenerator.DoesNotThrow:
                    return thrown == null ? null : "threw " + thrown.GetType().Name + " on " + input;
                case ContractClaimProgramGenerator.EnforcePure:
                    if (stateWrites != 0 || state != 0 || other != 0 ||
                        (original != null && !((int[])args[2]!).SequenceEqual(original)))
                    {
                        return "mutated state (State=" + state + ", State writes=" + stateWrites + ", Other=" + other + ") on " + input;
                    }
                    return null;
                case ContractClaimProgramGenerator.ZeroAllocations:
                    // Throwing allocates the exception object, so an escaping
                    // exception is an allocation witness on its own.
                    if (thrown != null)
                    {
                        return "threw " + thrown.GetType().Name + " (exception object allocated) on " + input;
                    }
                    var allocated = measure();
                    return allocated > 0 ? "allocated " + allocated + " bytes on " + input : null;
                default:
                    if (thrown == null && !(bool)post.Invoke(null, [(int)result!, x, y, 0, state])!)
                    {
                        return "postcondition false with result " + result + " on " + input;
                    }
                    return null;
            }
        }

        private static long Measure(MethodInfo target, int x, int y, int[]? a, string? s, Action reset)
        {
            var call = target.CreateDelegate<Func<int, int, int[]?, string?, int>>();
            reset();
            _ = call(x, y, a?.ToArray(), s);
            var minimum = long.MaxValue;
            for (var i = 0; i < 3; i++)
            {
                var copy = a?.ToArray();
                reset();
                var before = GC.GetAllocatedBytesForCurrentThread();
                _ = call(x, y, copy, s);
                minimum = Math.Min(minimum, GC.GetAllocatedBytesForCurrentThread() - before);
            }
            return minimum;
        }
    }

    private sealed class OptionsProvider(IReadOnlyDictionary<string, string> values) : AnalyzerConfigOptionsProvider
    {
        private readonly AnalyzerConfigOptions _options = new DictionaryOptions(values);

        public override AnalyzerConfigOptions GlobalOptions => _options;

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree)
        {
            return _options;
        }

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile)
        {
            return _options;
        }
    }

    private sealed class DictionaryOptions(IReadOnlyDictionary<string, string> values) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value)
        {
            if (values.TryGetValue(key, out var found))
            {
                value = found;
                return true;
            }
            value = string.Empty;
            return false;
        }
    }
}

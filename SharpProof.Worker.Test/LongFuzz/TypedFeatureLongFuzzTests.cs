using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.CompilerArtifact;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test.LongFuzz;

// Each seed selects one type-correct feature family. The CLR first evaluates
// the generated body, then the same body is checked with a true or false
// postcondition (or DoesNotThrow for a fault). The witness is retained in the
// hit source, rather than inferred from a separate model of C# semantics.
[TestFixture]
[Explicit("Long-running fuzz campaign; select with TestCategory=LongFuzz.")]
[Category(LongFuzzSession.Category)]
[Order(-1)]
[NonParallelizable]
public sealed class TypedFeatureLongFuzzTests
{
    private const string Harness = "typed-features";
    private static readonly int[] s_anchors = [int.MinValue, -257, -3, -1, 0, 1, 2, 3, 7, 255, 256, int.MaxValue];
    private static readonly string[] s_families =
    [
        "checked-arithmetic", "bitwise-shifts", "checked-conversion", "lifted-nullable",
        "array-index", "string-from-end", "switch-pattern", "tuple-deconstruction",
        "boxing-pattern", "local-function", "short-circuit", "conditional-access",
        "enum-conversion", "user-conversion", "math-clamp",
        // Keep each unsupported-path family in the cycle, and spend an equal
        // additional share on the families that yielded concrete verdicts.
        "bitwise-shifts", "checked-arithmetic", "checked-conversion",
        "local-function", "short-circuit", "switch-pattern"
    ];

    [Test]
    public async Task GeneratedTypedClaimsAgreeWithClr()
    {
        // This fixture runs first and consumes one quarter of the shared budget.
        var session = new LongFuzzSession(Harness, harnessIndex: 0, harnessCount: 6);
        foreach (var seed in session.Seeds())
        {
            var family = s_families[seed % s_families.Length];
            session.Count("selected:" + family);
            var generatedSource = "// Seed " + seed;
            try
            {
                await RunCase(session, seed, family, source => { System.ArgumentNullException.ThrowIfNull(source); generatedSource = source; });
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                session.RecordHit(new LongFuzzHit("HarnessException", LongFuzzHit.Error, seed, family,
                    "", "", "", exception.ToString(), generatedSource));
            }
        }
        var summary = session.Finish();
        Assert.That(session.Cases, Is.Not.Zero, "The campaign must run at least one case: " + summary);
    }

    private static async Task RunCase(LongFuzzSession session, int seed, string family, Action<string> rememberSource)
    {
        System.ArgumentNullException.ThrowIfNull(family);
        System.ArgumentNullException.ThrowIfNull(rememberSource);
        System.ArgumentNullException.ThrowIfNull(session);
        var random = new Random(seed);
        var anchor = s_anchors[random.Next(s_anchors.Length)];
        var body = Body(family, random);
        var probeSource = Source("", body);
        rememberSource(probeSource);
        var probe = RunClr(probeSource, [anchor]);
        if (probe.Error is not null)
        {
            session.RecordHit(new LongFuzzHit("GeneratorOrClrError", LongFuzzHit.Error, seed, family,
                "", "", "", probe.Error, probeSource));
            return;
        }
        session.Count("compiled:" + family);
        var first = probe.Runs[0];
        var falseClaim = (seed / s_families.Length) % 2 == 0;
        var claim = first.Exception is not null
            ? "[DoesNotThrow]"
            : "Contract.Ensures(Contract.Old(x) != " + Literal(anchor) +
              " || Contract.Result<int>() == " + Literal(falseClaim ? unchecked(first.Value + 1) : first.Value) + ");";
        var source = Source(claim, body);
        rememberSource(source);
        session.Count("claim:" + family + (first.Exception is null ? ":Ensures" : ":DoesNotThrow"));
        var runtime = RunClr(source, Inputs(anchor));
        if (runtime.Error is not null)
        {
            session.RecordHit(new LongFuzzHit("RuntimeOracleError", LongFuzzHit.Error, seed, claim,
                "", "", "", runtime.Error, source));
            return;
        }
        var finalAtAnchor = runtime.Runs[0];
        if (finalAtAnchor.Exception != first.Exception ||
            (first.Exception is null && finalAtAnchor.Value != first.Value))
        {
            session.RecordHit(new LongFuzzHit("OracleMismatch", LongFuzzHit.Error, seed, claim,
                "", "", Format(finalAtAnchor), "Adding the claim changed the CLR result at the witness input.", source));
            return;
        }
        var violation = claim == "[DoesNotThrow]"
            ? runtime.Runs.FirstOrDefault(run => run is null ? throw new System.ArgumentNullException(nameof(run)) : (run.Exception is not null))
            : runtime.Runs.FirstOrDefault(run => run is null ? throw new System.ArgumentNullException(nameof(run)) : (run.Input == anchor && run.Exception is null &&
                run.Value != (falseClaim ? unchecked(first.Value + 1) : first.Value)));
        if (violation is not null)
        {
            session.Count("witness:" + family);
        }
        session.Count("clr:" + family + (first.Exception is null ? ":value" : ":throws"));

        var compilation = Compile(source, contractsEnabled: true);
        var errors = compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)).ToArray();
        if (errors.Length != 0)
        {
            session.RecordHit(new LongFuzzHit("GeneratorCompileError", LongFuzzHit.Error, seed, claim,
                "", "", Format(violation), string.Join(" | ", errors.Select(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.ToString()))), source));
            return;
        }
        session.Count("claim-compiled:" + family);
        var discovery = new ClaimManifestBuilder(compilation).Build();
        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0", WorkerFeatureSet.All,
            discovery, WorkerBudgets.DefaultMaximumExpressionDepth, TestContext.CurrentContext.CancellationToken);
        using var project = new ShadowTestProject(artifact);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, TestContext.CurrentContext.CancellationToken);
        if (response.Errors.Length != 0 || !WorkerProtocolJson.Validate(response).IsValid)
        {
            session.RecordHit(new LongFuzzHit("WorkerErrors", LongFuzzHit.Error, seed, claim,
                "", "", Format(violation), string.Join(" | ", response.Errors.Select(error =>
                    error is null ? throw new System.ArgumentNullException(nameof(error)) : (error.Code + ":" + error.Message))), source));
            return;
        }
        var claims = response.ClaimResults.Where(result => result is null ? throw new System.ArgumentNullException(nameof(result)) : (artifact.Manifest.Claims.Any(item =>
            item is null ? throw new System.ArgumentNullException(nameof(item)) : (item.ClaimId == result.ClaimId && item.CallableId.Contains("Target", StringComparison.Ordinal))))).ToArray();
        if (claims.Length == 0)
        {
            session.RecordHit(new LongFuzzHit("NoTargetClaim", LongFuzzHit.Error, seed, claim,
                "", "", Format(violation), "The explicit Target claim was not present in the worker response.", source));
            return;
        }
        if (claims.Length != 1)
        {
            session.RecordHit(new LongFuzzHit("ClaimSelectionError", LongFuzzHit.Error, seed, claim,
                "", "", Format(violation), "Expected one Target claim, got " + claims.Length, source));
            return;
        }
        var result = claims[0];
        session.Count("verdict:" + family + ":" + result.Outcome);
        if (result.Outcome == WorkerClaimOutcome.Unknown)
        {
            session.Count("unknown-reason:" + family + ":" + result.Reason);
        }
        if (result.Outcome == WorkerClaimOutcome.Proven && violation is not null)
        {
            session.RecordHit(new LongFuzzHit("FalseProven", LongFuzzHit.Error, seed, claim,
                result.Outcome.ToString(), result.Reason.ToString(), Format(violation),
                "The CLR falsifies a Proven claim at the recorded input.", source));
        }
        else if (result.Outcome == WorkerClaimOutcome.Refuted && violation is null)
        {
            session.RecordHit(new LongFuzzHit("RefutedNotObserved", LongFuzzHit.Warning, seed, claim,
                result.Outcome.ToString(), result.Reason.ToString(), "no violation on " + runtime.Runs.Count + " inputs",
                "The CLR oracle did not observe the reported refutation.", source));
        }
    }

    private static string Body(string family, Random random)
    {
        System.ArgumentNullException.ThrowIfNull(random);
        var offset = random.Next(-4, 5);
        var shift = random.Next(0, 35);
        return family switch
        {
            "checked-arithmetic" => "return checked(x + " + Literal(offset) + ");",
            "bitwise-shifts" => "int v = unchecked(x * " + (random.Next(2, 7)) + "); return (v << " + shift + ") ^ (v >>> 1);",
            "checked-conversion" => "return checked((byte)unchecked(x + " + Literal(offset) + "));",
            "lifted-nullable" => "int? n = (x & 1) == 0 ? null : x; return (n + " + Literal(offset) + ") ?? 17;",
            "array-index" => "int[] items = [x, unchecked(x + 1), -3]; return items[x & 3];",
            "string-from-end" => "string text = (x & 1) == 0 ? \"abc\" : \"\"; return text[^" + random.Next(1, 5) + "];",
            "switch-pattern" => "return x switch { < 0 => -1, 0 => 0, >= 1 and <= 4 => unchecked(x + " + Literal(offset) + "), _ => 9 };",
            "tuple-deconstruction" => "(int a, int b) = (x, unchecked(x + " + Literal(offset) + ")); (a, b) = (b, a); return unchecked(a - b);",
            "boxing-pattern" => "object? value = (x & 1) == 0 ? null : (object)x; return value is int n ? n : -7;",
            "local-function" => "int Local(int v) => unchecked(v * " + random.Next(2, 7) + " + " + Literal(offset) + "); return Local(x);",
            "short-circuit" => "bool okay = x != 0 && 12 / x > 2; return okay ? x : -1;",
            "conditional-access" => "string? text = (x & 1) == 0 ? null : \"alpha\"; return text?.Length ?? x;",
            "enum-conversion" => "Shade shade = (Shade)unchecked((byte)x); return shade == Shade.Second ? 2 : (int)shade;",
            "user-conversion" => "return (int)new Cell(x);",
            "math-clamp" => "return Math.Clamp(x, -3, 3);",
            _ => throw new ArgumentOutOfRangeException(nameof(family))
        };
    }

    private static string Source(string claim, string body)
    {
        System.ArgumentNullException.ThrowIfNull(body);
        System.ArgumentNullException.ThrowIfNull(claim);
        var attribute = claim == "[DoesNotThrow]" ? claim + " " : "";
        var inlineClaim = attribute.Length == 0 ? claim : "";
        return "using System; using SharpProof.Attributes; public static class C { " +
            "private enum Shade : byte { First = 1, Second = 2 } " +
            "private readonly struct Cell { public readonly int Value; public Cell(int value) { Value = value; } " +
            "public static explicit operator int(Cell value) => unchecked(value.Value + 1); } " +
            attribute + "public static int Target(int x) { " + inlineClaim + " " + body + " } }";
    }

    private static CSharpCompilation Compile(string source, bool contractsEnabled)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        string[] symbols = contractsEnabled ? [Contract.ConditionalSymbol] : [];
        var tree = CSharpSyntaxTree.ParseText(source,
            new CSharpParseOptions(LanguageVersion.CSharp12, preprocessorSymbols: symbols), "/project/Subject.cs");
        return CSharpCompilation.Create("TypedFuzz_" + Guid.NewGuid().ToString("N"), [tree],
            TestMetadataReferences.WithSharpProof,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release, nullableContextOptions: NullableContextOptions.Enable));
    }

    private sealed record ClrRun(int Input, int Value, string? Exception);
    private sealed record ClrResult(List<ClrRun> Runs, string? Error)
    {
        public List<ClrRun> Runs { get; init; } = Runs ?? throw new System.ArgumentNullException(nameof(Runs));
    }

    private static ClrResult RunClr(string source, int[] inputs)
    {
        System.ArgumentNullException.ThrowIfNull(inputs, nameof(inputs));
        System.ArgumentNullException.ThrowIfNull(source);
        var compilation = Compile(source, contractsEnabled: false);
        var errors = compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)).ToArray();
        if (errors.Length != 0)
        {
            return new([], "generated program did not compile: " + string.Join(" | ", errors.Select(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.ToString()))));
        }
        using var image = new MemoryStream();
        var emit = compilation.Emit(image);
        if (!emit.Success)
        {
            return new([], "generated program did not emit: " + string.Join(" | ", emit.Diagnostics));
        }
        var context = new AssemblyLoadContext("typed-longfuzz", isCollectible: true);
        try
        {
            image.Position = 0;
            var method = context.LoadFromStream(image).GetType("C")!.GetMethod("Target", BindingFlags.Public | BindingFlags.Static)!;
            var runs = new List<ClrRun>(inputs.Length);
            foreach (var input in inputs)
            {
                try
                {
                    runs.Add(new(input, (int)method.Invoke(null, [input])!, null));
                }
                catch (TargetInvocationException exception)
                {
                    runs.Add(new(input, 0, exception.InnerException!.GetType().FullName));
                }
            }
            return new(runs, null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new([], exception.ToString());
        }
        finally
        {
            context.Unload();
        }
    }

    private static int[] Inputs(int anchor)
    {
        return new[] { anchor, int.MinValue, -1, 0, 1, 2, int.MaxValue }.Distinct().ToArray();
    }

    private static string Format(ClrRun? run)
    {
        return run is null ? "no witnessed violation" : "x=" + Literal(run.Input) +
            (run.Exception is null ? " result=" + Literal(run.Value) : " threw " + run.Exception);
    }

    private static string Literal(int value)
    {
        return value == int.MinValue ? "int.MinValue" : value == int.MaxValue ? "int.MaxValue" :
            value.ToString(CultureInfo.InvariantCulture);
    }
}

using System.Collections.Immutable;
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

// Each seed composes a typed int expression with a cleanup/control-flow scope.
// The CLR evaluates return, exception and state on a boundary grid before the
// same source with one claim is sent to the worker. Every 41 consecutive seeds
// cover all 25 pairs; the 16 pairs with concrete pilot verdicts run twice.
// Odd cycles also recover exceptions after the scope's cleanup has run.
[TestFixture]
[Explicit("Long-running fuzz campaign; select with TestCategory=LongFuzz.")]
[Category(LongFuzzSession.Category)]
[Order(3)]
[NonParallelizable]
public sealed class CompositionalLongFuzzTests
{
    private const string Harness = "compositional";
    private sealed record Operation(string Name, string Expression, int Anchor)
    {
        public string Name { get; init; } = Name ?? throw new System.ArgumentNullException(nameof(Name));
        public string Expression { get; init; } = Expression ?? throw new System.ArgumentNullException(nameof(Expression));
    }
    private sealed record Scope(string Name, string Template, string ClaimMode)
    {
        public string Name { get; init; } = Name ?? throw new System.ArgumentNullException(nameof(Name));
        public string Template { get; init; } = Template ?? throw new System.ArgumentNullException(nameof(Template));
        public string ClaimMode { get; init; } = ClaimMode ?? throw new System.ArgumentNullException(nameof(ClaimMode));
    }
    private sealed record Observation(int Input, int? Value, string? Exception, int State, int Writes);
    private sealed record ClrResult(ImmutableArray<Observation> Runs, string? Error)
    {
        public ImmutableArray<Observation> Runs { get; init; } = Runs.IsDefault ? throw new System.ArgumentException("Array must be initialized.", nameof(Runs)) : Runs;
    }

    private static readonly ImmutableArray<Operation> s_operations =
    [
        new("checked-add", "checked(y + 1)", int.MaxValue),
        new("shift-divide", "8 / (y & 3) + (y << 1)", 0),
        new("array-index", "new[] { y, 2, 3 }[y & 3]", 3),
        new("switch-pattern", "y switch { < 0 => checked(y * 2), 0 => 0, _ => y << 1 }", 2),
        new("checked-byte", "checked((byte)y)", 256)
    ];

    // @@ is always an int expression over y. Each scope contains a return,
    // so every operation/scope pair is syntactically valid C#.
    private static readonly ImmutableArray<Scope> s_scopes =
    [
        new("pure-finally", "int y = x; try { return @@; } finally { _ = y; }", "Auto"),
        new("state-finally", "int y = x; try { return @@; } finally { Mark(y); }", "EnforcePure"),
        new("filter-finally", "int y = x; try { return @@; } " +
            "catch (Exception) when ((State = y) == 0) { Mark(7); return 7; } " +
            "finally { Mark(9); }", "Auto"),
        new("observable-using", "int y = x; using (var d = new D(y)) { return @@; }", "AutoOrPurity"),
        new("helper-finally", "int y = x; try { y = Helper(x); return @@; } finally { Mark(y); }",
            "EnforcePure")
    ];

    private static readonly ImmutableArray<int> s_inputs =
        [int.MinValue, int.MinValue + 1, -4, -3, -2, -1, 0, 1, 2, 3, 4, 7, 255, 256,
            int.MaxValue - 1, int.MaxValue];
    private static readonly ImmutableArray<(int Operation, int Scope)> s_cycle = BuildCycle();

    [Test]
    public async Task GeneratedCompositionsAgreeWithClr()
    {
        var session = new LongFuzzSession(Harness, harnessIndex: 4, harnessCount: 6);
        foreach (var seed in session.Seeds())
        {
            var unsignedSeed = unchecked((uint)seed);
            var slot = s_cycle[(int)(unsignedSeed % (uint)s_cycle.Length)];
            var cycle = (int)(unsignedSeed / (uint)s_cycle.Length);
            var operation = Vary(s_operations[slot.Operation], cycle);
            var scope = s_scopes[slot.Scope];
            var pair = operation.Name + "/" + scope.Name;
            var recover = (cycle & 1) != 0;
            session.Count("selected:" + pair);
            session.Count("variant:" + (recover ? "catch-recover:" : "plain:") + pair);
            var generatedSource = "// Compositional LongFuzz seed " + seed;
            try
            {
                await RunCase(session, seed, operation, scope, pair, recover, source => { System.ArgumentNullException.ThrowIfNull(source); generatedSource = source; });
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                session.RecordHit(new LongFuzzHit("HarnessException", LongFuzzHit.Error, seed, pair,
                    "", "", "", exception.ToString(), generatedSource));
            }
        }
        var summary = session.Finish();
        Assert.That(session.Cases, Is.Not.Zero, "The campaign must run at least one case: " + summary);
    }

    private static ImmutableArray<(int Operation, int Scope)> BuildCycle()
    {
        var slots = ImmutableArray.CreateBuilder<(int Operation, int Scope)>();
        for (var operation = 0; operation < s_operations.Length; operation++)
        {
            for (var scope = 0; scope < s_scopes.Length; scope++)
            {
                slots.Add((operation, scope));
            }
        }
        for (var operation = 0; operation < s_operations.Length; operation++)
        {
            for (var scope = 0; scope < s_scopes.Length; scope++)
            {
                if (operation != 2 && scope != 3)
                {
                    slots.Add((operation, scope));
                }
            }
        }
        return slots.ToImmutable();
    }

    private static Operation Vary(Operation operation, int cycle)
    {
        if (cycle == 0)
        {
            return operation;
        }
        return operation.Name switch
        {
            "checked-add" => CheckedAdd(cycle),
            "shift-divide" => ShiftDivide(cycle),
            "array-index" => ArrayIndex(cycle),
            "switch-pattern" => SwitchPattern(cycle),
            "checked-byte" => CheckedByte(cycle),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
    }

    private static Operation CheckedAdd(int cycle)
    {
        if (cycle < 0)
        { throw new System.ArgumentException("Value must be initialized.", nameof(cycle)); }

        var delta = ImmutableArray.Create(1, 2, -1, -2)[cycle % 4];
        return new("checked-add", "checked(y + " + Literal(delta) + ")",
            delta < 0 ? int.MinValue : int.MaxValue);
    }

    private static Operation ShiftDivide(int cycle)
    {
        if (cycle < 0)
        { throw new System.ArgumentException("Value must be initialized.", nameof(cycle)); }

        var mask = ImmutableArray.Create(3, 7)[cycle % 2];
        var shift = ImmutableArray.Create(1, 31, 32, 33)[cycle % 4];
        return new("shift-divide", "8 / (y & " + mask + ") + (y << " + shift + ")", 0);
    }

    private static Operation ArrayIndex(int cycle)
    {
        if (cycle < 0)
        { throw new System.ArgumentException("Value must be initialized.", nameof(cycle)); }

        var longArray = cycle % 2 == 0;
        var elements = longArray ? "y, -2, 3, 5" : "y, 2, 3";
        var mask = longArray ? 7 : 3;
        return new("array-index", "new[] { " + elements + " }[y & " + mask + "]",
            longArray ? 7 : 3);
    }

    private static Operation SwitchPattern(int cycle)
    {
        if (cycle < 0)
        { throw new System.ArgumentException("Value must be initialized.", nameof(cycle)); }

        var shift = ImmutableArray.Create(1, 2, 31, 32)[cycle % 4];
        return new("switch-pattern", "y switch { < 0 => checked(y * 2), 0 => 0, _ => y << " +
            shift + " }", 2);
    }

    private static Operation CheckedByte(int cycle)
    {
        if (cycle < 0)
        { throw new System.ArgumentException("Value must be initialized.", nameof(cycle)); }

        var delta = cycle % 3;
        return new("checked-byte", "checked((byte)(y + " + delta + "))", 256);
    }

    private static async Task RunCase(LongFuzzSession session, int seed, Operation operation, Scope scope,
        string pair, bool recover, Action<string> rememberSource)
    {
        System.ArgumentNullException.ThrowIfNull(operation);
        System.ArgumentNullException.ThrowIfNull(rememberSource);
        System.ArgumentNullException.ThrowIfNull(scope);
        var plainBody = scope.Template.Replace("@@", operation.Expression, StringComparison.Ordinal);
        var body = recover ? "try { " + plainBody + " } catch (Exception) { Mark(5); return 5; }" : plainBody;
        var inputs = new[] { operation.Anchor }.Concat(s_inputs).Distinct().ToArray();
        var probeSource = Source("", body);
        rememberSource(probeSource);
        var probe = RunClr(probeSource, inputs);
        if (probe.Error is not null)
        {
            session.RecordHit(new LongFuzzHit("GeneratorOrClrError", LongFuzzHit.Error, seed, pair,
                "", "", "", probe.Error, probeSource));
            return;
        }
        session.Count("compiled:" + pair);
        if (recover && !CheckRecovery(session, seed, pair, plainBody, inputs, probe.Runs, probeSource))
        {
            return;
        }

        var claim = Claim(scope, operation, probe.Runs[0]);
        var source = Source(claim, body);
        rememberSource(source);
        session.Count("claim:" + pair + ":" + ClaimName(claim));
        var runtime = RunClr(source, inputs);
        if (runtime.Error is not null)
        {
            session.RecordHit(new LongFuzzHit("RuntimeOracleError", LongFuzzHit.Error, seed, pair,
                "", "", "", runtime.Error, source));
            return;
        }
        if (!probe.Runs.SequenceEqual(runtime.Runs))
        {
            var mismatch = probe.Runs.Zip(runtime.Runs).FirstOrDefault(item => item.First != item.Second);
            var detail = mismatch.First is null ? "The CLR run count changed after adding the claim." :
                "Before: " + Format(mismatch.First) + "; after: " + Format(mismatch.Second);
            session.RecordHit(new LongFuzzHit("OracleMismatch", LongFuzzHit.Error, seed, pair,
                "", "", detail, "Adding the claim changed the CLR return, exception or state.", source));
            return;
        }
        session.Count("clr-matched:" + pair);
        var witness = Witness(claim, operation, probe.Runs[0], runtime.Runs);
        if (witness is not null)
        {
            session.Count("witness:" + pair);
        }

        var compilation = Compile(source, contractsEnabled: true);
        var errors = compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)).ToImmutableArray();
        if (errors.Length != 0)
        {
            session.RecordHit(new LongFuzzHit("GeneratorCompileError", LongFuzzHit.Error, seed, pair,
                "", "", Format(witness), string.Join(" | ", errors.Select(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.ToString()))), source));
            return;
        }
        session.Count("claim-compiled:" + pair);
        var discovery = new ClaimManifestBuilder(compilation).Build();
        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0", WorkerFeatureSet.All,
            discovery, WorkerBudgets.DefaultMaximumExpressionDepth, TestContext.CurrentContext.CancellationToken);
        using var project = new ShadowTestProject(artifact);
        artifact = project.Artifact;
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, TestContext.CurrentContext.CancellationToken);
        if (response.Errors.Length != 0 || !WorkerProtocolJson.Validate(response).IsValid)
        {
            session.RecordHit(new LongFuzzHit("WorkerErrors", LongFuzzHit.Error, seed, pair,
                "", "", Format(witness), string.Join(" | ", response.Errors.Select(error =>
                    error is null ? throw new System.ArgumentNullException(nameof(error)) : (error.Code + ":" + error.Message))), source));
            return;
        }
        var claims = response.ClaimResults.Where(result => result is null ? throw new System.ArgumentNullException(nameof(result)) : (artifact.Manifest.Claims.Any(item =>
            item is null ? throw new System.ArgumentNullException(nameof(item)) : (item.ClaimId == result.ClaimId && item.CallableId.Contains("Target", StringComparison.Ordinal))))).ToImmutableArray();
        if (claims.Length != 1)
        {
            session.RecordHit(new LongFuzzHit("ClaimSelectionError", LongFuzzHit.Error, seed, pair,
                "", "", Format(witness), "Expected one Target claim, got " + claims.Length, source));
            return;
        }
        session.Count("worker:" + pair);
        var result = claims[0];
        session.Count("verdict:" + pair + ":" + result.Outcome);
        session.Count("verdict:" + (recover ? "catch-recover:" : "plain:") + pair + ":" + result.Outcome);
        if (result.Outcome == WorkerClaimOutcome.Unknown)
        {
            session.Count("unknown-reason:" + pair + ":" + result.Reason);
        }
        else
        {
            session.Count("concrete:" + pair);
        }
        if (result.Outcome == WorkerClaimOutcome.Proven && witness is not null)
        {
            session.RecordHit(new LongFuzzHit("FalseProven", LongFuzzHit.Error, seed, pair,
                result.Outcome.ToString(), result.Reason.ToString(), Format(witness),
                "The CLR falsifies a Proven claim at the recorded input.", source));
        }
        else if (result.Outcome == WorkerClaimOutcome.Refuted && witness is null)
        {
            session.RecordHit(new LongFuzzHit("RefutedNotObserved", LongFuzzHit.Warning, seed, pair,
                result.Outcome.ToString(), result.Reason.ToString(), "no violation on " + runtime.Runs.Length + " inputs",
                "The CLR oracle did not observe the reported refutation.", source));
        }
    }

    private static bool CheckRecovery(LongFuzzSession session, int seed, string pair, string plainBody,
        int[] inputs, ImmutableArray<Observation> recovered, string recoveredSource)
    {
        System.ArgumentNullException.ThrowIfNull(inputs, nameof(inputs));
        System.ArgumentNullException.ThrowIfNull(pair);
        System.ArgumentNullException.ThrowIfNull(plainBody);
        System.ArgumentNullException.ThrowIfNull(recoveredSource);
        System.ArgumentNullException.ThrowIfNull(session);
        var plain = RunClr(Source("", plainBody), inputs);
        if (plain.Error is not null)
        {
            session.RecordHit(new LongFuzzHit("RecoveryBaselineError", LongFuzzHit.Error, seed, pair,
                "", "", "", plain.Error, recoveredSource));
            return false;
        }
        if (plain.Runs.Length != recovered.Length)
        {
            session.RecordHit(new LongFuzzHit("RecoveryOracleMismatch", LongFuzzHit.Error, seed, pair,
                "", "", "", "The CLR run count changed under the recovery wrapper.", recoveredSource));
            return false;
        }
        var recoveredInputs = 0;
        for (var index = 0; index < inputs.Length; index++)
        {
            var before = plain.Runs[index];
            var after = recovered[index];
            if (before.Exception is null)
            {
                if (before != after)
                {
                    session.RecordHit(new LongFuzzHit("RecoveryOracleMismatch", LongFuzzHit.Error, seed, pair,
                        "", "", Format(after), "A normal CLR path changed under the recovery wrapper; before: " +
                        Format(before), recoveredSource));
                    return false;
                }
                session.Count("route:normal-input:" + pair);
            }
            else
            {
                if (after.Exception is not null || after.Value != 5 || after.State != 5 ||
                    after.Writes != before.Writes + 1)
                {
                    session.RecordHit(new LongFuzzHit("RecoveryOracleMismatch", LongFuzzHit.Error, seed, pair,
                        "", "", Format(after), "The CLR exception was not recovered after cleanup; before: " +
                        Format(before), recoveredSource));
                    return false;
                }
                session.Count("route:recovered-input:" + pair);
                session.Count("route:recovered-exception:" + pair + ":" + before.Exception);
                recoveredInputs++;
            }
            session.Count("route:checked-input:" + pair);
        }
        if (recoveredInputs != 0)
        {
            session.Count("route:recovered-case:" + pair);
        }
        return true;
    }

    private static string Claim(Scope scope, Operation operation, Observation anchor)
    {
        System.ArgumentNullException.ThrowIfNull(operation);
        if (scope.ClaimMode == "EnforcePure" ||
            (scope.ClaimMode == "AutoOrPurity" && anchor.Exception is null))
        {
            return "[EnforcePure]";
        }
        if (anchor.Exception is not null)
        {
            return "[DoesNotThrow]";
        }
        var value = anchor.Value!.Value;
        var expected = operation.Name == "shift-divide" ? unchecked(value + 1) : value;
        return "Contract.Ensures(Contract.Old(x) != " + Literal(operation.Anchor) +
            " || Contract.Result<int>() == " + Literal(expected) + ");";
    }

    private static Observation? Witness(string claim, Operation operation, Observation atAnchor, ImmutableArray<Observation> runs)
    {
        if (runs.IsDefault)
        {
            throw new ArgumentException("Array must be initialized.", nameof(runs));
        }
        System.ArgumentNullException.ThrowIfNull(atAnchor);
        System.ArgumentNullException.ThrowIfNull(operation);
        if (claim == "[DoesNotThrow]")
        {
            return runs.FirstOrDefault(run => run is null ? throw new System.ArgumentNullException(nameof(run)) : (run.Exception is not null));
        }
        if (claim == "[EnforcePure]")
        {
            return runs.FirstOrDefault(run => run is null ? throw new System.ArgumentNullException(nameof(run)) : (run.Writes != 0 || run.State != 0));
        }
        var expected = operation.Name == "shift-divide" ? unchecked(atAnchor.Value!.Value + 1) :
            atAnchor.Value!.Value;
        return runs.FirstOrDefault(run => run is null ? throw new System.ArgumentNullException(nameof(run)) : (run.Input == operation.Anchor && run.Exception is null && run.Value != expected));
    }

    private static string ClaimName(string claim)
    {
        System.ArgumentNullException.ThrowIfNull(claim);
        return claim == "[DoesNotThrow]" ? "DoesNotThrow" :
            claim == "[EnforcePure]" ? "EnforcePure" : "Ensures";
    }

    private static string Format(Observation? run)
    {
        if (run is null)
        {
            return "no witnessed violation";
        }
        return "x=" + Literal(run.Input) + " " + (run.Exception is null ? "return:" + Literal(run.Value!.Value) :
            "throw:" + run.Exception) + "/state:" + Literal(run.State) + "/writes:" + run.Writes;
    }

    private static string Source(string claim, string body)
    {
        System.ArgumentNullException.ThrowIfNull(body);
        System.ArgumentNullException.ThrowIfNull(claim);
        var attribute = claim.StartsWith('[') ? claim : "";
        var inline = attribute.Length == 0 ? claim : "";
        return "using System; using SharpProof.Attributes; public static class C { " +
            "public static int State; public static int Writes; " +
            "private static void Mark(int value) { State = value; Writes++; } " +
            "private static int Helper(int value) => checked(value + 1); " +
            "private sealed class D : IDisposable { private readonly int _value; " +
            "public D(int value) { _value = value; } public void Dispose() => Mark(_value); } " +
            attribute + " public static int Target(int x) { " + inline + " " + body + " } }";
    }

    private static CSharpCompilation Compile(string source, bool contractsEnabled)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        ImmutableArray<string> symbols = contractsEnabled ? [Contract.ConditionalSymbol] : [];
        var tree = CSharpSyntaxTree.ParseText(source,
            new CSharpParseOptions(LanguageVersion.CSharp12, preprocessorSymbols: symbols), "/project/Subject.cs");
        return CSharpCompilation.Create("CompositionProbe_" + Guid.NewGuid().ToString("N"), [tree],
            TestMetadataReferences.WithSharpProof,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release, nullableContextOptions: NullableContextOptions.Enable));
    }

    private static ClrResult RunClr(string source, int[] inputs)
    {
        System.ArgumentNullException.ThrowIfNull(inputs, nameof(inputs));
        System.ArgumentNullException.ThrowIfNull(source);
        var compilation = Compile(source, contractsEnabled: false);
        var errors = compilation.GetDiagnostics().Where(d => d is null ? throw new System.ArgumentNullException(nameof(d)) : (d.Severity == DiagnosticSeverity.Error)).ToImmutableArray();
        if (errors.Length != 0)
        {
            return new([], "generated program did not compile: " + string.Join(" | ", errors.Select(error => error is null ? throw new System.ArgumentNullException(nameof(error)) : (error.ToString()))));
        }
        using var image = new MemoryStream();
        var emit = compilation.Emit(image);
        if (!emit.Success)
        {
            return new([], "generated program did not emit: " + string.Join(" | ", emit.Diagnostics));
        }
        var context = new AssemblyLoadContext("composition-probe", isCollectible: true);
        try
        {
            image.Position = 0;
            var type = context.LoadFromStream(image).GetType("C")!;
            var state = type.GetField("State")!;
            var writes = type.GetField("Writes")!;
            var method = type.GetMethod("Target", BindingFlags.Public | BindingFlags.Static)!;
            var runs = ImmutableList<Observation>.Empty;
            foreach (var input in inputs)
            {
                state.SetValue(null, 0);
                writes.SetValue(null, 0);
                int? value = null;
                string? exceptionName = null;
                try
                {
                    value = (int)method.Invoke(null, [input])!;
                }
                catch (TargetInvocationException exception)
                {
                    exceptionName = exception.InnerException!.GetType().FullName;
                }
                runs = runs.Add(new(input, value, exceptionName, (int)state.GetValue(null)!, (int)writes.GetValue(null)!));
            }
            return new([.. runs], null);
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

    private static string Literal(int value)
    {
        return value == int.MinValue ? "int.MinValue" : value == int.MaxValue ? "int.MaxValue" :
            value.ToString(CultureInfo.InvariantCulture);
    }
}

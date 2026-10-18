using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.CompilerArtifact;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test.LongFuzz;

// Each seed nests one or two exception/control-flow leaves in up to four
// control-flow wrappers (try/finally, filters, loops, switch, goto, lock,
// using, local functions), executes the body on the CLR for ground truth,
// then verifies one claim through the compiler artifact and the worker.
// A Proven claim that the CLR falsifies is a hit, as is any worker error or
// escaped exception.
[TestFixture]
[Explicit("Long-running fuzz campaign; select with TestCategory=LongFuzz.")]
[Category(LongFuzzSession.Category)]
[Order(1)]
[NonParallelizable]
public sealed class ControlFlowLongFuzzTests
{
    private const string Harness = "control-flow";

    // @@ is the nested body; %% marks a loop head, where the runtime copy
    // spends fuel so a nonterminating input stops instead of spinning.
    private static readonly string[] s_wrappers =
    [
        "try { @@ } finally { x++; }",
        "try { @@ } catch (System.DivideByZeroException) { x--; }",
        "try { @@ } catch (System.Exception e) when (e is System.DivideByZeroException) { x = 7; }",
        "try { @@ } catch (System.Exception) when (x++ > 2) { x = 7; }",
        "while (x < 5) { %% @@ x++; }",
        "for (int i = 0; i < 2; i++) { %% @@ }",
        "do { %% @@ x++; } while (x < 3);",
        "switch (x) { case 1: @@ break; case 2 when x > 0: x = 9; break; default: x = 2; break; }",
        "lock (new object()) { @@ }",
        "using (var d = new D()) { @@ }",
        "if (x > 1) { @@ } else { x = 4; }",
        "checked { @@ }",
        "while (true) { %% @@ break; }",
        "foreach (var c in \"ab\") { %% x += c; @@ }",
        "{ if (x == 4) goto L; @@ L: x++; }",
        "try { try { @@ } finally { x--; } } catch (System.DivideByZeroException) { x = 3; }",
        "try { @@ } catch (System.DivideByZeroException) { x--; } finally { x += 2; }",
        "try { @@ } catch (System.DivideByZeroException) { throw; }",
        "try { @@ } catch (System.DivideByZeroException) when (x > 100) { x = 1; }",
        "do { %% try { @@ } finally { x++; } } while (true);",
        "try { @@ } finally { checked { x = x + 1; } }",
        "try { @@ } catch (System.DivideByZeroException) { State = 1; }",
        "try { @@ } finally { if (x == 7) State = 2; }",
        "try { @@ } catch (System.Exception) when ((State = x) > 100) { x = 1; }",
        "switch (x) { case > 3: @@ break; case < 0: goto case 1; case 1: x = 2; break; }",
        "int L1() { @@ return x; } x = L1();",
        "try { @@ } catch (System.OverflowException) { } catch (System.IndexOutOfRangeException) { x = 1; }",
        "try { @@ } catch (System.ArithmeticException) { x = 1; }",
        "try { @@ } catch (System.SystemException) when (x != 3) { }",
        "try { @@ } catch { x = 2; }",
        "try { @@ } catch (System.InvalidOperationException) { x = 3; }",
        "try { @@ } catch (System.ArgumentException e) when (e.ParamName == null) { x = 3; }",
        "try { @@ } catch (System.DivideByZeroException) { throw new System.InvalidOperationException(); }",
        "try { @@ } catch (System.ArithmeticException e) { throw new System.ArgumentException(null, e); }",
        "try { @@ } catch (System.InvalidOperationException) { x = 10 / (x - x); }",
        "try { @@ } catch (System.ArgumentException) { throw new System.OverflowException(); }",
        "try { @@ } catch (System.Exception e) when (e is System.ArithmeticException) { throw new System.InvalidOperationException(); }",
        "try { @@ } finally { if (x == 8) throw new System.InvalidOperationException(); }",
    ];

    private const string BreakLeaf = "if (x == 3) break;";
    private const string ContinueLeaf = "if (x == 3) continue;";

    private static readonly string[] s_leaves =
    [
        "x = 10 / x;",
        BreakLeaf,
        ContinueLeaf,
        "if (x == 0) return 1;",
        "return 10 / (x - 1);",
        "if (x == 2) throw new System.InvalidOperationException();",
        "x = x == 0 ? 1 : x;",
        "x = (x > 0 ? x : throw new System.ArgumentException());",
        "x = N(x) ?? throw new System.ArgumentException();",
        "x = x switch { 0 => 1, > 5 when x < 9 => 10 / (x - 6), _ => x };",
        "if (x == 1) goto Out;",
        "x = checked(x * 2);",
        "if (x == 6) State = 3;",
        "x += State;",
        "if (x == 9) { State = 4; return 0; }",
        "x = new int[3][x & 7];",
        "x = checked((byte)x);",
        "x = \"abcd\"[x & 7];",
        "x = x % (x - 2);",
        "x = (x == 4 ? int.MinValue : x) / (x == 4 ? -1 : 1);",
        "switch (x) { case 1: x = 3; goto case 3; case 3: x = 10 / (x - 3); break; }",
        "x = N(x)!.Value;",
        "x = checked((int)(uint)x);",
        "x = (x & 1) == 0 ? x : 10 / (x - x);",
        "x = x is > 2 and < 5 ? throw new System.ArgumentOutOfRangeException() : x;",
        "x = (x == 7 ? null : (object)x) is int k ? k : throw new System.InvalidCastException();",
    ];

    // Hand-written filter-versus-finally ordering cases: a filter observes
    // state before inner finally blocks run.
    private static readonly string[] s_manual =
    [
        "try { try { x = 1; throw new System.InvalidOperationException(); } finally { x = 2; } } catch (System.Exception) when (x == 2) { return 100; }",
        "try { try { x = 1; throw new System.InvalidOperationException(); } finally { x = 2; } } catch (System.Exception) when (x == 1) { return 100; }",
        "try { try { x = 1; x = 10 / (x - 1); } finally { x = 2; } } catch (System.Exception) when (x == 2) { return 100; }",
        "try { try { x = 1; x = 10 / (x - 1); } finally { x = 2; } } catch (System.Exception) when (x == 1) { return 100; }",
        "int y = 0; try { try { y = 1; throw new System.InvalidOperationException(); } finally { y = 2; } } " +
            "catch (System.Exception) when (y == 1) { return y; }",
        "try { try { State = 1; throw new System.InvalidOperationException(); } finally { State = 2; } } " +
            "catch (System.Exception) when (State == 2) { return 100; }",
        "int y = 0; try { try { y = 1; throw new System.InvalidOperationException(); } " +
            "catch (System.Exception) when ((y = 5) == 0) { } finally { y = y + 1; } } catch (System.Exception) when (y == 5) { return y; }",
        "int y = 0; try { for (int i = 0; i < 2; i++) { %% try { y = i; if (i == 1) throw new System.InvalidOperationException(); } " +
            "finally { y = 9; } } } catch (System.Exception) when (y == 9) { return y; }",
        "int y = 0; try { Thrower(ref y); } catch (System.Exception) when (y == 2) { return y; }",
        "int y = 0; try { Thrower(ref y); } catch (System.Exception) when (y == 1) { return y; }",
    ];

    // The random wrappers rarely put a break or continue inside a cleanup
    // region, and the original disposable has no observable Dispose effect.
    // Every combination below is well-formed and runs on the full CLR input
    // grid, so cleanup order and exception precedence are exercised regularly.
    private static readonly (string Name, string Body)[] s_cleanupScopes =
    [
        ("finally-write", "try { @@ } finally { State = x; }"),
        ("finally-throw", "try { @@ } finally { State = x; if (x == 2) throw new System.InvalidOperationException(); }"),
        ("nested-finally", "try { try { @@ } finally { State = 1; } } finally { State = 2; }"),
        ("catch-after-finally", "try { try { @@ } finally { State = 1; } } catch (System.DivideByZeroException) { State = 2; x = 3; }"),
        ("filter-before-finally", "try { try { State = 1; @@ } finally { State = 2; } } catch (System.DivideByZeroException) when (State == 1) { State = 3; x = 4; }"),
        ("using-statement", "using (var d = new ObservableDisposal(x, false)) { @@ }"),
    ];

    private static readonly (string Name, string Body)[] s_cleanupExits =
    [
        ("normal", "x += 1;"),
        ("return", "if (x == 0) return x; x += 1;"),
        ("throw", "if (x == 1) throw new System.DivideByZeroException(); x += 1;"),
        ("break", "if (x == 0) break; x += i;"),
        ("continue", "if (x == 0) continue; x += i;"),
    ];

    // An outer filter runs before the inner using scope is unwound. The two
    // predicates give both a matching and a nonmatching catch on the grid.
    private static readonly (string Name, string Body)[] s_disposalCases =
    [
        ("using-declaration-return", "{ using var d = new ObservableDisposal(x, false); if (x == 0) return x; x++; }"),
        ("using-nested-dispose-order", "{ using var first = new ObservableDisposal(1, false); using var second = new ObservableDisposal(x, false); x++; }"),
        ("using-throw-precedence", "using (var d = new ObservableDisposal(x, x == 2)) { if (x == 1) throw new System.DivideByZeroException(); x++; }"),
        ("filter-before-dispose-match", "try { using (var d = new ObservableDisposal(2, false)) { State = 1; throw new System.InvalidOperationException(); } } catch (System.Exception) when (State == 1) { x = 5; }"),
        ("filter-before-dispose-miss", "try { using (var d = new ObservableDisposal(2, false)) { State = 1; throw new System.InvalidOperationException(); } } catch (System.Exception) when (State == 2) { x = 5; }"),
    ];

    private static readonly string[] s_modes =
        ["dnt-int", "dnt-void", "dnt-long", "dnt-obj", "dnt-bool", "thr-int", "thr-eff", "pure-int", "ens-int"];

    private static readonly string[] s_tails = [" Out: return x;", " return x;"];

    private static readonly int[] s_inputs = [int.MinValue, -3, -2, -1, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, int.MaxValue - 1, int.MaxValue];

    private const string Prelude =
        "using SharpProof.Attributes; public sealed class D : System.IDisposable { public void Dispose() { } } " +
        "public sealed class ObservableDisposal : System.IDisposable { private readonly int _value; private readonly bool _throws; " +
        "public ObservableDisposal(int value, bool throws) { _value = value; _throws = throws; } " +
        "public void Dispose() { C.State = _value; if (_throws) throw new System.InvalidOperationException(); } } " +
        "public static class C { STATE " +
        "private static int? N(int x) => x == 5 ? null : x; " +
        "private static void Thrower(ref int y) { try { y = 1; throw new System.InvalidOperationException(); } finally { y = 2; } } ";

    private const string VerifiedState = "public static int State; ";

    private const string RuntimeState =
        "private static int s_state; public static int StateWrites; public static int LockEntries; " +
        "public static int State { get { return s_state; } set { s_state = value; StateWrites++; } } " +
        "public static class R { public static volatile bool Exhausted; [System.ThreadStatic] private static int t_fuel; " +
        "public static object TrackLock(object value) { LockEntries++; return value; } " +
        "public static void F() { if (++t_fuel > 100000) { Exhausted = true; throw new System.OperationCanceledException(); } } } ";

    [Test]
    public async Task GeneratedControlFlowClaimsAgreeWithRuntime()
    {
        var session = new LongFuzzSession(Harness, harnessIndex: 2, harnessCount: 6);
        foreach (var seed in session.Seeds())
        {
            var random = new Random(seed);
            var mode = s_modes[random.Next(s_modes.Length)];
            var generated = Body(seed, random);
            session.Count("mode:" + mode);
            session.Count("selected:" + generated.Family);
            try
            {
                await RunCase(session, seed, mode, generated);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                session.RecordHit(new LongFuzzHit("HarnessException", LongFuzzHit.Error, seed, mode, "", "", "", exception.ToString(), generated.Body));
            }
        }
        var summary = session.Finish();
        Assert.That(session.Cases, Is.Not.Zero, "The campaign must run at least one case: " + summary);
    }

    private sealed record GeneratedBody(string Family, string Body)
    {
        public string Family { get; init; } = Family ?? throw new System.ArgumentNullException(nameof(Family));
        public string Body { get; init; } = Body ?? throw new System.ArgumentNullException(nameof(Body));
    }

    internal static CSharpCompilation CompileCandidateForSeed(int seed)
    {
        var random = new Random(seed);
        _ = random.Next(s_modes.Length);
        var generated = Body(seed, random);
        return Compile(Source(VerifiedState, "public static int Target(int x) { ",
            Verified(generated.Body + s_tails[0])));
    }

    private static GeneratedBody Body(int seed, Random random)
    {
        if (seed % 3 == 0)
        {
            var combinations = s_cleanupScopes.Length * s_cleanupExits.Length;
            var index = (seed / 3) % (combinations + s_disposalCases.Length);
            if (index >= combinations)
            {
                var scenario = s_disposalCases[index - combinations];
                return new GeneratedBody(scenario.Name, scenario.Body);
            }
            var scope = s_cleanupScopes[index / s_cleanupExits.Length];
            var exit = s_cleanupExits[index % s_cleanupExits.Length];
            var cleanupBody = scope.Body.Replace("@@", exit.Body, StringComparison.Ordinal);
            if (exit.Name is "break" or "continue")
            {
                cleanupBody = "for (int i = 0; i < 2; i++) { %% " + cleanupBody + " }";
            }
            return new GeneratedBody("cleanup:" + scope.Name + ":" + exit.Name, cleanupBody);
        }
        if (random.Next(20) == 0)
        {
            return new GeneratedBody("legacy-manual", s_manual[random.Next(s_manual.Length)]);
        }
        var firstLeaf = s_leaves[random.Next(s_leaves.Length)];
        var secondLeaf = random.Next(2) == 0 ? s_leaves[random.Next(s_leaves.Length)] : null;
        var depth = 1 + random.Next(4);
        var wrappers = new int[depth];
        for (var level = 0; level < depth; level++)
        {
            wrappers[level] = random.Next(s_wrappers.Length);
        }

        var canBreak = false;
        var canContinue = false;
        foreach (var wrapper in wrappers)
        {
            // A local function cannot break or continue an enclosing wrapper.
            if (wrapper == 25)
            {
                break;
            }
            if (wrapper is 4 or 5 or 6 or 12 or 13 or 19)
            {
                canBreak = true;
                canContinue = true;
            }
            else if (wrapper is 7 or 24)
            {
                canBreak = true;
            }
        }

        firstLeaf = ScopeExit(firstLeaf, canBreak, canContinue);
        secondLeaf = secondLeaf is null ? null : ScopeExit(secondLeaf, canBreak, canContinue);
        var body = secondLeaf is null ? firstLeaf : firstLeaf + " " + secondLeaf;
        foreach (var wrapper in wrappers)
        {
            body = s_wrappers[wrapper].Replace("@@", body, StringComparison.Ordinal);
        }
        return new GeneratedBody("legacy-random", body);
    }

    private static string ScopeExit(string leaf, bool canBreak, bool canContinue)
    {
        if ((leaf != BreakLeaf || canBreak) && (leaf != ContinueLeaf || canContinue))
        {
            return leaf;
        }

        return "for (int __flowExit = 0; __flowExit < 1; __flowExit++) { %% " + leaf + " x++; }";
    }

    private static async Task RunCase(LongFuzzSession session, int seed, string mode, GeneratedBody generated)
    {
        System.ArgumentNullException.ThrowIfNull(generated);
        System.ArgumentNullException.ThrowIfNull(mode);
        System.ArgumentNullException.ThrowIfNull(session);
        var template = generated.Body;
        var parts = mode.Split('-');
        var kind = parts[0];
        var type = parts[1];
        string? methodBody = null;
        foreach (var tail in s_tails)
        {
            var candidate = template + tail;
            if (!HasErrors(Compile(Source(VerifiedState, "public static int Target(int x) { ", Verified(candidate)))))
            {
                methodBody = candidate;
                break;
            }
        }
        if (methodBody is null)
        {
            session.Count("compile-error");
            return;
        }
        session.Count("compiled:" + generated.Family);
        methodBody = Retype(methodBody, type == "eff" ? "int" : type);
        var signature = "public static " + TypeName(type == "eff" ? "int" : type) + " Target(int x) { ";
        var runtimeCompilation = Compile(Source(RuntimeState, signature, Runtime(methodBody)));
        if (HasErrors(runtimeCompilation))
        {
            session.Count("retype-error");
            return;
        }
        var runs = Run(runtimeCompilation);
        if (runs is null)
        {
            session.Count("nonterminating");
            return;
        }
        session.Count("oracle:" + generated.Family);
        string header;
        string? truthViolation;
        switch (kind)
        {
            case "dnt":
                {
                    header = "[DoesNotThrow] " + signature;
                    var run = runs.FirstOrDefault(r => r is null ? throw new System.ArgumentNullException(nameof(r)) : (r.ExceptionType is not null));
                    truthViolation = run is null ? null : "threw " + run.ExceptionType!.Name + " at x=" + run.X;
                    break;
                }
            case "thr":
                {
                    header = type == "eff"
                        ? "[EffectContract(SharpProofEffect.ReadsStaticState | SharpProofEffect.WritesStaticState | " +
                            "SharpProofEffect.Allocates | SharpProofEffect.Throws, ThrownExceptions = new[] { " +
                            "typeof(System.ArgumentException), typeof(System.InvalidOperationException) })] " + signature
                        : "[AllowedExceptions(typeof(System.ArgumentException), typeof(System.InvalidOperationException))] " + signature;
                    var run = runs.FirstOrDefault(r => r is null ? throw new System.ArgumentNullException(nameof(r)) : (r.ExceptionType is not null &&
                        !typeof(ArgumentException).IsAssignableFrom(r.ExceptionType) &&
                        !typeof(InvalidOperationException).IsAssignableFrom(r.ExceptionType)));
                    truthViolation = run is null ? null : "threw " + run.ExceptionType!.Name + " at x=" + run.X;
                    break;
                }
            case "pure":
                {
                    header = "[EnforcePure] " + signature;
                    var run = runs.FirstOrDefault(r => r is null ? throw new System.ArgumentNullException(nameof(r)) : (r.StateWrites != 0 || r.LockEntries != 0));
                    truthViolation = run is null ? null : run.StateWrites != 0
                        ? "wrote State " + run.StateWrites + " time(s), final " + run.State + ", at x=" + run.X
                        : "entered lock " + run.LockEntries + " time(s) at x=" + run.X;
                    break;
                }
            default:
                {
                    var run = runs.FirstOrDefault(r => r is null ? throw new System.ArgumentNullException(nameof(r)) : (r.ExceptionType is null));
                    if (run is null)
                    {
                        session.Count("no-normal-run");
                        return;
                    }
                    header = signature + "Contract.Ensures(Contract.Old(x) != " + Literal(run.X) + " || Contract.Result<int>() != " +
                        Literal(run.Result) + "); ";
                    truthViolation = "returns " + run.Result + " at x=" + run.X;
                    break;
                }
        }
        var source = Source(VerifiedState, header, Verified(methodBody));
        var claim = header.Trim();
        var runtime = truthViolation ?? "no violation on " + runs.Count + " inputs";
        WorkerClaimResult claimResult;
        try
        {
            var final = Compile(source);
            var discovery = new ClaimManifestBuilder(final).Build();
            var artifact = CompilerManifestArtifactProducer.Create(final, "/project", "net9.0", WorkerFeatureSet.All, discovery,
                WorkerBudgets.DefaultMaximumExpressionDepth, TestContext.CurrentContext.CancellationToken);
            using var project = new ShadowTestProject(artifact);
            using var worker = SharpProofWorker.Create(project.Request.Budgets);
            var response = await worker.VerifyAsync(project.Request, project.Snapshot, TestContext.CurrentContext.CancellationToken);
            if (response.Errors.Length > 0)
            {
                session.RecordHit(new LongFuzzHit("WorkerErrors", LongFuzzHit.Error, seed, claim, "", "", runtime,
                    string.Join(" | ", response.Errors.Select(error => error is null ? throw new System.ArgumentNullException(nameof(error)) : (error.Code + ":" + error.Message))), source));
                return;
            }
            if (response.ClaimResults.Length != 1)
            {
                session.Count("claims:" + response.ClaimResults.Length);
                return;
            }
            claimResult = response.ClaimResults[0];
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            session.RecordHit(new LongFuzzHit("WorkerException", LongFuzzHit.Error, seed, claim, "", "", runtime,
                exception.ToString(), source));
            return;
        }
        session.Count("outcome:" + claimResult.Outcome + (claimResult.Outcome == WorkerClaimOutcome.Unknown ? ":" + claimResult.Reason : ""));
        session.Count("verdict:" + generated.Family + ":" + claimResult.Outcome);
        if (claimResult.Outcome == WorkerClaimOutcome.Proven && truthViolation is not null)
        {
            session.RecordHit(new LongFuzzHit("FalseProven", LongFuzzHit.Error, seed, claim, claimResult.Outcome.ToString(),
                claimResult.Reason.ToString(), runtime, "The CLR falsifies the proven claim: " + truthViolation, source));
        }
        else if (claimResult.Outcome == WorkerClaimOutcome.Refuted && truthViolation is null)
        {
            // The input grid is sparse (17 values), so a refutation without
            // a grid witness is counted for review rather than reported.
            session.Count("refuted-unwitnessed");
        }
    }

    private static string Verified(string body)
    {
        System.ArgumentNullException.ThrowIfNull(body);
        return body.Replace("%% ", "", StringComparison.Ordinal);
    }

    private static string Runtime(string body)
    {
        // The generated lock wrapper always locks a fresh object. Count a
        // reached lock only in the CLR copy; verification keeps the original.
        System.ArgumentNullException.ThrowIfNull(body);
        return body.Replace("%%", "R.F();", StringComparison.Ordinal)
            .Replace("lock (new object())", "lock (R.TrackLock(new object()))", StringComparison.Ordinal);
    }

    private static string Source(string state, string header, string body)
    {
        System.ArgumentNullException.ThrowIfNull(body);
        System.ArgumentNullException.ThrowIfNull(header);
        System.ArgumentNullException.ThrowIfNull(state);
        return Prelude.Replace("STATE", state, StringComparison.Ordinal) + header + body + " } }";
    }

    private static string TypeName(string type)
    {
        System.ArgumentNullException.ThrowIfNull(type);
        return type switch
        {
            "void" => "void",
            "long" => "long",
            "obj" => "object",
            "bool" => "bool",
            _ => "int"
        };
    }

    private static string Retype(string body, string type)
    {
        System.ArgumentNullException.ThrowIfNull(body);
        if (type is not ("void" or "bool"))
        {
            return body;
        }

        // Keep the generated source positions while making loop-fuel markers
        // parseable. Only returns from Target need a different return type;
        // nested local functions and lambdas keep their own return types.
        const string prefix = "class C { static int Target(int x) { ";
        var parseable = body.Replace("%%", "; ", StringComparison.Ordinal);
        var root = CSharpSyntaxTree.ParseText(prefix + parseable + " } }",
            new CSharpParseOptions(LanguageVersion.CSharp12)).GetRoot();
        var returns = root.DescendantNodes().OfType<ReturnStatementSyntax>()
            .Where(statement => statement is null ? throw new System.ArgumentNullException(nameof(statement)) : (statement.Expression is not null &&
                !statement.Ancestors().Any(ancestor => ancestor is null ? throw new System.ArgumentNullException(nameof(ancestor)) : (ancestor is LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax))))
            .OrderByDescending(statement => statement is null ? throw new System.ArgumentNullException(nameof(statement)) : (statement.SpanStart));
        foreach (var statement in returns)
        {
            var expression = statement.Expression!;
            var value = body.Substring(expression.SpanStart - prefix.Length, expression.Span.Length);
            var replacement = type == "void"
                ? "{ _ = " + value + "; return; }"
                : "return (" + value + ") != 0;";
            var start = statement.SpanStart - prefix.Length;
            body = body.Remove(start, statement.Span.Length).Insert(start, replacement);
        }

        return body;
    }

    private static string Literal(int value)
    {
        return value == int.MinValue ? "int.MinValue" : value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static bool HasErrors(CSharpCompilation compilation)
    {
        System.ArgumentNullException.ThrowIfNull(compilation);
        return compilation.GetDiagnostics().Any(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error));
    }

    private static CSharpCompilation Compile(string source)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        var tree = CSharpSyntaxTree.ParseText(source,
            new CSharpParseOptions(LanguageVersion.CSharp12, preprocessorSymbols: [Contract.ConditionalSymbol]));
        return CSharpCompilation.Create("P" + Guid.NewGuid().ToString("N"), [tree], TestMetadataReferences.WithSharpProof,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
    }

    private sealed record RunResult(int X, Type? ExceptionType, int Result, int State, int StateWrites, int LockEntries);

    // Returns null when an input does not terminate within the loop fuel.
    private static List<RunResult>? Run(CSharpCompilation compilation)
    {
        using var stream = new MemoryStream();
        if (!compilation.Emit(stream).Success)
        {
            return [];
        }
        var context = new AssemblyLoadContext("longfuzz-flow", isCollectible: true);
        try
        {
            stream.Position = 0;
            var assembly = context.LoadFromStream(stream);
            var type = assembly.GetType("C")!;
            var method = type.GetMethod("Target")!;
            var state = type.GetProperty("State")!;
            var writes = type.GetField("StateWrites")!;
            var locks = type.GetField("LockEntries")!;
            var exhausted = type.GetNestedType("R")!.GetField("Exhausted")!;
            var results = new List<RunResult>();
            foreach (var x in s_inputs)
            {
                Type? exceptionType = null;
                var result = 0;
                state.SetValue(null, 0);
                writes.SetValue(null, 0);
                locks.SetValue(null, 0);
                var thread = new Thread(() =>
                {
                    try
                    {
                        var value = method.Invoke(null, [x]);
                        result = value is int number ? number : 0;
                    }
                    catch (TargetInvocationException exception)
                    {
                        exceptionType = exception.InnerException!.GetType();
                    }
                }, 1 << 20)
                { IsBackground = true };
                thread.Start();
                var waited = 0;
                while (!thread.Join(10))
                {
                    waited += 10;
                    if ((bool)exhausted.GetValue(null)! || waited > 30000)
                    {
                        // Loop fuel was exhausted, so this candidate does not terminate.
                        return null;
                    }
                }
                if ((bool)exhausted.GetValue(null)!)
                {
                    return null;
                }
                var lockCount = (int)locks.GetValue(null)!;
                results.Add(new RunResult(x, exceptionType, result, (int)state.GetValue(null)!,
                    (int)writes.GetValue(null)!, lockCount));
            }
            return results;
        }
        finally
        {
            context.Unload();
        }
    }
}

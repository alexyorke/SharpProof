using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.CompilerArtifact;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test.LongFuzz;

// Each seed selects one compiler-valid contract case. The compiled CLR
// determines the admitted input domain and checks the generated body on a
// bounded grid before the Worker verdict is accepted.
[TestFixture]
[Explicit("Long-running fuzz campaign; select with TestCategory=LongFuzz.")]
[Category(LongFuzzSession.Category)]
[Order(4)]
[NonParallelizable]
public sealed class ContractMatrixLongFuzzTests
{
    private const string Harness = "contract-matrix";
    private static readonly int[] s_values = [-4, -3, -2, -1, 0, 1, 2, 3, 4];
    private static readonly string[] s_specMaxExpressions =
    [
        "Math.Max(value, value + 2)",
        "Math.Max(val2: value + 2, val1: value)",
        "Math.Max(val2: ++value, val1: value *= 2)",
        "Math.Max(value *= 2, ++value)"
    ];
    private static readonly int[][] s_specMaxMaximum = [[0, 2], [0, 2], [-1, 2], [-3, 1]];
    private static readonly int[][] s_specMaxValue = [[-2, 0], [-2, 0], [-2, 2], [-3, 1]];

    private sealed record Domain(string Name, string[] Callee, string[] WiderCaller)
    {
        public string Name { get; init; } = Name ?? throw new System.ArgumentNullException(nameof(Name));
        public string[] Callee { get; init; } = Callee ?? throw new System.ArgumentNullException(nameof(Callee));
        public string[] WiderCaller { get; init; } = WiderCaller ?? throw new System.ArgumentNullException(nameof(WiderCaller));
    }
    private sealed record GeneratedCase(string Name, string Source, bool Call, int Variant, int Bias,
        int Rewrite, bool Joint = false)
    {
        public string Name { get; init; } = Name ?? throw new System.ArgumentNullException(nameof(Name));
        public string Source { get; init; } = Source ?? throw new System.ArgumentNullException(nameof(Source));
    }
    private sealed record Observation(int Admitted, int Excluded, int ExcludedClaimViolations,
        int CalleeAdmitted, int CalleeRejected, int GuardedSkipped, int Violations,
        string? Witness, bool BodyMismatch);
    private sealed record SpecAbsCase(string Name, string Source, int Anchor, int ExpectedReturn,
        int ExpectedState, bool FalseClaim)
    {
        public string Name { get; init; } = Name ?? throw new System.ArgumentNullException(nameof(Name));
        public string Source { get; init; } = Source ?? throw new System.ArgumentNullException(nameof(Source));
    }
    private sealed record SpecMaxCase(string Name, string Source, string ProbeSource, string StateSource,
        int Anchor, int ExpectedMaximum, int ExpectedValue, bool FalseClaim)
    {
        public string Name { get; init; } = Name ?? throw new System.ArgumentNullException(nameof(Name));
        public string Source { get; init; } = Source ?? throw new System.ArgumentNullException(nameof(Source));
        public string ProbeSource { get; init; } = ProbeSource ?? throw new System.ArgumentNullException(nameof(ProbeSource));
        public string StateSource { get; init; } = StateSource ?? throw new System.ArgumentNullException(nameof(StateSource));
    }

    private static readonly Domain[] s_domains =
    [
        new("interval", ["x >= -2", "x <= 2"], ["x >= -3", "x <= 3"]),
        new("implication", ["x >= -2", "x < 1 || y >= 1"], ["x >= -2"]),
        new("excluded-point", ["x >= -3", "x <= 3", "x != 0"], ["x >= -3", "x <= 3"]),
        new("dependent-boundary", ["x >= -2", "x <= 2", "x < 0 || y >= 0"],
            ["x >= -2", "x <= 2"])
    ];
    private static readonly string[] s_jointDomains =
    [
        "x == 0 || x == 1",
        "x >= 0 && x <= 1",
        "x > -1 && x <= 1",
        "x >= 0 && x < 2"
    ];

    [Test]
    public async Task GeneratedContractsAgreeWithClrAdmission()
    {
        var session = new LongFuzzSession(Harness, harnessIndex: 5, harnessCount: 6);
        foreach (var seed in session.Seeds())
        {
            // Keep every existing seed-to-case mapping. Math.Abs adds one case beside
            // each Math.Max slot; two 64-seed cycles visit all 16 variants of both.
            var offset = (int)(unchecked((uint)seed) % 64u);
            var slot = offset < 48 ? offset * 37 % 48 : offset;
            if (slot >= 56)
            {
                var specMax = BuildSpecMaxCase(seed, slot);
                session.Count("selected:" + specMax.Name);
                try
                {
                    await RunSpecMaxCase(session, seed, specMax);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    session.RecordHit(new LongFuzzHit("HarnessException", LongFuzzHit.Error, seed, specMax.Name,
                        "", "", "", exception.ToString(), specMax.Source));
                }
                var specAbs = BuildSpecAbsCase(seed, slot);
                session.Count("selected:" + specAbs.Name);
                try
                {
                    await RunSpecAbsCase(session, seed, specAbs);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    session.RecordHit(new LongFuzzHit("HarnessException", LongFuzzHit.Error, seed, specAbs.Name,
                        "", "", "", exception.ToString(), specAbs.Source));
                }
                continue;
            }
            var rewrite = (int)((unchecked((uint)seed) / 64u) % 4u);
            var item = BuildCase(slot, rewrite);
            session.Count("selected:" + item.Name);
            session.Count("rewrite:" + rewrite);
            try
            {
                await RunCase(session, seed, item);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                session.RecordHit(new LongFuzzHit("HarnessException", LongFuzzHit.Error, seed, item.Name,
                    "", "", "", exception.ToString(), item.Source));
            }
            if (slot == 55)
            {
                // Sparse supplemental syntax path. The original case still runs for this seed.
                var syntaxCaseId = (int)((((uint)seed / 64u) * 5u) % 16u);
                var source = "";
                session.Count("selected:syntax-mutation:" + syntaxCaseId);
                try
                {
                    source = RoslynSyntaxMutationRegressionTests.SourceForCase(syntaxCaseId);
                    await RunSyntaxMutationCase(session, seed, syntaxCaseId);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    session.RecordHit(new LongFuzzHit("HarnessException", LongFuzzHit.Error, seed,
                        "syntax-mutation/" + syntaxCaseId, "", "", "", exception.ToString(), source));
                }
            }
        }
        var summary = session.Finish();
        Assert.That(session.Cases, Is.Not.Zero, "The campaign must run at least one case: " + summary);
    }

    private static async Task RunSyntaxMutationCase(LongFuzzSession session, int seed, int caseId)
    {
        System.ArgumentNullException.ThrowIfNull(session);
        var observed = await RoslynSyntaxMutationRegressionTests.ProbeCase(caseId);
        session.Count("compiled:syntax-mutation");
        session.Count("clr-validated:syntax-mutation");
        if (observed.FalseClaim)
        { session.Count("clr-witness:syntax-mutation"); }
        session.Count("worker:syntax-mutation");
        session.Count("verdict:syntax-mutation:" + observed.Outcome);
        var witness = "x=" + observed.Anchor + ",variant=" + observed.Variant;
        var name = "syntax-mutation/" + caseId;
        if (observed.FalseClaim && observed.Outcome == WorkerClaimOutcome.Proven)
        {
            session.RecordHit(new LongFuzzHit("FalseProven", LongFuzzHit.Error, seed, name,
                observed.Outcome.ToString(), observed.Reason.ToString(), witness,
                "The admitted CLR input falsifies the Proven postcondition.", observed.Source));
        }
        else if (!observed.FalseClaim && observed.Outcome == WorkerClaimOutcome.Refuted)
        {
            session.RecordHit(new LongFuzzHit("RefutedNotObserved", LongFuzzHit.Error, seed, name,
                observed.Outcome.ToString(), observed.Reason.ToString(), witness,
                "The only admitted CLR input satisfies the postcondition.", observed.Source));
        }
        else if (observed.Outcome != (observed.FalseClaim
            ? WorkerClaimOutcome.Refuted : WorkerClaimOutcome.Proven) ||
            observed.Reason != WorkerClaimReason.None)
        {
            session.RecordHit(new LongFuzzHit("ConcreteVerdictRegression", LongFuzzHit.Error, seed, name,
                observed.Outcome.ToString(), observed.Reason.ToString(), witness,
                "This compiler-valid CLR-oracled syntax case had a concrete Worker verdict.",
                observed.Source));
        }
    }

    private static SpecAbsCase BuildSpecAbsCase(int seed, int slot)
    {
        var offset = slot - 56;
        var broadCatch = (offset & 1) != 0;
        var falseClaim = (offset & 2) != 0;
        var secondAnchor = (offset & 4) != 0;
        var oddCycle = ((unchecked((uint)seed) / 64u) & 1u) != 0;
        var anchor = oddCycle ? (secondAnchor ? 1 : 0) : (secondAnchor ? int.MinValue : -1);
        var expectedReturn = anchor == int.MinValue ? (broadCatch ? 9 : 7) : Math.Abs(anchor);
        var expectedState = anchor == int.MinValue ? 2 : 1;
        var name = "bcl-abs/" + anchor + "/" + (broadCatch ? "exception" : "overflow") +
            "/" + (falseClaim ? "false" : "true");
        return new(name, SpecAbsSource(anchor, broadCatch, falseClaim),
            anchor, expectedReturn, expectedState, falseClaim);
    }

    private static string SpecAbsSource(int anchor, bool broadCatch, bool falseClaim)
    {
        var catchType = broadCatch ? "Exception" : "OverflowException";
        var sentinel = broadCatch ? 9 : 7;
        var predicate = falseClaim ? "Contract.Result<int>() < 0" : "Contract.Result<int>() >= 0";
        return "using System; using SharpProof.Attributes; public static class C { " +
            "public static int State; public static int Target(int x) { " +
            "Contract.Requires(x == " + anchor + "); Contract.Ensures(" + predicate + "); " +
            "State = 0; try { int result = Math.Abs(x); State = 1; return result; } " +
            "catch (" + catchType + ") { State = 2; return " + sentinel + "; } } }";
    }

    internal static async Task<(int Return, int State, bool FalseClaim,
        WorkerClaimOutcome Outcome, WorkerClaimReason Reason)> ProbeSpecAbsSeed(int seed)
    {
        var slot = (int)(unchecked((uint)seed) % 64u);
        if (slot < 56)
        {
            throw new ArgumentOutOfRangeException(nameof(seed), "The seed does not select a BCL Abs slot.");
        }
        var item = BuildSpecAbsCase(seed, slot);
        var observed = await ProbeSpecAbsCase(item);
        return (observed.Return, observed.State, item.FalseClaim,
            observed.Claim.Outcome, observed.Claim.Reason);
    }

    private static async Task RunSpecAbsCase(LongFuzzSession session, int seed, SpecAbsCase item)
    {
        System.ArgumentNullException.ThrowIfNull(item);
        System.ArgumentNullException.ThrowIfNull(session);
        var observed = await ProbeSpecAbsCase(item);
        session.Count("compiled:spec-abs");
        session.Count("clr-validated:spec-abs");
        if (item.FalseClaim)
        {
            session.Count("clr-witness:spec-abs");
        }
        var claim = observed.Claim;
        session.Count("worker:spec-abs");
        session.Count("verdict:" + item.Name + ":" + claim.Outcome);
        if (claim.Outcome == WorkerClaimOutcome.Unknown)
        {
            session.Count("unknown-reason:spec-abs:" + claim.Reason);
            session.RecordHit(new LongFuzzHit("ConcreteVerdictRegression", LongFuzzHit.Error,
                seed, item.Name, claim.Outcome.ToString(), claim.Reason.ToString(),
                "x=" + item.Anchor + ",return=" + observed.Return + ",state=" + observed.State,
                "The pinned compiler-valid default-catalog Math.Abs case had a concrete verdict.",
                item.Source));
        }
        else if (item.FalseClaim && claim.Outcome == WorkerClaimOutcome.Proven)
        {
            session.RecordHit(new LongFuzzHit("FalseProven", LongFuzzHit.Error,
                seed, item.Name, claim.Outcome.ToString(), claim.Reason.ToString(),
                "x=" + item.Anchor + ",return=" + observed.Return + ",state=" + observed.State,
                "The admitted CLR witness falsifies the Proven postcondition.", item.Source));
        }
        else if (!item.FalseClaim && claim.Outcome == WorkerClaimOutcome.Refuted)
        {
            session.RecordHit(new LongFuzzHit("RefutedNotObserved", LongFuzzHit.Error,
                seed, item.Name, claim.Outcome.ToString(), claim.Reason.ToString(),
                "x=" + item.Anchor + ",return=" + observed.Return + ",state=" + observed.State,
                "The only admitted input satisfies the postcondition.", item.Source));
        }
    }

    private static async Task<(int Return, int State, WorkerClaimResult Claim)> ProbeSpecAbsCase(
        SpecAbsCase item)
    {
        var observed = RunSpecAbsClr(item.Source, item.Anchor);
        if (observed.Return != item.ExpectedReturn || observed.State != item.ExpectedState)
        {
            throw new InvalidOperationException("Math.Abs CLR return/exception-state oracle failed for " +
                item.Name + ": return=" + observed.Return + ",state=" + observed.State);
        }
        var claim = await Verify(item.Source, exactTargetPostcondition: true);
        return (observed.Return, observed.State, claim);
    }

    private static (int Return, int State) RunSpecAbsClr(string source, int input)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        var compilation = Compile(source, contractsEnabled: false);
        var errors = compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)).ToArray();
        if (errors.Length != 0)
        {
            throw new InvalidOperationException("Math.Abs CLR program did not compile: " +
                string.Join(" | ", errors.Select(error => error is null ? throw new System.ArgumentNullException(nameof(error)) : (error.ToString()))));
        }
        using var image = new MemoryStream();
        var emission = compilation.Emit(image);
        if (!emission.Success)
        {
            throw new InvalidOperationException("Math.Abs CLR program did not emit: " +
                string.Join(" | ", emission.Diagnostics));
        }
        image.Position = 0;
        var context = new AssemblyLoadContext("contract-matrix-spec-abs", isCollectible: true);
        try
        {
            var type = context.LoadFromStream(image).GetType("C")!;
            var target = type.GetMethod("Target", BindingFlags.Public | BindingFlags.Static)!;
            int value;
            try
            {
                value = (int)target.Invoke(null, [input])!;
            }
            catch (TargetInvocationException exception)
            {
                throw new InvalidOperationException("Math.Abs CLR oracle unexpectedly threw.",
                    exception.InnerException);
            }
            var state = (int)type.GetField("State", BindingFlags.Public | BindingFlags.Static)!
                .GetValue(null)!;
            return (value, state);
        }
        finally
        {
            context.Unload();
        }
    }

    private static SpecMaxCase BuildSpecMaxCase(int seed, int slot)
    {
        var offset = slot - 56;
        var shape = offset / 2;
        var falseClaim = (offset & 1) != 0;
        var anchor = ((unchecked((uint)seed) / 64u) & 1u) == 0 ? -2 : 0;
        var anchorIndex = anchor == -2 ? 0 : 1;
        var maximum = s_specMaxMaximum[shape][anchorIndex];
        var value = s_specMaxValue[shape][anchorIndex];
        var expected = unchecked(maximum * 16 + value);
        var name = "spec-max/" + shape + "/" + anchor + "/" + (falseClaim ? "false" : "true");
        var expression = s_specMaxExpressions[shape];
        return new(name, SpecMaxSource(anchor, expression, expected + (falseClaim ? 1 : 0)),
            SpecMaxSource(anchor, expression, null), SpecMaxSource(anchor, expression, null, stateProbe: true),
            anchor, maximum, value, falseClaim);
    }

    private static string SpecMaxSource(int anchor, string expression, int? claimedValue, bool stateProbe = false)
    {
        System.ArgumentNullException.ThrowIfNull(expression);
        var claim = claimedValue.HasValue
            ? "Contract.Ensures(Contract.Result<int>() == " +
              claimedValue.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "); "
            : "";
        return "using System; using SharpProof.Attributes; public static class C { " +
            "public static " + (stateProbe ? "int[]" : "int") + " Target(int x) { Contract.Requires(x == " +
            anchor.ToString(System.Globalization.CultureInfo.InvariantCulture) + "); " + claim +
            "int value = x; int maximum = " + expression + "; " +
            (stateProbe ? "return new[] { maximum, value }; } }"
                : "return unchecked(maximum * 16 + value); } }");
    }

    private static async Task RunSpecMaxCase(LongFuzzSession session, int seed, SpecMaxCase item)
    {
        System.ArgumentNullException.ThrowIfNull(session);
        var state = (int[])RunSpecMaxClr(item.StateSource, item.Anchor);
        var observed = (int)RunSpecMaxClr(item.ProbeSource, item.Anchor);
        if (state.Length != 2 || state[0] != item.ExpectedMaximum || state[1] != item.ExpectedValue ||
            observed != unchecked(state[0] * 16 + state[1]) ||
            (int)RunSpecMaxClr(item.Source, item.Anchor) != observed)
        {
            throw new InvalidOperationException("CLR source-order/state oracle failed for " + item.Name);
        }
        session.Count("compiled:spec-max");
        session.Count("clr-validated:spec-max");
        if (item.FalseClaim)
        {
            session.Count("clr-witness:spec-max");
        }
        var claim = await Verify(item.Source, scalarPack: true);
        session.Count("worker:spec-max");
        session.Count("verdict:" + item.Name + ":" + claim.Outcome);
        if (claim.Outcome == WorkerClaimOutcome.Unknown)
        {
            session.Count("unknown-reason:spec-max:" + claim.Reason);
            session.RecordHit(new LongFuzzHit("ConcreteVerdictRegression", LongFuzzHit.Error, seed, item.Name,
                claim.Outcome.ToString(), claim.Reason.ToString(),
                "x=" + item.Anchor + ",return=" + observed,
                "The pinned compiler-valid specification-pack case previously had a concrete verdict.",
                item.Source));
        }
        else if (item.FalseClaim && claim.Outcome == WorkerClaimOutcome.Proven)
        {
            session.RecordHit(new LongFuzzHit("FalseProven", LongFuzzHit.Error, seed, item.Name,
                claim.Outcome.ToString(), claim.Reason.ToString(),
                "x=" + item.Anchor + ",return=" + observed + ",claimed=" + (observed + 1),
                "The admitted CLR witness falsifies the Proven postcondition.", item.Source));
        }
        else if (!item.FalseClaim && claim.Outcome == WorkerClaimOutcome.Refuted)
        {
            session.RecordHit(new LongFuzzHit("RefutedNotObserved", LongFuzzHit.Error, seed, item.Name,
                claim.Outcome.ToString(), claim.Reason.ToString(),
                "x=" + item.Anchor + ",return=" + observed,
                "The complete admitted domain contains only the pinned anchor.", item.Source));
        }
    }

    private static object RunSpecMaxClr(string source, int input)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        var compilation = Compile(source, contractsEnabled: false);
        var errors = compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)).ToArray();
        if (errors.Length != 0)
        {
            throw new InvalidOperationException("Spec Max CLR program did not compile: " +
                string.Join(" | ", errors.Select(error => error is null ? throw new System.ArgumentNullException(nameof(error)) : (error.ToString()))));
        }
        using var image = new MemoryStream();
        var emission = compilation.Emit(image);
        if (!emission.Success)
        {
            throw new InvalidOperationException("Spec Max CLR program did not emit: " +
                string.Join(" | ", emission.Diagnostics));
        }
        image.Position = 0;
        var context = new AssemblyLoadContext("contract-matrix-spec-max", isCollectible: true);
        try
        {
            var target = context.LoadFromStream(image).GetType("C")!
                .GetMethod("Target", BindingFlags.Public | BindingFlags.Static)!;
            try
            {
                return target.Invoke(null, [input])!;
            }
            catch (TargetInvocationException exception)
            {
                throw new InvalidOperationException("Spec Max CLR oracle unexpectedly threw.",
                    exception.InnerException);
            }
        }
        finally
        {
            context.Unload();
        }
    }

    private static GeneratedCase BuildCase(int slot, int rewrite)
    {
        if (slot < 32)
        {
            var domain = Rewrite(s_domains[slot / 8], rewrite);
            var variant = slot % 8;
            var admissionBody = variant >= 6;
            var falseClaim = admissionBody ? variant == 7 : (variant & 1) != 0;
            var bias = admissionBody ? 0 : variant - 3;
            var callerPre = Conjunction(domain.Callee);
            var requires = Requires(domain.Callee);
            var body = admissionBody
                ? "return (" + callerPre + ") ? 1 : 0;"
                : "return unchecked(x + y + " + bias + ");";
            var claimed = admissionBody
                ? (falseClaim ? "0" : "1")
                : "unchecked(Contract.Old(x) + Contract.Old(y) + " + (bias + (falseClaim ? 1 : 0)) + ")";
            var source = "using SharpProof.Attributes; public static class C { " +
                "public static bool CallerPre(int x, int y) => " + callerPre + "; " +
                "public static int Target(int x, int y) { " + requires +
                " Contract.Ensures(Contract.Result<int>() == " + claimed + "); " + body + " } }";
            var name = "direct/" + domain.Name + "/" + (admissionBody ? "admission" : "arithmetic") +
                "/" + variant;
            return new(name, source, false, variant, bias, rewrite);
        }
        else if (slot < 48)
        {
            var offset = slot - 32;
            var domain = Rewrite(s_domains[offset / 4], rewrite);
            var variant = offset % 4;
            var callerClauses = variant < 2 ? domain.Callee : domain.WiderCaller;
            var callerPre = Conjunction(callerClauses);
            var calleePre = Conjunction(domain.Callee);
            var body = variant == 3
                ? "if (!(" + calleePre + ")) return 1; return Callee(x, y);"
                : "return Callee(x, y);";
            var source = "using SharpProof.Attributes; public static class C { " +
                "public static bool CallerPre(int x, int y) => " + callerPre + "; " +
                "public static bool CalleePre(int x, int y) => " + calleePre + "; " +
                "private static int Callee(int x, int y) { " + Requires(domain.Callee) +
                " return (" + calleePre + ") ? 1 : 0; } " +
                "public static int Target(int x, int y) { " + Requires(callerClauses) +
                " Contract.Ensures(Contract.Result<int>() == " + (variant == 1 ? "0" : "1") +
                "); " + body + " } }";
            var variantName = new[] { "aligned-true", "aligned-false", "wider-true", "guarded-true" }[variant];
            return new("call/" + domain.Name + "/" + variantName, source, true, variant, 0, rewrite);
        }
        else
        {
            var offset = slot - 48;
            var domain = Rewrite(s_jointDomains[offset / 2], rewrite);
            var falseClaims = (offset & 1) != 0;
            var source = "using System; using SharpProof.Attributes; public static class C { " +
                "public static bool CallerPre(int x) => " + domain + "; " +
                "[DoesNotThrow] public static int Target(int x) { " +
                "Contract.Requires(" + domain + "); " +
                "Contract.Ensures(Contract.Result<int>() == Contract.Old(x) + " +
                (falseClaims ? "1" : "0") + "); " +
                (falseClaims ? "if (x == 1) throw new InvalidOperationException(); " : "") +
                "return x; } }";
            return new("joint/" + (offset / 2) + "/" + (falseClaims ? "both-refuted" : "both-proven"),
                source, false, falseClaims ? 1 : 0, 0, rewrite, Joint: true);
        }
    }

    private static Domain Rewrite(Domain domain, int rewrite)
    {
        System.ArgumentNullException.ThrowIfNull(domain);
        return new Domain(domain.Name, domain.Callee.Select(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (Rewrite(clause, rewrite))).ToArray(),
            domain.WiderCaller.Select(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (Rewrite(clause, rewrite))).ToArray());
    }

    private static string Rewrite(string predicate, int rewrite)
    {
        System.ArgumentNullException.ThrowIfNull(predicate);
        return rewrite switch
        {
            0 => predicate,
            1 => "!(!(" + predicate + "))",
            2 => "(" + predicate + ") == true",
            3 => "(" + predicate + ") != false",
            _ => throw new ArgumentOutOfRangeException(nameof(rewrite))
        };
    }

    private static string Conjunction(string[] clauses)
    {
        System.ArgumentNullException.ThrowIfNull(clauses, nameof(clauses));
        return string.Join(" && ", clauses.Select(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : ("(" + clause + ")")));
    }

    private static string Requires(string[] clauses)
    {
        System.ArgumentNullException.ThrowIfNull(clauses, nameof(clauses));
        return string.Join(" ", clauses.Select(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : ("Contract.Requires(" + clause + ");")));
    }

    private static async Task RunCase(LongFuzzSession session, int seed, GeneratedCase item)
    {
        System.ArgumentNullException.ThrowIfNull(session);
        if (item.Joint)
        {
            await RunJointCase(session, seed, item);
            return;
        }
        var observed = ObserveClr(item);
        session.Count("compiled:" + (item.Call ? "call" : "direct"));
        session.Count("admitted-inputs", observed.Admitted);
        session.Count("excluded-inputs", observed.Excluded);
        session.Count("callee-admitted-inputs", observed.CalleeAdmitted);
        session.Count("callee-rejected-inputs", observed.CalleeRejected);
        session.Count("guarded-skipped-inputs", observed.GuardedSkipped);
        session.Count("excluded-claim-violations", observed.ExcludedClaimViolations);
        if (observed.Witness is not null)
        {
            session.Count("clr-witness:" + (item.Call ? "call" : "direct"));
        }
        ValidateOracle(item, observed);
        session.Count("clr-validated:" + (item.Call ? "call" : "direct"));

        var claim = await Verify(item.Source);
        session.Count("worker:" + (item.Call ? "call" : "direct"));
        session.Count("verdict:" + item.Name + ":" + claim.Outcome);
        session.Count("verdict:rewrite:" + item.Rewrite + ":" + claim.Outcome);
        if (claim.Outcome == WorkerClaimOutcome.Unknown)
        {
            session.Count("unknown-reason:" + claim.Reason);
            if (item.Rewrite == 0)
            {
                // All 48 pinned source shapes had concrete verdicts in the pilot.
                session.RecordHit(new LongFuzzHit("ConcreteVerdictRegression", LongFuzzHit.Error, seed, item.Name,
                    claim.Outcome.ToString(), claim.Reason.ToString(), observed.Witness ?? "no CLR violation",
                    "This compiler-valid pinned case previously had a concrete verdict.", item.Source));
            }
        }
        else if (claim.Outcome == WorkerClaimOutcome.Proven && observed.Witness is not null)
        {
            session.RecordHit(new LongFuzzHit("FalseProven", LongFuzzHit.Error, seed, item.Name,
                claim.Outcome.ToString(), claim.Reason.ToString(), observed.Witness,
                "An admitted CLR input falsifies the Proven postcondition.", item.Source));
        }
        else if (claim.Outcome == WorkerClaimOutcome.Refuted && observed.Witness is null)
        {
            session.RecordHit(new LongFuzzHit("RefutedNotObserved", LongFuzzHit.Error, seed, item.Name,
                claim.Outcome.ToString(), claim.Reason.ToString(), "no violation on " + observed.Admitted +
                " admitted inputs", "The pinned CLR grid does not witness this refutation.", item.Source));
        }
    }

    private static void ValidateOracle(GeneratedCase item, Observation observed)
    {
        if (observed.Admitted == 0 || observed.Excluded == 0 || observed.BodyMismatch)
        {
            throw new InvalidOperationException("CLR admission/body oracle failed for " + item.Name);
        }
        if (item.Call)
        {
            var shouldViolate = item.Variant is 1 or 2;
            if ((observed.Witness is not null) != shouldViolate || observed.CalleeAdmitted == 0 ||
                (item.Variant == 2 && observed.CalleeRejected == 0) ||
                (item.Variant == 3 && (observed.CalleeRejected != 0 || observed.GuardedSkipped == 0)))
            {
                throw new InvalidOperationException("Caller/callee CLR oracle failed for " + item.Name);
            }
        }
        else
        {
            var falseClaim = item.Variant >= 6 ? item.Variant == 7 : (item.Variant & 1) != 0;
            if ((observed.Witness is not null) != falseClaim ||
                (item.Variant == 6 && observed.ExcludedClaimViolations == 0) ||
                (item.Variant == 7 && observed.ExcludedClaimViolations != 0))
            {
                throw new InvalidOperationException("Direct precondition CLR oracle failed for " + item.Name);
            }
        }
    }

    private sealed record JointObservation(int Admitted, int Excluded, int Normal, int Exceptional,
        string? PostconditionWitness, string? DoesNotThrowWitness);

    private static async Task RunJointCase(LongFuzzSession session, int seed, GeneratedCase item)
    {
        System.ArgumentNullException.ThrowIfNull(session);
        var observed = ObserveJointClr(item);
        session.Count("compiled:joint");
        session.Count("admitted-inputs", observed.Admitted);
        session.Count("excluded-inputs", observed.Excluded);
        session.Count("joint:normal-inputs", observed.Normal);
        session.Count("joint:exceptional-inputs", observed.Exceptional);
        if (observed.Admitted != 2 || observed.Excluded != 2 ||
            observed.Normal != (item.Variant == 1 ? 1 : 2) ||
            observed.Exceptional != (item.Variant == 1 ? 1 : 0) ||
            (observed.PostconditionWitness is not null) != (item.Variant == 1) ||
            (observed.DoesNotThrowWitness is not null) != (item.Variant == 1))
        {
            throw new InvalidOperationException("Joint CLR oracle failed for " + item.Name);
        }
        session.Count("clr-validated:joint");

        var results = await VerifyJoint(item.Source);
        session.Count("worker:joint");
        CheckJointClaim("Postcondition", results.Postcondition, observed.PostconditionWitness);
        CheckJointClaim("DoesNotThrow", results.DoesNotThrow, observed.DoesNotThrowWitness);

        void CheckJointClaim(string kind, WorkerClaimResult claim, string? witness)
        {
            System.ArgumentNullException.ThrowIfNull(claim);
            System.ArgumentNullException.ThrowIfNull(kind);
            session.Count("verdict:joint:" + kind + ":" + claim.Outcome);
            session.Count("verdict:rewrite:" + item.Rewrite + ":" + claim.Outcome);
            if (claim.Outcome == WorkerClaimOutcome.Unknown)
            {
                session.Count("unknown-reason:joint:" + kind + ":" + claim.Reason);
            }
            else if (claim.Outcome == WorkerClaimOutcome.Proven && witness is not null)
            {
                session.RecordHit(new LongFuzzHit("FalseProven", LongFuzzHit.Error, seed, item.Name + "/" + kind,
                    claim.Outcome.ToString(), claim.Reason.ToString(), witness,
                    "An admitted CLR input falsifies this Proven claim.", item.Source));
            }
            else if (claim.Outcome == WorkerClaimOutcome.Refuted && witness is null)
            {
                session.RecordHit(new LongFuzzHit("RefutedNotObserved", LongFuzzHit.Error, seed, item.Name + "/" + kind,
                    claim.Outcome.ToString(), claim.Reason.ToString(), "no CLR violation",
                    "The complete admitted domain is {0, 1}; neither input violates this claim.", item.Source));
            }
        }
    }

    private static JointObservation ObserveJointClr(GeneratedCase item)
    {
        System.ArgumentNullException.ThrowIfNull(item);
        var compilation = Compile(item.Source, contractsEnabled: false);
        var errors = compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)).ToArray();
        if (errors.Length != 0)
        {
            throw new InvalidOperationException("Joint CLR program did not compile: " +
                string.Join(" | ", errors.Select(error => error is null ? throw new System.ArgumentNullException(nameof(error)) : (error.ToString()))));
        }
        using var image = new MemoryStream();
        var emission = compilation.Emit(image);
        if (!emission.Success)
        {
            throw new InvalidOperationException("Joint CLR program did not emit: " +
                string.Join(" | ", emission.Diagnostics));
        }
        image.Position = 0;
        var context = new AssemblyLoadContext("contract-matrix-joint", isCollectible: true);
        try
        {
            var type = context.LoadFromStream(image).GetType("C")!;
            var callerPre = type.GetMethod("CallerPre", BindingFlags.Public | BindingFlags.Static)!;
            var target = type.GetMethod("Target", BindingFlags.Public | BindingFlags.Static)!;
            var admitted = 0;
            var excluded = 0;
            var normal = 0;
            var exceptional = 0;
            string? postWitness = null;
            string? effectWitness = null;
            foreach (var x in new[] { -1, 0, 1, 2 })
            {
                var allowed = (bool)callerPre.Invoke(null, [x])!;
                if (allowed)
                {
                    admitted++;
                }
                else
                {
                    excluded++;
                }
                int? value = null;
                string? exception = null;
                try
                {
                    value = (int)target.Invoke(null, [x])!;
                }
                catch (TargetInvocationException invocation)
                {
                    exception = invocation.InnerException!.GetType().FullName;
                }
                var shouldThrow = item.Variant == 1 && x == 1;
                if (shouldThrow)
                {
                    if (exception != typeof(InvalidOperationException).FullName)
                    {
                        throw new InvalidOperationException("Joint body failed exceptional CLR path for x=" + x);
                    }
                }
                else if (exception is not null || value != x)
                {
                    throw new InvalidOperationException("Joint body failed normal CLR path for x=" + x);
                }
                if (!allowed)
                {
                    continue;
                }
                if (exception is null)
                {
                    normal++;
                    if (value != x + item.Variant)
                    {
                        postWitness ??= "x=" + x + ",return=" + value + ",claimed=" + (x + item.Variant);
                    }
                }
                else
                {
                    exceptional++;
                    effectWitness ??= "x=" + x + ",throws=" + exception;
                }
            }
            return new(admitted, excluded, normal, exceptional, postWitness, effectWitness);
        }
        finally
        {
            context.Unload();
        }
    }

    private static async Task<(WorkerClaimResult Postcondition, WorkerClaimResult DoesNotThrow)> VerifyJoint(
        string source)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        var compilation = Compile(source, contractsEnabled: true);
        var errors = compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)).ToArray();
        if (errors.Length != 0)
        {
            throw new InvalidOperationException("Joint claim program did not compile: " +
                string.Join(" | ", errors.Select(error => error is null ? throw new System.ArgumentNullException(nameof(error)) : (error.ToString()))));
        }
        var discovery = new ClaimManifestBuilder(compilation).Build();
        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0", WorkerFeatureSet.All,
            discovery, WorkerBudgets.DefaultMaximumExpressionDepth, CancellationToken.None);
        var targetClaims = artifact.Manifest.Claims.Where(claim =>
            claim is null ? throw new System.ArgumentNullException(nameof(claim)) : (claim.CallableId.Contains("Target", StringComparison.Ordinal))).ToArray();
        if (targetClaims.Length != 2 || targetClaims.Select(claim => claim is null ? throw new System.ArgumentNullException(nameof(claim)) : (claim.ClaimId)).Distinct().Count() != 2)
        {
            throw new InvalidOperationException("Expected two distinct Target ClaimIds, got " + targetClaims.Length);
        }
        var post = targetClaims.Single(claim => claim is null ? throw new System.ArgumentNullException(nameof(claim)) : (claim.Kind == WorkerClaimKind.Postcondition));
        var effect = targetClaims.Single(claim => claim is null ? throw new System.ArgumentNullException(nameof(claim)) : (claim.Kind == WorkerClaimKind.Effect &&
            claim.EffectContractKind == WorkerEffectContractKind.DoesNotThrow));
        using var project = new ShadowTestProject(artifact);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        if (response.Errors.Length != 0 || !WorkerProtocolJson.Validate(response).IsValid)
        {
            throw new InvalidOperationException("Joint worker errors: " +
                string.Join(" | ", response.Errors.Select(error => error is null ? throw new System.ArgumentNullException(nameof(error)) : (error.Code + ":" + error.Message))));
        }
        var results = response.ClaimResults.Where(result =>
            result is null ? throw new System.ArgumentNullException(nameof(result)) : (targetClaims.Any(claim => claim is null ? throw new System.ArgumentNullException(nameof(claim)) : (claim.ClaimId == result.ClaimId)))).ToArray();
        if (results.Length != 2 || results.Select(result => result is null ? throw new System.ArgumentNullException(nameof(result)) : (result.ClaimId)).Distinct().Count() != 2)
        {
            throw new InvalidOperationException("Expected one Worker result per Target ClaimId.");
        }
        return (results.Single(result => result is null ? throw new System.ArgumentNullException(nameof(result)) : (result.ClaimId == post.ClaimId)),
            results.Single(result => result is null ? throw new System.ArgumentNullException(nameof(result)) : (result.ClaimId == effect.ClaimId)));
    }

    private static Observation ObserveClr(GeneratedCase item)
    {
        System.ArgumentNullException.ThrowIfNull(item);
        var compilation = Compile(item.Source, contractsEnabled: false);
        var errors = compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)).ToArray();
        if (errors.Length != 0)
        {
            throw new InvalidOperationException("Generated CLR program did not compile: " +
                string.Join(" | ", errors.Select(error => error is null ? throw new System.ArgumentNullException(nameof(error)) : (error.ToString()))));
        }
        using var image = new MemoryStream();
        var emission = compilation.Emit(image);
        if (!emission.Success)
        {
            throw new InvalidOperationException("Generated CLR program did not emit: " +
                string.Join(" | ", emission.Diagnostics));
        }
        image.Position = 0;
        var context = new AssemblyLoadContext("contract-matrix", isCollectible: true);
        try
        {
            var type = context.LoadFromStream(image).GetType("C")!;
            var callerPre = type.GetMethod("CallerPre", BindingFlags.Public | BindingFlags.Static)!;
            var calleePre = item.Call ? type.GetMethod("CalleePre", BindingFlags.Public | BindingFlags.Static) : null;
            var target = type.GetMethod("Target", BindingFlags.Public | BindingFlags.Static)!;
            var admitted = 0;
            var excluded = 0;
            var excludedClaimViolations = 0;
            var calleeAdmitted = 0;
            var calleeRejected = 0;
            var guardedSkipped = 0;
            var violations = 0;
            var bodyMismatch = false;
            string? witness = null;
            foreach (var x in s_values)
            {
                foreach (var y in s_values)
                {
                    var callerAllowed = (bool)callerPre.Invoke(null, [x, y])!;
                    var calleeAllowed = item.Call && (bool)calleePre!.Invoke(null, [x, y])!;
                    int actual;
                    try
                    {
                        actual = (int)target.Invoke(null, [x, y])!;
                    }
                    catch (TargetInvocationException exception)
                    {
                        throw new InvalidOperationException("Generated Target unexpectedly threw for x=" + x +
                            ", y=" + y, exception.InnerException);
                    }
                    int expectedBody;
                    int expectedClaim;
                    if (item.Call)
                    {
                        var shouldCall = item.Variant != 3 || calleeAllowed;
                        expectedBody = shouldCall ? (calleeAllowed ? 1 : 0) : 1;
                        expectedClaim = item.Variant == 1 ? 0 : 1;
                        if (callerAllowed)
                        {
                            if (!shouldCall)
                            {
                                guardedSkipped++;
                            }
                            else if (calleeAllowed)
                            {
                                calleeAdmitted++;
                            }
                            else
                            {
                                calleeRejected++;
                            }
                        }
                    }
                    else
                    {
                        var admissionBody = item.Variant >= 6;
                        var falseClaim = admissionBody ? item.Variant == 7 : (item.Variant & 1) != 0;
                        expectedBody = admissionBody ? (callerAllowed ? 1 : 0) : unchecked(x + y + item.Bias);
                        expectedClaim = admissionBody ? (falseClaim ? 0 : 1) :
                            unchecked(expectedBody + (falseClaim ? 1 : 0));
                    }
                    bodyMismatch |= actual != expectedBody;
                    if (callerAllowed)
                    {
                        admitted++;
                        if (actual != expectedClaim)
                        {
                            violations++;
                            witness ??= "x=" + x + ",y=" + y + ",result=" + actual + ",claimed=" + expectedClaim;
                        }
                    }
                    else
                    {
                        excluded++;
                        if (actual != expectedClaim)
                        {
                            excludedClaimViolations++;
                        }
                    }
                }
            }
            return new(admitted, excluded, excludedClaimViolations, calleeAdmitted, calleeRejected,
                guardedSkipped, violations, witness, bodyMismatch);
        }
        finally
        {
            context.Unload();
        }
    }

    private static CSharpCompilation Compile(string source, bool contractsEnabled)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        string[] symbols = contractsEnabled ? [Contract.ConditionalSymbol] : [];
        var tree = CSharpSyntaxTree.ParseText(source,
            new CSharpParseOptions(LanguageVersion.CSharp12, preprocessorSymbols: symbols), "/project/Subject.cs");
        return CSharpCompilation.Create("ContractMatrix_" + Guid.NewGuid().ToString("N"), [tree],
            TestMetadataReferences.WithSharpProof,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release, nullableContextOptions: NullableContextOptions.Enable));
    }

    private static async Task<WorkerClaimResult> Verify(string source, bool scalarPack = false,
        bool exactTargetPostcondition = false)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        var compilation = Compile(source, contractsEnabled: true);
        var errors = compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)).ToArray();
        if (errors.Length != 0)
        {
            throw new InvalidOperationException("Generated claim program did not compile: " +
                string.Join(" | ", errors.Select(error => error is null ? throw new System.ArgumentNullException(nameof(error)) : (error.ToString()))));
        }
        var discovery = new ClaimManifestBuilder(compilation).Build();
        var artifact = scalarPack
            ? CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0", WorkerFeatureSet.All,
                discovery, WorkerBudgets.DefaultMaximumExpressionDepth, CancellationToken.None,
                specificationPacks: ImmutableArray.Create("dotnet.scalar"))
            : CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0", WorkerFeatureSet.All,
                discovery, WorkerBudgets.DefaultMaximumExpressionDepth, CancellationToken.None);
        if (scalarPack && (artifact.SpecificationPackIds.Length != 1 ||
            artifact.SpecificationPackIds[0] != "dotnet.scalar"))
        {
            throw new InvalidOperationException("The dotnet.scalar specification pack was not selected.");
        }
        if (exactTargetPostcondition && artifact.SpecificationPackIds.Length != 0)
        {
            throw new InvalidOperationException("The Math.Abs case must use only the default BCL catalog.");
        }
        using var project = new ShadowTestProject(artifact);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        if (response.Errors.Length != 0 || !WorkerProtocolJson.Validate(response).IsValid)
        {
            throw new InvalidOperationException("Worker errors: " +
                string.Join(" | ", response.Errors.Select(error => error is null ? throw new System.ArgumentNullException(nameof(error)) : (error.Code + ":" + error.Message))));
        }
        if (scalarPack || exactTargetPostcondition)
        {
            var targetClaims = artifact.Manifest.Claims.Where(claim =>
                claim is null ? throw new System.ArgumentNullException(nameof(claim)) : (claim.CallableId.Contains("Target", StringComparison.Ordinal) &&
                claim.Kind == WorkerClaimKind.Postcondition)).ToArray();
            if (targetClaims.Length != 1)
            {
                throw new InvalidOperationException("Expected exactly one Target Ensures claim, got " +
                    targetClaims.Length);
            }
            var matchingResults = response.ClaimResults.Where(result =>
                result is null ? throw new System.ArgumentNullException(nameof(result)) : (result.ClaimId == targetClaims[0].ClaimId)).ToArray();
            if (matchingResults.Length != 1)
            {
                throw new InvalidOperationException("Expected exactly one response for Target Ensures ClaimId, got " +
                    matchingResults.Length);
            }
            return matchingResults[0];
        }
        var claims = response.ClaimResults.Where(result => result is null ? throw new System.ArgumentNullException(nameof(result)) : (artifact.Manifest.Claims.Any(claim =>
            claim is null ? throw new System.ArgumentNullException(nameof(claim)) : (claim.ClaimId == result.ClaimId && claim.CallableId.Contains("Target", StringComparison.Ordinal))))).ToArray();
        if (claims.Length != 1)
        {
            throw new InvalidOperationException("Expected one Target claim, got " + claims.Length);
        }
        return claims[0];
    }
}

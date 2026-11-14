using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.CompilerCollector;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

internal static class WorkerResponseClaims
{
    // The claims a test source declares, without the call preconditions its
    // callers also carry.
    internal static ImmutableArray<WorkerClaimResult> DeclaredClaims(this WorkerVerifyResponse response)
    {
        System.ArgumentNullException.ThrowIfNull(response);
        var calls = response.Manifest.Claims.Where(static claim => claim.Kind == WorkerClaimKind.CallPrecondition)
            .Select(static claim => claim.ClaimId).ToImmutableHashSet(StringComparer.Ordinal);
        return [.. response.ClaimResults.Where(result => !calls.Contains(result.ClaimId))];
    }
}

// Requires creates an obligation at each modeled call: a caller that violates
// it fails with a replayed site, and a caller that satisfies it is proven.
[TestFixture]
public sealed class CallPreconditionClaimTests
{
    private const string Positive = "public static int F(int x) { Contract.Requires(x > 0); return x; }";

    private static string Subject(string members)
    {
        System.ArgumentNullException.ThrowIfNull(members);
        return "using SharpProof.Attributes;\npublic static class Subject {\n    " + Positive + "\n    " + members + "\n}\n";
    }

    private static async Task<WorkerVerifyResponse> Verify(string source, bool cacheEnabled = false)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        using var project = new ShadowTestProject(source, cacheEnabled);
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, TestContext.CurrentContext.CancellationToken);
        Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete), () => string.Join("; ", response.Errors.Select(static error => error.Code + ": " + error.Message)));
        return response;
    }

    private static ImmutableArray<(WorkerClaimManifestEntry Claim, WorkerClaimResult Result)> CallClaims(WorkerVerifyResponse response, string caller)
    {
        System.ArgumentNullException.ThrowIfNull(response);
        System.ArgumentNullException.ThrowIfNull(caller);
        return [.. response.Manifest.Claims
            .Where(claim => claim.Kind == WorkerClaimKind.CallPrecondition && claim.CallableId.Contains(caller, StringComparison.Ordinal))
            .Select(claim => (claim, response.ClaimResults.Single(result => result is null ? throw new System.ArgumentNullException(nameof(result)) : (result.ClaimId == claim.ClaimId))))];
    }

    private static (WorkerClaimManifestEntry Claim, WorkerClaimResult Result) SingleCallClaim(WorkerVerifyResponse response, string caller)
    {
        System.ArgumentNullException.ThrowIfNull(caller);
        System.ArgumentNullException.ThrowIfNull(response);
        var claims = CallClaims(response, caller);
        Assert.That(claims, Has.Length.EqualTo(1), caller);
        return claims[0];
    }

    [Test]
    public async Task ContractFreeCallerThatViolatesIsRefutedAtTheCall()
    {
        var source = Subject("public static int Caller() { return F(-1); }");
        var response = await Verify(source);
        var (claim, result) = SingleCallClaim(response, "Subject.Caller");
        var caller = response.Manifest.Callables.Single(callable => callable.CallableId.Contains("Subject.Caller", StringComparison.Ordinal));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted));
            Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.None));
            Assert.That(claim.Evidence, Is.EqualTo(WorkerClaimEvidence.CallSite));
            Assert.That(source.Substring(claim.Location.Start, claim.Location.Length), Is.EqualTo("F(-1)"));
            Assert.That(claim.Location.Line, Is.EqualTo(4));
            Assert.That(claim.CallPrecondition!.Callee, Does.Contain("Subject.F"));
            Assert.That(claim.CallPrecondition.ClauseOrdinal, Is.Zero);
            Assert.That(source.Substring(claim.CallPrecondition.ClauseLocation!.Start, claim.CallPrecondition.ClauseLocation.Length), Does.Contain("x > 0"));
            Assert.That(caller.SelectionReasons, Is.EqualTo(new[] { WorkerSelectionReason.DiscoveredCallPrecondition }));
            Assert.That(caller.SelectedFeatures, Is.EqualTo(new[] { WorkerSelectedFeature.Contracts }));
        }
    }

    [Test]
    public async Task ForwardingCallerIsRefutedWithItsOwnReplayableInput()
    {
        var response = await Verify(Subject("private static int Forward(int value) => F(value);"));
        var (_, result) = SingleCallClaim(response, "Subject.Forward");
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted));
        var input = result.Model.Single(value => value.Variable == "parameter:0");
        Assert.That(long.Parse(input.Value, System.Globalization.CultureInfo.InvariantCulture), Is.LessThanOrEqualTo(0));
    }

    [TestCase("Contract.Ensures(Contract.Result<int>() == -1);", "")]
    [TestCase("", "[DoesNotThrow]")]
    public async Task CallerWithOtherClaimsStillAnswersForTheCall(string clause, string attribute)
    {
        System.ArgumentNullException.ThrowIfNull(clause);
        System.ArgumentNullException.ThrowIfNull(attribute);
        var response = await Verify(Subject(attribute + " public static int Caller() { " + clause + " return F(-1); }"));
        var (_, call) = SingleCallClaim(response, "Subject.Caller");
        var other = response.Manifest.Claims.Single(claim => claim.CallableId.Contains("Subject.Caller", StringComparison.Ordinal) && claim.Kind != WorkerClaimKind.CallPrecondition);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.ClaimResults.Single(result => result.ClaimId == other.ClaimId).Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
            Assert.That(call.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted));
        }
    }

    [TestCase("public static int Caller() { return F(1); }", TestName = "Literal")]
    [TestCase("public static int Caller(int y) { Contract.Requires(y > 1); return F(y); }", TestName = "OwnRequires")]
    [TestCase("public static int Caller(int y) { return y > 0 ? F(y) : 0; }", TestName = "Guard")]
    [TestCase("public static int Caller(int y) { if (y <= 0) { return 0; } return F(y); }", TestName = "EarlyReturn")]
    public async Task SatisfiedCallIsProven(string caller)
    {
        System.ArgumentNullException.ThrowIfNull(caller);
        var response = await Verify(Subject(caller));
        var (_, result) = SingleCallClaim(response, "Subject.Caller");
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.Reason.ToString());
    }

    [Test]
    public async Task OwnRequiresThatEstablishesTheCallIsMarkedUsed()
    {
        var response = await Verify(Subject("public static int Caller(int y) { Contract.Requires(y > 1); return F(y); }"));
        var (_, result) = SingleCallClaim(response, "Subject.Caller");
        Assert.That(result.Assumptions.Single(assumption => assumption.Kind == WorkerAssumptionKind.Precondition).Used, Is.True);
    }

    [Test]
    public async Task CallInALoopIsProvenByTheLoopInvariant()
    {
        var response = await Verify(Subject("""
            public static int Caller(int n) {
                var total = 0;
                for (var i = 0; i < n; i++) { total = F(i + 1); }
                return total;
            }
            """));
        var (_, result) = SingleCallClaim(response, "Subject.Caller");
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.Reason.ToString());
    }

    [Test]
    public async Task ViolationInALoopIsRefuted()
    {
        var response = await Verify(Subject("""
            public static int Caller(int n) {
                var total = 0;
                for (var i = 0; i < n; i++) { total = F(i - 1); }
                return total;
            }
            """));
        var (_, result) = SingleCallClaim(response, "Subject.Caller");
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted), result.Reason.ToString());
    }

    [Test]
    public async Task LoopCutModelOfAHoldingCallIsNeverARefutation()
    {
        var response = await Verify(Subject("""
            public static int Caller() {
                var state = 1;
                for (var i = 0; i < 5; i++) { state = (state * 3 + 1) % 7 + 1; }
                return F(state);
            }
            """));
        Assert.That(SingleCallClaim(response, "Subject.Caller").Result.Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Refuted));
    }

    [Test]
    public async Task EachClauseIsItsOwnClaim()
    {
        var response = await Verify("""
            using SharpProof.Attributes;
            public static class Subject {
                public static int F(int x) { Contract.Requires(x > 0); Contract.Requires(x < 10); return x; }
                public static int Caller() { return F(20); }
            }
            """);
        var claims = CallClaims(response, "Subject.Caller");
        Assert.That(claims.Select(static row => (row.Claim.CallPrecondition!.ClauseOrdinal, row.Result.Outcome)),
            Is.EqualTo(new[] { (0, WorkerClaimOutcome.Proven), (1, WorkerClaimOutcome.Refuted) }));
    }

    [TestCase("""
        using SharpProof.Attributes;
        public sealed class C {
            public C(int n) { Contract.Requires(n > 0); }
            public static C Caller() { return new C(0); }
        }
        """, TestName = "Constructor")]
    [TestCase("""
        using SharpProof.Attributes;
        public sealed class C {
            public int M(int n) { Contract.Requires(n > 0); return n; }
            public static int Caller(C c) { Contract.Requires(c != null); return c.M(0); }
        }
        """, TestName = "InstanceMethod")]
    [TestCase("""
        using SharpProof.Attributes;
        public static class C {
            public static int M([Positive] int n) { return n; }
            public static int Caller() { return M(0); }
        }
        """, TestName = "ClosedParameterAttribute")]
    public async Task EveryModeledCallFormIsChecked(string source)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        var response = await Verify(source);
        Assert.That(SingleCallClaim(response, "C.Caller").Result.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted));
    }

    [Test]
    public async Task CompanionClauseIsAnObligation()
    {
        var response = await Verify("""
            using SharpProof.Attributes;
            public static class Target { public static int F(int x) => x; }
            [ContractFor(typeof(Target))]
            public static class TargetContracts {
                public static int F(int x) { Contract.Requires(x > 0); return 0; }
            }
            public static class Consumer { public static int Caller() { return Target.F(0); } }
            """);
        Assert.That(SingleCallClaim(response, "Consumer.Caller").Result.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted));
    }

    [Test]
    public async Task UnprovableCallIsUnknownNotRefuted()
    {
        var response = await Verify(Subject("public static int Caller() { return F(System.Environment.TickCount); }"));
        var (_, result) = SingleCallClaim(response, "Subject.Caller");
        var caller = response.CallableResults.Single(callable => callable.CallableId.Contains("Subject.Caller", StringComparison.Ordinal));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.CounterexampleNotReplayable));
            Assert.That(caller.Coverage, Is.EqualTo(WorkerCallableCoverage.Incomplete));
        }
    }

    [Test]
    public async Task ContradictoryCallerIsVacuous()
    {
        var response = await Verify(Subject("public static int Caller(int y) { Contract.Requires(y > 0); Contract.Requires(y < 0); return F(-1); }"));
        var (_, result) = SingleCallClaim(response, "Subject.Caller");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
            Assert.That(result.Vacuity, Is.EqualTo(WorkerVacuityKind.ContradictoryPreconditions));
        }
    }

    [Test]
    public async Task InlinedCalleeAnswersForItsOwnCalls()
    {
        var response = await Verify("""
            using SharpProof.Attributes;
            public static class Subject {
                public static int Inner(int x) { Contract.Requires(x > 0); return x; }
                public static int Middle(int y) { Contract.Requires(y > 0); return Inner(y); }
                public static int Outer() { return Middle(0); }
            }
            """);
        var outer = SingleCallClaim(response, "Subject.Outer");
        var middle = SingleCallClaim(response, "Subject.Middle");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(outer.Claim.CallPrecondition!.Callee, Does.Contain("Subject.Middle"));
            Assert.That(outer.Result.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted));
            Assert.That(middle.Result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
        }
    }

    [Test]
    public async Task ContractFreeMiddleCallerDoesNotRepeatItsFailureInItsCallers()
    {
        var response = await Verify("""
            using SharpProof.Attributes;
            public static class Subject {
                public static int Inner(int x) { Contract.Requires(x > 0); return x; }
                public static int Middle(int y) { return Inner(y); }
                public static int Outer() { return Middle(1); }
            }
            """);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(SingleCallClaim(response, "Subject.Middle").Result.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted));
            Assert.That(CallClaims(response, "Subject.Outer"), Is.Empty);
        }
    }

    [TestCase("public static int Caller(int y) { int Local(int v) => F(v); return Local(y); }", TestName = "LocalFunction")]
    [TestCase("private static int P => F(-1); public static int Caller() { Contract.Ensures(Contract.Result<int>() == -1); return P; }", TestName = "Getter")]
    public async Task FrameThatCannotBeARootLeavesTheCallWithItsOwner(string caller)
    {
        System.ArgumentNullException.ThrowIfNull(caller);
        var response = await Verify(Subject(caller));
        Assert.That(SingleCallClaim(response, "Subject.Caller").Result.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted));
    }

    [Test]
    public async Task EntryInitializerKeepsAWitnessFromBeingARefutation()
    {
        var response = await Verify("""
            using SharpProof.Attributes;
            public static class Target { public static int F(int x) { Contract.Requires(x > 0); return x; } }
            public static class Subject {
                private static readonly int[] Table = new int[4];
                public static int Caller() { return Target.F(-1); }
            }
            """);
        var (_, result) = SingleCallClaim(response, "Subject.Caller");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.CounterexampleNotReplayable));
        }
    }

    [TestCase("public static async System.Threading.Tasks.Task<int> Caller() { await System.Threading.Tasks.Task.Yield(); return F(-1); }", TestName = "Async")]
    [TestCase("public static System.Func<int> Caller() { return () => F(-1); }", TestName = "Lambda")]
    [TestCase("private static int P => F(-1); public static int Caller() { return P; }", TestName = "AccessorWithoutASelectedCaller")]
    public async Task CallInAnUnmodeledBodyHasNoClaim(string caller)
    {
        System.ArgumentNullException.ThrowIfNull(caller);
        var response = await Verify(Subject(caller));
        Assert.That(CallClaims(response, "Subject."), Is.Empty);
    }

    [TestCase("""
        using SharpProof.Attributes;
        public class C {
            public virtual int M(int n) { Contract.Requires(n > 0); return n; }
            public static int Caller(C c) { return c.M(0); }
        }
        """, TestName = "Virtual")]
    [TestCase("""
        using SharpProof.Attributes;
        public static class C {
            public static int M(ref int n) { Contract.Requires(n > 0); return n; }
            public static int Caller() { var n = 0; return M(ref n); }
        }
        """, TestName = "RefParameter")]
    [TestCase("""
        using SharpProof.Attributes;
        public static class T {
            private static readonly int[] Table = new int[4];
            public static int M(int n) { Contract.Requires(n > 0); return n; }
        }
        public static class C { public static int Caller() { return T.M(0); } }
        """, TestName = "InitializedCalleeType")]
    public async Task CallToAnUnmodeledCalleeHasNoClaim(string source)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        var response = await Verify(source);
        Assert.That(CallClaims(response, "C.Caller"), Is.Empty);
    }

    // A callee in the caller's own type runs under the initializer the caller
    // already answers for, so its Requires is checked at the call.
    [Test]
    public async Task CallToASameTypeCalleeUnderAnInitializerIsChecked()
    {
        var response = await Verify("""
            using SharpProof.Attributes;
            public static class C {
                private static readonly int[] Table = new int[4];
                public static int M(int n) { Contract.Requires(n > 0); return n; }
                public static int Caller() { return M(0); }
                public static int Fine() { return M(1); }
            }
            """);
        using (Assert.EnterMultipleScope())
        {
            // A refutation needs an entry free of initializer effects, and
            // this initializer allocates, so the violation is not reported
            // as definite. It is never Proven.
            Assert.That(SingleCallClaim(response, "C.Caller").Result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(SingleCallClaim(response, "C.Fine").Result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
        }
    }

    [Test]
    public async Task MethodsOfAGenericTypeCanBeCallers()
    {
        var response = await Verify("""
            using SharpProof.Attributes;
            public static class Target { public static int F(int x) { Contract.Requires(x > 0); return x; } }
            public sealed class Box<T> { public int Caller() { return Target.F(-1); } }
            """);
        Assert.That(SingleCallClaim(response, "Box").Result.Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven));
    }

    [Test]
    public async Task ProjectWithoutRequiresPublishesNothing()
    {
        var response = await Verify("public static class Subject { public static int F(int x) => x; public static int Caller() => F(1); }");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.Manifest.Callables, Is.Empty);
            Assert.That(response.Manifest.Claims, Is.Empty);
        }
    }

    [Test]
    public async Task CallerWithoutACheckedCallIsNotPublished()
    {
        var response = await Verify(Subject("public static int Caller(int y) { if (false) { return F(-1); } return y; }"));
        Assert.That(response.Manifest.Callables.Select(static callable => callable.CallableId), Has.None.Contains("Subject.Caller"));
    }

    [Test]
    public async Task ClaimIdentitySurvivesAMovedCallAndDistinguishesRepeats()
    {
        const string body = "public static int Caller() { return F(1) + F(2); }";
        var first = CallClaims(await Verify(Subject(body)), "Subject.Caller");
        var moved = CallClaims(await Verify(Subject("\n\n" + body)), "Subject.Caller");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Has.Length.EqualTo(2));
            Assert.That(first.Select(static row => row.Claim.ClaimId).Distinct().Count(), Is.EqualTo(2));
            Assert.That(moved.Select(static row => row.Claim.ClaimId), Is.EqualTo(first.Select(static row => row.Claim.ClaimId)));
            Assert.That(moved[0].Claim.Location.Line, Is.EqualTo(first[0].Claim.Location.Line + 2));
        }
    }

    [Test]
    public void ClaimIdentitySurvivesAnAssemblyVersionChange()
    {
        static string ClaimId(string version)
        {
            System.ArgumentNullException.ThrowIfNull(version);
            var source = Subject("public static int Caller() { return F(1); }").Replace("public static class Subject",
                "[assembly: System.Reflection.AssemblyVersion(\"" + version + "\")]\npublic static class Subject", StringComparison.Ordinal);
            return CompilerTotalCallableArtifactTests.CreateArtifact(source).Manifest.Claims.Single(static claim => claim.Kind == WorkerClaimKind.CallPrecondition).ClaimId;
        }
        Assert.That(ClaimId("2.0.0.0"), Is.EqualTo(ClaimId("1.0.0.0")));
    }

    // A real build leaves SHARPPROOF_CONTRACTS undefined, so clauses are not emitted.
    private static CSharpCompilation ConsumerCompilation(string name, string path, string source, params MetadataReference[] references)
    {
        System.ArgumentNullException.ThrowIfNull(name);
        System.ArgumentNullException.ThrowIfNull(path);
        System.ArgumentNullException.ThrowIfNull(source);
        System.ArgumentNullException.ThrowIfNull(references);
        var compilation = CSharpCompilation.Create(name, [CSharpSyntaxTree.ParseText(source, path: path)],
            TestMetadataReferences.WithSharpProof.AddRange(references), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        TestCompilation.AssertNoErrors(compilation);
        return compilation;
    }

    private static (ClaimManifestBuildResult Discovery, CompilerManifestArtifact Artifact) Census(string source, params MetadataReference[] references)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        System.ArgumentNullException.ThrowIfNull(references);
        var compilation = ConsumerCompilation("CallCensus", "Subject.cs", source, references);
        var discovery = new ClaimManifestBuilder(compilation).Build();
        return (discovery, CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0", WorkerFeatureSet.All, discovery,
            WorkerBudgets.DefaultMaximumExpressionDepth, TestContext.CurrentContext.CancellationToken));
    }

    [Test]
    public void BodyThatNamesNoCalleeWithAPreconditionStaysUnbound()
    {
        var (discovery, _) = Census(Subject("""
            public static int Caller() { return F(1); }
            public static int Unrelated(int x) { var y = x + 1; return System.Math.Max(y, 2); }
            """));
        var compilation = discovery.Targets.Keys.First(static method => method.Name == "F").ContainingAssembly;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(compilation.Name, Is.EqualTo("CallCensus"));
            Assert.That(discovery.BoundCallSyntax, Is.Not.Empty);
            Assert.That(discovery.BoundCallSyntax.Select(static node => node.ToString()), Has.None.Contains("Max"));
            Assert.That(discovery.BoundCallSyntax.Select(static node => node.Ancestors().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>().First().Identifier.ValueText), Has.All.EqualTo("Caller"));
        }
    }

    [Test]
    public void ProjectWithoutPreconditionsBindsNothing()
    {
        var (discovery, artifact) = Census("public static class Subject { public static int F(int x) => x; public static int Caller() => F(1); }");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(discovery.BoundCallSyntax, Is.Empty);
            Assert.That(artifact.Manifest.UncheckedCallSites, Is.Zero);
        }
    }

    [TestCase("public sealed class C { public C(int n) { Contract.Requires(n > 0); } public static C Caller() { return new(0); } }", TestName = "TargetTypedConstruction")]
    [TestCase("using Alias = C; public sealed class C { public C(int n) { Contract.Requires(n > 0); } public static C Caller() { return new Alias(0); } }", TestName = "AliasedConstruction")]
    public void CallThatDoesNotSpellItsCalleeIsStillFound(string body)
    {
        System.ArgumentNullException.ThrowIfNull(body);
        var usings = body.StartsWith("using ", StringComparison.Ordinal) ? "using SharpProof.Attributes; " : "using SharpProof.Attributes;\n";
        var (discovery, _) = Census(usings + body);
        Assert.That(discovery.PreconditionCallSites, Has.Length.EqualTo(1));
    }

    [TestCase("public static int Caller() { return F(1); }", 0, TestName = "Checked")]
    [TestCase("public static System.Func<int> Caller() { return () => F(-1); }", 1, TestName = "Lambda")]
    [TestCase("public static async System.Threading.Tasks.Task<int> Caller() { await System.Threading.Tasks.Task.Yield(); return F(-1); }", 1, TestName = "AsyncCaller")]
    [TestCase("public static int First() { return F(1); } public static System.Func<int> Second() { return () => F(1) + F(2); }", 2, TestName = "CheckedAndUnchecked")]
    public void SummaryCountsVisibleCallsNoClaimChecks(string members, int expected)
    {
        System.ArgumentNullException.ThrowIfNull(members);
        var (_, artifact) = Census(Subject(members));
        Assert.That(artifact.Manifest.UncheckedCallSites, Is.EqualTo(expected));
    }

    [Test]
    public void CallToAVirtualCalleeIsCountedAsUnchecked()
    {
        var (_, artifact) = Census("""
            using SharpProof.Attributes;
            public class C {
                public virtual int M(int n) { Contract.Requires(n > 0); return n; }
                public static int Caller(C c) { return c.M(0); }
            }
            """);
        Assert.That(artifact.Manifest.UncheckedCallSites, Is.EqualTo(1));
    }

    [Test]
    public void SameNameWithoutAPreconditionIsNotCounted()
    {
        var (_, artifact) = Census("""
            using SharpProof.Attributes;
            public static class Own { public static int Add(int n) { Contract.Requires(n > 0); return n; } }
            public static class Consumer {
                public static void Fill(System.Collections.Generic.List<int> list) { list.Add(1); }
            }
            """);
        Assert.That(artifact.Manifest.UncheckedCallSites, Is.Zero);
    }

    private static PortableExecutableReference Library(TempDirectory directory, string source)
    {
        System.ArgumentNullException.ThrowIfNull(directory);
        System.ArgumentNullException.ThrowIfNull(source);
        var path = Path.Combine(directory.FullName, "CallLibrary.dll");
        Assert.That(ConsumerCompilation("CallLibrary", "Library.cs", source).Emit(path).Success, Is.True);
        return MetadataReference.CreateFromFile(path);
    }

    [Test]
    public void ClosedAttributeInAnotherAssemblyIsChecked()
    {
        using var directory = new TempDirectory("sharpproof-call-library-");
        var library = Library(directory, """
            using SharpProof.Attributes;
            public static class Remote { public static int M([Positive] int n) => n; }
            """);
        var (_, artifact) = Census("public static class Consumer { public static int Caller() => Remote.M(0); }", library);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(artifact.Manifest.UncheckedCallSites, Is.Zero);
            Assert.That(artifact.Manifest.Claims.Single().CallPrecondition!.MetadataClause!.Attribute, Is.EqualTo("Positive"));
        }
    }

    [Test]
    public void ContractRequiresInAnotherAssemblyIsInvisible()
    {
        using var directory = new TempDirectory("sharpproof-call-library-");
        var library = Library(directory, """
            using SharpProof.Attributes;
            public static class Remote { public static int M(int n) { Contract.Requires(n > 0); return n; } }
            """);
        var (_, artifact) = Census("public static class Consumer { public static int Caller() => Remote.M(0); }", library);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(artifact.Manifest.UncheckedCallSites, Is.Zero);
            Assert.That(artifact.Manifest.Claims, Is.Empty);
        }
    }

    [Test]
    public void CompanionForAnotherAssemblyIsChecked()
    {
        using var directory = new TempDirectory("sharpproof-call-library-");
        var library = Library(directory, "public static class Remote { public static int M(int n) => n; }");
        var (_, artifact) = Census("""
            using SharpProof.Attributes;
            [ContractFor(typeof(Remote))]
            public static class RemoteContracts {
                public static int M(int n) { Contract.Requires(n > 0); return 0; }
            }
            public static class Consumer { public static int Caller() => Remote.M(0); }
            """, library);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(artifact.Manifest.UncheckedCallSites, Is.Zero);
            Assert.That(artifact.Manifest.Claims.Single(static claim => claim.Kind == WorkerClaimKind.CallPrecondition).CallPrecondition!.CallerDeclared, Is.True);
        }
    }

    [Test]
    public void DeclaredClaimsRunBeforeCallersSelectedOnlyForTheirCalls()
    {
        using var project = new ShadowTestProject("""
            using SharpProof.Attributes;
            public static class A { public static int Caller() { return Z.F(1); } }
            public static class B { public static int Declared(int x) { Contract.Ensures(Contract.Result<int>() == x); return x; } }
            public static class Z { public static int F(int x) { Contract.Requires(x > 0); return x; } }
            """);
        ImmutableArray<CompilerCallablePreparation> targets = [.. project.Snapshot.Callables.OrderBy(static target => target.Entry.CallableId, StringComparer.Ordinal)];
        Assert.That(targets[0].Entry.CallableId, Does.Contain("A.Caller"));
        Assert.That(targets[SharpProofWorker.VerificationOrder(targets)[^1]].Entry.CallableId, Does.Contain("A.Caller"));
    }

    [Test]
    public void EffectsOnlyFeaturesPublishNoCallClaim()
    {
        var compilation = TestCompilation.CreateExact("EffectsOnly", [("Subject.cs", Subject("[DoesNotThrow] public static int Caller() { return F(-1); }"))]);
        TestCompilation.AssertNoErrors(compilation);
        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0", WorkerFeatureSet.Effects,
            new ClaimManifestBuilder(compilation, WorkerFeatureSet.Effects).Build(), WorkerBudgets.DefaultMaximumExpressionDepth,
            TestContext.CurrentContext.CancellationToken);
        Assert.That(artifact.Manifest.Claims.Select(static claim => claim.Kind), Has.None.EqualTo(WorkerClaimKind.CallPrecondition));
    }

    [Test]
    public async Task CachedResponseKeepsTheCallClaims()
    {
        using var project = new ShadowTestProject(Subject("public static int Caller() { return F(-1); }"), cacheEnabled: true);
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var first = await worker.VerifyAsync(project.Request, project.Snapshot, TestContext.CurrentContext.CancellationToken);
        var second = await worker.VerifyAsync(project.Request, project.Bind(), TestContext.CurrentContext.CancellationToken);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(second.Summary.CacheHit, Is.True);
            Assert.That(SingleCallClaim(second, "Subject.Caller").Result.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted));
            Assert.That(second.Manifest.Hash, Is.EqualTo(first.Manifest.Hash));
        }
    }

    private static CompilerManifestArtifact CallArtifact()
    { return CompilerTotalCallableArtifactTests.CreateArtifact(Subject("public static int Caller() { return F(-1); }")); }

    private static CompilerManifestArtifact WithCallRow(CompilerManifestArtifact artifact, Func<CompilerTotalCallPreconditionArtifact, CompilerTotalCallPreconditionArtifact> change)
    {
        System.ArgumentNullException.ThrowIfNull(artifact);
        System.ArgumentNullException.ThrowIfNull(change);
        var caller = artifact.Callables.Single(static callable => callable.Total is { CallPreconditions.Length: 1 });
        var total = caller.Total! with { CallPreconditions = [change(caller.Total.CallPreconditions[0])] };
        return artifact with { Callables = artifact.Callables.Replace(caller, caller with { Total = total }) };
    }

    [Test]
    public void ArtifactBindsEachClaimToItsRow()
    {
        var artifact = CallArtifact();
        var row = artifact.Callables.Single(static callable => callable.Total is { CallPreconditions.Length: 1 }).Total!.CallPreconditions[0];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(row.ClaimId, Is.Not.Null);
            Assert.That(artifact.Manifest.Claims.Single(static claim => claim.Kind == WorkerClaimKind.CallPrecondition).ClaimId, Is.EqualTo(row.ClaimId));
        }
    }

    [TestCase("missing")]
    [TestCase("foreign")]
    [TestCase("callee")]
    [TestCase("clause")]
    public void ArtifactRejectsAClaimRowThatDoesNotMatchTheManifest(string mutation)
    {
        System.ArgumentNullException.ThrowIfNull(mutation);
        var artifact = WithCallRow(CallArtifact(), row => mutation switch
        {
            "missing" => row with { ClaimId = null },
            "foreign" => row with { ClaimId = "spc1:foreign" },
            "callee" => row with { CalleeIdentity = "unrelated::M:Other.Helper" },
            _ => row with { ClauseOrdinal = row.ClauseOrdinal + 1 }
        });
        var json = CompilerManifestArtifactJson.SerializeProducerValidated(artifact, TestContext.CurrentContext.CancellationToken);
        Assert.Throws<System.Text.Json.JsonException>(new Action(() => CompilerManifestArtifactJson.DeserializePrepared(json, out _, cancellationToken: TestContext.CurrentContext.CancellationToken)));
    }

    private static WorkerClaimManifest Resealed(WorkerClaimManifest manifest, Func<WorkerClaimManifestEntry, WorkerClaimManifestEntry> change)
    {
        System.ArgumentNullException.ThrowIfNull(change);
        System.ArgumentNullException.ThrowIfNull(manifest);
        var claim = manifest.Claims.Single(static claim => claim.Kind == WorkerClaimKind.CallPrecondition);
        var changed = manifest with { Claims = manifest.Claims.Replace(claim, change(claim)) };
        changed.Hash = string.Empty;
        return changed;
    }

    [TestCase("missing-target", "manifest.claim_call_precondition")]
    [TestCase("blank-callee", "manifest.claim_call_precondition")]
    [TestCase("negative-clause", "manifest.claim_call_precondition")]
    [TestCase("wrong-evidence", "manifest.claim_shape")]
    public void ManifestRejectsMalformedCallClaims(string mutation, string expected)
    {
        System.ArgumentNullException.ThrowIfNull(expected);
        System.ArgumentNullException.ThrowIfNull(mutation);
        var manifest = Resealed(CallArtifact().Manifest, claim => mutation switch
        {
            "missing-target" => claim with { CallPrecondition = null },
            "blank-callee" => claim with { CallPrecondition = claim.CallPrecondition! with { Callee = " " } },
            "negative-clause" => claim with { CallPrecondition = claim.CallPrecondition! with { ClauseOrdinal = -1 } },
            _ => claim with { Evidence = WorkerClaimEvidence.Attribute }
        });
        Assert.That(WorkerProtocolJson.ValidateManifest(manifest).Errors.Select(static error => error.Code), Has.Member(expected));
    }

    [Test]
    public void ManifestRejectsATargetOnAnotherClaimKind()
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(Subject("public static int Caller() { Contract.Ensures(Contract.Result<int>() == -1); return F(-1); }"));
        var ensures = artifact.Manifest.Claims.Single(static claim => claim.Kind == WorkerClaimKind.Postcondition);
        var call = artifact.Manifest.Claims.Single(static claim => claim.Kind == WorkerClaimKind.CallPrecondition);
        var changed = artifact.Manifest with { Claims = artifact.Manifest.Claims.Replace(ensures, ensures with { CallPrecondition = call.CallPrecondition }) };
        Assert.That(WorkerProtocolJson.ValidateManifest(changed).Errors.Select(static error => error.Code), Has.Member("manifest.claim_call_precondition"));
    }

    [Test]
    public void ManifestRejectsACallClaimOrderedBeforeAPostcondition()
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(Subject("public static int Caller() { Contract.Ensures(Contract.Result<int>() == -1); return F(-1); }"));
        var ensures = artifact.Manifest.Claims.Single(static claim => claim.Kind == WorkerClaimKind.Postcondition);
        var call = artifact.Manifest.Claims.Single(static claim => claim.Kind == WorkerClaimKind.CallPrecondition);
        var changed = artifact.Manifest with
        {
            Claims = artifact.Manifest.Claims.Replace(ensures, ensures with { Ordinal = call.Ordinal }).Replace(call, call with { Ordinal = ensures.Ordinal })
        };
        Assert.That(WorkerProtocolJson.ValidateManifest(changed).Errors.Select(static error => error.Code), Has.Member("manifest.claim_order"));
    }

    [Test]
    public async Task VacuousResultIsAcceptedOnlyForAProvenCallClaim()
    {
        var response = await Verify(Subject("public static int Caller() { return F(-1); }"));
        var (_, result) = SingleCallClaim(response, "Subject.Caller");
        var changed = response with { ClaimResults = response.ClaimResults.Replace(result, result with { Vacuity = WorkerVacuityKind.NoModeledNormalReturn, ProofCore = ["core"] }) };
        Assert.That(WorkerProtocolJson.Validate(changed, response.InputHash, response.Manifest).Errors.Select(static error => error.Code), Has.Member("response.vacuity"));
    }
}

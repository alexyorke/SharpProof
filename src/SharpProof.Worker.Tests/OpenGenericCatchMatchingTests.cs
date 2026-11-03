using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// Each case first pins what its compiled source does on the CLR. Run() takes
// the instantiation that a closed reading of the handler gets wrong, and
// returns 2 when the exception escapes Target.
[TestFixture]
public sealed class OpenGenericCatchMatchingTests
{
    private const string Postcondition = "Contract.Ensures(Contract.Result<int>() == 0); ";
    private const string Types = "using SharpProof.Attributes; public class E<T> : System.Exception { } public class D<T> : E<T> { } " +
        "public class Sub : E<int> { } public class X : System.Exception { } public class Outer<T> { public class N : System.Exception { } } ";

    private static string Subject(string members)
    {
        System.ArgumentNullException.ThrowIfNull(members);
        return Types + "public static class Subject { public static int State; " + members + " }";
    }

    private static string Run(string call)
    {
        System.ArgumentNullException.ThrowIfNull(call);
        return " public static int Run() { try { return " + call + "; } catch (System.Exception) { return 2; } }";
    }

    private static string Claim(string signature, string body, string call)
    {
        System.ArgumentNullException.ThrowIfNull(signature);
        System.ArgumentNullException.ThrowIfNull(body);
        System.ArgumentNullException.ThrowIfNull(call);
        return Subject("public static int " + signature + " { " + Postcondition + "if (x > 0) return 0; " + body + " }" + Run(call));
    }

    private static readonly TestCaseData[] FalseProofs =
    [
        Case("thrown-open-caught-closed", Claim("Target<T>(E<T> e, int x)", "try { throw e; } catch (E<int>) { return 1; }", "Target<int>(new E<int>(), 0)")),
        Case("thrown-closed-caught-open", Claim("Target<T>(E<int> e, int x)", "try { throw e; } catch (E<T>) { return 1; }", "Target<int>(new E<int>(), 0)")),
        Case("both-open-distinct-parameters", Claim("Target<T, U>(E<T> e, int x)", "try { throw e; } catch (E<U>) { return 1; }", "Target<int, int>(new E<int>(), 0)")),
        Case("open-subclass-of-closed-handler", Claim("Target<T>(D<T> e, int x)", "try { throw e; } catch (E<int>) { return 1; }", "Target<int>(new D<int>(), 0)")),
        Case("closed-subclass-of-open-handler", Claim("Target<T>(Sub e, int x)", "try { throw e; } catch (E<T>) { return 1; }", "Target<int>(new Sub(), 0)")),
        Case("open-thrown-narrower-closed-handler", Claim("Target<T>(E<T> e, int x)", "try { throw e; } catch (D<int>) { return 1; }", "Target<int>(new D<int>(), 0)")),
        Case("nested-in-open-type", Claim("Target<T>(Outer<T>.N e, int x)", "try { throw e; } catch (Outer<int>.N) { return 1; }", "Target<int>(new Outer<int>.N(), 0)")),
        Case("filtered-handler", Claim("Target<T>(E<T> e, int x)", "try { throw e; } catch (E<int>) when (x == 0) { return 1; }", "Target<int>(new E<int>(), 0)")),
        Case("later-definite-handler-cannot-shadow", Claim("Target<T>(E<T> e, int x)", "try { throw e; } catch (E<int>) { return 1; } catch (System.Exception) { return 0; }", "Target<int>(new E<int>(), 0)")),
        Case("through-finally", Claim("Target<T>(E<T> e, int x)", "try { try { throw e; } finally { State = 0; } } catch (E<int>) { return 1; }", "Target<int>(new E<int>(), 0)")),
        Case("rethrown-to-outer-handler", Claim("Target<T>(E<T> e, int x)", "try { try { throw e; } catch (System.Exception) { throw; } } catch (E<int>) { return 1; }", "Target<int>(new E<int>(), 0)")),
        Case("open-handler-in-callee-frame", Subject("public static int Target(E<int> e, int x) { " + Postcondition + "if (x > 0) return 0; return Helper<int>(e); } " +
            "static int Helper<T>(E<int> e) { try { throw e; } catch (E<T>) { return 1; } }" + Run("Target(new E<int>(), 0)"))),
        Case("open-handler-in-caller-frame", Subject("public static int Target<T>(E<int> e, int x) { " + Postcondition + "if (x > 0) return 0; " +
            "try { Helper(e); return 0; } catch (E<T>) { return 1; } } static void Helper(E<int> e) { throw e; }" + Run("Target<int>(new E<int>(), 0)"))),
        // The CLR erases tuple element names, dynamic and native-integer
        // spellings, so these closed types are one runtime type.
        Case("tuple-names-on-thrown", Claim("Target(E<(int a, int b)> e, int x)", "try { throw e; } catch (E<(int, int)>) { return 1; }", "Target(new E<(int a, int b)>(), 0)")),
        Case("tuple-names-on-handler", Claim("Target(E<(int, int)> e, int x)", "try { throw e; } catch (E<(int a, int b)>) { return 1; }", "Target(new E<(int, int)>(), 0)")),
        Case("nested-tuple-names", Claim("Target(E<E<(int a, int b)>> e, int x)", "try { throw e; } catch (E<E<(int, int)>>) { return 1; }", "Target(new E<E<(int a, int b)>>(), 0)")),
        Case("dynamic-and-object", Claim("Target(E<dynamic> e, int x)", "try { throw e; } catch (E<object>) { return 1; }", "Target(new E<object>(), 0)")),
        Case("nint-and-intptr", Claim("Target(E<nint> e, int x)", "try { throw e; } catch (E<System.IntPtr>) { return 1; }", "Target(new E<nint>(), 0)"))
    ];

    // TryCatchKinds does not admit a type-parameter handler, so these never
    // reach handler matching. They guard that rejection.
    private static readonly TestCaseData[] TypeParameterHandlers =
    [
        Case("exact-closed-thrown", Claim("Target<T>(int x) where T : System.Exception", "try { throw new X(); } catch (T) { return 1; }", "Target<X>(0)")),
        Case("static-closed-thrown", Claim("Target<T>(X e, int x) where T : System.Exception", "try { throw e; } catch (T) { return 1; }", "Target<X>(new X(), 0)")),
        Case("helper-frame", Subject("public static int Target(int x) { " + Postcondition + "if (x > 0) return 0; return Helper<X>(); } " +
            "static int Helper<T>() where T : System.Exception { try { throw new X(); } catch (T) { return 1; } }" + Run("Target(0)")))
    ];

    private static TestCaseData Case(string name, string source)
    {
        System.ArgumentNullException.ThrowIfNull(name);
        System.ArgumentNullException.ThrowIfNull(source);
        return new TestCaseData(source) { TestName = "{m}(" + name + ")" };
    }

    [TestCaseSource(nameof(FalseProofs))]
    public async Task HandlerThatTheClrRunsIsNotProvenUnreachable(string source)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        Assert.That(RunOnClr(source), Is.EqualTo(1));
        await AssertPostconditionAsync(source, Is.Not.EqualTo(WorkerClaimOutcome.Proven));
    }

    [TestCaseSource(nameof(TypeParameterHandlers))]
    public async Task TypeParameterHandlerIsNotProvenUnreachable(string source)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        Assert.That(RunOnClr(source), Is.EqualTo(1));
        await AssertPostconditionAsync(source, Is.Not.EqualTo(WorkerClaimOutcome.Proven));
    }

    [TestCase("Target<T>(E<T> e)", "E<int>", "Target<int>(new E<int>())")]
    [TestCase("Target<T>(E<int> e)", "E<T>", "Target<int>(new E<int>())")]
    public async Task HandlerEffectsAreNotHidden(string signature, string handler, string call)
    {
        System.ArgumentNullException.ThrowIfNull(signature);
        System.ArgumentNullException.ThrowIfNull(handler);
        System.ArgumentNullException.ThrowIfNull(call);
        var source = Subject("[EnforcePure] public static void " + signature + " { try { throw e; } catch (" + handler + ") { State = 1; } catch (System.Exception) { } } " +
            "public static int Run() { " + call + "; return State; }");
        Assert.That(RunOnClr(source), Is.EqualTo(1));
        var result = await NativeEffectSiteVerifier.VerifyPurityAsync(Prepare(source), new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Outcome, Is.Not.TypeOf<ProvenOutcome>(), result.Reason.ToString());
    }

    [TestCase("Target<T>(E<T> e)", "E<int>", "Target<int>(new E<int>())")]
    [TestCase("Target<T>(E<int> e)", "E<T>", "Target<int>(new E<int>())")]
    public async Task HandlerThrowsAreNotHidden(string signature, string handler, string call)
    {
        System.ArgumentNullException.ThrowIfNull(signature);
        System.ArgumentNullException.ThrowIfNull(handler);
        System.ArgumentNullException.ThrowIfNull(call);
        var source = Subject("[DoesNotThrow] public static void " + signature + " { try { throw e; } catch (" + handler + ") { throw null; } catch (System.Exception) { } } " +
            "public static int Run() { try { " + call + "; return 0; } catch (System.NullReferenceException) { return 1; } }");
        Assert.That(RunOnClr(source), Is.EqualTo(1));
        var result = await NativeExceptionEffectVerifier.VerifyAsync(Prepare(source), new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Outcome, Is.Not.TypeOf<ProvenOutcome>(), result.Reason.ToString());
    }

    // A user exception's constructor is an opaque call. Its Unknown edge reaches
    // every narrower handler, so no claim can observe the exact throw alone; the
    // match at the exact site itself must still be a choice, not an exclusion.
    [TestCase("Target<T>(int x)", "try { throw new E<T>(); } catch (E<int>) { return 1; }", "Target<int>(0)")]
    [TestCase("Target<T>(int x)", "try { throw new E<int>(); } catch (E<T>) { return 1; }", "Target<int>(0)")]
    [TestCase("Target<T, U>(int x)", "try { throw new E<T>(); } catch (E<U>) { return 1; }", "Target<int, int>(0)")]
    [TestCase("Target<T>(int x)", "try { throw new D<T>(); } catch (E<int>) { return 1; }", "Target<int>(0)")]
    [TestCase("Target<T>(int x)", "try { throw new Sub(); } catch (E<T>) { return 1; }", "Target<int>(0)")]
    [TestCase("Target<T>(int x)", "try { throw new Outer<T>.N(); } catch (Outer<int>.N) { return 1; }", "Target<int>(0)")]
    public async Task ExactThrowInvolvingAnOpenTypeLeavesItsHandlerUncertain(string signature, string body, string call)
    {
        System.ArgumentNullException.ThrowIfNull(signature);
        System.ArgumentNullException.ThrowIfNull(body);
        System.ArgumentNullException.ThrowIfNull(call);
        var source = Claim(signature, body, call);
        Assert.That(RunOnClr(source), Is.EqualTo(1));
        Assert.That(UncertainMatchesAtExactThrows(source), Is.EqualTo(1));
        await AssertPostconditionAsync(source, Is.Not.EqualTo(WorkerClaimOutcome.Proven));
    }

    [TestCase("Target(int x)", "try { throw new E<string>(); } catch (E<int>) { return 1; }", "Target(0)", 2)]
    [TestCase("Target(int x)", "try { throw new Outer<string>.N(); } catch (Outer<int>.N) { return 1; }", "Target(0)", 2)]
    [TestCase("Target<T>(int x)", "try { throw new E<T>(); } catch (E<T>) { return 1; }", "Target<int>(0)", 1)]
    [TestCase("Target<T>(int x)", "try { throw new D<T>(); } catch (E<T>) { return 1; }", "Target<int>(0)", 1)]
    [TestCase("Target<T>(int x)", "try { throw new E<T>(); } catch (System.Exception) { return 1; }", "Target<int>(0)", 1)]
    public void ExactThrowWithAKnownAnswerStaysDefinite(string signature, string body, string call, int expected)
    {
        System.ArgumentNullException.ThrowIfNull(signature);
        System.ArgumentNullException.ThrowIfNull(body);
        System.ArgumentNullException.ThrowIfNull(call);
        var source = Claim(signature, body, call);
        Assert.That(RunOnClr(source), Is.EqualTo(expected));
        Assert.That(UncertainMatchesAtExactThrows(source), Is.Zero);
    }

    // Closed types keep their exact answers: an unrelated handler is excluded,
    // so these remain Proven.
    [TestCase("Target(E<string> e, int x)", "try { throw e; } catch (E<int>) { return 1; }", "Target(new E<string>(), 0)")]
    [TestCase("Target(E<string> e, int x)", "try { throw e; } catch (D<int>) { return 1; }", "Target(new E<string>(), 0)")]
    [TestCase("Target(Sub e, int x)", "try { throw e; } catch (E<string>) { return 1; }", "Target(new Sub(), 0)")]
    [TestCase("Target(Outer<string>.N e, int x)", "try { throw e; } catch (Outer<int>.N) { return 1; }", "Target(new Outer<string>.N(), 0)")]
    [TestCase("Target(X e, int x)", "try { throw e; } catch (E<int>) { return 1; }", "Target(new X(), 0)")]
    [TestCase("Target(E<(int a, string b)> e, int x)", "try { throw e; } catch (E<(int, int)>) { return 1; }", "Target(new E<(int a, string b)>(), 0)")]
    [TestCase("Target(int x)", "try { throw new System.Exception(); } catch (E<int>) { return 1; }", "Target(0)")]
    [TestCase("Target(int x)", "try { throw new System.Exception(); } catch (System.ArgumentException) { return 1; }", "Target(0)")]
    public async Task ClosedUnrelatedHandlerStaysExcluded(string signature, string body, string call)
    {
        System.ArgumentNullException.ThrowIfNull(signature);
        System.ArgumentNullException.ThrowIfNull(body);
        System.ArgumentNullException.ThrowIfNull(call);
        var source = Claim(signature, body, call);
        Assert.That(RunOnClr(source), Is.EqualTo(2));
        await AssertPostconditionAsync(source, Is.EqualTo(WorkerClaimOutcome.Proven));
    }

    // A static throw may have a narrower runtime type, with or without generics.
    [Test]
    public async Task ClosedNarrowerHandlerOfAStaticThrowStaysUncertain()
    {
        var source = Claim("Target(E<int> e, int x)", "try { throw e; } catch (D<int>) { return 1; }", "Target(new D<int>(), 0)");
        Assert.That(RunOnClr(source), Is.EqualTo(1));
        await AssertPostconditionAsync(source, Is.Not.EqualTo(WorkerClaimOutcome.Proven));
    }

    // A handler that catches for every instantiation stays definite, so an open
    // throw keeps the proof or the refutation it had.
    [TestCase("Target<T>(E<T> e, int x)", "try { throw e; } catch (System.Exception) { return 0; }", "Target<int>(new E<int>(), 0)", 0, true)]
    [TestCase("Target<T>(E<T> e, int x)", "try { throw e; } catch (E<T>) { return 0; }", "Target<int>(new E<int>(), 0)", 0, true)]
    [TestCase("Target<T>(D<T> e, int x)", "try { throw e; } catch (E<T>) { return 0; }", "Target<int>(new D<int>(), 0)", 0, true)]
    [TestCase("Target<T>(E<T> e, int x)", "try { throw e; } catch { return 0; }", "Target<int>(new E<int>(), 0)", 0, true)]
    [TestCase("Target<T>(E<T> e, int x)", "try { throw e; } catch (System.Exception) { return 1; }", "Target<int>(new E<int>(), 0)", 1, false)]
    public async Task DefiniteHandlerOfAnOpenThrowKeepsItsOutcome(string signature, string body, string call, int expected, bool proven)
    {
        System.ArgumentNullException.ThrowIfNull(signature);
        System.ArgumentNullException.ThrowIfNull(body);
        System.ArgumentNullException.ThrowIfNull(call);
        var source = Claim(signature, body, call);
        Assert.That(RunOnClr(source), Is.EqualTo(expected));
        await AssertPostconditionAsync(source, Is.EqualTo(proven ? WorkerClaimOutcome.Proven : WorkerClaimOutcome.Refuted));
    }

    private static async Task AssertPostconditionAsync(string source, NUnit.Framework.Constraints.IResolveConstraint expected)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        System.ArgumentNullException.ThrowIfNull(expected);
        using var project = new ShadowTestProject(source);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, TestContext.CurrentContext.CancellationToken);
        Assert.That(response.Errors, Is.Empty);
        var claim = response.Manifest.Claims.Single(claim => claim is null ? throw new System.ArgumentNullException(nameof(claim)) :
            (claim.Kind == WorkerClaimKind.Postcondition && claim.CallableId.Contains("Subject.Target", StringComparison.Ordinal)));
        var result = response.ClaimResults.Single(result => result is null ? throw new System.ArgumentNullException(nameof(result)) : (result.ClaimId == claim.ClaimId));
        Assert.That(result.Outcome, expected, result.Reason.ToString());
    }

    // Counts the approximate handler choices taken at `throw new` sites.
    private static int UncertainMatchesAtExactThrows(string source)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        using var project = new ShadowTestProject(source);
        var program = project.Snapshot.Callables.Single(callable => callable is null ? throw new System.ArgumentNullException(nameof(callable)) :
            (callable.Entry.CallableId.Contains("Subject.Target", StringComparison.Ordinal))).Total!.Program;
        return program.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions)).OfType<IrHavocInstruction>()
            .Count(havoc => havoc is null ? throw new System.ArgumentNullException(nameof(havoc)) :
                (havoc.Origin == IrHavocOrigin.Approximation && ExplicitThrowSites.IsExact(program.Factory, havoc.Operation)));
    }

    private static CompilerCallablePreparation Prepare(string source)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(source);
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken), out var preparations, cancellationToken: TestContext.CurrentContext.CancellationToken);
        return preparations.Single(preparation => preparation is null ? throw new System.ArgumentNullException(nameof(preparation)) : (preparation.Entry.CallableId.Contains("Subject.Target", StringComparison.Ordinal)));
    }

    // Contract.Result<T>() throws outside the rewriter, so the CLR runs the body alone.
    private static object? RunOnClr(string source)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        using var image = new MemoryStream();
        Assert.That(TestCompilation.Create("OpenGenericCatchRuntime", source.Replace(Postcondition, "", StringComparison.Ordinal)).Emit(image).Success, Is.True);
        image.Position = 0;
        var context = new System.Runtime.Loader.AssemblyLoadContext("OpenGenericCatchRuntime", isCollectible: true);
        try
        { return context.LoadFromStream(image).GetType("Subject")!.GetMethod("Run")!.Invoke(null, null); }
        finally { context.Unload(); }
    }
}

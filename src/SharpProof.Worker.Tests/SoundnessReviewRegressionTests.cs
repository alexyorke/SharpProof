using System.Runtime.Loader;
using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// Each case pairs a verifier claim with the compiled CLR behaviour that
// decides it, so a wrong outcome cannot be blamed on the expectation.
[TestFixture]
internal sealed class SoundnessReviewRegressionTests
{
    private const string Usings = "using SharpProof.Attributes; ";
    private const string AllReads = "SharpProofEffect.ReadsReceiverState | SharpProofEffect.ReadsArgumentState | " +
        "SharpProofEffect.ReadsCapturedState | SharpProofEffect.ReadsStaticState | SharpProofEffect.ReadsAmbientState";

    private const string StringTarget = "public static class Subject { public static bool Target(string a, string b) { ";
    private const string CountTarget = "public static class Subject { public static int Target(int n) { ";
    private const string CaughtLength =
        "try { return new string('x', n).Length; } catch (System.ArgumentOutOfRangeException) { return -1; } } }";
    private const string StringInputs = "Contract.Requires(a != null && b != null && a == \"x\" && b == \"y\"); ";

    [TestCase("object first = a + b; object second = a + b; return first == second;")]
    [TestCase("return object.ReferenceEquals(a + b, a + b);")]
    public async Task SeparateConcatenationsAreDistinctObjects(string body)
    {
        System.ArgumentNullException.ThrowIfNull(body);
        Assert.That(Run(StringTarget + body + " } }", "Subject", ["x", "y"]), Is.False);
        var claim = await VerifyAsync(StringTarget + StringInputs + "Contract.Ensures(Contract.Result<bool>() == true); " + body + " } }");
        Assert.That(claim.Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven), claim.Reason.ToString());
    }

    [Test]
    public async Task OneConcatenationKeepsItsIdentityThroughACopy()
    {
        const string body = "object first = a + b; object second = first; return first == second; } }";
        Assert.That(Run(StringTarget + body, "Subject", ["x", "y"]), Is.True);
        var claim = await VerifyAsync(StringTarget + StringInputs + "Contract.Ensures(Contract.Result<bool>() == true); " + body);
        Assert.That(claim.Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Refuted), claim.Reason.ToString());
    }

    [Test]
    public async Task NegativeStringLengthReachesItsCatchHandler()
    {
        Assert.That(Run(CountTarget + CaughtLength, "Subject", [-1]), Is.EqualTo(-1));
        var claim = await VerifyAsync(CountTarget + "Contract.Ensures(Contract.Result<int>() >= 0); " + CaughtLength);
        Assert.That(claim.Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven), claim.Reason.ToString());
    }

    [Test]
    public async Task CaughtStringLengthFaultDoesNotEscape()
    {
        const string target = "public static class Subject { [DoesNotThrow] public static int Target(int n) { ";
        Assert.That(Run(target + CaughtLength, "Subject", [-1]), Is.EqualTo(-1));
        var claim = await VerifyAsync(target + "Contract.Requires(n <= 10); " + CaughtLength);
        Assert.That(claim.Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Refuted), claim.Reason.ToString());
    }

    [Test]
    public async Task ValidStringLengthStillProves()
    {
        Assert.That(Run(CountTarget + CaughtLength, "Subject", [3]), Is.EqualTo(3));
        var claim = await VerifyAsync(CountTarget +
            "Contract.Requires(n >= 0 && n <= 10); Contract.Ensures(Contract.Result<int>() == n); " + CaughtLength);
        Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), claim.Reason.ToString());
    }

    [Test]
    public async Task CapturedFieldReadCountsAsARead()
    {
        const string source = "public sealed class C { public int F = 5; [EffectContract(SharpProofEffect.None)] " +
            "public int Target(bool c) { return c ? F : 0; } }";
        Assert.That(Run(source, "C", [true]), Is.EqualTo(5));
        var claim = await VerifyAsync(source);
        Assert.That(claim.Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven), claim.Reason.ToString());
    }

    [Test]
    public async Task DirectFieldReadCountsAsARead()
    {
        const string source = "public sealed class C { public int F = 5; [EffectContract(SharpProofEffect.None)] " +
            "public int Target() { return F; } }";
        Assert.That(Run(source, "C", []), Is.EqualTo(5));
        var claim = await VerifyAsync(source);
        Assert.That(claim.Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven), claim.Reason.ToString());
    }

    [Test]
    public async Task DeclaredReadsCoverACapturedFieldRead()
    {
        const string source = "public sealed class C { public int F = 5; [EffectContract(" + AllReads + ")] " +
            "public int Target(bool c) { return c ? F : 0; } }";
        var claim = await VerifyAsync(source);
        Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), claim.Reason.ToString());
    }

    [TestCase("System.ArgumentException")]
    [TestCase("System.Exception")]
    public async Task NegativeStringLengthReachesBaseHandlers(string handler)
    {
        System.ArgumentNullException.ThrowIfNull(handler);
        var body = "try { return new string('x', n).Length; } catch (" + handler + ") { return -1; } } }";
        Assert.That(Run(CountTarget + body, "Subject", [-1]), Is.EqualTo(-1));
        var claim = await VerifyAsync(CountTarget + "Contract.Ensures(Contract.Result<int>() >= 0); " + body);
        Assert.That(claim.Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven), claim.Reason.ToString());
    }

    [Test]
    public async Task UncaughtNegativeStringLengthStillEscapes()
    {
        const string target = "public static class Subject { [DoesNotThrow] public static int Target(int n) { ";
        const string body = "return new string('x', n).Length; } }";
        var thrown = Assert.Throws<System.Reflection.TargetInvocationException>(new Action(() => Run(target + body, "Subject", [-1])));
        Assert.That(thrown!.InnerException, Is.TypeOf<ArgumentOutOfRangeException>());
        var claim = await VerifyAsync(target + "Contract.Requires(n <= 10); " + body);
        Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted), claim.Reason.ToString());
    }

    [Test]
    public async Task OverlongStringIsNotProvenSafe()
    {
        const string target = "public static class Subject { [DoesNotThrow] public static int Target(int n) { ";
        const string body = "return new string('x', n).Length; } }";
        var thrown = Assert.Throws<System.Reflection.TargetInvocationException>(new Action(() => Run(target + body, "Subject", [int.MaxValue])));
        Assert.That(thrown!.InnerException, Is.TypeOf<OutOfMemoryException>());
        var claim = await VerifyAsync(target + "Contract.Requires(n >= 0); " + body);
        Assert.That(claim.Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven), claim.Reason.ToString());
    }

    [Test]
    public async Task ListsOfOpenAndClosedElementTypesCanAlias()
    {
        const string target = "using System.Collections.Generic; public static class Subject { " +
            "public static int Target<T>(List<T> a, List<int> b, T x) { ";
        const string body = "b[0] = 0; a[0] = x; return b[0]; } }";
        var shared = new List<int> { 1 };
        Assert.That(Run(target + body, "Subject", [shared, shared, 7], typeof(int)), Is.EqualTo(7));
        var claim = await VerifyWithFrameworkAsync(target +
            "Contract.Requires(a != null && b != null && a.Count > 0 && b.Count > 0); " +
            "Contract.Ensures(Contract.Result<int>() == 0); " + body);
        Assert.That(claim.Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven), claim.Reason.ToString());
    }

    [Test]
    public async Task DictionariesOfOpenAndClosedKeyTypesCanAlias()
    {
        const string target = "using System.Collections.Generic; public static class Subject { " +
            "public static int Target<K>(Dictionary<K, int> a, Dictionary<int, int> b, K key) { ";
        const string body = "b[1] = 0; a[key] = 7; return b[1]; } }";
        var shared = new Dictionary<int, int>();
        Assert.That(Run(target + body, "Subject", [shared, shared, 1], typeof(int)), Is.EqualTo(7));
        var claim = await VerifyWithFrameworkAsync(target + "Contract.Requires(a != null && b != null); " +
            "Contract.Ensures(Contract.Result<int>() == 0); " + body);
        Assert.That(claim.Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven), claim.Reason.ToString());
    }

    [Test]
    public async Task ArraysOfOpenAndClosedElementTypesCanAlias()
    {
        const string target = "public static class Subject { public static int Target<T>(T[] a, int[] b, T x) { ";
        const string body = "b[0] = 0; a[0] = x; return b[0]; } }";
        var shared = new[] { 1 };
        Assert.That(Run(target + body, "Subject", [shared, shared, 7], typeof(int)), Is.EqualTo(7));
        var claim = await VerifyWithFrameworkAsync(target +
            "Contract.Requires(a != null && b != null && a.Length > 0 && b.Length > 0); " +
            "Contract.Ensures(Contract.Result<int>() == 0); " + body);
        Assert.That(claim.Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven), claim.Reason.ToString());
    }

    [Test]
    public async Task ClosedListInstantiationsStaySeparate()
    {
        const string source = "using System.Collections.Generic; public static class Subject { " +
            "public static int Target(List<int> a, List<string> b, string x) { " +
            "Contract.Requires(a != null && b != null && a.Count > 0 && b.Count > 0); " +
            "Contract.Ensures(Contract.Result<int>() == 0); " +
            "a[0] = 0; b[0] = x; return a[0]; } }";
        var claim = await VerifyWithFrameworkAsync(source);
        Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), claim.Reason.ToString());
    }

    [Test]
    public async Task ForbiddenWriteInALoopIsStillRefuted()
    {
        const string source = "public sealed class C { public int Stored; " +
            "[EffectContract(SharpProofEffect.WritesReceiverState | SharpProofEffect.ReadsReceiverState)] " +
            "public void Target(C other, int n) { Contract.Requires(other != null && other != this && n == 2); " +
            "for (var i = 0; i < n; i++) { other.Stored = i; } } }";
        var claim = await VerifyAsync(source);
        Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted), claim.Reason.ToString());
    }

    [TestCase("for (var i = 0; i < n; i++) { Stored = Stored + 1; }")]
    [TestCase("if (n > 0) { Stored = Stored + 1; }")]
    public async Task AllowedReceiverWriteIsNeverAWitness(string body)
    {
        System.ArgumentNullException.ThrowIfNull(body);
        var source = "public sealed class C { public int Stored; " +
            "[EffectContract(SharpProofEffect.WritesReceiverState | SharpProofEffect.ReadsReceiverState)] " +
            "public void Target(int n) { Contract.Requires(n >= 0 && n <= 2); " + body + " } }";
        var claim = await VerifyAsync(source);
        Assert.That(claim.Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Refuted), claim.Reason.ToString());
    }

    private static object? Run(string source, string typeName, object?[] arguments, Type? typeArgument = null)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        System.ArgumentNullException.ThrowIfNull(typeName);
        System.ArgumentNullException.ThrowIfNull(arguments);
        using var image = new MemoryStream();
        Assert.That(TestCompilation.Create("SoundnessReview", Usings + source).Emit(image).Success, Is.True);
        image.Position = 0;
        var context = new AssemblyLoadContext("soundness-review", isCollectible: true);
        try
        {
            var type = context.LoadFromStream(image).GetType(typeName)!;
            var method = type.GetMethod("Target")!;
            if (typeArgument != null)
            { method = method.MakeGenericMethod(typeArgument); }
            return method.Invoke(method.IsStatic ? null : Activator.CreateInstance(type), arguments);
        }
        finally { context.Unload(); }
    }

    private static async Task<WorkerClaimResult> VerifyWithFrameworkAsync(string source)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        using var project = WorkerTests.TestProject.Create(Usings + source);
        project.UseNetCoreReferencePack();
        var request = project.CreateRequest(cacheEnabled: false, targetFramework: "net9.0");
        using var worker = SharpProofWorker.Create(request.Budgets);
        var response = await worker.VerifyAsync(request, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(response.Errors, Is.Empty);
        return response.ClaimResults.Single();
    }

    private static async Task<WorkerClaimResult> VerifyAsync(string source)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        using var project = new ShadowTestProject(Usings + source, cacheEnabled: false);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, TestContext.CurrentContext.CancellationToken);
        Assert.That(response.Errors, Is.Empty);
        return response.ClaimResults.Single();
    }
}

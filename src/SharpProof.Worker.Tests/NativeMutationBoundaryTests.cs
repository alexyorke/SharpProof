using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Smt;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
internal sealed class NativeMutationBoundaryTests
{
    [TestCase("System.Func<object>", false, "")]
    [TestCase("System.Action<string>", false, "")]
    [TestCase("System.Action", true, "")]
    [TestCase("string", true, "")]
    [TestCase("Carrier<object>.Producer", false, "public interface Carrier<out T> { public delegate T Producer(); }")]
    [TestCase("Holder<object>.Producer", true, "public class Holder<T> { public delegate T Producer(); }")]
    public async Task DelegateArrayStoreRetainsRuntimeCovariance(string element, bool exact, string additionalSource)
    {
        System.ArgumentNullException.ThrowIfNull(additionalSource);
        System.ArgumentNullException.ThrowIfNull(element);
        var preparation = Prepare("[DoesNotThrow] public static void Target(" + element + "[] values, " + element +
            " item) { Contract.Requires(values != null && values.Length > 0); values[0] = item; }", additionalSource);
        Assert.That(preparation.Total, Is.Not.Null);
        var result = await NativeExceptionEffectVerifier.VerifyAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Outcome, exact ? Is.TypeOf<ProvenOutcome>() : Is.Null, result.Reason.ToString());
    }

    [Test]
    public void RuntimeVariantDelegateArrayRejectsIncompatibleStore()
    {
        System.Func<object>[] values = new System.Func<string>[1];
        System.Func<object> item = () => new object();
        Assert.Throws<System.ArrayTypeMismatchException>((Action)(() => values[0] = item));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ImplicitConstructorRetainsTypeInitialization(bool throwing)
    {
        var preparation = Prepare("[DoesNotThrow] public static object Target() => new Bomb();",
            "public sealed class Bomb { " + (throwing ? "static Bomb() { throw new System.InvalidOperationException(); }" : "") + " }");
        Assert.That(preparation.Total, Is.Not.Null);
        var result = await NativeExceptionEffectVerifier.VerifyAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Outcome, throwing ? Is.Null : Is.TypeOf<ProvenOutcome>(), result.Reason.ToString());
    }

    private static CompilerCallablePreparation Prepare(string method, string additionalSource = "")
    {
        System.ArgumentNullException.ThrowIfNull(additionalSource);
        System.ArgumentNullException.ThrowIfNull(method);
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(
            "using SharpProof.Attributes; public static class C { " + method + " } " + additionalSource);
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken), out var preparations, cancellationToken: TestContext.CurrentContext.CancellationToken);
        return preparations.Single(preparation => preparation.Entry.CallableId.Contains("C.Target", StringComparison.Ordinal));
    }

    [Test]
    public async Task UnknownTypeInitializationPrecedesInstanceAllocationWitness()
    {
        var preparation = Prepare("[ZeroAllocations] public static object Target() => new Bomb();",
            "public sealed class Bomb { static Bomb() { throw null; } }");
        var result = await NativeEffectSiteVerifier.VerifyAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Outcome, Is.Null, result.Reason.ToString());
        Assert.That(result.AllocationWitness, Is.Null);
    }

    [TestCase("public int Value;", 85, WorkerClaimOutcome.Proven)]
    [TestCase("public int Value;", 38, WorkerClaimOutcome.Refuted)]
    [TestCase("public int Value { get; set; }", 85, WorkerClaimOutcome.Proven)]
    [TestCase("public int Value { get; set; }", 38, WorkerClaimOutcome.Refuted)]
    public async Task WorkerUsesCapturedCompoundReceiver(string member, int expected, WorkerClaimOutcome outcome)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var source = $$"""
            using SharpProof.Attributes;
            public sealed class Box { {{member}} }
            public static class C {
                public static int Target() {
                    Contract.Ensures(Contract.Result<int>() == {{expected}});
                    Box a = new Box(); a.Value = 3;
                    Box b = new Box(); b.Value = 5;
                    Box original = a;
                    a.Value += (a = b).Value;
                    return original.Value * 10 + b.Value;
                }
            }
            """;
        using var project = new ShadowTestProject(source);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, TestContext.CurrentContext.CancellationToken);
        Assert.That(response.Errors, Is.Empty);
        Assert.That(response.ClaimResults.Single().Outcome, Is.EqualTo(outcome));
    }

    // A store of a reference into an array is not modeled, since arrays of
    // other element types may be the same object, but it names its array: a
    // store into one the body allocated is no write a caller can see.
    [TestCase("public static string[] Target() { var a = new string[2]; a[0] = \"x\"; a[1] = \"y\"; return a; }", false)]
    [TestCase("public static T[] Target<T>(T x) { var a = new T[1]; a[0] = x; return a; }", false)]
    [TestCase("public static object[] Target() { var a = new object[3]; for (var i = 0; i < a.Length; i++) { a[i] = \"x\"; } return a; }", false)]
    [TestCase("public static string[] Target() { var a = new string[1]; a[0] += \"x\"; return a; }", false)]
    [TestCase("public static void Target(string[] a) { Contract.Requires(a != null && a.Length > 0); a[0] = \"x\"; }", true)]
    [TestCase("public static void Target(string[] p) { Contract.Requires(p != null && p.Length > 0); var a = new string[1]; a[0] = \"x\"; p[0] = \"y\"; }", true)]
    public async Task ReferenceElementStoresAreRefutedOnlyForTheCallersArrays(string method, bool refuted)
    {
        System.ArgumentNullException.ThrowIfNull(method);
        using var project = new ShadowTestProject("using SharpProof.Attributes; public static class C { [EnforcePure] " + method + " }");
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, TestContext.CurrentContext.CancellationToken);
        Assert.That(response.Errors, Is.Empty);
        var outcome = response.ClaimResults.Single().Outcome;
        Assert.That(outcome, refuted ? Is.EqualTo(WorkerClaimOutcome.Refuted) : Is.Not.EqualTo(WorkerClaimOutcome.Refuted));
    }

    [TestCase("int?", "new int?[] { null }", WorkerClaimOutcome.Unknown)]
    [TestCase("int?", "[null]", WorkerClaimOutcome.Unknown)]
    [TestCase("object", "new object[] { null }", WorkerClaimOutcome.Proven)]
    [TestCase("object", "[null]", WorkerClaimOutcome.Proven)]
    [TestCase("string", "new string[] { null }", WorkerClaimOutcome.Proven)]
    [TestCase("string", "[null]", WorkerClaimOutcome.Proven)]
    public async Task WorkerHandlesNullableInitializerWithoutManifestFailure(string element, string initializer, WorkerClaimOutcome outcome)
    {
        System.ArgumentNullException.ThrowIfNull(element);
        System.ArgumentNullException.ThrowIfNull(initializer);
        using var project = new ShadowTestProject("using SharpProof.Attributes; public static class C { " +
            "public static int Target() { Contract.Ensures(Contract.Result<int>() == 1); " + element +
            "[] values = " + initializer + "; return values.Length; } }");
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, TestContext.CurrentContext.CancellationToken);
        Assert.That(response.Errors, Is.Empty);
        Assert.That(response.ClaimResults.Single().Outcome, Is.EqualTo(outcome));
    }
}

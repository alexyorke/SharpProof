using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class NativeMutationBoundaryTests
{
    [TestCase("System.Func<object>", false, "")]
    [TestCase("System.Action<string>", false, "")]
    [TestCase("System.Action", true, "")]
    [TestCase("string", true, "")]
    [TestCase("Carrier<object>.Producer", false, "public interface Carrier<out T> { public delegate T Producer(); }")]
    [TestCase("Holder<object>.Producer", true, "public class Holder<T> { public delegate T Producer(); }")]
    public async Task DelegateArrayStoreRetainsRuntimeCovariance(string element, bool exact, string additionalSource)
    {
        var preparation = Prepare("[DoesNotThrow] public static void Target(" + element + "[] values, " + element +
            " item) { Contract.Requires(values != null && values.Length > 0); values[0] = item; }", additionalSource);
        Assert.That(preparation.Total, Is.Not.Null);
        var result = await NativeExceptionEffectVerifier.VerifyAsync(preparation, new WorkerBudgets());
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
        var result = await NativeExceptionEffectVerifier.VerifyAsync(preparation, new WorkerBudgets());
        Assert.That(result.Outcome, throwing ? Is.Null : Is.TypeOf<ProvenOutcome>(), result.Reason.ToString());
    }

    private static CompilerCallablePreparation Prepare(string method, string additionalSource = "")
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(
            "using SharpProof.Attributes; public static class C { " + method + " } " + additionalSource);
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out var preparations);
        return preparations.Single(preparation => preparation.Entry.CallableId.Contains("C.Target", StringComparison.Ordinal));
    }

    [Test]
    public async Task UnknownTypeInitializationPrecedesInstanceAllocationWitness()
    {
        var preparation = Prepare("[ZeroAllocations] public static object Target() => new Bomb();",
            "public sealed class Bomb { static Bomb() { throw null; } }");
        var result = await NativeEffectSiteVerifier.VerifyAsync(preparation, new WorkerBudgets());
        Assert.That(result.Outcome, Is.Null, result.Reason.ToString());
        Assert.That(result.AllocationWitness, Is.Null);
    }

    [TestCase("public int Value;", 85, WorkerClaimOutcome.Proven)]
    [TestCase("public int Value;", 38, WorkerClaimOutcome.Refuted)]
    [TestCase("public int Value { get; set; }", 85, WorkerClaimOutcome.Proven)]
    [TestCase("public int Value { get; set; }", 38, WorkerClaimOutcome.Refuted)]
    public async Task WorkerUsesCapturedCompoundReceiver(string member, int expected, WorkerClaimOutcome outcome)
    {
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
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        Assert.That(response.ClaimResults.Single().Outcome, Is.EqualTo(outcome));
    }

    [TestCase("int?", "new int?[] { null }", WorkerClaimOutcome.Unknown)]
    [TestCase("int?", "[null]", WorkerClaimOutcome.Unknown)]
    [TestCase("object", "new object[] { null }", WorkerClaimOutcome.Proven)]
    [TestCase("object", "[null]", WorkerClaimOutcome.Proven)]
    [TestCase("string", "new string[] { null }", WorkerClaimOutcome.Proven)]
    [TestCase("string", "[null]", WorkerClaimOutcome.Proven)]
    public async Task WorkerHandlesNullableInitializerWithoutManifestFailure(string element, string initializer, WorkerClaimOutcome outcome)
    {
        using var project = new ShadowTestProject("using SharpProof.Attributes; public static class C { " +
            "public static int Target() { Contract.Ensures(Contract.Result<int>() == 1); " + element +
            "[] values = " + initializer + "; return values.Length; } }");
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        Assert.That(response.ClaimResults.Single().Outcome, Is.EqualTo(outcome));
    }
}

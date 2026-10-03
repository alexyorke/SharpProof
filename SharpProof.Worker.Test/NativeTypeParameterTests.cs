using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// Type-parameter values are opaque: stored, passed, returned and type-tested,
// with no operator, conversion or default applied to them.
[TestFixture]
public sealed class NativeTypeParameterTests
{
    private const string IsNumber = """
        public static bool Target<T>(T value) {
            if (value is sbyte) return true;
            if (value is int) return true;
            if (value is ulong) return true;
            if (value is decimal) return true;
            return false;
        }
        """;

    [Test]
    public async Task TypeTestsNeitherThrowNorAllocateNorWrite()
    {
        var preparation = Prepare("[DoesNotThrow, ZeroAllocations, EnforcePure] " + IsNumber);
        var exceptions = await NativeExceptionEffectVerifier.VerifyAsync(preparation, new WorkerBudgets());
        var allocations = await NativeEffectSiteVerifier.VerifyAsync(preparation, new WorkerBudgets());
        var purity = await NativeEffectSiteVerifier.VerifyPurityAsync(preparation, new WorkerBudgets());
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exceptions.Outcome, Is.TypeOf<ProvenOutcome>(), exceptions.Reason.ToString());
            Assert.That(allocations.Outcome, Is.TypeOf<ProvenOutcome>(), allocations.Reason.ToString());
            Assert.That(purity.Outcome, Is.TypeOf<ProvenOutcome>(), purity.Reason.ToString());
        }
    }

    [Test]
    public void TypeTestsOnTypeParametersDoNotAllocateAtRuntime()
    {
        // The ZeroAllocations proof relies on the JIT folding `box T; isinst`.
        using var image = new MemoryStream();
        Assert.That(TestCompilation.Create("TypeTestRuntime",
            "using SharpProof.Attributes; public static class C { " + IsNumber + " }").Emit(image).Success, Is.True);
        image.Position = 0;
        var context = new System.Runtime.Loader.AssemblyLoadContext("TypeTestRuntime", isCollectible: true);
        try
        {
            var generic = context.LoadFromStream(image).GetType("C")!.GetMethod("Target")!;
            using (Assert.EnterMultipleScope())
            {
                Assert.That(MeasureAllocation(generic, 5), Is.Zero);
                Assert.That(MeasureAllocation(generic, 5L), Is.Zero);
                Assert.That(MeasureAllocation(generic, 2.5), Is.Zero);
                Assert.That(MeasureAllocation(generic, "text"), Is.Zero);
            }
        }
        finally { context.Unload(); }
    }

    private static long MeasureAllocation<TValue>(System.Reflection.MethodInfo generic, TValue argument)
    {
        var target = generic.MakeGenericMethod(typeof(TValue)).CreateDelegate<Func<TValue, bool>>();
        for (var warmup = 0; warmup < 1000; warmup++)
        { _ = target(argument); }
        var before = GC.GetAllocatedBytesForCurrentThread();
        _ = target(argument);
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [TestCase("return value;", true)]
    [TestCase("T copy = value; return copy;", true)]
    [TestCase("return default(T);", false)]
    [TestCase("return value == null ? value : value;", false)]
    public async Task OpaqueValuesFlowButOperatorsAbstain(string body, bool supported)
    {
        var preparation = Prepare("[DoesNotThrow] public static T Target<T>(T value) { " + body + " }");
        var result = await NativeExceptionEffectVerifier.VerifyAsync(preparation, new WorkerBudgets());
        if (supported)
        { Assert.That(result.Outcome, Is.TypeOf<ProvenOutcome>(), result.Reason.ToString()); }
        else
        {
            Assert.That(result.Outcome, Is.Null);
            Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.UnsupportedBody));
        }
    }

    private static CompilerCallablePreparation Prepare(string method)
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(
            "using SharpProof.Attributes; public static class C { " + method + " }");
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out var preparations);
        return preparations.Single(preparation => preparation.EffectClaims.Length != 0);
    }
}

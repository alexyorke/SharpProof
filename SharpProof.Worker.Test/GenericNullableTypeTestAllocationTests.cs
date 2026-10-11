using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// A type test of a type-parameter value against a closed Nullable<X> compiles
// to `box; isinst Nullable<X>`. Neither JIT folds that box: a Nullable T with a
// value allocates in Release and Debug, and a non-nullable value-type T
// allocates in Debug.
public sealed class GenericNullableTypeTestAllocationTests
{
    private static readonly (string Members, string Run, bool ReleaseAllocates)[] s_nullable =
    [
        ("public static bool Target<T>(T value) { return value is int?; }",
            "Target<int?>(x == 0 ? null : x)", true),
        ("public static bool Target<T>(T value) { if (value is int?) return true; return false; }",
            "Target<int?>(x == 0 ? null : x)", true),
        ("public static bool Target<T>(T value) { return !(value is int?); }",
            "Target<int?>(x == 0 ? null : x)", true),
        ("public static bool Target<T>(T value) { return value is System.Nullable<int>; }",
            "Target<int?>(x == 0 ? null : x)", true),
        ("public static bool Target<T>(T value) { return value is E?; }",
            "Target<E?>(x == 0 ? null : (E)x)", true),
        ("public static bool Target<T>(T value) { return value is S?; }",
            "Target<S?>(x == 0 ? null : new S { A = x })", true),
        ("public static bool Target<T>(T value) { return value is int?; }",
            "Target<S?>(x == 0 ? null : new S { A = x })", true),
        ("public static bool Target<T>(T value) where T : struct { return value is int?; }",
            "Target<int>(x)", false),
        ("public static bool Target<T>(T value) { return value is S?; }",
            "Target<S>(new S { A = x })", false)
    ];

    private static readonly (string Members, string Run)[] s_nonNullable =
    [
        ("public static bool Target<T>(T value) { return value is int; }", "Target<int?>(x == 0 ? null : x)"),
        ("public static bool Target<T>(T value) { if (value is S) return true; return false; }", "Target<S?>(x == 0 ? null : new S { A = x })"),
        ("public static bool Target<T>(T value) { return value is System.IComparable; }", "Target<int?>(x == 0 ? null : x)"),
        ("public static bool Target<T>(T value) { return value is string; }", "Target<int?>(x == 0 ? null : x)")
    ];

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(5)]
    [TestCase(6)]
    [TestCase(7)]
    [TestCase(8)]
    public async Task NullableTypeTestCannotProveZeroAllocations(int index)
    {
        var (members, run, releaseAllocates) = s_nullable[index];
        var source = Source(members, run);
        Assert.That(AllocatedBytes(source, OptimizationLevel.Debug, 1), Is.GreaterThan(0), "Debug x=1");
        Assert.That(AllocatedBytes(source, OptimizationLevel.Release, 1), releaseAllocates ? Is.GreaterThan(0) : Is.Zero, "Release x=1");
        foreach (var optimization in new[] { OptimizationLevel.Debug, OptimizationLevel.Release })
        {
            if (optimization == OptimizationLevel.Release && !releaseAllocates)
            { continue; }
            var native = await NativeEffectSiteVerifier.VerifyAsync(Prepare(source, optimization), new WorkerBudgets());
            Assert.That(native.Outcome, Is.Not.TypeOf<ProvenOutcome>(), optimization + ": " + native.Reason);
        }
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public async Task NonNullableTypeTestStillProvesZeroAllocations(int index)
    {
        var (members, run) = s_nonNullable[index];
        var source = Source(members, run);
        foreach (var optimization in new[] { OptimizationLevel.Debug, OptimizationLevel.Release })
        {
            foreach (var input in new[] { 0, 1 })
            { Assert.That(AllocatedBytes(source, optimization, input), Is.Zero, optimization + " x=" + input); }
            var native = await NativeEffectSiteVerifier.VerifyAsync(Prepare(source, optimization), new WorkerBudgets());
            Assert.That(native.Outcome, Is.TypeOf<ProvenOutcome>(), optimization + ": " + native.Reason);
        }
    }

    private static string Source(string members, string run)
    {
        return "using SharpProof.Attributes; public enum E { A, B } public struct S { public int A; } " +
            "public static class C { [ZeroAllocations] " + members + " public static int Run(int x) => " + run + " ? 1 : 0; }";
    }

    private static CompilerCallablePreparation Prepare(string source, OptimizationLevel optimization)
    {
        var compilation = TestCompilation.Create("GenericNullableTypeTest", ("Subject.cs", source));
        compilation = compilation.WithOptions(compilation.Options.WithOptimizationLevel(optimization));
        TestCompilation.AssertNoErrors(compilation);
        var discovery = new ClaimManifestBuilder(compilation).Build();
        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0", WorkerFeatureSet.All, discovery,
            WorkerBudgets.DefaultMaximumExpressionDepth, CancellationToken.None);
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out var preparations);
        return preparations.Single(preparation => preparation.Entry.CallableId.Contains("C.Target", StringComparison.Ordinal));
    }

    private static long AllocatedBytes(string source, OptimizationLevel optimization, int input)
    {
        var compilation = TestCompilation.Create("GenericNullableTypeTestRuntime", source);
        using var image = new MemoryStream();
        var emission = compilation.WithOptions(compilation.Options.WithOptimizationLevel(optimization)).Emit(image);
        Assert.That(emission.Success, Is.True, string.Join("\n", emission.Diagnostics));
        image.Position = 0;
        var context = new System.Runtime.Loader.AssemblyLoadContext("GenericNullableTypeTestRuntime", isCollectible: true);
        try
        {
            var run = context.LoadFromStream(image).GetType("C")!.GetMethod("Run")!.CreateDelegate<Func<int, int>>();
            for (var repeat = 0; repeat < 3; repeat++)
            { run(input); }
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var repeat = 0; repeat < 32; repeat++)
            { run(input); }
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
        finally
        { context.Unload(); }
    }
}

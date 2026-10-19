using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// When a type-parameter value converts implicitly to the tested type, the
// compiler emits only `box; ldnull; cgt.un`. A Debug build's JIT does not fold
// that box, so the test allocates for a value-type argument. Release folds it.
[TestFixture]
public sealed class GenericImplicitTypeTestAllocationTests
{
    private const string Run = " public static int Run(int x) => Target<int>(x) ? 1 : 0; }";

    private static readonly ImmutableArray<string> s_implicit =
    [
        "public static bool Target<T>(T value) { return value is object; }" + Run,
        "public static bool Target<T>(T value) { if (value is object) return true; return false; }" + Run,
        "public static bool Target<T>(T value) { return !(value is object); }" + Run,
        "public static bool Target<T>(T value) { return value is object _; }" + Run,
        "public static bool Target<T>(T value) where T : System.IComparable { return value is System.IComparable; }" + Run,
        "public static bool Target<T>(T value) where T : notnull { return value is object; }" + Run,
        "public static bool Target<T>(T value) { T local = default(T); return local is object; }" + Run,
        "public static bool Target<T>(T value) { return Box<T>.F(); }" + Run +
            " public static class Box<T> { public static bool F() { T local = default(T); return local is object; } }",
        "public static bool Target<T>(T value) { return F<int>(); } static bool F<U>() { U local = default(U); return local is object; }" + Run
    ];

    private static readonly ImmutableArray<string> s_folded =
    [
        "public static bool Target<T>(T value) { if (value is int) return true; return false; }" + Run,
        "public static bool Target<T>(T value) { return value is System.IComparable; }" + Run,
        "public static bool Target<T>(T value) { return value is string; }" + Run
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
    public async Task DebugImplicitTypeTestCannotProveZeroAllocations(int index)
    {
        if (index < 0)
        { throw new System.ArgumentException("Value must be initialized.", nameof(index)); }

        var source = Source(s_implicit[index]);
        foreach (var input in ImmutableArray.Create(0, 1))
        { Assert.That(AllocatedBytes(source, OptimizationLevel.Debug, input), Is.GreaterThan(0), "x=" + input); }
        var native = await NativeEffectSiteVerifier.VerifyAsync(Prepare(source, OptimizationLevel.Debug), new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(native.Outcome, Is.Not.TypeOf<ProvenOutcome>(), native.Reason.ToString());
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(4)]
    [TestCase(6)]
    public async Task ReleaseImplicitTypeTestStillProvesZeroAllocations(int index)
    {
        if (index < 0)
        { throw new System.ArgumentException("Value must be initialized.", nameof(index)); }

        var source = Source(s_implicit[index]);
        foreach (var input in ImmutableArray.Create(0, 1))
        { Assert.That(AllocatedBytes(source, OptimizationLevel.Release, input), Is.Zero, "x=" + input); }
        var native = await NativeEffectSiteVerifier.VerifyAsync(Prepare(source, OptimizationLevel.Release), new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(native.Outcome, Is.TypeOf<ProvenOutcome>(), native.Reason.ToString());
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public async Task DebugIsinstTypeTestStillProvesZeroAllocations(int index)
    {
        if (index < 0)
        { throw new System.ArgumentException("Value must be initialized.", nameof(index)); }

        var source = Source(s_folded[index]);
        foreach (var optimization in ImmutableArray.Create(OptimizationLevel.Debug, OptimizationLevel.Release))
        {
            foreach (var input in ImmutableArray.Create(0, 1))
            { Assert.That(AllocatedBytes(source, optimization, input), Is.Zero, optimization + " x=" + input); }
        }
        var native = await NativeEffectSiteVerifier.VerifyAsync(Prepare(source, OptimizationLevel.Debug), new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(native.Outcome, Is.TypeOf<ProvenOutcome>(), native.Reason.ToString());
    }

    private static string Source(string members)
    {
        System.ArgumentNullException.ThrowIfNull(members);
        var split = members.IndexOf(" public static class Box", StringComparison.Ordinal);
        var attributed = "[ZeroAllocations] " + (split < 0 ? members : members[..split]);
        return "using SharpProof.Attributes; public static class C { " + attributed + (split < 0 ? "" : members[split..]);
    }

    private static CompilerCallablePreparation Prepare(string source, OptimizationLevel optimization)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        var compilation = TestCompilation.Create("GenericImplicitTypeTest", ("Subject.cs", source));
        compilation = compilation.WithOptions(compilation.Options.WithOptimizationLevel(optimization));
        TestCompilation.AssertNoErrors(compilation);
        var discovery = new ClaimManifestBuilder(compilation).Build();
        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0", WorkerFeatureSet.All, discovery,
            WorkerBudgets.DefaultMaximumExpressionDepth, TestContext.CurrentContext.CancellationToken);
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken), out var preparations, cancellationToken: TestContext.CurrentContext.CancellationToken);
        return preparations.Single(preparation => preparation is null ? throw new System.ArgumentNullException(nameof(preparation)) : (preparation.Entry.CallableId.Contains("C.Target", StringComparison.Ordinal)));
    }

    private static long AllocatedBytes(string source, OptimizationLevel optimization, int input)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        var compilation = TestCompilation.Create("GenericImplicitTypeTestRuntime", source);
        using var image = new MemoryStream();
        var emission = compilation.WithOptions(compilation.Options.WithOptimizationLevel(optimization)).Emit(image);
        Assert.That(emission.Success, Is.True, string.Join("\n", emission.Diagnostics));
        image.Position = 0;
        var context = new System.Runtime.Loader.AssemblyLoadContext("GenericImplicitTypeTestRuntime", isCollectible: true);
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

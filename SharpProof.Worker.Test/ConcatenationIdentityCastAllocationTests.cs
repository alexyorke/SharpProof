using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// An explicit identity cast to string emits no code, so the compiler splices
// a `+` concatenation beneath it into the enclosing chain. With more than four
// spliced operands it calls the array overload, which allocates even when only
// one operand is nonempty.
[TestFixture]
public sealed class ConcatenationIdentityCastAllocationTests
{
    private const string Empty = "(x == 0 ? \"\" : \"\")";
    private const string Letter = "(x == 0 ? \"a\" : \"b\")";

    private static readonly string[] s_spliced =
    [
        "return (" + Letter + " + (string)(" + Empty + " + " + Empty + ") + " + Empty + " + " + Empty + ").Length;",
        "return ((string)(" + Letter + " + " + Empty + ") + " + Empty + " + " + Empty + " + " + Empty + ").Length;",
        "return (" + Letter + " + (string)((string)(" + Empty + " + " + Empty + ")) + " + Empty + " + " + Empty + ").Length;",
        "return (" + Letter + " + (string)(" + Empty + " + " + Empty + " + " + Empty + ") + " + Empty + ").Length;",
        "string a = " + Letter + "; string e = " + Empty + "; return (a + (string)(e + e) + e + e).Length;",
        "string a = " + Letter + "; string e = " + Empty + "; return ((string)(a + e) + (x == 5 ? e : e) + e + e).Length;"
    ];

    private static readonly string[] s_separate =
    [
        "return (" + Letter + " + (string)(" + Empty + " + " + Empty + ") + " + Empty + ").Length;",
        "string t = (string)(" + Empty + " + " + Empty + "); return (" + Letter + " + t + " + Empty + " + " + Empty + ").Length;",
        "return ((string)(" + Letter + " + " + Empty + ")).Length;"
    ];

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(5)]
    public async Task SplicedCastConcatenationCannotProveZeroAllocations(int index)
    {
        var source = Source(s_spliced[index]);
        foreach (var optimization in new[] { OptimizationLevel.Debug, OptimizationLevel.Release })
        {
            foreach (var input in new[] { 0, 1 })
            { Assert.That(AllocatedBytes(source, optimization, input), Is.GreaterThan(0), optimization + " x=" + input); }
        }
        var native = await NativeEffectSiteVerifier.VerifyAsync(Prepare(source), new WorkerBudgets());
        Assert.That(native.Outcome, Is.Not.TypeOf<ProvenOutcome>(), native.Reason.ToString());
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public async Task ShortCastConcatenationStillProvesZeroAllocations(int index)
    {
        var source = Source(s_separate[index]);
        foreach (var optimization in new[] { OptimizationLevel.Debug, OptimizationLevel.Release })
        {
            foreach (var input in new[] { 0, 1 })
            { Assert.That(AllocatedBytes(source, optimization, input), Is.Zero, optimization + " x=" + input); }
        }
        var native = await NativeEffectSiteVerifier.VerifyAsync(Prepare(source), new WorkerBudgets());
        Assert.That(native.Outcome, Is.TypeOf<ProvenOutcome>(), native.Reason.ToString());
    }

    [Test]
    public async Task NonemptyCastConcatenationStillRefutesZeroAllocations()
    {
        var source = Source("return (" + Letter + " + (string)(" + Letter + " + " + Empty + ")).Length;");
        Assert.That(AllocatedBytes(source, OptimizationLevel.Release, 0), Is.GreaterThan(0));
        var native = await NativeEffectSiteVerifier.VerifyAsync(Prepare(source), new WorkerBudgets());
        Assert.That(native.Outcome, Is.TypeOf<RefutedOutcome>(), native.Reason.ToString());
    }

    private static string Source(string body)
    {
        return "using SharpProof.Attributes; public static class C { [ZeroAllocations] public static int Target(int x) { " + body + " } }";
    }

    private static CompilerCallablePreparation Prepare(string source)
    {
        var json = CompilerManifestArtifactJson.SerializeProducerValidated(CompilerTotalCallableArtifactTests.CreateArtifact(source));
        CompilerManifestArtifactJson.DeserializePrepared(json, out var preparations);
        return preparations.Single();
    }

    private static long AllocatedBytes(string source, OptimizationLevel optimization, int input)
    {
        var compilation = TestCompilation.Create("ConcatIdentityCastRuntime", source);
        using var image = new MemoryStream();
        var emission = compilation.WithOptions(compilation.Options.WithOptimizationLevel(optimization)).Emit(image);
        Assert.That(emission.Success, Is.True, string.Join("\n", emission.Diagnostics));
        image.Position = 0;
        var context = new System.Runtime.Loader.AssemblyLoadContext("ConcatIdentityCastRuntime", isCollectible: true);
        try
        {
            var run = context.LoadFromStream(image).GetType("C")!.GetMethod("Target")!.CreateDelegate<Func<int, int>>();
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

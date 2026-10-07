using System.Collections.Immutable;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Gates.Corpus;
using SharpProof.Host;
using SharpProof.Worker.Protocol;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class AllocationOracleCompilationOptionsTests
{
    private const string StaticBody = "System.Func<int, int> action = new System.Func<int, int>(Target); return x;";
    private const string NullReceiverBody = "System.Func<string> action = new System.Func<string>(((string)null).Trim); return x;";

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task LegacyAllocationBodiesUseTheirProofCompilation(bool nullReceiver, bool release)
    {
        var source = "public static class C { public static int Target(int x) { " +
            (nullReceiver ? NullReceiverBody : StaticBody) + " } }";
        var compilation = Prepare(source, allocations: true, release);
        var measured = Measure<int>(compilation, "C");
        Assert.That(measured.Throws, Is.EqualTo(nullReceiver && !release ? 32 : 0));
        Assert.That(measured.FailureType, nullReceiver && !release ? Is.EqualTo(typeof(ArgumentException)) : Is.Null);
        Assert.That(measured.Bytes, release ? Is.Zero : Is.GreaterThan(0));
        if (measured.Throws == 0)
        { Assert.That(measured.Result, Is.EqualTo(7)); }
        var report = await NativeEffectOracleGate.ObserveAsync(compilation, ["sample"], RepositoryLayout.FindRoot(), "test", 1, allocations: true);
        var row = report.Rows.Single();
        await LogAsync($"nullReceiver={nullReceiver}; release={release}", measured.Bytes, measured.Il, row);
        Assert.That(row.NativeOutcome, Is.EqualTo(release ? WorkerClaimOutcome.Proven :
            nullReceiver ? WorkerClaimOutcome.Unknown : WorkerClaimOutcome.Refuted));
        if (!release && nullReceiver)
        {
            Assert.That(row.RuntimeOracle, Is.EqualTo("NotRun"));
            Assert.That(row.RuntimeChecks, Is.Zero);
        }
        else
        {
            Assert.That(row.RuntimeOracle, Is.EqualTo("Confirmed"));
            Assert.That(row.RuntimeChecks, Is.EqualTo(1));
            Assert.That(row.AllocatedBytes, release ? Is.Zero : Is.GreaterThan(0));
            Assert.That(row.AllocationIlOracle, Is.EqualTo(release ? "NoReachableAllocationOpcode" : "PotentialAllocationOpcode"));
        }
        Assert.That(report.RuntimeContradictions, Is.Zero);
    }

    [Test]
    public async Task LegacyNullReceiverReleaseDoesNotThrow()
    {
        var compilation = Prepare("public static class C { public static int Target(int x) { " + NullReceiverBody + " } }",
            allocations: false, release: true);
        var measured = Measure<int>(compilation, "C");
        Assert.That(measured.Throws, Is.Zero);
        Assert.That(measured.Result, Is.EqualTo(7));
        Assert.That(measured.Bytes, Is.Zero);
        var report = await NativeEffectOracleGate.ObserveAsync(compilation, ["sample"], RepositoryLayout.FindRoot(), "test", 1);
        var row = report.Rows.Single();
        await LogAsync("nullReceiver=true; contract=DoesNotThrow; release=true", measured.Bytes, measured.Il, row);
        Assert.That(row.NativeOutcome, Is.EqualTo(WorkerClaimOutcome.Proven));
        Assert.That(row.RuntimeOracle, Is.EqualTo("NotRun"));
        Assert.That(report.RuntimeContradictions, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task EscapingDelegateStillAllocates(bool release)
    {
        var compilation = Prepare("public static class C { static int Echo(int x) => x;\n" +
            "public static System.Func<int, int> Target(int x) { return new System.Func<int, int>(Echo); } }",
            allocations: true, release);
        var measured = Measure<Func<int, int>>(compilation, "C");
        Assert.That(measured.Throws, Is.Zero);
        Assert.That(measured.Bytes, Is.GreaterThan(0));
        Assert.That(measured.Result(7), Is.EqualTo(7));
        var report = await NativeEffectOracleGate.ObserveAsync(compilation, ["sample"], RepositoryLayout.FindRoot(), "test", 1, allocations: true);
        var row = report.Rows.Single();
        await LogAsync($"escapes=true; release={release}", measured.Bytes, measured.Il, row);
        Assert.That(row.NativeOutcome, Is.EqualTo(WorkerClaimOutcome.Refuted));
        Assert.That(row.RuntimeOracle, Is.EqualTo("Confirmed"));
        Assert.That(row.AllocatedBytes, Is.GreaterThan(0));
        Assert.That(row.AllocationIlOracle, Is.EqualTo("PotentialAllocationOpcode"));
        Assert.That(report.RuntimeContradictions, Is.Zero);
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task PositiveAndNegativeEmissionCachesAreIndependent(bool nullReceiver, bool positiveFirst)
    {
        var source = "public static class Positive { public static int Target(int x) { " +
            (nullReceiver ? NullReceiverBody : StaticBody) + " } }\n" +
            "public static class Negative { public static int Target(int x) { object value = x; return x; } }";
        var document = Document(source, positiveFirst);
        var compilation = OpenSourceCorpusRunner.PrepareExceptionProbe(document, CancellationToken.None, allocations: true);
        var measured = Measure<int>(compilation, "Positive");
        Assert.That(measured.Bytes, Is.Zero);
        Assert.That(measured.Throws, Is.Zero);
        var report = await NativeEffectOracleGate.ObserveAsync(compilation, ["a", "b"], RepositoryLayout.FindRoot(), "test", 2, allocations: true);
        var positive = report.Rows.Single(row => row.MethodId == (positiveFirst ? "a" : "b"));
        var negative = report.Rows.Single(row => row.MethodId == (positiveFirst ? "b" : "a"));
        await LogAsync($"nullReceiver={nullReceiver}; positiveFirst={positiveFirst}", measured.Bytes, measured.Il, positive);
        await TestContext.Progress.WriteLineAsync($"negative native={negative.NativeOutcome}; oracle={negative.RuntimeOracle}; bytes32={negative.AllocatedBytes}; ILOracle={negative.AllocationIlOracle}");
        Assert.That(positive.NativeOutcome, Is.EqualTo(WorkerClaimOutcome.Proven));
        Assert.That(positive.RuntimeOracle, Is.EqualTo("Confirmed"));
        Assert.That(positive.AllocatedBytes, Is.Zero);
        Assert.That(positive.AllocationIlOracle, Is.EqualTo("NoReachableAllocationOpcode"));
        // Preserve the existing Refuted oracle policy that observes source
        // allocation sites in Debug, independently of the positive image.
        Assert.That(negative.NativeOutcome, Is.EqualTo(WorkerClaimOutcome.Refuted));
        Assert.That(negative.RuntimeOracle, Is.EqualTo("Confirmed"));
        Assert.That(negative.AllocatedBytes, Is.GreaterThan(0));
        Assert.That(negative.AllocationIlOracle, Is.EqualTo("PotentialAllocationOpcode"));
        Assert.That(report.RuntimeContradictions, Is.Zero);
    }

    private static CSharpCompilation Prepare(string source, bool allocations, bool release)
    {
        var compilation = OpenSourceCorpusRunner.PrepareExceptionProbe(Document(source), CancellationToken.None, allocations);
        return compilation.WithOptions(compilation.Options.WithOptimizationLevel(release ? OptimizationLevel.Release : OptimizationLevel.Debug));
    }

    private static OpenSourceCorpusDocument Document(string source, bool positiveFirst = true)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var methods = ImmutableArray.CreateBuilder<OpenSourceCorpusMethod>();
        foreach (var method in tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Where(method => method.Identifier.ValueText == "Target"))
        {
            var owner = ((ClassDeclarationSyntax)method.Parent!).Identifier.ValueText;
            var id = owner == "C" ? "sample" : (owner == "Positive") == positiveFirst ? "a" : "b";
            var span = method.GetLocation().GetLineSpan();
            methods.Add(new(id, "test", "sample.cs", span.StartLinePosition.Line + 1, span.EndLinePosition.Line + 1,
                "test", "Target", "effects", CorpusVerdict.Unknown, CorpusSupport.Supported));
        }
        return new(2, [], [new("test", "sample.cs", "test", source)], methods.ToImmutable());
    }

    private static (long Bytes, TResult Result, int Throws, Type? FailureType, string Il) Measure<TResult>(CSharpCompilation compilation, string owner)
    {
        using var image = new MemoryStream();
        Assert.That(compilation.Emit(image).Success, Is.True);
        image.Position = 0;
        var runtime = new AssemblyLoadContext("ExactLegacyAllocationOracle", isCollectible: true);
        try
        {
            var method = runtime.LoadFromStream(image).GetType(owner)!.GetMethod("Target")!;
            var target = method.CreateDelegate<Func<int, TResult>>();
            for (var repeat = 0; repeat < 3; repeat++)
            {
                try
                {
                    _ = target(7);
                }
                catch (ArgumentException)
                { }
            }
            TResult result = default!;
            Exception? failure = null;
            var throws = 0;
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var repeat = 0; repeat < 32; repeat++)
            {
                try
                {
                    result = target(7);
                }
                catch (ArgumentException exception)
                {
                    failure = exception;
                    throws++;
                }
            }
            var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            return (bytes, result, throws, failure?.GetType(), Convert.ToHexString(method.GetMethodBody()!.GetILAsByteArray()!));
        }
        finally { runtime.Unload(); }
    }

    private static Task LogAsync(string shape, long bytes, string il, NativeEffectOracleRow row)
    {
        return TestContext.Progress.WriteLineAsync($"{shape}; directBytes32={bytes}; directIL={il}; native={row.NativeOutcome}; oracle={row.RuntimeOracle}; oracleBytes32={row.AllocatedBytes}; ILOracle={row.AllocationIlOracle}");
    }
}

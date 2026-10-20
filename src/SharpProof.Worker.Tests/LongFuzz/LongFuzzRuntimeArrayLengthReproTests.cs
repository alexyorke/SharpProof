using System.Collections.Immutable;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace SharpProof.Worker.Test.LongFuzz;

[TestFixture]
[NonParallelizable]
public sealed class LongFuzzRuntimeArrayLengthReproTests
{
    private const string OriginalHit = """
        // LongFuzz analyzer-pipeline seed 448627802 kind RefutedNotObserved
        // claim: [EnforcePure] requires !((a == null ? 0 : a.Length) != 9)
        // worker: Refuted None
        // runtime: no violation on 0 inputs
        // No tested input violates the claim the worker refuted (0 inputs).
        using System;
        using SharpProof.Attributes;
        public static class C
        {
            public static int State;
            public static int Other;
            private static int Need(int v) { Contract.Requires(v > 0); return v - 1; }
            private static int Twice(int v) { Contract.Ensures(Contract.Result<int>() == unchecked(Contract.Old(v) * 2)); return unchecked(v * 2); }
            private static int Bump(int v) { State++; return v; }
            private static int Clamp(int v) => v < 0 ? 0 : v > 9 ? 9 : v;
            private static int Thrower(int v) { if (v == 3) throw new InvalidOperationException(); return v; }
            private static int RuntimeValue(int v) => v;
            private static int Sum(int[] items) { var total = 0; foreach (var item in items) total = unchecked(total + item); return total; }
            private static bool Positive(int v) { Contract.Ensures(Contract.Result<bool>() == (Contract.Old(v) > 0)); return v > 0; }
            [EnforcePure]
            public static int Target(int x, int y, int[]? a, string? s)
            {
                Contract.Requires(!((a == null ? 0 : a.Length) != 9));
                
                int v0 = Bump((x > 0 ? (a == null ? 0 : a.Length) : (a == null ? 0 : a.Length))); int v1 = (a != null ? Sum(a) : 0); return y;
            }
            public static bool Pre(int x, int y, int[]? a, string? s) => !((a == null ? 0 : a.Length) != 9);
            public static bool Post(int r, int x0, int y0, int s0, int st) => true;
        }
        """ + "\n";

    [Test]
    public async Task OriginalArrayLengthNineHitFindsCompiledClrWitness()
    {
        Assert.That(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(OriginalHit))),
            Is.EqualTo("AF24AC741BF99A29A7DE74EE1A6CFB46B0DAD03B05FF13C0955836889A29AF58"));
        var source = OriginalHit[OriginalHit.IndexOf("using System;", StringComparison.Ordinal)..].TrimEnd();
        var (generatedSource, mode, claim) = new ContractClaimProgramGenerator(new Random(448627802)).Program();
        Assert.That(generatedSource.Replace("\r\n", "\n", StringComparison.Ordinal), Is.EqualTo(source));
        Assert.That(mode, Is.EqualTo(ContractClaimProgramGenerator.EnforcePure));
        Assert.That(claim, Is.EqualTo("[EnforcePure] requires !((a == null ? 0 : a.Length) != 9)"));
        Assert.That(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))),
            Is.EqualTo("5E1615E488DA79F249F89BBECBD436E7D88ECA7A9FC70B14912692BB2151F860"));

        // Same-source Release CLR compilation with contract calls disabled, as in
        // the default harness. RunCase separately emits its tracked-state image.
        var tree = CSharpSyntaxTree.ParseText(source,
            new CSharpParseOptions(LanguageVersion.CSharp12, preprocessorSymbols: []));
        var compilation = CSharpCompilation.Create("OriginalArrayLengthNineHit", [tree],
            TestMetadataReferences.WithSharpProof,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release, nullableContextOptions: NullableContextOptions.Enable));
        TestCompilation.AssertNoErrors(compilation);
        using var image = new MemoryStream();
        var emission = compilation.Emit(image);
        Assert.That(emission.Success, Is.True, string.Join("\n", emission.Diagnostics));
        image.Position = 0;
        var context = new AssemblyLoadContext("OriginalArrayLengthNineHit", isCollectible: true);
        try
        {
            var type = context.LoadFromStream(image).GetType("C")!;
            ImmutableArray<object?> arguments = [0, 0, new int[9], null];
            Assert.That((int)type.GetField("State")!.GetValue(null)!, Is.Zero);
            Assert.That((bool)type.GetMethod("Pre")!.Invoke(null, arguments.ToArray())!, Is.True);
            Assert.That((int)type.GetMethod("Target")!.Invoke(null, arguments.ToArray())!, Is.Zero);
            Assert.That((int)type.GetField("State")!.GetValue(null)!, Is.EqualTo(1));
            TestContext.WriteLine("CLR Release witness: x=0 y=0 a=new int[9] s=null; Pre=true return=0 State=1");
        }
        finally
        {
            context.Unload();
        }

        var countsBuilder = ImmutableDictionary.CreateBuilder<string, int>(StringComparer.Ordinal);
        var result = await AnalyzerPipelineLongFuzzTests.RunCase(source, mode,
            key => { System.ArgumentNullException.ThrowIfNull(key); countsBuilder[key] = countsBuilder.GetValueOrDefault(key) + 1; });
        var counts = countsBuilder.ToImmutable();
        TestContext.WriteLine($"Native: {result.Outcome} {result.Reason}; default runtime: {result.Runtime}");
        TestContext.WriteLine(string.Join("\n", result.Problems.Select(problem => problem.Kind + ": " + problem.Details)));
        Assert.That(counts.GetValueOrDefault("compiled"), Is.EqualTo(1));
        Assert.That(counts.GetValueOrDefault("manifest"), Is.EqualTo(1));
        Assert.That(result.Outcome, Is.EqualTo("Refuted"));
        Assert.That(result.Reason, Is.EqualTo("None"));
        Assert.That(result.Problems.Where(problem => problem.Kind != "RefutedNotObserved"), Is.Empty);
        Assert.That(result.Problems, Is.Empty,
            "The default public LongFuzz path should observe the compiled CLR purity witness.");
    }
}

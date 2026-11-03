using System.Numerics;
using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

// Each compiled control below runs on the CLR, so a shape's true count never
// depends on the analysis under test.
public sealed partial class ResourceBoundsTests
{
    private static readonly (int N, int M)[] ControlInputs = [(-1, 2), (0, 0), (1, 1), (2, 3), (5, 4), (7, 7)];

    // "@" marks a loop body entry. The analyzed source drops the mark and the
    // compiled control turns it into a counter, so both run the same body.
    private static string WithoutEntryMarks(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        return body.Replace("@", "", StringComparison.Ordinal);
    }

    private static long[] CompiledEntries(string body, params (int N, int M)[] inputs)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(inputs);
        var source = "public static class Probe { public static long Entries; public static void M(int n, int m) { " +
            body.Replace("@", "Entries++;", StringComparison.Ordinal) + " } }";
        var counts = new long[inputs.Length];
        RuntimeAssemblyTestHost.WithRuntimeAssembly("ResourceBoundsEntryControl",
            AnalyzerTestHost.EmitImage(AnalyzerTestHost.CreateCompilation(source, [])), assembly =>
        {
            ArgumentNullException.ThrowIfNull(assembly);
            var probe = assembly.GetType("Probe", throwOnError: true)!;
            var target = probe.GetMethod("M")!.CreateDelegate<Action<int, int>>();
            var entries = probe.GetField("Entries")!;
            for (var index = 0; index < inputs.Length; index++)
            {
                entries.SetValue(null, 0L);
                target(inputs[index].N, inputs[index].M);
                counts[index] = (long)entries.GetValue(null)!;
            }
        });
        return counts;
    }

    private static long CompiledBytes(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var source = "public static class Probe { public static void M(int n, int m) { " + body + " } }";
        long bytes = 0;
        RuntimeAssemblyTestHost.WithRuntimeAssembly("ResourceBoundsAllocationControl",
            AnalyzerTestHost.EmitImage(AnalyzerTestHost.CreateCompilation(source, [])), assembly =>
        {
            ArgumentNullException.ThrowIfNull(assembly);
            var target = assembly.GetType("Probe", throwOnError: true)!.GetMethod("M")!.CreateDelegate<Action<int, int>>();
            target(0, 0);
            var before = GC.GetAllocatedBytesForCurrentThread();
            target(0, 0);
            bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        });
        return bytes;
    }

    private static BigInteger Evaluate(ResourceBoundsAnalysis.Bound bound, int n, int m)
    {
        ArgumentNullException.ThrowIfNull(bound);
        return bound.Expression.Evaluate(input => input is null ? throw new ArgumentNullException(nameof(input)) :
            input.DisplayName == "n" ? n : m, TestContext.CurrentContext.CancellationToken);
    }

    // The true count grows with n*m in the first shape.
    [TestCase("int i=1; for(int j=0;j<n;j++) { @ while(i>0) { @ i--; } i=m; }", 3, 4, 12)]
    [TestCase("int i=1; for(int j=0;j<n;j++) { @ while(i>0) { @ i--; } i=m; }", 10, 10, 101)]
    [TestCase("int i=1; int j=n; while(j>0) { @ while(i>0) { @ i--; } i=m; j--; }", 3, 4, 12)]
    [TestCase("int i=0; for(int j=0;j<n;j++) { @ while(i<5) { @ i++; } i=-m; }", 3, 4, 26)]
    [TestCase("int i=1; for(int a=0;a<n;a++) { @ for(int b=0;b<2;b++) { @ while(i>0) { @ i--; } } i=m; }", 3, 10, 30)]
    [TestCase("int i=1; int left=n,right=n; while(left>0||right>0) { if(left>0) { @ while(i>0) { @ i--; } i=m; left--; } else { @ right--; } }", 2, 4, 9)]
    [TestCase("int left=1,right=1; for(int j=0;j<n;j++) { @ while(left>0||right>0) { if(left>0) { @ left--; } else { @ right--; } } left=m; right=m; }", 3, 4, 21)]
    [TestCase("int credit=0; for(int j=0;j<n;j++) { @ for(int i=0;i<n;i++) { @ credit++; while(credit>0) { @ credit--; } } credit=m; }", 2, 10, 20)]
    [TestCase("int k=1; int credit=0; for(int i=0;i<n;i++) { @ credit++; while(k>0) { @ k--; } k=m; while(credit>0) { @ credit--; } }", 3, 4, 15)]
    [TestCase("int k=1; int credit=0; for(int i=0;i<n;i++) { @ credit++; while(credit>0) { @ while(k>0) { @ k--; } k=m; credit--; } }", 3, 4, 15)]
    // No statement here restores k, so the old bound was sound but loose. The
    // structural rule cannot tell that from a later write, and it abstains.
    [TestCase("int k=5; for(int j=0;j<n;j++) { @ while(k>0) { @ k--; } }", 3, 0, 8)]
    public void LocalsWrittenInAnEnclosingLoopBodyLoseTheirInitialValueFacts(string body, int n, int m, int entries)
    {
        ArgumentNullException.ThrowIfNull(body);
        Assert.That(CompiledEntries(body, (n, m)).Single(), Is.EqualTo(entries));
        var result = Analyze(WithoutEntryMarks(body));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Proven, Is.False, () => "False bound: " + result.Bounds!.LoopIterations.Text);
            Assert.That(result.UnknownReason, Is.EqualTo("UnprovedLoopProgress"));
            Assert.That(result.Bounds, Is.Null);
        }
    }

    [TestCase("for(int j=0;j<n;j++) { @ int i=m; while(i>0) { @ i--; } }")]
    [TestCase("for(int j=0;j<n;j++) { @ int i=m; while(i>0) { @ i--; } i=n; }")]
    [TestCase("for(int j=0;j<n;j++) { @ int i=0; while(i<5) { @ i++; } }")]
    [TestCase("for(int j=0;j<n;j++) { @ for(int i=j;i>0;i--) { @ } }")]
    [TestCase("for(int j=0;j<n;j++) { @ for(int i=0;i<m;i++) { @ } int k=2; while(k>0) { @ k--; } }")]
    [TestCase("int j=n; while(j>0) { @ int i=m; while(i>0) { @ i--; } j--; }")]
    [TestCase("int i=n; while(i>0) { @ i--; } int k=m; while(k>0) { @ k--; }")]
    [TestCase("int credit=0; for(int i=0;i<n;i++) { @ credit++; while(credit>0) { @ credit--; } }")]
    [TestCase("long credit=2; for(int i=0;i<n;i++) { @ credit+=3; int k=2; while(k>0) { @ k--; } while(credit>0) { @ credit--; } }")]
    [TestCase("for(int j=0;j<n;j++) { @ int credit=0; for(int i=0;i<m;i++) { @ credit++; while(credit>0) { @ credit--; } } }")]
    [TestCase("int left=n,right=m; while(left>0||right>0) { if(left>0) { @ int k=2; while(k>0) { @ k--; } left--; } else { @ right--; } }")]
    public void LocalsAssignedOnEveryIterationKeepTheirBounds(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var result = Analyze(WithoutEntryMarks(body));
        Assert.That(result.Proven, Is.True, result.UnknownReason);
        var entries = CompiledEntries(body, ControlInputs);
        for (var index = 0; index < ControlInputs.Length; index++)
        {
            var (n, m) = ControlInputs[index];
            Assert.That(new BigInteger(entries[index]), Is.LessThanOrEqualTo(Evaluate(result.Bounds!.LoopIterations, n, m)), $"n={n}, m={m}");
        }
    }

    [TestCase("for(int i=0;i<new int[1000].Length;i++) { }", 1001 * 4024L, "UnsupportedLoopBound")]
    [TestCase("for(long i=0;i<new int[1000].Length;i++) { }", 1001 * 4024L, "UnsupportedLoopBound")]
    [TestCase("for(int i=new int[1000].Length;i>0;i--) { }", 4024L, "UnsupportedLoopStart")]
    [TestCase("int i=0; while(i<new int[1000].Length) { i++; }", 1001 * 4024L, "UnsupportedLoopBound")]
    [TestCase("for(int i=0;i<new object[1000].Length;i++) { }", 1001 * 8024L, "UnsupportedLoopBound")]
    [TestCase("for(int i=0;i<new string('x',1000).Length;i++) { }", 1001 * 2022L, "UnsupportedLoopBound")]
    [TestCase("var a=new int[new int[1000].Length];", 2 * 4024L, "UnsupportedAllocationSize")]
    [TestCase("var a=new int[new string('x',1000).Length];", 4024L + 2022L, "UnsupportedAllocationSize")]
    public void AnUnchargedSizeOperandCannotAllocate(string body, long minimumBytes, string reason)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(reason);
        Assert.That(CompiledBytes(body), Is.GreaterThanOrEqualTo(minimumBytes));
        var result = Analyze(body);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Proven, Is.False, () => "False bounds: arrays " + result.Bounds!.Arrays.Text + ", elements " + result.Bounds.Elements.Text);
            Assert.That(result.UnknownReason, Is.EqualTo(reason));
            Assert.That(result.Bounds, Is.Null);
        }
    }

    [Test]
    public void AnAllocatingDimensionInsideAChargedArgumentStaysUnknown()
    {
        var result = AnalyzeSource("""
            public static class C {
                public static void Root() { Leaf(new int[new int[1000].Length]); }
                private static void Leaf(int[] data) { }
            }
            """);
        Assert.That(result.Proven, Is.False);
        Assert.That(result.UnknownReason, Is.EqualTo("UnsupportedAllocationSize"));
        Assert.That(result.Bounds, Is.Null);
    }

    // The visitor charges these operands once, and only the length read is free.
    [TestCase("int k=new int[5].Length; while(k>0) { k--; }", "5", "1", "5", 48L)]
    [TestCase("var a=new string(count:new int[3].Length, c:'x');", "0", "1", "3", 72L)]
    [TestCase("for(int i=0;i<\"abc\".Length;i++) { }", "3", "0", "0", 0L)]
    [TestCase("for(int i=0;i<3;i++) { var a=new int[2]; }", "3", "3", "6", 3 * 32L)]
    public void ChargedSizeOperandsKeepTheirBounds(string body, string loops, string arrays, string elements, long bytes)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(loops);
        ArgumentNullException.ThrowIfNull(arrays);
        ArgumentNullException.ThrowIfNull(elements);
        Assert.That(CompiledBytes(body), Is.EqualTo(bytes));
        var result = Analyze(body);
        Assert.That(result.Proven, Is.True, result.UnknownReason);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Bounds!.LoopIterations.Text, Is.EqualTo(loops));
            Assert.That(result.Bounds.Arrays.Text, Is.EqualTo(arrays));
            Assert.That(result.Bounds.Elements.Text, Is.EqualTo(elements));
        }
    }

    [Test]
    public void AllocatedArgumentsStillSubstituteTheirSizesInCalleesAndConstructors()
    {
        var call = AnalyzeSource("""
            public static class C {
                public static void Root() { Leaf(new string('x', 3), new int[2]); }
                private static void Leaf(string text, int[] data) {
                    for (int i=0; i<text.Length; i++) { }
                    for (int i=0; i<data.Length; i++) { }
                }
            }
            """);
        Assert.That(call.Proven, Is.True, call.UnknownReason);
        Assert.That(call.Bounds!.LoopIterations.Text, Is.EqualTo("5"));
        Assert.That(call.Bounds.Arrays.Text, Is.EqualTo("1"));
        Assert.That(call.Bounds.Elements.Text, Is.EqualTo("2"));
        var construction = AnalyzeSource("""
            public class Cell { public Cell(int[] data) { for (int i=0; i<data.Length; i++) { } } }
            public static class Subject { public static void Root() { var cell=new Cell(new int[3]); } }
            """);
        Assert.That(construction.Proven, Is.True, construction.UnknownReason);
        Assert.That(construction.Bounds!.LoopIterations.Text, Is.EqualTo("3"));
        Assert.That(construction.Bounds.Arrays.Text, Is.EqualTo("1"));
        Assert.That(construction.Bounds.Elements.Text, Is.EqualTo("3"));
    }

    [Test]
    public void TheVerifiedTypesInitializerRunsInsideItsFirstCall()
    {
        const string source = """
            public static class C {
                public static int[] Hidden;
                static C() { Hidden = new int[100000]; }
                public static void Root() { }
            }
            """;
        long first = 0;
        long second = 0;
        RuntimeAssemblyTestHost.WithRuntimeAssembly("ResourceBoundsInitializerControl",
            AnalyzerTestHost.EmitImage(AnalyzerTestHost.CreateCompilation(source, [])), assembly =>
        {
            ArgumentNullException.ThrowIfNull(assembly);
            var method = assembly.GetType("C", throwOnError: true)!.GetMethod("Root")!;
            var before = GC.GetAllocatedBytesForCurrentThread();
            var target = method.CreateDelegate<Action>();
            target();
            first = GC.GetAllocatedBytesForCurrentThread() - before;
            before = GC.GetAllocatedBytesForCurrentThread();
            target();
            second = GC.GetAllocatedBytesForCurrentThread() - before;
        });
        Assert.That(first, Is.GreaterThanOrEqualTo(400024));
        Assert.That(second, Is.Zero);
        var result = AnalyzeSource(source);
        Assert.That(result.Proven, Is.False, () => "False bound: arrays " + result.Bounds!.Arrays.Text);
        Assert.That(result.UnknownReason, Is.EqualTo("EntryTypeInitializationUnknown"));
        Assert.That(result.Bounds, Is.Null);
    }

    [TestCase("static C() { }")]
    [TestCase("static int[] Hidden = new int[4];")]
    [TestCase("static readonly int Limit = 5;")]
    public void AVerifiedMethodInATypeWithAnInitializerIsUnknown(string declarations)
    {
        ArgumentNullException.ThrowIfNull(declarations);
        var result = AnalyzeSource("public class C { " + declarations + " public static void Root(int n) { for(int i=0;i<n;i++) { } } }");
        Assert.That(result.Proven, Is.False);
        Assert.That(result.UnknownReason, Is.EqualTo("EntryTypeInitializationUnknown"));
        Assert.That(result.Bounds, Is.Null);
    }

    [TestCase("const int Limit = 5;")]
    [TestCase("static int Shared;")]
    [TestCase("static int[] Hidden;")]
    [TestCase("int instance;")]
    public void AVerifiedMethodInATypeWithoutAnInitializerKeepsItsBound(string declarations)
    {
        ArgumentNullException.ThrowIfNull(declarations);
        var result = AnalyzeSource("public class C { " + declarations + " public static void Root(int n) { for(int i=0;i<n;i++) { } } }");
        Assert.That(result.Proven, Is.True, result.UnknownReason);
        Assert.That(result.Bounds!.LoopIterations.Text, Is.EqualTo("max(0, n)"));
    }
}

using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

public sealed partial class ResourceBoundsTests
{
    [TestCase("n-1", "max(0, n)")]
    [TestCase("n-3", "ceil(max(0, n) / 3)")]
    [TestCase("n/2", "digits(max(0, n), 2)")]
    public void GuardedTailRecurrencesHaveAllocationBounds(string progress, string count)
    {
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(count);
        var result = AnalyzeSource("public static class C { public static void Root(int n) { if(n<=0)return; var a = new int[2]; Root(" + progress + "); } }");
        Assert.That(result.Proven, Is.True, result.UnknownReason);
        Assert.That(result.Bounds!.Arrays.Text, Is.EqualTo(count));
        Assert.That(result.Bounds.Elements.Text, Is.EqualTo("(" + count + " * 2)"));
    }

    [Test]
    public void CallerBindingsAndUnchangedArrayArgumentsFlowThroughRecurrences()
    {
        var result = AnalyzeSource("""
            public static class C {
                public static void Root() { Leaf(4, new int[3]); }
                static void Leaf(int n, int[] data) {
                    if(n<=0) { return; }
                    var a = new int[data.Length];
                    Leaf(n-1, data);
                }
            }
            """);
        Assert.That(result.Proven, Is.True, result.UnknownReason);
        Assert.That(result.Bounds!.Arrays.Text, Is.EqualTo("5"));
        Assert.That(result.Bounds.Elements.Text, Is.EqualTo("15"));
    }

    [TestCase("n-1, width, data", true)]
    [TestCase("n-1, width+1, data", false)]
    [TestCase("n-1, width, new int[3]", false)]
    [TestCase("n-1, 2, data", false)]
    public void TailRecurrencesRequireForwardedNonRankArguments(string arguments, bool supported)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var result = AnalyzeSource("public static class C { public static void Root() { Leaf(4, 2, new int[3]); } " +
            "static void Leaf(int n, int width, int[] data) { if(n<=0)return; var a=new int[width]; var b=new int[data.Length]; Leaf(" + arguments + "); } }");
        if (!supported)
        {
            Assert.That(result.Proven, Is.False);
            Assert.That(result.UnknownReason, Is.EqualTo("UnsupportedRecursiveArguments"));
            Assert.That(result.Bounds, Is.Null, "Neither caller allocations nor a recursive prefix may become a partial bound.");
            return;
        }
        int arrays = 1, elements = 3;
        void Execute(int n, int width, int[] data)
        {
            ArgumentNullException.ThrowIfNull(data);
            ArgumentOutOfRangeException.ThrowIfNegative(width);
            if (n <= 0)
            { return; }
            var first = new int[width];
            var second = new int[data.Length];
            arrays += 2;
            elements += first.Length + second.Length;
            Execute(n - 1, width, data);
        }
        Execute(4, 2, new int[3]);
        Assert.That(result.Proven, Is.True, result.UnknownReason);
        Assert.That(result.Bounds!.Arrays.Constant, Is.EqualTo(new System.Numerics.BigInteger(arrays)));
        Assert.That(result.Bounds.Elements.Constant, Is.EqualTo(new System.Numerics.BigInteger(elements)));
        Assert.That(arrays, Is.EqualTo(9));
        Assert.That(elements, Is.EqualTo(23));
    }

    [TestCase("if(n<0)return; var a = new int[2]; Root(n-1);", "RecursiveCostUnknown")]
    [TestCase("if(n<=0)return; var a = new int[2]; Root(n+1);", "UnprovedRecursiveProgress")]
    [TestCase("if(n<=0)return; var a = new int[2]; Root(n/1);", "UnprovedRecursiveProgress")]
    [TestCase("if(n<=0)return; Root(n-1); Root(n-1);", "RecursiveCostUnknown")]
    [TestCase("if(n<=0)return; n=100; Root(n-1);", "UnstableInputOrCounter")]
    public void UnsupportedRecurrencesNeverBorrowTheTailRule(string body, string reason)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(reason);
        var result = AnalyzeSource("public static class C { public static void Root(int n) { " + body + " } }");
        Assert.That(result.Proven, Is.False);
        Assert.That(result.UnknownReason, Is.EqualTo(reason));
        Assert.That(result.Bounds, Is.Null);
    }

    [Test]
    public void RecursiveAllocationBoundsMatchIndependentSmallExecutions()
    {
        for (int size = -2; size <= 16; size++)
        {
            int arrays = 0, elements = 0;
            void Execute(int remaining)
            {
                if (remaining <= 0)
                { return; }
                var allocated = new int[2];
                arrays++;
                elements += allocated.Length;
                Execute(remaining - 1);
            }
            Execute(size);
            var result = AnalyzeSource($"public static class C {{ public static void Root() {{ Leaf({size}); }} static void Leaf(int n) {{ if(n<=0)return; var a = new int[2]; Leaf(n-1); }} }}");
            Assert.That(result.Proven, Is.True, result.UnknownReason);
            Assert.That(result.Bounds!.Arrays.Constant, Is.EqualTo(new System.Numerics.BigInteger(arrays)));
            Assert.That(result.Bounds.Elements.Constant, Is.EqualTo(new System.Numerics.BigInteger(elements)));
        }
    }

    [Test]
    public async Task AnalyzerProjectsComposedRecurrenceEvidence()
    {
        const string source = """
            public static class C {
                public static void Root() { Leaf(4, new int[3]); }
                static void Leaf(int n, int[] data) {
                    if(n<=0)return;
                    var a = new int[data.Length];
                    Leaf(n-1, data);
                }
            }
            """;
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(source, null, ["SP0060"]);
        var root = diagnostics.Single(diagnostic => diagnostic is null ? throw new ArgumentNullException(nameof(diagnostic)) :
            diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture).StartsWith("Method 'Root':", StringComparison.Ordinal));
        Assert.That(root.Properties["ResourceStatus"], Is.EqualTo("Proven"));
        Assert.That(root.Properties["ResourceCostModel"], Is.EqualTo("loop-entries-and-primitive-array-elements-v3"));
        Assert.That(root.Properties["ResourceExpressionModel"], Is.EqualTo("1"));
        Assert.That(root.Properties["ArrayAllocationsUpperBound"], Is.EqualTo("5"));
        Assert.That(root.Properties["AllocatedElementsUpperBound"], Is.EqualTo("15"));
    }

    [Test]
    public void RecurrenceResultStillObeysTheExpressionBudget()
    {
        var name = new string('n', 9000);
        var result = AnalyzeSource($"public static class C {{ public static void Root(int {name}) {{ if({name}<=0)return; var a = new byte[1]; Root({name}-1); }} }}");
        Assert.That(result.Proven, Is.False);
        Assert.That(result.UnknownReason, Is.EqualTo("AnalysisBudgetExceeded"));
        Assert.That(result.Bounds, Is.Null);
    }
}

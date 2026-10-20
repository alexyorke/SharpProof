using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using NUnit.Framework;
using SharpProof.Frontend;
using SharpProof.Frontend.Host;
using SharpProof.Ir;
using System.Runtime.Loader;

namespace SharpProof.Worker.Test;

public sealed class HashSetStateOracleTests
{
    [Test]
    public void CustomComparerMembershipCanInvokeUserCode()
    {
        var comparer = new CallbackComparer();
        var data = new HashSet<int>(comparer);
        data.Add(17);
        var before = comparer.Calls;
        Assert.That(data.Contains(23), Is.True);
        Assert.That(comparer.Calls, Is.GreaterThan(before));
        Assert.That(data.Count, Is.EqualTo(1));
    }

    private sealed class CallbackComparer : IEqualityComparer<int>
    {
        internal int Calls { get; private set; }
        public bool Equals(int first, int second)
        { Calls++; return true; }
        public int GetHashCode(int value)
        { Calls++; return 0; }
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(8)]
    [TestCase(64)]
    public void ReentrantComparerRemovalCanProduceASignedCount(int nestedRemovals)
    {
        var comparer = new ReentrantComparer();
        var data = new HashSet<int>(comparer);
        comparer.Data = data;
        data.Add(17);
        comparer.Remaining = nestedRemovals;
        Assert.That(data.Remove(17), Is.True);
        Assert.That(data.Count, Is.EqualTo(-nestedRemovals));
        Assert.That(((ICollection<int>)data).Count, Is.EqualTo(-nestedRemovals));
    }

    private sealed class ReentrantComparer : IEqualityComparer<int>
    {
        internal HashSet<int> Data { get; set; } = null!;
        internal Dictionary<int, int>? Map { get; set; }
        internal int Remaining { get; set; }
        public bool Equals(int first, int second)
        {
            if (Remaining > 0)
            {
                Remaining--;
                if (Map is { } map)
                { map.Remove(first); }
                else
                { Data.Remove(first); }
            }
            return first == second;
        }
        public int GetHashCode(int value)
        { return 0; }
    }

    [TestCase(1)]
    [TestCase(8)]
    [TestCase(64)]
    public void CustomDictionaryComparerRemovalCanProduceASignedCount(int nestedRemovals)
    {
        var comparer = new ReentrantComparer();
        var data = new Dictionary<int, int>(comparer);
        comparer.Map = data;
        data.Add(17, 23);
        comparer.Remaining = nestedRemovals;
        Assert.That(data.Remove(17), Is.True);
        Assert.That(data.Count, Is.EqualTo(-nestedRemovals));
    }

    [TestCase(1)]
    [TestCase(8)]
    public void DefaultDictionaryKeyCallbacksCanAlsoProduceASignedCount(int nestedRemovals)
    {
        var key = new ReentrantDictionaryKey();
        var data = new Dictionary<ReentrantDictionaryKey, int>();
        key.Data = data;
        data.Add(key, 17);
        key.Remaining = nestedRemovals;
        Assert.That(data.Remove(key), Is.True);
        Assert.That(data.Count, Is.EqualTo(-nestedRemovals));
    }

    private sealed class ReentrantDictionaryKey : IEquatable<ReentrantDictionaryKey>
    {
        internal Dictionary<ReentrantDictionaryKey, int> Data { get; set; } = null!;
        internal int Remaining { get; set; }
        public bool Equals(ReentrantDictionaryKey? other)
        {
            if (Remaining > 0)
            {
                Remaining--;
                Data.Remove(this);
            }
            return ReferenceEquals(this, other);
        }
        public override bool Equals(object? other)
        { return other is ReentrantDictionaryKey key && Equals(key); }
        public override int GetHashCode()
        { return 0; }
    }

    [TestCase("sbyte")]
    [TestCase("byte")]
    [TestCase("short")]
    [TestCase("ushort")]
    [TestCase("char")]
    [TestCase("int")]
    [TestCase("uint")]
    [TestCase("long")]
    [TestCase("ulong")]
    public void PrimitiveMembershipMatchesCompiledRuntime(string element)
    {
        ArgumentNullException.ThrowIfNull(element);
        Compare("var data = new System.Collections.Generic.HashSet<" + element + ">(1); var alias = data; " +
            "var first = data.Add(" + element + ".MinValue); var second = alias.Add(" + element + ".MaxValue); var duplicate = data.Add(" + element + ".MinValue); " +
            "var removed = data.Remove(" + element + ".MinValue); return first && second && !duplicate && removed && !alias.Remove(" + element + ".MinValue) && " +
            "alias.Contains(" + element + ".MaxValue) && !data.Contains(" + element + ".MinValue) && data.Count == 1 ? 1 : 0;");
    }

    [TestCase("var data = new System.Collections.Generic.HashSet<int>(); return data.Count;")]
    [TestCase("var data = new System.Collections.Generic.HashSet<int>(0); return data.Contains(0) ? 1 : 0;")]
    [TestCase("var data = new System.Collections.Generic.HashSet<decimal>(); return data.Count;")]
    [TestCase("var data = new System.Collections.Generic.HashSet<bool>(); data.Add(false); data.Add(true); return data.Count == 2 && data.Remove(false) && !data.Contains(false) && data.Contains(true) ? 1 : 0;")]
    [TestCase("var data = new System.Collections.Generic.HashSet<int>(); var alias = data; data.Add((data = new System.Collections.Generic.HashSet<int>()).Count + 17); return alias.Contains(17) && data.Count == 0 ? 1 : 0;")]
    [TestCase("var data = new System.Collections.Generic.HashSet<int>(); data.Add(1); data.Add(2); data.Add(3); data.Add(4); data.Add(5); data.Add(6); data.Remove(3); data.Add(7); return data.Count == 6 && !data.Contains(3) && data.Contains(7) ? 1 : 0;")]
    public void SharedStateAndEvaluationOrderMatchCompiledRuntime(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        Compare(body);
    }

    [TestCase("sbyte")]
    [TestCase("byte")]
    [TestCase("short")]
    [TestCase("ushort")]
    [TestCase("char")]
    [TestCase("int")]
    [TestCase("uint")]
    [TestCase("long")]
    [TestCase("ulong")]
    [TestCase("bool")]
    public void CollectionInterfaceMembershipMatchesCompiledRuntime(string element)
    {
        ArgumentNullException.ThrowIfNull(element);
        var first = element == "bool" ? "false" : element + ".MinValue";
        var second = element == "bool" ? "true" : element + ".MaxValue";
        Compare("var data = new System.Collections.Generic.HashSet<" + element + ">(1); System.Collections.Generic.ICollection<" + element + "> alias = data; " +
            "alias.Add(" + first + "); alias.Add(" + second + "); alias.Add(" + first + "); var before = data.Count; " +
            "return before == 2 && alias.Remove(" + first + ") && !data.Contains(" + first + ") && alias.Contains(" + second + ") && " +
            "!alias.Remove(" + first + ") && alias.Count == 1 ? 1 : 0;");
    }

    [Test]
    public void ASubclassCanReplaceTheCollectionCountImplementation()
    {
        ICollection<int> data = new ReimplementedCollection();
        Assert.That(data.Count, Is.EqualTo(-9));
        Assert.That(((HashSet<int>)data).Count, Is.Zero);
    }

    private sealed class ReimplementedCollection : HashSet<int>, ICollection<int>
    {
        int ICollection<int>.Count => -9;
    }
    [TestCase("return data.Count;")]
    [TestCase("data.Add(marker = 3); return 0;")]
    [TestCase("return data.Contains(17) ? 1 : 0;")]
    [TestCase("return data.Remove(17) ? 1 : 0;")]
    public void NullCollectionReceiversPreserveFaultsAndArgumentEvaluation(string operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        Compare("int marker = 7; System.Collections.Generic.ICollection<int> data = null; try { " + operation +
            " } catch (System.NullReferenceException) { return marker; }", expectedApproximation: false);
    }
    private static void Compare(string body, bool expectedApproximation = true)
    {
        ArgumentNullException.ThrowIfNull(body);
        var tree = CSharpSyntaxTree.ParseText("public static class Probe { public static int Target() { " + body + " } }");
        var compilation = CSharpCompilation.Create("HashSetStateOracle", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(";", emitted.Diagnostics));
        var model = compilation.GetSemanticModel(tree);
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, model.GetDeclaredSymbol(syntax)!);
        var lowered = new RoslynProgramLowerer(factory).LowerCandidate(ControlFlowGraph.Create(syntax, model)!, context,
            static _ => true, null, TestContext.CurrentContext.CancellationToken, opaqueCalls: true);
        Assert.That(lowered.IsExact, Is.True);
        var options = new IrProgramReplayOptions(request =>
        {
            ArgumentNullException.ThrowIfNull(request);
            return factory.GetVariableInfo(request.Variable).Type == factory.BooleanType ? factory.CreateBooleanValue(false) : null;
        });
        var replay = new IrProgramInterpreter(factory).Execute(lowered.Program, null, 5000, options);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned), replay.Unsupported?.Detail);
        Assert.That(replay.ConsumedApproximation, Is.EqualTo(expectedApproximation));
        var runtime = new AssemblyLoadContext("HashSetStateOracle", isCollectible: true);
        try
        {
            image.Position = 0;
            var actual = runtime.LoadFromStream(image).GetType("Probe")!.GetMethod("Target")!.Invoke(null, null);
            Assert.That(replay.ReturnValue!.Integer, Is.EqualTo(actual));
            Assert.That(replay.ReturnValue.Integer + 1, Is.Not.EqualTo(actual));
        }
        finally { runtime.Unload(); }
    }
}

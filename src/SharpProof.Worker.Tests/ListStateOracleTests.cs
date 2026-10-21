using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using NUnit.Framework;
using SharpProof.Frontend;
using SharpProof.Frontend.Host;
using SharpProof.Ir;
using System.Reflection;
using System.Runtime.Loader;

namespace SharpProof.Worker.Test;

public sealed class ListStateOracleTests
{
    [TestCase("sbyte")]
    [TestCase("byte")]
    [TestCase("short")]
    [TestCase("ushort")]
    [TestCase("char")]
    [TestCase("int")]
    [TestCase("uint")]
    [TestCase("long")]
    [TestCase("ulong")]
    public void SharedTypedContentsAgreeWithCompiledRuntimeAtBothBoundaries(string element)
    {
        ArgumentNullException.ThrowIfNull(element);
        Compare("var data = new System.Collections.Generic.List<" + element + ">(); data.Add(" + element + ".MinValue); data.Add(" + element + ".MaxValue); " +
            "System.Collections.Generic.IList<" + element + "> alias = data; alias[0] = " + element + ".MaxValue; " +
            "return data.Count == 2 && alias[0] == " + element + ".MaxValue && data[1] == " + element + ".MaxValue ? 1 : 0;");
    }

    [TestCase("var data = new System.Collections.Generic.List<bool>(); data.Add(false); data.Add(true); data[0] = true; return data.Count == 2 && data[0] && data[1] ? 1 : 0;")]
    [TestCase("var data = new System.Collections.Generic.List<object>(); var value = new object(); data.Add(value); data.Add(null); return object.ReferenceEquals(data[0], value) && data[1] == null && data.Count == 2 ? 1 : 0;")]
    [TestCase("var data = new System.Collections.Generic.List<string>(); data.Add(\"first\"); data.Add(null); data[0] = \"second\"; return data[0] == \"second\" && data[1] == null ? data.Count : -1;")]
    [TestCase("var data = new System.Collections.Generic.List<decimal>(); return data.Count;")]
    [TestCase("var data = new System.Collections.Generic.List<int>(); var other = new System.Collections.Generic.List<int>(); data.Add(17); other.Add(29); return data[0] + other[0];")]
    [TestCase("var data = new System.Collections.Generic.List<int>(); data.Add(17); var alias = data; data[0] = (data = new System.Collections.Generic.List<int>()).Count; return alias[0];")]
    [TestCase("var data = new System.Collections.Generic.List<int>(); data.Add(17); data.Add(29); int index = 0; data[index++] = (index = 0) + 31; return data[0] * 100 + data[1];")]
    [TestCase("var data = new System.Collections.Generic.List<int>() { 2, 3 }; return data.Count * 100 + data[0] + data[1];")]
    public void NormalStateAndEvaluationOrderAgreeWithCompiledRuntime(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        Compare(body);
    }

    [TestCase("return data[-1];")]
    [TestCase("return data[1];")]
    [TestCase("data[-1] = 23; return 0;")]
    [TestCase("data[1] = 23; return 0;")]
    [TestCase("return data[int.MaxValue];")]
    public void BoundsFaultKindAgreesWithCompiledRuntime(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        Compare("var data = new System.Collections.Generic.List<int>(); data.Add(17); " + body);
    }

    [Test]
    public void ConstructionAndStorageDoNotInvokeElementCode()
    {
        var data = new List<Observed>();
        var value = new Observed();
        data.Add(value);
        data.Add(value);
        data[0] = value;
        Assert.That(data.Count, Is.EqualTo(2));
        Assert.That(ReferenceEquals(data[0], value), Is.True);
        Assert.That(value.Callbacks, Is.Zero);
        Compare("return new System.Collections.Generic.List<Uninitializable>().Count;",
            "public sealed class Uninitializable { static Uninitializable() { throw new System.InvalidOperationException(); } public Uninitializable() { throw new System.InvalidOperationException(); } }");
    }

    private sealed class Observed
    {
        public int Callbacks { get; private set; }
        public override bool Equals(object? obj)
        {
            Callbacks++;
            return ReferenceEquals(this, obj);
        }
        public override int GetHashCode()
        {
            Callbacks++;
            return 0;
        }
    }

    private static void Compare(string body, string members = "")
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(members);
        var tree = CSharpSyntaxTree.ParseText(members + "public static class Probe { public static int Target() { " + body + " } }");
        var compilation = CSharpCompilation.Create("ListStateOracle", [tree], TestMetadataReferences.Platform,
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
        var runtime = new AssemblyLoadContext("ListStateOracle", isCollectible: true);
        try
        {
            image.Position = 0;
            object? actual = null;
            Exception? fault = null;
            try
            { actual = runtime.LoadFromStream(image).GetType("Probe")!.GetMethod("Target")!.Invoke(null, null); }
            catch (TargetInvocationException exception)
            { fault = exception.InnerException; }
            // Select bounded normal initialization/growth and intended bounds
            // faults. These choices consume approximation, not replay authority.
            var options = new IrProgramReplayOptions(request =>
            {
                ArgumentNullException.ThrowIfNull(request);
                return factory.GetVariableInfo(request.Variable).Type == factory.BooleanType ? factory.CreateBooleanValue(false) : null;
            });
            var result = new IrProgramInterpreter(factory).Execute(lowered.Program, ImmutableDictionary<ScopedIrId<IrVariableTag>, IrValue>.Empty, 5000, options);
            Assert.That(result.ConsumedApproximation, Is.True);
            if (fault != null)
            {
                Assert.That(fault, Is.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(result.Status, Is.EqualTo(IrProgramExecutionStatus.Exception), result.Unsupported?.Detail);
                Assert.That(result.Exception!.Kind, Is.EqualTo(IrExceptionKind.ArgumentOutOfRange));
            }
            else
            {
                Assert.That(result.Status, Is.EqualTo(IrProgramExecutionStatus.Returned), result.Unsupported?.Detail);
                Assert.That(result.ReturnValue!.Integer, Is.EqualTo(actual));
            }
        }
        finally { runtime.Unload(); }
    }
}

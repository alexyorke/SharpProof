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

public sealed class QueueStateOracleTests
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
    public void QueueFifoBoundariesAgreeWithCompiledRuntime(string element)
    {
        ArgumentNullException.ThrowIfNull(element);
        Compare("var data = new System.Collections.Generic.Queue<" + element + ">(1); data.Enqueue(" + element + ".MinValue); data.Enqueue(" + element + ".MaxValue); var alias = data; return alias.Dequeue() == " + element + ".MinValue && data.Dequeue() == " + element + ".MaxValue && alias.Count == 0 ? 1 : 0;");
    }

    [TestCase("var data = new System.Collections.Generic.Queue<int>(); data.Enqueue(17); data.Enqueue(23); var first = data.Dequeue(); data.Enqueue(29); return first * 10000 + data.Dequeue() * 100 + data.Dequeue();")]
    [TestCase("var data = new System.Collections.Generic.Queue<object>(); var item = new object(); data.Enqueue(item); data.Enqueue(null); return object.ReferenceEquals(data.Dequeue(), item) && data.Dequeue() == null && data.Count == 0 ? 1 : 0;")]
    [TestCase("var data = new System.Collections.Generic.Queue<int>(); var alias = data; data.Enqueue((data = new System.Collections.Generic.Queue<int>()).Count + 17); return alias.Dequeue() + data.Count;")]
    [TestCase("var data = new System.Collections.Generic.Queue<int>(0); return data.Count;")]
    [TestCase("var data = new System.Collections.Generic.Queue<decimal>(); return data.Count;")]
    [TestCase("var data = new System.Collections.Generic.Queue<int>(); data.Enqueue(17); data.Enqueue(23); return data.Count * 100 + data.Dequeue();")]
    public void QueueNormalStateAndEvaluationOrderAgreeWithCompiledRuntime(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        Compare(body);
    }

    [TestCase(0, false)]
    [TestCase(int.MaxValue, false)]
    [TestCase(0, true)]
    [TestCase(int.MaxValue, true)]
    public void QueueLogicalHeadWrapPreservesFifoForIncomingState(int head, bool populated = false)
    {
        var body = populated ? "var first = data.Dequeue(); data.Enqueue(29); return first * 10000 + data.Dequeue() * 100 + data.Dequeue() + data.Count;"
            : "data.Enqueue(17); data.Enqueue(23); return data.Dequeue() * 10000 + data.Dequeue() + data.Count;";
        var tree = CSharpSyntaxTree.ParseText("public static class Probe { public static int Target(System.Collections.Generic.Queue<int> data) { " + body + " } }");
        var compilation = CSharpCompilation.Create("QueueWrapOracle", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var model = compilation.GetSemanticModel(tree);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, model.GetDeclaredSymbol(syntax)!);
        var lowered = new RoslynProgramLowerer(factory).LowerCandidate(ControlFlowGraph.Create(syntax, model)!, context,
            static _ => true, null, TestContext.CurrentContext.CancellationToken, opaqueCalls: true);
        Assert.That(lowered.IsExact, Is.True);
        IrMemberId Field(string name)
        {
            ArgumentNullException.ThrowIfNull(name);
            var identity = factory.InternExternalIdentity(("queue", name, factory.IntegerType), EqualityComparer<(string, string, IrTypeId)>.Default);
            return factory.GetOrCreateMember(identity, factory.ObjectType, "field:queue:" + name, factory.IntegerType, false, name == "value" ? [factory.IntegerType] : []);
        }
        var state = new IrObjectState().WithField(Field("count"), factory.CreateIntegerValue(populated ? 2 : 0)).WithField(Field("head"), factory.CreateIntegerValue(head));
        if (populated)
        {
            state.WithKeyedField(Field("value"), factory.CreateIntegerValue(head), factory.CreateIntegerValue(17));
            state.WithKeyedField(Field("value"), factory.CreateIntegerValue(head == int.MaxValue ? 0 : head + 1), factory.CreateIntegerValue(23));
        }
        var parameter = context.Parameters.Single().Entry;
        var inputs = new Dictionary<ScopedIrId<IrVariableTag>, IrValue>
        {
            [parameter] = factory.CreateReferenceValue(factory.GetVariableInfo(parameter).Type, state)
        };
        var options = new IrProgramReplayOptions(request =>
        {
            ArgumentNullException.ThrowIfNull(request);
            return factory.GetVariableInfo(request.Variable).Type == factory.BooleanType ? factory.CreateBooleanValue(false) : null;
        });
        var result = new IrProgramInterpreter(factory).Execute(lowered.Program, inputs, 5000, options);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(";", emitted.Diagnostics));
        var runtime = new AssemblyLoadContext("QueueWrapOracle", isCollectible: true);
        try
        {
            image.Position = 0;
            var actual = runtime.LoadFromStream(image).GetType("Probe")!.GetMethod("Target")!.Invoke(null, [populated ? new Queue<int>([17, 23]) : new Queue<int>()]);
            Assert.That(result.Status, Is.EqualTo(IrProgramExecutionStatus.Returned), result.Unsupported?.Detail);
            Assert.That(result.ConsumedApproximation, Is.True);
            Assert.That(result.ReturnValue!.Integer, Is.EqualTo(actual));
        }
        finally { runtime.Unload(); }
    }
    [Test]
    public void QueueStorageDoesNotInvokeEqualityHashOrComparisonCallbacks()
    {
        var item = new NoCallbacks();
        var data = new Queue<NoCallbacks>(1);
        data.Enqueue(item);
        data.Enqueue(null!);
        Assert.That(data.Dequeue(), Is.SameAs(item));
        Assert.That(data.Dequeue(), Is.Null);
        Assert.That(data.Count, Is.Zero);
        Assert.That(item.Callbacks, Is.Zero);
    }

    private sealed class NoCallbacks : IComparable<NoCallbacks>
    {
        public int Callbacks { get; private set; }
        public override bool Equals(object? obj)
        { Callbacks++; return ReferenceEquals(this, obj); }
        public override int GetHashCode()
        { Callbacks++; return 0; }
        public int CompareTo(NoCallbacks? other)
        { Callbacks++; return ReferenceEquals(this, other) ? 0 : 1; }
    }
    private static void Compare(string body, string members = "")
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(members);
        var tree = CSharpSyntaxTree.ParseText(members + "public static class Probe { public static int Target() { " + body + " } }");
        var compilation = CSharpCompilation.Create("QueueStateOracle", [tree], TestMetadataReferences.Platform,
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
        var runtime = new AssemblyLoadContext("QueueStateOracle", isCollectible: true);
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
            var result = new IrProgramInterpreter(factory).Execute(lowered.Program, new Dictionary<ScopedIrId<IrVariableTag>, IrValue>(), 5000, options);
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

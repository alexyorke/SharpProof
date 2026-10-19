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

public sealed class ListRangeOracleTests
{
    [TestCase("data.Insert(0, 17); return data[0] * 100 + data[1] * 10 + data[2];")]
    [TestCase("data.Insert(1, 17); return data[0] * 100 + data[1] * 10 + data[2];")]
    [TestCase("data.Insert(2, 17); return data[0] * 100 + data[1] * 10 + data[2];")]
    [TestCase("int i = 0; data.Insert(i++, (i = 2) + 17); return data[0] * 100 + data[1] * 10 + data[2] + i;")]
    [TestCase("var alias = data; data.Insert(1, (data = new System.Collections.Generic.List<int>()).Count); return alias[0] * 100 + alias[1] * 10 + alias[2];")]
    [TestCase("var copy = data.GetRange(0, 2); copy[0] = 17; data[1] = 29; return copy[0] * 100 + copy[1] * 10 + data[0] + data[1];")]
    [TestCase("int i = 0; var copy = data.GetRange(i++, i); return copy[0] * 100 + copy.Count * 10 + i;")]
    [TestCase("var copy = data.GetRange(2, 0); copy.Insert(0, 17); return data.Count * 100 + copy.Count * 10 + copy[0];")]
    [TestCase("data.Insert(-1, 17); return 0;")]
    [TestCase("data.Insert(3, 17); return 0;")]
    [TestCase("data.GetRange(-1, -1); return 0;")]
    [TestCase("data.GetRange(0, -1); return 0;")]
    [TestCase("data.GetRange(3, 0); return 0;")]
    [TestCase("data.GetRange(1, 2); return 0;")]
    [TestCase("data.GetRange(int.MaxValue, int.MaxValue); return 0;")]
    [TestCase("System.Collections.Generic.List<int> missing = null; int index = 0; try { missing.Insert(index++, index++); } catch (System.NullReferenceException) { return index; } return -1;")]
    [TestCase("System.Collections.Generic.List<int> missing = null; int index = 0; try { missing.GetRange(index++, index++); } catch (System.NullReferenceException) { return index; } return -1;")]
    [TestCase("var values = new System.Collections.Generic.List<bool>(); values.Insert(0, false); values.Insert(1, true); var copy = values.GetRange(0, 2); values[1] = false; return !copy[0] && copy[1] && !values[1] ? 1 : 0;")]
    [TestCase("int i = 0; data.Insert(item: (i = 1), index: i++); return data[0] * 100 + data[1] * 10 + data[2] + i;")]
    [TestCase("int i = 0; var copy = data.GetRange(count: ++i, index: i++); return copy[0] * 100 + copy.Count * 10 + i;")]
    public void RangeContentsOrderAndFaultsAgreeWithCompiledRuntime(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        Compare("var data = new System.Collections.Generic.List<int>(); data.Add(11); data.Add(23); " + body);
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
    public void RangeCopiesKeepBothScalarBoundaries(string element)
    {
        ArgumentNullException.ThrowIfNull(element);
        Compare("var data = new System.Collections.Generic.List<" + element + ">(); data.Add(" + element + ".MinValue); data.Insert(0, " + element + ".MaxValue); var copy = data.GetRange(0, 2); copy[1] = " + element + ".MaxValue; return data[0] == " + element + ".MaxValue && data[1] == " + element + ".MinValue && copy[0] == " + element + ".MaxValue && copy[1] == " + element + ".MaxValue ? 1 : 0;");
    }
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(4)]
    [TestCase(5)]
    [TestCase(12)]
    public void RangeCopiesRetainTheEntireResidualLoopBeyondThePeeledPrefix(int count)
    {
        var adds = string.Join(" ", Enumerable.Range(1, count).Select(value => "data.Add(" + value + ");"));
        var sum = string.Join(" + ", Enumerable.Range(0, count + 1).Select(index => "copy[" + index + "]"));
        Compare("var data = new System.Collections.Generic.List<int>(); " + adds + " data.Insert(0, 73); var copy = data.GetRange(0, " + (count + 1) + "); data[0] = 99; return " + sum + ";");
    }
    private static void Compare(string body, string members = "")
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(members);
        var tree = CSharpSyntaxTree.ParseText(members + "public static class Probe { public static int Target() { " + body + " } }");
        var compilation = CSharpCompilation.Create("ListRangeOracle", [tree], TestMetadataReferences.Platform,
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
        var runtime = new AssemblyLoadContext("ListRangeOracle", isCollectible: true);
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
                Assert.That(fault, Is.InstanceOf<ArgumentException>());
                Assert.That(result.Status, Is.EqualTo(IrProgramExecutionStatus.Exception), result.Unsupported?.Detail);
                Assert.That(result.Exception!.Kind, Is.EqualTo(fault is ArgumentOutOfRangeException ? IrExceptionKind.ArgumentOutOfRange : IrExceptionKind.Argument));
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

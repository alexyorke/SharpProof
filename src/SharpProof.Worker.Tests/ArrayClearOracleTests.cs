using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using NUnit.Framework;
using SharpProof.Frontend.Host;
using SharpProof.Frontend;
using SharpProof.Ir;
using System.Runtime.Loader;

namespace SharpProof.Worker.Test;

public sealed class ArrayClearOracleTests
{
    [Test]
    public void ArrayClearOwnerHasNoStaticTypeInitializer()
    {
        Assert.That(typeof(Array).TypeInitializer, Is.Null, typeof(Array).Assembly.FullName);
    }

    [TestCase("int[] data = null; var state = 0; var value = int.MaxValue; try { System.Array.Clear(data, checked(value + 1), state = 7); return -1; } catch (System.OverflowException) { return state; } catch (System.Exception) { return state + 10; }")]
    [TestCase("var data = new int[] { 3, 5, 7, 11 }; var alias = data; System.Array.Clear(data, 1, 2); return alias[0] + alias[1] + alias[2] + alias[3];")]
    [TestCase("var data = new int[] { 3, 5, 7, 11 }; System.Array.Clear(data); return data[0] + data[3];")]
    [TestCase("var data = new int[] { 3, 5, 7, 11 }; System.Array.Clear(data, data.Length, 0); return data[0] + data[3];")]
    [TestCase("var data = new int[] { 3, 5, 7, 11 }; var alias = data; var other = new int[] { 17, 19, 23, 29 }; System.Array.Clear(data, (data = other).Length - 3, 2); return alias[0] + alias[1] + alias[2] + alias[3] + data[1];")]
    [TestCase("var data = new int[] { 3, 5, 7, 11 }; var i = 0; System.Array.Clear(length: ++i, array: data, index: ++i); return i + data[0] + data[1] + data[2] + data[3];")]
    [TestCase("var data = new int[] { 3, 5, 7, 11 }; System.Array.Clear(data, 0, 4); data[1] = 13; System.Array.Clear(data, 2, 2); return data[0] + data[1] + data[2] + data[3];")]
    public void ArrayClearStateAndArgumentOrderAgreeWithClr(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        CheckArrayClearClrAndIr(body, null);
    }

    [Test]
    public void ArrayClearSafeCovariantCastPreservesArgumentOrder()
    {
        string[] data = ["first", "second"];
        CheckArrayClearClrAndIr("var i = 0; System.Array.Clear((object[])data, i++, ++i - 1); return i;", data);
        Assert.That(data[0], Is.Null);
        Assert.That(data[1], Is.EqualTo("second"));
    }

    private static void CheckArrayClearClrAndIr(string body, string[]? data)
    {
        ArgumentNullException.ThrowIfNull(body);
        var tree = CSharpSyntaxTree.ParseText("public static class Probe { public static int Target(" + (data == null ? "" : "string[] data") + ") { " + body + " } }");
        var compilation = CSharpCompilation.Create("ArrayClearOracle", [tree], TestMetadataReferences.Platform,
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
        Assert.That(lowered.IsExact, Is.True, lowered.Classification.ToString());
        var runtime = new AssemblyLoadContext("ArrayClearOracle", isCollectible: true);
        try
        {
            image.Position = 0;
            var inputs = data == null ? null : ImmutableDictionary<ScopedIrId<IrVariableTag>, IrValue>.Empty.Add(
                context.Parameters[0].Entry, factory.CreateSequenceValue(factory.GetVariableInfo(context.Parameters[0].Entry).Type,
                    data.Select(factory.CreateStringValue)));
            var actual = runtime.LoadFromStream(image).GetType("Probe")!.GetMethod("Target")!.Invoke(null, data == null ? null : [data]);
            var result = new IrProgramInterpreter(factory).Execute(lowered.Program, inputs, 5000);
            Assert.That(result.Status, Is.EqualTo(IrProgramExecutionStatus.Returned), result.Unsupported?.Detail);
            Assert.That(result.ConsumedApproximation, Is.False);
            Assert.That(result.ReturnValue!.Integer, Is.EqualTo(actual));
        }
        finally
        { runtime.Unload(); }
    }
}

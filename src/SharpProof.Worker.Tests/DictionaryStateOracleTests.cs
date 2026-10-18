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

public sealed class DictionaryStateOracleTests
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
    public void BoundaryKeysAliasesMembershipAndUpdatesAgreeWithCompiledClr(string type)
    {
        ArgumentNullException.ThrowIfNull(type);
        Compare("var data = new System.Collections.Generic.Dictionary<" + type + ", int>(); " +
            "var alias = data; alias.Add(value: 23, key: " + type + ".MinValue); " +
            "data[" + type + ".MaxValue] = 29; alias[" + type + ".MinValue] = 31; " +
            "return data.Count == 2 && data[" + type + ".MinValue] == 31 && " +
            "data[" + type + ".MaxValue] == 29 && data.ContainsKey(" + type + ".MinValue) ? 1 : 0;");
    }

    [TestCase("return data.Count;")]
    [TestCase("return data.ContainsKey(0) ? 1 : 0;")]
    [TestCase("data.Add(17, 23); return data[17];")]
    [TestCase("data.Add(17, 23); return data.ContainsKey(18) ? 1 : 0;")]
    [TestCase("data[17] = 23; data[17] = 29; return data.Count * 100 + data[17];")]
    [TestCase("var alias = data; alias.Add(17, 23); return data[17];")]
    [TestCase("data.Add(17, 23); var before = data[17]; data[17] = 29; return before + data[17];")]
    [TestCase("var other = new System.Collections.Generic.Dictionary<int, int>(); data.Add(17, 23); other.Add(17, 99); return data[17];")]
    public void FreshIntegerDictionaryProgramsAgreeWithCompiledClr(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        Compare("var data = new System.Collections.Generic.Dictionary<int, int>(); " + body);
    }

    [Test]
    public void DistinctLongKeysWithEqualHashCodesKeepSeparateValues()
    {
        Compare("var data = new System.Collections.Generic.Dictionary<long, bool>(); data.Add(1L, true); " +
            "data.Add(4294967296L, false); return data.Count == 2 && data[1L] && !data[4294967296L] ? 1 : 0;");
    }

    private static void Compare(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var tree = CSharpSyntaxTree.ParseText("public static class Probe { public static int Target() { " + body + " } }");
        var compilation = CSharpCompilation.Create("DictionaryStateOracle", [tree], TestMetadataReferences.Platform,
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
        var runtime = new AssemblyLoadContext("DictionaryStateOracle", isCollectible: true);
        try
        {
            image.Position = 0;
            var actual = runtime.LoadFromStream(image).GetType("Probe")!.GetMethod("Target")!.Invoke(null, null);
            // The CLR invocation succeeded and warmed the exact framework paths
            // before replay selects the no-initialization/no-fault alternative.
            var options = new IrProgramReplayOptions(_ => null)
            {
                ConcreteHavocValueProvider = request => IrOpaqueCallSite.DictionaryInitializationPrimitive(factory, request.Site) != null &&
                    factory.GetVariableInfo(request.Variable).Type == factory.BooleanType ? factory.CreateBooleanValue(false) : null
            };
            var result = new IrProgramInterpreter(factory).Execute(lowered.Program, null, 5000, options);
            Assert.That(result.Status, Is.EqualTo(IrProgramExecutionStatus.Returned), result.Unsupported?.Detail);
            Assert.That(result.ConsumedApproximation, Is.False);
            Assert.That(result.ReturnValue!.Integer, Is.EqualTo(actual));
        }
        finally
        { runtime.Unload(); }
    }
}

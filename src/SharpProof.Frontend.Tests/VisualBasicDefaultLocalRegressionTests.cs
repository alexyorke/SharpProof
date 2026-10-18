using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class VisualBasicDefaultLocalRegressionTests
{
    [Test]
    public void ExactWholeCfgPreservesTheCompiledDefaultIntegerLocal()
    {
        const string source = "Public Class Subject\nPublic Shared Function Target() As Integer\nDim value As Integer\nReturn value\nEnd Function\nEnd Class";
        var tree = VisualBasicSyntaxTree.ParseText(source, path: "uninitialized-local.vb");
        var compilation = VisualBasicCompilation.Create("DefaultLocalSubject", [tree], TestMetadataReferences.Platform,
            new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optionStrict: OptionStrict.On, optimizationLevel: OptimizationLevel.Release));
        Assert.That(compilation.GetDiagnostics().Where(d => d is null ? throw new System.ArgumentNullException(nameof(d)) : (d.Severity == DiagnosticSeverity.Error)), Is.Empty);
        using var image = new MemoryStream();
        Assert.That(compilation.Emit(image).Success, Is.True);
        var runtime = new System.Runtime.Loader.AssemblyLoadContext("DefaultLocalSubject", isCollectible: true);
        try
        {
            image.Position = 0;
            var method = runtime.LoadFromStream(image).GetType("Subject")!.GetMethod("Target")!;
            var actual = method.Invoke(null, null);
            Assert.That(actual, Is.EqualTo(0));
            var model = compilation.GetSemanticModel(tree);
            var syntax = tree.GetRoot().DescendantNodes().OfType<MethodBlockSyntax>().Single();
            var graph = ControlFlowGraph.Create(syntax, model)!;
            var target = compilation.GetTypeByMetadataName("Subject")!.GetMembers("Target").OfType<IMethodSymbol>().Single();
            var factory = new IrFactory(IrExecutionSemantics.Total);
            var context = new TotalLoweringContext(factory, target);
            Assert.That(context.OwnsBody(graph.OriginalOperation), Is.True);
            var lowered = new RoslynProgramLowerer(factory).LowerCandidate(graph, context);
            var replay = new IrProgramInterpreter(factory).Execute(lowered.Program);
            Assert.That(lowered.IsExact, Is.True);
            Assert.That(lowered.ConstructionLimitExceeded, Is.False);
            Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
            Assert.That(replay.ConsumedApproximation, Is.False);
            Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(System.Numerics.BigInteger.Zero));
        }
        finally { runtime.Unload(); }
    }
}

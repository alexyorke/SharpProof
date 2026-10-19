using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class VisualBasicUnusedLocalAdmissionCensus
{
    [Test]
    public void InspectPreviouslyAdmittedUnusedLocalStorage()
    {
        ImmutableArray<(string Name, string Body)> cases =
        [
            ("unused-decimal", "Dim unused As Decimal\nReturn 7"),
            ("unused-static", "Static unused As Integer\nReturn 7"),
            ("dead-supported-local", "If False Then\nDim unused As Integer\nReturn unused\nEnd If\nReturn 7")
        ];
        foreach (var item in cases)
        {
            var source = "Public Class Subject\nPublic Shared Function Target() As Integer\n" + item.Body + "\nEnd Function\nEnd Class";
            var tree = VisualBasicSyntaxTree.ParseText(source, path: item.Name + ".vb");
            var compilation = VisualBasicCompilation.Create("UnusedLocal_" + item.Name, [tree], TestMetadataReferences.Platform,
                new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optionStrict: OptionStrict.On, optimizationLevel: OptimizationLevel.Release));
            Assert.That(compilation.GetDiagnostics().Where(d => d is null ? throw new System.ArgumentNullException(nameof(d)) : (d.Severity == DiagnosticSeverity.Error)), Is.Empty, item.Name);
            using var image = new MemoryStream();
            Assert.That(compilation.Emit(image).Success, Is.True, item.Name);
            var runtime = new System.Runtime.Loader.AssemblyLoadContext(item.Name, isCollectible: true);
            try
            {
                image.Position = 0;
                var actual = runtime.LoadFromStream(image).GetType("Subject")!.GetMethod("Target")!.Invoke(null, null);
                Assert.That(actual, Is.EqualTo(7), item.Name);
                var graph = ControlFlowGraph.Create(tree.GetRoot().DescendantNodes().OfType<MethodBlockSyntax>().Single(), compilation.GetSemanticModel(tree))!;
                var target = compilation.GetTypeByMetadataName("Subject")!.GetMembers("Target").OfType<IMethodSymbol>().Single();
                var factory = new IrFactory(IrExecutionSemantics.Total);
                var context = new TotalLoweringContext(factory, target);
                Assert.That(context.OwnsBody(graph.OriginalOperation), Is.True, item.Name);
                var lowered = new RoslynProgramLowerer(factory).LowerCandidate(graph, context);
                var replay = new IrProgramInterpreter(factory).Execute(lowered.Program);
                Assert.That(lowered.IsExact, Is.True, item.Name);
                Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned), item.Name);
                Assert.That(replay.ConsumedApproximation, Is.False, item.Name);
                Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(7)), item.Name);
            }
            finally { runtime.Unload(); }
        }
    }
}

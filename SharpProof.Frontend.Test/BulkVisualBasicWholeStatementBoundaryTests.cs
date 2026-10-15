using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class BulkVisualBasicWholeStatementBoundaryTests
{
    [TestCase("uninitialized-local", "Dim value As Integer\nReturn value", 0, true)]
    [TestCase("multiple-declarators", "Dim left, right As Integer\nleft = 8\nright = 9\nReturn left + right", 17, true)]
    [TestCase("redim", "Dim values(1) As Integer\nReDim values(2)\nReturn values.Length", 3, false)]
    [TestCase("erase", "Dim values = New Integer() {1, 2}\nErase values\nReturn If(values Is Nothing, 17, 0)", 17, false)]
    [TestCase("for-loop", "Dim total As Integer = 0\nFor i As Integer = 1 To 4\ntotal += i\nNext\nReturn total", 10, true)]
    [TestCase("redim-single-element", "Dim values(1) As Integer\nReDim values(0)\nReturn values.Length", 1, false)]
    [TestCase("redim-preserve-expand", "Dim values = New Integer() {3, 5}\nReDim Preserve values(3)\nReturn values(0) + values(1) + values(2) + values.Length", 12, false)]
    [TestCase("redim-preserve-shrink", "Dim values = New Integer() {3, 5}\nReDim Preserve values(0)\nReturn values(0) + values.Length", 4, false)]
    [TestCase("erase-two-arrays", "Dim first = New Integer() {1}\nDim second = New Integer() {2}\nErase first, second\nReturn If(first Is Nothing AndAlso second Is Nothing, 17, 0)", 17, false)]
    [TestCase("erase-uninitialized-array", "Dim values() As Integer\nErase values\nReturn If(values Is Nothing, 17, 0)", 17, false)]
    public void WholePublicCfgMatchesSupportedClrBehaviorOrClosesUnsupportedStatements(string name, string body, int expected, bool exact)
    {
        var source = "Public Class Subject\nPublic Shared Function Target() As Integer\n" + body + "\nEnd Function\nEnd Class";
        var tree = VisualBasicSyntaxTree.ParseText(source, path: name + ".vb");
        var compilation = VisualBasicCompilation.Create("WholeStatement_" + name, [tree], TestMetadataReferences.Platform,
            new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optionStrict: OptionStrict.On, optimizationLevel: OptimizationLevel.Release));
        Assert.That(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error), Is.Empty);
        using var image = new MemoryStream();
        Assert.That(compilation.Emit(image).Success, Is.True);
        var runtime = new System.Runtime.Loader.AssemblyLoadContext(name, isCollectible: true);
        try
        {
            image.Position = 0;
            Assert.That(runtime.LoadFromStream(image).GetType("Subject")!.GetMethod("Target")!.Invoke(null, null), Is.EqualTo(expected));
            var model = compilation.GetSemanticModel(tree);
            var graph = ControlFlowGraph.Create(tree.GetRoot().DescendantNodes().OfType<MethodBlockSyntax>().Single(), model)!;
            var target = compilation.GetTypeByMetadataName("Subject")!.GetMembers("Target").OfType<IMethodSymbol>().Single();
            var factory = new IrFactory(IrExecutionSemantics.Total);
            var context = new TotalLoweringContext(factory, target);
            Assert.That(context.OwnsBody(graph.OriginalOperation), Is.True);
            var lowered = new RoslynProgramLowerer(factory).LowerCandidate(graph, context);
            Assert.That(lowered.IsExact, Is.EqualTo(exact));
            Assert.That(lowered.ConstructionLimitExceeded, Is.False);
            if (exact)
            {
                var replay = new IrProgramInterpreter(factory).Execute(lowered.Program);
                Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
                Assert.That(replay.ConsumedApproximation, Is.False);
                Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(expected)));
            }
            else
            { Assert.That(lowered.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedStatement)); }
        }
        finally { runtime.Unload(); }
    }
}

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class ExplicitAccessorAdmissionCoverageTests
{
    [TestCase("get { return 17; }")]
    [TestCase("get => 17;")]
    public void ExplicitGetterUsesItsOwnedBodyInsteadOfAutoAccessorAdmission(string getter)
    {
        System.ArgumentNullException.ThrowIfNull(getter);
        var source = "public sealed class Subject { public static int Value { " + getter + " } }";
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp12), "explicit-accessor.cs");
        var compilation = CSharpCompilation.Create("ExplicitAccessor", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        using var image = new System.IO.MemoryStream();
        Assert.That(compilation.Emit(image).Success, Is.True);
        var runtime = new System.Runtime.Loader.AssemblyLoadContext("ExplicitAccessor", isCollectible: true);
        try
        {
            image.Position = 0;
            var native = runtime.LoadFromStream(image).GetType("Subject")!.GetProperty("Value")!.GetValue(null);
            Assert.That(native, Is.EqualTo(17));
            var declaration = tree.GetRoot().DescendantNodes().OfType<AccessorDeclarationSyntax>().Single();
            var model = compilation.GetSemanticModel(tree);
            var target = (IMethodSymbol)model.GetDeclaredSymbol(declaration)!;
            var factory = new IrFactory(IrExecutionSemantics.Total);
            var context = new TotalLoweringContext(factory, target);
            var graph = ControlFlowGraph.Create(declaration, model)!;
            Assert.That(context.OwnsBody(graph.OriginalOperation), Is.True);
            var lowerer = new RoslynProgramLowerer(factory);
            Assert.That(lowerer.LowerAutoAccessor(context, declaration), Is.Null);
            var lowered = lowerer.LowerCandidate(graph, context);
            Assert.That(lowered.IsExact, Is.True);
            Assert.That(lowered.ConstructionLimitExceeded, Is.False);
            var replay = new IrProgramInterpreter(factory).Execute(lowered.Program);
            Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
            Assert.That(replay.ConsumedApproximation, Is.False);
            Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger((int)native!)));
        }
        finally
        { runtime.Unload(); }
    }
}

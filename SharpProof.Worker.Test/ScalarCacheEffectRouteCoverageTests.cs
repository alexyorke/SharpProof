using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.Frontend;
using SharpProof.Ir;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class ScalarCacheEffectRouteCoverageTests
{
    [TestCase("int")]
    [TestCase("string")]
    public void RegisteredEmptyArrayRetainsItsCacheAllocationEffect(string elementType)
    {
        var tree = CSharpSyntaxTree.ParseText(
            "public static class Subject { public static int Target() => System.Array.Empty<" + elementType + ">().Length; }");
        var compilation = CSharpCompilation.Create("ScalarCacheControl", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        using var image = new MemoryStream();
        Assert.That(compilation.Emit(image).Success, Is.True);
        var runtime = new System.Runtime.Loader.AssemblyLoadContext("ScalarCacheOracle", isCollectible: true);
        try
        {
            image.Position = 0;
            var target = runtime.LoadFromStream(image).GetType("Subject")!.GetMethod("Target")!;
            Assert.That(target.Invoke(null, []), Is.EqualTo(0));
        }
        finally
        { runtime.Unload(); }
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var model = compilation.GetSemanticModel(tree);
        var method = (IMethodSymbol)model.GetDeclaredSymbol(syntax)!;
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, method);
        var lowering = TotalBodyLowering.Lower(compilation, ControlFlowGraph.Create(syntax, model)!, context, default);
        Assert.That(lowering.IsExact, Is.True);
        Assert.That(lowering.ConstructionLimitExceeded, Is.False);
        var call = lowering.Program.Blocks.SelectMany(block => block.Instructions).OfType<IrCallInstruction>().Single();
        var invocation = (Microsoft.CodeAnalysis.Operations.IInvocationOperation)model.GetOperation(
            syntax.DescendantNodes().OfType<InvocationExpressionSyntax>().Single())!;
        var description = factory.GetOperationInfo(call.Operation).Description;
        Assert.That(description, Is.Not.Null);
        Assert.That(factory.GetString(description!.Value),
            Is.EqualTo("opaque-call:2:" + CompilerIdentityBridge.CreateSymbolDisplay(invocation.TargetMethod)));
    }
}

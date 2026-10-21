using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class GenericRootShadowTypeTestCoverageTests
{
    [Test]
    public void ActualGenericRootTypeTestRecordsOnlyThePossibleBoxingBoundaryInShadowBody()
    {
        const string source = "public static class Subject { public static bool Target<T, U>(T value) => value is U; }";
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp12), "generic-root.cs");
        var compilation = CSharpCompilation.Create("ActualGenericRoot", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        using var image = new System.IO.MemoryStream();
        Assert.That(compilation.Emit(image).Success, Is.True);
        var runtime = new System.Runtime.Loader.AssemblyLoadContext("ActualGenericRoot", isCollectible: true);
        try
        {
            image.Position = 0;
            var target = runtime.LoadFromStream(image).GetType("Subject")!.GetMethod("Target")!;
            Assert.That(target.MakeGenericMethod(typeof(int), typeof(int)).Invoke(null, [17]), Is.True);
            Assert.That(target.MakeGenericMethod(typeof(int), typeof(string)).Invoke(null, [17]), Is.False);
            var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
            var model = compilation.GetSemanticModel(tree);
            var method = (IMethodSymbol)model.GetDeclaredSymbol(syntax)!;
            var operation = (IIsTypeOperation)model.GetOperation(syntax.ExpressionBody!.Expression)!;
            Assert.That(CSharpOperationSemantics.OpaqueTypeTestOperand(operation), Is.SameAs(operation.ValueOperand));
            Assert.That(CSharpOperationSemantics.OpaqueTypeTestMayAllocate(operation, compilation), Is.True);
            var factory = new IrFactory(IrExecutionSemantics.Total);
            var context = new TotalLoweringContext(factory, method);
            var graph = ControlFlowGraph.Create(syntax, model)!;
            var shadow = new RoslynProgramLowerer(factory).LowerShadowSourceBody(graph, context, static _ => true, default);
            Assert.That(shadow.Classification.IsExact, Is.True, shadow.Classification.Abstention.ToString());
            Assert.That(shadow.IsShadowCallSkeleton, Is.True);
            Assert.That(shadow.IsExact, Is.False);
            Assert.That(shadow.PreservedSourceCalls, Is.Empty);
            var instructions = shadow.Program.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions)).ToImmutableArray();
            Assert.That(instructions.OfType<IrCallInstruction>(), Is.Empty);
            Assert.That(instructions.OfType<IrAllocationInstruction>(), Is.Empty);
            var havoc = instructions.OfType<IrHavocInstruction>().Single();
            Assert.That(havoc.Origin, Is.EqualTo(IrHavocOrigin.Approximation));
            Assert.That(factory.GetString(factory.GetOperationInfo(havoc.Operation).Description!.Value), Is.EqualTo("opaque-call:2:type-test-boxing"));
            using var supported = TypedProgramSubject.Create("bool Target(bool value) => value;");
            Assert.That(supported.Invoke([true]), Is.True);
            Assert.That(supported.Invoke([false]), Is.False);
            var lowering = supported.Lower();
            Assert.That(lowering.IsExact, Is.True);
            var execution = supported.Execute(lowering, [true]);
            Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
            Assert.That(execution.ConsumedApproximation, Is.False);
            Assert.That(execution.ReturnValue!.Boolean, Is.True);
        }
        finally { runtime.Unload(); }
    }
}

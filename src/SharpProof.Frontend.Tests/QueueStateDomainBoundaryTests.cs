using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Frontend.Host;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

public sealed class QueueStateDomainBoundaryTests
{
    [TestCase("data.Count", 255)]
    [TestCase("Get().Count", 0)]
    [TestCase("Static", 0)]
    public void QueueClausesDeclineUnmodeledReceiverAndDepth(string expression, int depth)
    {
        ArgumentNullException.ThrowIfNull(expression);
        var tree = CSharpSyntaxTree.ParseText("public static class Probe { public static int Target(System.Collections.Generic.Queue<int> data) { return " + expression + "; } static System.Collections.Generic.Queue<int> Get() => null; static int Static => 0; }");
        var compilation = CSharpCompilation.Create("QueueClauseBoundary", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var model = compilation.GetSemanticModel(tree);
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().First();
        var context = new TotalLoweringContext(new IrFactory(IrExecutionSemantics.Total), model.GetDeclaredSymbol(syntax)!);
        var operation = model.GetOperation(syntax.DescendantNodes().OfType<ReturnStatementSyntax>().Single().Expression!)!;
        var lowered = new RoslynTotalExpressionLowerer(context).LowerClause(operation, depth: depth);
        Assert.That(lowered.Classification.IsExact, Is.False);
    }

    [TestCase("data.Count", 255)]
    [TestCase("data.Enqueue(key + 0)", 254)]
    [TestCase("new System.Collections.Generic.Queue<int>(key + 0)", 254)]
    [TestCase("data.Peek()", 0)]
    public void QueueRejectedBodyNeverEmitsLogicalStores(string expression, int depth)
    {
        ArgumentNullException.ThrowIfNull(expression);
        var tree = CSharpSyntaxTree.ParseText("public static class Probe { public static void Target(System.Collections.Generic.Queue<int> data, int key) { " +
            (expression == "data.Count" ? "var value = " + expression : expression) + "; } }");
        var compilation = CSharpCompilation.Create("QueueBodyBoundary", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new ArgumentNullException(nameof(diagnostic)) : diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var model = compilation.GetSemanticModel(tree);
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var context = new TotalLoweringContext(new IrFactory(IrExecutionSemantics.Total), model.GetDeclaredSymbol(syntax)!);
        var operation = expression == "data.Count"
            ? model.GetOperation(syntax.DescendantNodes().OfType<MemberAccessExpressionSyntax>().Single())!
            : model.GetOperation(syntax.Body!.Statements.Single()) is IExpressionStatementOperation statement ? statement.Operation : throw new InvalidOperationException();
        var builder = new IrProgramBuilder(context.Factory);
        var entry = builder.CreateBlock();
        var exceptional = builder.CreateBlock();
        var site = context.Factory.CreateOperation();
        builder.ExceptionalExit(exceptional, site);
        var lowered = new RoslynTotalExpressionLowerer(context, builder, exceptional).LowerBodyValue(operation, entry, depth);
        Assert.That(lowered.Classification.IsExact, Is.False);
        builder.Return(lowered.Continuation, site);
        Assert.That(builder.Build().Blocks.SelectMany(block => block is null ? throw new ArgumentNullException(nameof(block)) : block.Instructions)
            .OfType<IrWriteInstruction>().Where(write => write is null ? throw new ArgumentNullException(nameof(write)) : write.IsStore), Is.Empty);
    }
}

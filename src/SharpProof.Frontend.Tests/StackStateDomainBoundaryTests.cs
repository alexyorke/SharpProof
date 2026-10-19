using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Frontend.Host;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

public sealed class StackStateDomainBoundaryTests
{
    [TestCase("int")]
    [TestCase("object")]
    [TestCase("decimal")]
    [TestCase("System.Guid")]
    public void StackStorageAdmitsScalarReferenceAndOpaqueDomains(string element)
    {
        ArgumentNullException.ThrowIfNull(element);
        var (model, syntax, _) = Prepare("System.Collections.Generic.Stack<" + element + "> data", "data.Push(default(" + element + ")); data.Pop(); return data.Count;");
        foreach (var invocation in syntax.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            Assert.That(CSharpOperationSemantics.IsStackStateMember(((IInvocationOperation)model.GetOperation(invocation)!).TargetMethod), Is.True);
        }
    }

    [TestCase("data.Count", 255)]
    [TestCase("Get().Count", 0)]
    [TestCase("Static", 0)]
    public void StackClausesDeclineUnsupportedReceiversAndDepth(string expression, int depth)
    {
        ArgumentNullException.ThrowIfNull(expression);
        var (model, syntax, context) = Prepare("System.Collections.Generic.Stack<int> data", "return " + expression + ";");
        var operation = model.GetOperation(syntax.DescendantNodes().OfType<ReturnStatementSyntax>().Single().Expression!)!;
        var lowered = new RoslynTotalExpressionLowerer(context).LowerClause(operation, depth: depth);
        Assert.That(lowered.Classification.IsExact, Is.False);
    }

    [TestCase("data.Push(key + 0)", 254)]
    [TestCase("new System.Collections.Generic.Stack<int>(key + 0)", 254)]
    [TestCase("data.Pop()", 255)]
    [TestCase("data.Clear()", 0)]
    public void RejectedStackAccessDoesNotEmitLogicalStores(string expression, int depth)
    {
        ArgumentNullException.ThrowIfNull(expression);
        var (model, syntax, context) = Prepare("System.Collections.Generic.Stack<int> data, int key", expression + "; return 0;");
        var operation = model.GetOperation(syntax.DescendantNodes().OfType<ExpressionStatementSyntax>().Single().Expression)!;
        var builder = new IrProgramBuilder(context.Factory);
        var entry = builder.CreateBlock();
        var exceptional = builder.CreateBlock();
        var site = context.Factory.CreateOperation();
        builder.ExceptionalExit(exceptional, site);
        var lowered = new RoslynTotalExpressionLowerer(context, builder, exceptional).LowerBodyValue(operation, entry, depth);
        builder.Return(lowered.Continuation, site, context.Factory.Integer(0));
        var program = builder.Build();
        Assert.That(program.Blocks.SelectMany(block => block is null ? throw new ArgumentNullException(nameof(block)) : block.Instructions)
            .OfType<IrWriteInstruction>().Where(write => write is null ? throw new ArgumentNullException(nameof(write)) : write.IsStore), Is.Empty);
    }

    private static (SemanticModel Model, MethodDeclarationSyntax Syntax, TotalLoweringContext Context) Prepare(string parameters, string body)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(body);
        var tree = CSharpSyntaxTree.ParseText("public static class Probe { public static int Target(" + parameters + ") { " + body + " } static System.Collections.Generic.Stack<int> Get() => null; static int Static => 0; }");
        var compilation = CSharpCompilation.Create("StackDomainBoundary", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new ArgumentNullException(nameof(diagnostic)) : diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var model = compilation.GetSemanticModel(tree);
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().First();
        var context = new TotalLoweringContext(new IrFactory(IrExecutionSemantics.Total), model.GetDeclaredSymbol(syntax)!);
        return (model, syntax, context);
    }
}

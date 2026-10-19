using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using NUnit.Framework;
using SharpProof.Frontend.Host;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

public sealed class DictionaryClauseAdmissionBoundaryTests
{
    [TestCase("return data.Count;", 255)]
    [TestCase("return data.ContainsKey(key + 0) ? 1 : 0;", 254)]
    [TestCase("return data[key + 0];", 254)]
    [TestCase("return data.Count;", 256)]
    public void DeepDictionaryClauseChildrenRemainOutsideTheSupportedDomain(string body, int depth)
    {
        ArgumentNullException.ThrowIfNull(body);
        var (model, syntax, context) = Prepare(body);
        var expression = syntax.DescendantNodes().OfType<ReturnStatementSyntax>().Single().Expression!;
        if (expression is ConditionalExpressionSyntax conditional)
        { expression = conditional.Condition; }
        var lowered = new RoslynTotalExpressionLowerer(context).LowerClause(model.GetOperation(expression)!, TotalParameterState.Current, depth);
        Assert.That(lowered.Classification.IsExact, Is.False);
        Assert.That(lowered.Classification.Abstention, Is.EqualTo(FrontendAbstention.ExpressionDepthLimit));
        Assert.That(new IrInterpreter(context.Factory).Evaluate(lowered.SafeCondition).Value!.Boolean, Is.False);
    }

    [TestCase("return (data = new System.Collections.Generic.Dictionary<int, int>()).Count;")]
    [TestCase("return data.ContainsKey(ReadKey()) ? 1 : 0;")]
    [TestCase("return data[ReadKey()];")]
    public void DictionaryClauseAdmissionDoesNotHideReceiverWritesOrKeyCalls(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var (model, syntax, context) = Prepare(body);
        var expression = syntax.DescendantNodes().OfType<ReturnStatementSyntax>().Single().Expression!;
        if (expression is ConditionalExpressionSyntax conditional)
        { expression = conditional.Condition; }
        var lowered = new RoslynTotalExpressionLowerer(context).LowerClause(model.GetOperation(expression)!);
        Assert.That(lowered.Classification.IsExact, Is.False);
        Assert.That(lowered.Classification.Abstention, Is.Not.EqualTo(FrontendAbstention.None));
        Assert.That(new IrInterpreter(context.Factory).Evaluate(lowered.SafeCondition).Value!.Boolean, Is.False);
    }

    [Test]
    public void MutatingDictionaryAddCannotBecomeAPureClauseExpression()
    {
        var (model, syntax, context) = Prepare("data.Add(key, 17); return 0;");
        var add = syntax.Body!.Statements.OfType<ExpressionStatementSyntax>().Single().Expression;
        var lowered = new RoslynTotalExpressionLowerer(context).LowerClause(model.GetOperation(add)!);
        Assert.That(lowered.Classification.IsExact, Is.False);
        Assert.That(lowered.Classification.Abstention, Is.Not.EqualTo(FrontendAbstention.None));
        Assert.That(new IrInterpreter(context.Factory).Evaluate(lowered.SafeCondition).Value!.Boolean, Is.False);
    }

    private static (SemanticModel Model, MethodDeclarationSyntax Syntax, TotalLoweringContext Context) Prepare(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var tree = CSharpSyntaxTree.ParseText("public static class Probe { public static int Target(int key) { " +
            "var data = new System.Collections.Generic.Dictionary<int, int>(); " + body + " } static int ReadKey() => 1; }");
        var compilation = CSharpCompilation.Create("DictionaryClauseAdmissionBoundary", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new ArgumentNullException(nameof(diagnostic)) : diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var model = compilation.GetSemanticModel(tree);
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method is null ? throw new ArgumentNullException(nameof(method)) : method.Identifier.ValueText == "Target");
        var context = new TotalLoweringContext(new IrFactory(IrExecutionSemantics.Total), model.GetDeclaredSymbol(syntax)!);
        context.TrackDefaultDictionaries(ControlFlowGraph.Create(syntax, model)!);
        return (model, syntax, context);
    }
}

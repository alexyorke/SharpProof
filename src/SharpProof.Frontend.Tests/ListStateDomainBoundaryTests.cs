using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Frontend.Host;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

public sealed class ListStateDomainBoundaryTests
{
    [TestCase("var alias = data;", true)]
    [TestCase("var alias = data; alias = data = new System.Collections.Generic.List<int>();", true)]
    [TestCase("System.Collections.Generic.IList<int> alias = data;", true)]
    [TestCase("System.Collections.Generic.ICollection<int> alias = data;", true)]
    [TestCase("var alias = choose ? data : new System.Collections.Generic.List<int>();", true)]
    [TestCase("var alias = choose ? data : incoming;", false)]
    [TestCase("var alias = data; publish(alias);", false)]
    [TestCase("var alias = data; System.Action callback = () => publish(alias);", false)]
    [TestCase("var alias = data; ref var byref = ref alias;", false)]
    [TestCase("var alias = data; alias = alias;", false)]
    [TestCase("var alias = data; publish(data);", false)]
    public void OnlyFreshUnpublishedListAliasesEstablishInterfaceImplementation(string declarations, bool expected)
    {
        ArgumentNullException.ThrowIfNull(declarations);
        var (model, syntax, graph, context) = Prepare("bool choose, System.Collections.Generic.IList<int> incoming, System.Action<System.Collections.Generic.IList<int>> publish",
            "var data = new System.Collections.Generic.List<int>(); " + declarations + " return alias.Count;");
        context.TrackFreshLists(graph);
        var property = (IPropertyReferenceOperation)model.GetOperation(syntax.DescendantNodes().OfType<ReturnStatementSyntax>().Single().Expression!)!;
        Assert.That(context.IsFreshList(property.Instance!), Is.EqualTo(expected));
    }

    [TestCase("int", true)]
    [TestCase("long", true)]
    [TestCase("object", true)]
    [TestCase("string", true)]
    [TestCase("int[]", true)]
    [TestCase("decimal", false)]
    [TestCase("System.Guid", false)]
    public void ElementContentsRequireAnInterpretedValueDomain(string element, bool contents)
    {
        ArgumentNullException.ThrowIfNull(element);
        var (model, syntax, _, _) = Prepare("System.Collections.Generic.List<" + element + "> data",
            "data.Add(default(" + element + ")); var item = data[0]; return data.Count;");
        var invocation = (IInvocationOperation)model.GetOperation(syntax.DescendantNodes().OfType<InvocationExpressionSyntax>().Single())!;
        var properties = syntax.DescendantNodes().OfType<ElementAccessExpressionSyntax>().Select(node => node is null ? throw new ArgumentNullException(nameof(node)) : (IPropertyReferenceOperation)model.GetOperation(node)!).ToArray();
        Assert.That(CSharpOperationSemantics.IsListStateMember(invocation.TargetMethod), Is.EqualTo(contents));
        Assert.That(CSharpOperationSemantics.IsConcreteListStateMember(invocation.TargetMethod), Is.EqualTo(contents));
        Assert.That(CSharpOperationSemantics.IsListStateMember(properties.Single().Property.GetMethod!), Is.EqualTo(contents));
        var count = (IPropertyReferenceOperation)model.GetOperation(syntax.DescendantNodes().OfType<ReturnStatementSyntax>().Single().Expression!)!;
        Assert.That(CSharpOperationSemantics.IsListStateMember(count.Property.GetMethod!), Is.True);
        Assert.That(CSharpOperationSemantics.IsConcreteListStateMember(count.Property.GetMethod!), Is.True);
    }

    [TestCase("new System.Collections.Generic.List<int>()", true)]
    [TestCase("new System.Collections.Generic.List<decimal>()", true)]
    [TestCase("new System.Collections.Generic.List<int>(4)", false)]
    [TestCase("new System.Collections.Generic.List<int>() { 17 }", false)]
    public void OnlyParameterlessUninitializedConstructionEstablishesEmptyState(string expression, bool expected)
    {
        ArgumentNullException.ThrowIfNull(expression);
        var (model, syntax, _, _) = Prepare("", "var data = " + expression + "; return data.Count;");
        var creation = syntax.DescendantNodes().OfType<ObjectCreationExpressionSyntax>().Single();
        Assert.That(CSharpOperationSemantics.IsEmptyListCreation(model.GetOperation(creation)!), Is.EqualTo(expected));
    }

    [TestCase("receiver", 255)]
    [TestCase("argument", 254)]
    [TestCase("value", 254)]
    [TestCase("unsupported", 0)]
    public void RejectedAccessNeverEmitsLogicalStores(string shape, int depth)
    {
        ArgumentNullException.ThrowIfNull(shape);
        var body = shape switch
        {
            "receiver" => "return data.Count;",
            "argument" => "return data[0 + key];",
            "value" => "data[0] = key + 0; return 0;",
            "unsupported" => "data.Clear(); return 0;",
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };
        var (model, syntax, graph, context) = Prepare("int key", "var data = new System.Collections.Generic.List<int>(); " + body);
        context.TrackFreshLists(graph);
        SyntaxNode expression = shape switch
        {
            "receiver" or "argument" => syntax.DescendantNodes().OfType<ReturnStatementSyntax>().Single().Expression!,
            "value" => syntax.DescendantNodes().OfType<AssignmentExpressionSyntax>().Single(),
            _ => syntax.DescendantNodes().OfType<InvocationExpressionSyntax>().Single()
        };
        var builder = new IrProgramBuilder(context.Factory);
        var entry = builder.CreateBlock();
        var exceptional = builder.CreateBlock();
        var site = context.Factory.CreateOperation();
        builder.ExceptionalExit(exceptional, site);
        var lowered = new RoslynTotalExpressionLowerer(context, builder, exceptional).LowerBodyValue(model.GetOperation(expression)!, entry, depth);
        Assert.That(lowered.Classification.IsExact, Is.False);
        builder.Return(lowered.Continuation, site, context.Factory.Integer(0));
        Assert.That(builder.Build().Blocks.SelectMany(block => block is null ? throw new ArgumentNullException(nameof(block)) : block.Instructions)
            .OfType<IrWriteInstruction>().Where(write => write is null ? throw new ArgumentNullException(nameof(write)) : write.IsStore), Is.Empty);
    }

    [Test]
    public void PublishedAssignmentExpressionsDoNotEstablishOwnedReceivers()
    {
        var tree = CSharpSyntaxTree.ParseText("public static class Probe { public static System.Collections.Generic.List<int> Published; public static int Target() { return (Published = new System.Collections.Generic.List<int>()).Count; } }");
        var compilation = CSharpCompilation.Create("ListPublication", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var model = compilation.GetSemanticModel(tree);
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var context = new TotalLoweringContext(new IrFactory(IrExecutionSemantics.Total), model.GetDeclaredSymbol(syntax)!);
        context.TrackFreshLists(ControlFlowGraph.Create(syntax, model)!);
        var property = (IPropertyReferenceOperation)model.GetOperation(syntax.DescendantNodes().OfType<ReturnStatementSyntax>().Single().Expression!)!;
        Assert.That(context.IsFreshList(property.Instance!), Is.False);
    }

    [TestCase("data.Count", 255)]
    [TestCase("Get().Count", 0)]
    [TestCase("data[Index()]", 0)]
    public void ListClausesDeclineUnsupportedReceiversIndexesAndDepth(string expression, int depth)
    {
        ArgumentNullException.ThrowIfNull(expression);
        var tree = CSharpSyntaxTree.ParseText("public static class Probe { public static int Target(System.Collections.Generic.List<int> data) { return " + expression + "; } static System.Collections.Generic.List<int> Get() => null; static int Index() => 0; }");
        var compilation = CSharpCompilation.Create("ListClauseBoundary", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var model = compilation.GetSemanticModel(tree);
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().First();
        var context = new TotalLoweringContext(new IrFactory(IrExecutionSemantics.Total), model.GetDeclaredSymbol(syntax)!);
        var operation = model.GetOperation(syntax.DescendantNodes().OfType<ReturnStatementSyntax>().Single().Expression!)!;
        var lowered = new RoslynTotalExpressionLowerer(context).LowerClause(operation, depth: depth);
        Assert.That(lowered.Classification.IsExact, Is.False);
    }

    private static (SemanticModel Model, MethodDeclarationSyntax Syntax, ControlFlowGraph Graph, TotalLoweringContext Context) Prepare(string parameters, string body)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(body);
        var tree = CSharpSyntaxTree.ParseText("public static class Probe { public static int Target(" + parameters + ") { " + body + " } }");
        var compilation = CSharpCompilation.Create("ListDomainBoundary", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new ArgumentNullException(nameof(diagnostic)) : diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var model = compilation.GetSemanticModel(tree);
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var context = new TotalLoweringContext(new IrFactory(IrExecutionSemantics.Total), model.GetDeclaredSymbol(syntax)!);
        return (model, syntax, ControlFlowGraph.Create(syntax, model)!, context);
    }
}

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using NUnit.Framework;
using SharpProof.Frontend.Host;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

public sealed class DictionaryDomainBoundaryTests
{
    [TestCase("((System.Collections.Generic.Dictionary<int, int>)data).Count", true)]
    [TestCase("((System.Collections.Generic.Dictionary<int, int>)(object)data).Count", false)]
    public void DictionaryConstructorOriginCrossesIdentityCastsButRequiresEvidenceForReferenceCasts(string expression, bool expected)
    {
        ArgumentNullException.ThrowIfNull(expression);
        var (model, syntax, graph, context) = Prepare("",
            "var data = new System.Collections.Generic.Dictionary<int, int>(); return " + expression + ";");
        context.TrackDefaultDictionaries(graph);
        var property = (Microsoft.CodeAnalysis.Operations.IPropertyReferenceOperation)model.GetOperation(
            syntax.DescendantNodes().OfType<ReturnStatementSyntax>().Single().Expression!)!;
        Assert.That(context.IsDefaultDictionaryOrigin(property.Instance!), Is.EqualTo(expected));
    }

    [TestCase("receiver", 255)]
    [TestCase("argument", 254)]
    [TestCase("value", 254)]
    [TestCase("unsupported-member", 0)]
    [TestCase("capacity", 254)]
    public void UnsupportedOrOverDeepAccessNeverEmitsDictionaryStores(string shape, int depth)
    {
        ArgumentNullException.ThrowIfNull(shape);
        var body = shape switch
        {
            "receiver" => "return data.Count;",
            "capacity" => "return new System.Collections.Generic.Dictionary<int, int>(key + 0).Count;",
            "argument" => "return data.ContainsKey(key + 0) ? 1 : 0;",
            "value" => "data[0] = value + 0; return 0;",
            "unsupported-member" => "data.Clear(); return 0;",
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };
        var (model, syntax, graph, context) = Prepare("int key, int value", "var data = new System.Collections.Generic.Dictionary<int, int>(); " + body);
        context.TrackDefaultDictionaries(graph);
        SyntaxNode expression = shape switch
        {
            "receiver" => syntax.DescendantNodes().OfType<ReturnStatementSyntax>().Single().Expression!,
            "argument" or "unsupported-member" => syntax.DescendantNodes().OfType<InvocationExpressionSyntax>().Single(),
            "value" => syntax.DescendantNodes().OfType<AssignmentExpressionSyntax>().Single(),
            "capacity" => syntax.DescendantNodes().OfType<ObjectCreationExpressionSyntax>().Last(),
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };
        var builder = new IrProgramBuilder(context.Factory);
        var entry = builder.CreateBlock();
        var exceptional = builder.CreateBlock();
        var site = context.Factory.CreateOperation();
        builder.ExceptionalExit(exceptional, site);
        var lowered = new RoslynTotalExpressionLowerer(context, builder, exceptional).LowerBodyValue(model.GetOperation(expression)!, entry, depth);
        Assert.That(lowered.Classification.IsExact, Is.False);
        if (depth != 0)
        { Assert.That(lowered.Classification.Abstention, Is.EqualTo(FrontendAbstention.ExpressionDepthLimit)); }
        builder.Return(lowered.Continuation, site, context.Factory.Integer(0));
        Assert.That(builder.Build().Blocks.SelectMany(block => block is null ? throw new ArgumentNullException(nameof(block)) : block.Instructions)
            .OfType<IrWriteInstruction>().Where(write => write is null ? throw new ArgumentNullException(nameof(write)) : write.IsStore), Is.Empty);
    }

    [TestCase("var alias = data;", true)]
    [TestCase("publish(data);", false)]
    [TestCase("var alias = data; publish(alias);", false)]
    [TestCase("var first = data; var second = first; publish(second);", false)]
    [TestCase("var alias = choose ? data : new System.Collections.Generic.Dictionary<int, int>(); publish(alias);", false)]
    public void EscapeProvenanceIncludesAllAliasesAndCapturedJoins(string body, bool expected)
    {
        ArgumentNullException.ThrowIfNull(body);
        var (model, syntax, graph, context) = Prepare("bool choose, System.Action<System.Collections.Generic.Dictionary<int, int>> publish",
            "var data = new System.Collections.Generic.Dictionary<int, int>(); " + body + " return data.Count;");
        context.TrackDefaultDictionaries(graph);
        var property = (Microsoft.CodeAnalysis.Operations.IPropertyReferenceOperation)model.GetOperation(syntax.DescendantNodes().OfType<ReturnStatementSyntax>().Single().Expression!)!;
        Assert.That(context.IsDefaultDictionary(property.Instance!), Is.EqualTo(expected));
    }

    [TestCase("publish(data[17]);", true)]
    [TestCase("var value = data[17]; publish(value);", true)]
    [TestCase("var value = data[17]; System.Action callback = () => publish(value);", true)]
    [TestCase("publish(new object[] { data[17] });", true)]
    [TestCase("var alias = data; publish(alias[17]);", true)]
    [TestCase("publish(data);", false)]
    [TestCase("data[17] = data;", false)]
    [TestCase("var alias = data; data[17] = alias;", false)]
    [TestCase("System.Action callback = () => publish(data[17]);", false)]
    [TestCase("var alias = data; System.Action callback = () => publish(alias[17]);", false)]
    [TestCase("var holder = new object[] { data }; publish(holder);", false)]
    public void ReferenceValuePublicationDoesNotPublishOwnerButCapturesStillDo(string body, bool expected)
    {
        ArgumentNullException.ThrowIfNull(body);
        var (model, syntax, graph, context) = Prepare("System.Action<object> publish",
            "var data = new System.Collections.Generic.Dictionary<int, object>(); data[17] = null; " + body + " return data.Count;");
        context.TrackDefaultDictionaries(graph);
        var property = (Microsoft.CodeAnalysis.Operations.IPropertyReferenceOperation)model.GetOperation(syntax.DescendantNodes().OfType<ReturnStatementSyntax>().Single().Expression!)!;
        Assert.That(context.IsDefaultDictionary(property.Instance!), Is.EqualTo(expected));
    }

    [TestCase("int", "T", "", true, true)]
    [TestCase("int", "T", "where T : class", true, true)]
    [TestCase("int", "T", "where T : struct", true, true)]
    [TestCase("int", "T", "where T : unmanaged", true, true)]
    [TestCase("int", "T", "where T : notnull", true, true)]
    [TestCase("int", "T", "where T : System.IDisposable", true, true)]
    [TestCase("int", "T", "where T : class, System.IDisposable", true, true)]
    [TestCase("int", "T", "where T : System.Exception", true, true)]
    [TestCase("string", "object", "", false, false)]
    [TestCase("T", "int", "where T : System.IComparable<T>", false, false)]
    [TestCase("int", "decimal", "", true, true)]
    [TestCase("int", "object[][]", "", true, false)]
    [TestCase("int", "object[]", "", true, true)]
    [TestCase("int", "int[,]", "", true, true)]
    public void DictionaryValueDomainsLimitContentsRatherThanEmptyState(string key, string value, string constraints, bool emptyState, bool contents)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(constraints);
        var tree = CSharpSyntaxTree.ParseText("public static class Probe { public static void Target<T>() " + constraints +
            " { var data = new System.Collections.Generic.Dictionary<" + key + ", " + value + ">(); } }");
        var compilation = CSharpCompilation.Create("DictionaryValueDomains", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new ArgumentNullException(nameof(diagnostic)) : diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var creation = tree.GetRoot().DescendantNodes().OfType<ObjectCreationExpressionSyntax>().Single();
        var constructor = (IMethodSymbol)compilation.GetSemanticModel(tree).GetSymbolInfo(creation).Symbol!;
        var type = constructor.ContainingType;
        var count = type.GetMembers("Count").OfType<IPropertySymbol>().Single();
        var contains = type.GetMembers("ContainsKey").OfType<IMethodSymbol>().Single();
        var add = type.GetMembers("Add").OfType<IMethodSymbol>().Single();
        var indexer = type.GetMembers().OfType<IPropertySymbol>().Single(property => property is null ? throw new ArgumentNullException(nameof(property)) : property.IsIndexer);
        Assert.That(CSharpOperationSemantics.IsDefaultDictionaryStateMember(constructor), Is.True);
        Assert.That(CSharpOperationSemantics.IsDefaultDictionaryStateMember(count.GetMethod!), Is.True);
        Assert.That(CSharpOperationSemantics.IsDefaultDictionaryStateMember(contains), Is.EqualTo(emptyState));
        Assert.That(CSharpOperationSemantics.IsDefaultDictionaryStateMember(add), Is.EqualTo(contents));
        Assert.That(CSharpOperationSemantics.IsDefaultDictionaryStateMember(indexer.GetMethod!), Is.EqualTo(contents));
        Assert.That(CSharpOperationSemantics.IsDefaultDictionaryStateMember(indexer.SetMethod!), Is.EqualTo(contents));
    }

    [TestCase("new System.Collections.Generic.Dictionary<int, decimal>()", true, true)]
    [TestCase("new System.Collections.Generic.Dictionary<int, decimal>(3)", true, true)]
    [TestCase("new System.Collections.Generic.Dictionary<int, decimal>((System.Collections.Generic.IEqualityComparer<int>)null)", false, false)]
    [TestCase("new System.Collections.Generic.Dictionary<int, decimal> { }", false, true)]
    public void OpaqueValueEmptyCreationAndDefaultReceiverAreDistinctFacts(string expression, bool syntaxEmpty, bool trackedDefaultComparer)
    {
        ArgumentNullException.ThrowIfNull(expression);
        var (model, syntax, graph, context) = Prepare("", "var data = " + expression + "; return data.Count;");
        var creation = syntax.DescendantNodes().OfType<ObjectCreationExpressionSyntax>().Single();
        Assert.That(CSharpOperationSemantics.IsEmptyDefaultDictionaryCreation(model.GetOperation(creation)!), Is.EqualTo(syntaxEmpty));
        context.TrackDefaultDictionaries(graph);
        var property = (Microsoft.CodeAnalysis.Operations.IPropertyReferenceOperation)model.GetOperation(syntax.DescendantNodes().OfType<ReturnStatementSyntax>().Single().Expression!)!;
        Assert.That(context.IsDefaultDictionary(property.Instance!), Is.EqualTo(trackedDefaultComparer));
    }

    [TestCase("((System.Collections.Generic.HashSet<int>)data).Count")]
    [TestCase("(data = new System.Collections.Generic.HashSet<int>()).Count")]
    public void HashSetOriginIncludesIdentityConversionsAndAssignmentValues(string expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        var (model, syntax, graph, context) = Prepare("", "var data = new System.Collections.Generic.HashSet<int>(); return " + expression + ";");
        context.TrackDefaultHashSets(graph);
        var property = (Microsoft.CodeAnalysis.Operations.IPropertyReferenceOperation)model.GetOperation(
            syntax.DescendantNodes().OfType<ReturnStatementSyntax>().Single().Expression!)!;
        Assert.That(context.IsDefaultHashSetOrigin(property.Instance!), Is.True);
    }

    [Test]
    public void HashSetClauseDoesNotHideReceiverAllocation()
    {
        var (model, syntax, _, context) = Prepare("", "return new System.Collections.Generic.HashSet<int>().Count;");
        var expression = syntax.DescendantNodes().OfType<ReturnStatementSyntax>().Single().Expression!;
        var lowered = new RoslynTotalExpressionLowerer(context).LowerClause(model.GetOperation(expression)!);
        Assert.That(lowered.Classification.IsExact, Is.False);
        Assert.That(new IrInterpreter(context.Factory).Evaluate(lowered.SafeCondition).Value!.Boolean, Is.False);
    }

    [TestCase("return data.Count;", 255)]
    [TestCase("return data.Contains(key + 0) ? 1 : 0;", 254)]
    public void HashSetBodyRejectsOverDeepReceiverAndArgumentWithoutStores(string body, int depth)
    {
        ArgumentNullException.ThrowIfNull(body);
        var (model, syntax, graph, context) = Prepare("int key", "var data = new System.Collections.Generic.HashSet<int>(); " + body);
        context.TrackDefaultHashSets(graph);
        SyntaxNode expression = syntax.DescendantNodes().OfType<ReturnStatementSyntax>().Single().Expression!;
        if (expression is ConditionalExpressionSyntax conditional)
        { expression = conditional.Condition; }
        var builder = new IrProgramBuilder(context.Factory);
        var entry = builder.CreateBlock();
        var exceptional = builder.CreateBlock();
        var site = context.Factory.CreateOperation();
        builder.ExceptionalExit(exceptional, site);
        var lowered = new RoslynTotalExpressionLowerer(context, builder, exceptional).LowerBodyValue(model.GetOperation(expression)!, entry, depth);
        Assert.That(lowered.Classification.IsExact, Is.False);
        Assert.That(lowered.Classification.Abstention, Is.EqualTo(FrontendAbstention.ExpressionDepthLimit));
        builder.Return(lowered.Continuation, site, context.Factory.Integer(0));
        Assert.That(builder.Build().Blocks.SelectMany(block => block is null ? throw new ArgumentNullException(nameof(block)) : block.Instructions)
            .OfType<IrWriteInstruction>().Where(write => write is null ? throw new ArgumentNullException(nameof(write)) : write.IsStore), Is.Empty);
    }
    [TestCase("List", true)]
    [TestCase("Queue", true)]
    [TestCase("Stack", true)]
    [TestCase("HashSet", false)]
    public void OpaqueCountFallbackPreservesOnlyCertifiedNonnegativeDomains(string collection, bool nonnegative)
    {
        ArgumentNullException.ThrowIfNull(collection);
        var (model, syntax, _, context) = Prepare("System.Collections.Generic." + collection + "<int> data", "return data.Count;");
        var expression = syntax.DescendantNodes().OfType<ReturnStatementSyntax>().Single().Expression!;
        var operation = (Microsoft.CodeAnalysis.Operations.IPropertyReferenceOperation)model.GetOperation(expression)!;
        var builder = new IrProgramBuilder(context.Factory);
        var entry = builder.CreateBlock();
        var exceptional = builder.CreateBlock();
        builder.ExceptionalExit(exceptional, context.Factory.CreateOperation());
        var lowered = new RoslynTotalExpressionLowerer(context, builder, exceptional).EmitOpaqueCall(
            operation, operation.Property.GetMethod!, [], entry, null, default);
        Assert.That(lowered.Classification.IsExact, Is.True);
        builder.Return(lowered.Continuation, context.Site(operation), lowered.Value);
        var masks = builder.Build().Blocks.SelectMany(block => block is null ? throw new ArgumentNullException(nameof(block)) : block.Instructions)
            .OfType<IrAssignInstruction>().Select(assignment => assignment is null ? throw new ArgumentNullException(nameof(assignment)) : assignment.Value)
            .OfType<IrBinaryTerm>().Where(term => term is null ? throw new ArgumentNullException(nameof(term)) : term.Operator == IrBinaryOperator.BitwiseAnd).ToImmutableArray();
        Assert.That(masks.Length, Is.EqualTo(nonnegative ? 1 : 0));
        if (nonnegative)
        { Assert.That(new IrInterpreter(context.Factory).Evaluate(masks.Single().Right).Value!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(int.MaxValue))); }
    }
    private static (SemanticModel Model, MethodDeclarationSyntax Syntax, ControlFlowGraph Graph, TotalLoweringContext Context) Prepare(string parameters, string body)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(body);
        var tree = CSharpSyntaxTree.ParseText("public static class Probe { public static int Target(" + parameters + ") { " + body + " } }");
        var compilation = CSharpCompilation.Create("DictionaryDomainBoundary", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new ArgumentNullException(nameof(diagnostic)) : diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var model = compilation.GetSemanticModel(tree);
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var context = new TotalLoweringContext(new IrFactory(IrExecutionSemantics.Total), model.GetDeclaredSymbol(syntax)!);
        return (model, syntax, ControlFlowGraph.Create(syntax, model)!, context);
    }
}

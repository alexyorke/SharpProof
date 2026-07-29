using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Dataflow;

namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class ManagedAbstractFlowTests
{
    [Test]
    public void OperationBudgetExhaustionFailsClosed()
    {
        var statements = string.Concat(
            Enumerable.Repeat("value++;", ManagedAbstractFlow.MaxAnalyzedOperations + 1));
        var compilation = EffectTestHost.CreateCompilation(
            "public static class Sample { public static void Calls() { int value = 0;" +
            statements +
            "} }");
        var syntax = compilation.SyntaxTrees.Single().GetRoot()
            .DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var model = compilation.GetSemanticModel(syntax.SyntaxTree);
        var root = (IMethodBodyOperation)model.GetOperation(syntax)!;
        var method = (IMethodSymbol)model.GetDeclaredSymbol(syntax)!;
        var graph = ControlFlowGraph.Create(root);

        var analysis = ManagedAbstractFlow.ForCompilation(compilation)
            .Analyze(method, graph, null, default);

        Assert.That(graph.Blocks.Length, Is.LessThanOrEqualTo(ManagedAbstractFlow.MaxAnalyzedBlocks));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(analysis.Status, Is.EqualTo(ManagedFlowStatus.BudgetExceeded));
            Assert.That(
                analysis.IncompleteReason,
                Is.EqualTo(EffectAnalysisIncompleteReason.OperationBudgetExceeded));
            Assert.That(analysis.Result, Is.Null);
        }
    }

    [Test]
    public void BlockBudgetExhaustionHasASeparateDeterministicReason()
    {
        var branches = string.Concat(
            Enumerable.Repeat(
                "if (condition) { value++; }",
                ManagedAbstractFlow.MaxAnalyzedBlocks));
        var compilation = EffectTestHost.CreateCompilation(
            "public static class Sample {" +
            " public static void Calls(bool condition) { int value = 0;" +
            branches +
            "} }");
        var syntax = compilation.SyntaxTrees.Single().GetRoot()
            .DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var model = compilation.GetSemanticModel(syntax.SyntaxTree);
        var root = (IMethodBodyOperation)model.GetOperation(syntax)!;
        var method = (IMethodSymbol)model.GetDeclaredSymbol(syntax)!;
        var graph = ControlFlowGraph.Create(root);

        var analysis = ManagedAbstractFlow.ForCompilation(compilation)
            .Analyze(method, graph, null, default);

        Assert.That(graph.Blocks.Length, Is.GreaterThan(ManagedAbstractFlow.MaxAnalyzedBlocks));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(analysis.Status, Is.EqualTo(ManagedFlowStatus.BudgetExceeded));
            Assert.That(
                analysis.IncompleteReason,
                Is.EqualTo(EffectAnalysisIncompleteReason.BlockBudgetExceeded));
            Assert.That(analysis.Result, Is.Null);
        }
    }

    [Test]
    public void CyclicControlFlowIsTypedAndHasNoScalarResult()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                public static void Calls(bool condition) {
                    while (condition) {
                    }
                }
            }
            """);
        var syntax = compilation.SyntaxTrees.Single().GetRoot()
            .DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var model = compilation.GetSemanticModel(syntax.SyntaxTree);
        var root = (IMethodBodyOperation)model.GetOperation(syntax)!;
        var method = (IMethodSymbol)model.GetDeclaredSymbol(syntax)!;

        var analysis = ManagedAbstractFlow.ForCompilation(compilation)
            .Analyze(method, ControlFlowGraph.Create(root), null, default);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(analysis.Status, Is.EqualTo(ManagedFlowStatus.Cyclic));
            Assert.That(
                analysis.IncompleteReason,
                Is.EqualTo(EffectAnalysisIncompleteReason.CyclicControlFlow));
            Assert.That(analysis.Result, Is.Null);
        }
    }

    [Test]
    public void IncrementAndDecrementUpdateSubsequentIntervals()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                private static void Sink(int value) {
                }

                public static void Calls(bool condition) {
                    var safe = condition ? 0 : 1;
                    safe++;
                    Sink(safe);

                    var violated = 0;
                    violated--;
                    Sink(violated);
                }
            }
            """);
        var syntax = compilation.SyntaxTrees.Single().GetRoot()
            .DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(static method => method.Identifier.ValueText == "Calls");
        var model = compilation.GetSemanticModel(syntax.SyntaxTree);
        var root = (IMethodBodyOperation)model.GetOperation(syntax)!;
        var method = (IMethodSymbol)model.GetDeclaredSymbol(syntax)!;
        var analysis = ManagedAbstractFlow.ForCompilation(compilation)
            .Analyze(method, ControlFlowGraph.Create(root), null, default);
        var flow = analysis.Result;
        var calls = root.Descendants().OfType<IInvocationOperation>()
            .OrderBy(static call => call.Syntax.SpanStart).ToArray();

        Assert.That(analysis.Status, Is.EqualTo(ManagedFlowStatus.Complete));
        Assert.That(flow, Is.Not.Null);
        Assert.That(calls, Has.Length.EqualTo(2));
        Assert.That(flow!.TryEvaluate(calls[0], calls[0].Arguments[0].Value, out var safe), Is.True);
        Assert.That(flow.TryEvaluate(calls[1], calls[1].Arguments[0].Value, out var violated), Is.True);
        Assert.That(safe.TryGetInteger(out var safeInterval), Is.True);
        Assert.That(violated.TryGetInteger(out var violatedInterval), Is.True);
        Assert.That(safeInterval, Is.EqualTo(IntervalValue.Range(1, 2)));
        Assert.That(violatedInterval, Is.EqualTo(IntervalValue.Constant(-1)));
    }

    [Test]
    public void EqualityBranchesIntersectBothVariableIntervals()
    {
        var (analysis, call) = AnalyzeSingleCall(
            """
            public static class Sample {
                private static void Sink(int left, int right) {
                }

                public static void Calls(
                    [SharpProof.Attributes.InRange(0, 10)] int left,
                    [SharpProof.Attributes.InRange(3, 7)] int right) {
                    if (left == right)
                        Sink(left, right);
                }
            }
            """);

        Assert.That(analysis.Status, Is.EqualTo(ManagedFlowStatus.Complete));
        Assert.That(
            analysis.Result!.TryEvaluate(
                call,
                call.Arguments[0].Value,
                out var left),
            Is.True);
        Assert.That(
            analysis.Result.TryEvaluate(
                call,
                call.Arguments[1].Value,
                out var right),
            Is.True);
        Assert.That(left.TryGetInteger(out var leftInterval), Is.True);
        Assert.That(right.TryGetInteger(out var rightInterval), Is.True);
        Assert.That(leftInterval, Is.EqualTo(IntervalValue.Range(3, 7)));
        Assert.That(rightInterval, Is.EqualTo(IntervalValue.Range(3, 7)));
    }

    [Test]
    public void OrderedBranchesRefineBothVariableIntervals()
    {
        var (analysis, call) = AnalyzeSingleCall(
            """
            public static class Sample {
                private static void Sink(int left, int right) {
                }

                public static void Calls(
                    [SharpProof.Attributes.InRange(0, 10)] int left,
                    [SharpProof.Attributes.InRange(3, 7)] int right) {
                    if (left < right)
                        Sink(left, right);
                }
            }
            """);

        Assert.That(analysis.Status, Is.EqualTo(ManagedFlowStatus.Complete));
        Assert.That(
            analysis.Result!.TryEvaluate(
                call,
                call.Arguments[0].Value,
                out var left),
            Is.True);
        Assert.That(
            analysis.Result.TryEvaluate(
                call,
                call.Arguments[1].Value,
                out var right),
            Is.True);
        Assert.That(left.TryGetInteger(out var leftInterval), Is.True);
        Assert.That(right.TryGetInteger(out var rightInterval), Is.True);
        Assert.That(leftInterval, Is.EqualTo(IntervalValue.Range(0, 6)));
        Assert.That(rightInterval, Is.EqualTo(IntervalValue.Range(3, 7)));
    }

    [Test]
    public void SharedEdgeRefinementPreservesBooleanFactsAcrossOperationIdentity()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                private static void Sink(bool value) {
                }

                public static void Calls(bool condition) {
                    if (condition != true)
                        Sink(condition);
                }
            }
            """);
        var syntax = compilation.SyntaxTrees.Single().GetRoot()
            .DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(static method => method.Identifier.ValueText == "Calls");
        var model = compilation.GetSemanticModel(syntax.SyntaxTree);
        var root = (IMethodBodyOperation)model.GetOperation(syntax)!;
        var method = (IMethodSymbol)model.GetDeclaredSymbol(syntax)!;
        var call = root.Descendants().OfType<IInvocationOperation>().Single();

        var analysis = ManagedAbstractFlow.ForCompilation(compilation)
            .Analyze(method, ControlFlowGraph.Create(root), null, default);
        var flow = analysis.Result;

        Assert.That(analysis.Status, Is.EqualTo(ManagedFlowStatus.Complete));
        Assert.That(flow, Is.Not.Null);
        Assert.That(flow!.TryEvaluate(call, call.Arguments[0].Value, out var value), Is.True);
        Assert.That(value.TryGetBoolean(out var boolean), Is.True);
        Assert.That(boolean, Is.False);
    }

    private static (ManagedFlowAnalysis Analysis, IInvocationOperation Call)
        AnalyzeSingleCall(string source)
    {
        var compilation = EffectTestHost.CreateCompilation(source);
        var syntax = compilation.SyntaxTrees.Single().GetRoot()
            .DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(static method => method.Identifier.ValueText == "Calls");
        var model = compilation.GetSemanticModel(syntax.SyntaxTree);
        var root = (IMethodBodyOperation)model.GetOperation(syntax)!;
        var method = (IMethodSymbol)model.GetDeclaredSymbol(syntax)!;
        var call = root.Descendants().OfType<IInvocationOperation>().Single();
        var analysis = ManagedAbstractFlow.ForCompilation(compilation)
            .Analyze(method, ControlFlowGraph.Create(root), null, default);
        return (analysis, call);
    }

    [Test]
    public void ApprovedApiSpecificationRefinesReturnNullnessAndCardinality()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                public static int[] Calls() => System.Array.Empty<int>();
            }
            """);
        var invocation = compilation.SyntaxTrees.Single().GetRoot()
            .DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
        var model = compilation.GetSemanticModel(invocation.SyntaxTree);
        var operation = (IInvocationOperation)model.GetOperation(invocation)!;

        var value = ManagedAbstractFlow.ForCompilation(compilation)
            .Evaluate(operation, ManagedFlowState.Empty);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(value.IsDefinitelyNonNull, Is.True);
            Assert.That(value.TryGetCardinality(out var cardinality), Is.True);
            Assert.That(cardinality, Is.EqualTo(IntervalValue.Constant(0)));
        }
    }
}

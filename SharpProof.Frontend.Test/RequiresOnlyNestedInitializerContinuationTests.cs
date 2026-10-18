using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Contracts;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class RequiresOnlyNestedInitializerContinuationTests
{
    private static readonly ImmutableArray<bool> RuntimeInputs = [false, true];

    [TestCase(false)]
    [TestCase(true)]
    public void SeparateInitializerArmsRetainDistinctRegisteredStops(bool initializers)
    {
        var predicate = initializers ? "flag ? new System.Collections.Generic.List<int> { 1 }.Count > 0 : new System.Collections.Generic.List<int> { 2 }.Count > 0" : "flag ? true : false";
        var compilation = TestCompilation.Create("RequiresOnlyNestedInitializerContinuation", $$"""
            using SharpProof.Attributes;
            public static class Subject {
                public static int Target(bool flag) {
                    Contract.Requires(true);
                    Contract.Ensures({{predicate}});
                    return 7;
                }
            }
            """);
        var originalTree = compilation.SyntaxTrees.Single();
        var options = ((CSharpParseOptions)originalTree.Options).WithPreprocessorSymbols(Array.Empty<string>());
        compilation = compilation.ReplaceSyntaxTree(originalTree,
            CSharpSyntaxTree.ParseText(originalTree.GetText(), options, originalTree.FilePath));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var target = (IMethodSymbol)model.GetDeclaredSymbol(syntax)!;
        var ensuresSyntax = syntax.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Single(invocation => invocation is null ? throw new System.ArgumentNullException(nameof(invocation)) : (invocation.Expression.ToString() == "Contract.Ensures"));
        var ensures = (IInvocationOperation)model.GetOperation(ensuresSyntax)!;
        Assert.That(ensures.TargetMethod.IsConditional, Is.True);
        Assert.That(CSharpPreprocessorSymbols.GetDefined(tree, TestContext.CurrentContext.CancellationToken), Does.Not.Contain("SHARPPROOF_CONTRACTS"));
        var emission = new CSharpInvocationEmissionPolicy(compilation);
        Assert.That(emission.IsElided(ensures), Is.True);
        var graph = ControlFlowGraph.Create(syntax, model)!;
        Assert.That(graph.OriginalOperation.SemanticModel!.Compilation, Is.SameAs(compilation));
        var conditionals = graph.Blocks.Where(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.ConditionKind != ControlFlowConditionKind.None)).ToArray();
        Assert.That(conditionals, Has.Length.EqualTo(1));
        Assert.That(conditionals[0].IsReachable, Is.True);
        Assert.That(conditionals[0].BranchValue, Is.InstanceOf<IParameterReferenceOperation>());
        Assert.That(ensuresSyntax.Span.Contains(conditionals[0].BranchValue!.Syntax.Span), Is.True);
        Assert.That(graph.Blocks.Any(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.FallThroughSuccessor?.Semantics is
            ControlFlowBranchSemantics.Throw or ControlFlowBranchSemantics.Rethrow)), Is.False);
        Assert.That(Regions(graph.Root).All(region => region is null ? throw new System.ArgumentNullException(nameof(region)) : (region.Kind is
            ControlFlowRegionKind.Root or ControlFlowRegionKind.LocalLifetime)), Is.True);
        var additions = graph.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Operations.Select(operation =>
            operation is null ? throw new System.ArgumentNullException(nameof(operation)) : ((Block: block, Operation: operation is IExpressionStatementOperation statement ? statement.Operation : operation)))))
            .Where(pair => pair.Operation is IInvocationOperation { TargetMethod.Name: "Add" }).ToArray();
        Assert.That(additions, Has.Length.EqualTo(initializers ? 2 : 0));
        var list = compilation.GetTypeByMetadataName("System.Collections.Generic.List`1");
        Assert.That(list, Is.Not.Null);
        foreach (var addition in additions)
        {
            var invocation = (IInvocationOperation)addition.Operation;
            Assert.That(addition.Block.IsReachable, Is.True);
            Assert.That(SymbolEqualityComparer.Default.Equals(invocation.TargetMethod.ContainingType.OriginalDefinition, list), Is.True);
            Assert.That(ensuresSyntax.Span.Contains(invocation.Syntax.Span), Is.True);
            Assert.That(emission.IsElided(invocation), Is.True);
            Assert.That(emission.IsElided(invocation, requireOwningInvocation: true), Is.False);
            var owner = emission.OwningInvocation(invocation);
            Assert.That(owner, Is.Not.Null);
            Assert.That(owner!.Syntax.SyntaxTree, Is.SameAs(tree));
            Assert.That(owner!.Syntax.Span, Is.EqualTo(ensuresSyntax.Span));
        }
        if (initializers)
        {
            Assert.That(additions[0].Block, Is.Not.SameAs(additions[1].Block));
            Assert.That(additions[0].Operation.Syntax.Span, Is.Not.EqualTo(additions[1].Operation.Syntax.Span));
            var fall = Reachable(conditionals[0].FallThroughSuccessor!.Destination!).ToHashSet();
            var conditional = Reachable(conditionals[0].ConditionalSuccessor!.Destination!).ToHashSet();
            Assert.That(additions.Count(addition => fall.Contains(addition.Block)), Is.EqualTo(1));
            Assert.That(additions.Count(addition => conditional.Contains(addition.Block)), Is.EqualTo(1));
            Assert.That(additions.Any(addition => fall.Contains(addition.Block) && conditional.Contains(addition.Block)), Is.False);
        }

        var factory = new IrFactory(IrExecutionSemantics.Total);
        var fullContext = new TotalLoweringContext(factory, target);
        var binder = new ContractBinder(compilation, factory);
        var full = binder.BindTotal(fullContext);
        Assert.That(full.IsSuccess, Is.EqualTo(!initializers));
        Assert.That(full.Failure, Is.EqualTo(initializers ? ContractBindingFailure.UnsupportedExpression : ContractBindingFailure.None));
        Assert.That(full.Clauses, Has.Length.EqualTo(initializers ? 0 : 2));
        var context = new TotalLoweringContext(factory, target);
        Assert.That(context.HasScalarSignature, Is.True);
        Assert.That(context.OwnsBody(graph.OriginalOperation), Is.True);
        var requires = binder.BindTotalRequires(context);
        Assert.That(requires.IsSuccess, Is.True, requires.Failure.ToString());
        Assert.That(requires.Clauses, Has.Length.EqualTo(1));
        Assert.That(requires.Clauses[0].Kind, Is.EqualTo(BoundContractKind.Requires));
        Assert.That(context.IsExcludedSpecificationCall(ensures), Is.True);
        Assert.That(context.IsSpecificationOperation(conditionals[0].BranchValue!), Is.True);
        foreach (var addition in additions)
        { Assert.That(context.IsSpecificationOperation(addition.Operation), Is.True); }
        var lowering = new RoslynProgramLowerer(factory).LowerCandidate(graph, context);
        Assert.That(lowering.ConstructionLimitExceeded, Is.False);
        Assert.That(lowering.IsExact, Is.EqualTo(!initializers));
        Assert.That(lowering.Classification.Abstention, Is.EqualTo(initializers
            ? FrontendAbstention.UnsupportedControlFlow : FrontendAbstention.None));
        if (!initializers)
        {
            foreach (var input in RuntimeInputs)
            {
                var replay = new IrProgramInterpreter(factory).Execute(lowering.Program,
                    new Dictionary<IrVarId, IrValue> { [context.Parameters.Single().Entry] = factory.CreateBooleanValue(input) });
                Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
                Assert.That(replay.ConsumedApproximation, Is.False);
                Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new BigInteger(7)));
            }
        }
        var load = new AssemblyLoadContext("RequiresOnlyNestedInitializerContinuation", isCollectible: true);
        try
        {
            image.Position = 0;
            var runtime = load.LoadFromStream(image).GetType("Subject")!.GetMethod("Target")!;
            Assert.That(runtime.Invoke(null, [false]), Is.EqualTo(7));
            Assert.That(runtime.Invoke(null, [true]), Is.EqualTo(7));
        }
        finally
        { load.Unload(); }
    }

    private static IEnumerable<BasicBlock> Reachable(BasicBlock first)
    {
        System.ArgumentNullException.ThrowIfNull(first);
        var visited = new HashSet<BasicBlock>();
        var pending = new Stack<BasicBlock>();
        pending.Push(first);
        while (pending.TryPop(out var block))
        {
            if (!visited.Add(block))
            { continue; }
            yield return block;
            if (block.FallThroughSuccessor?.Destination is { } fall)
            { pending.Push(fall); }
            if (block.ConditionalSuccessor?.Destination is { } conditional)
            { pending.Push(conditional); }
        }
    }

    private static IEnumerable<ControlFlowRegion> Regions(ControlFlowRegion region)
    {
        System.ArgumentNullException.ThrowIfNull(region);
        yield return region;
        foreach (var child in region.NestedRegions)
        {
            foreach (var descendant in Regions(child))
            { yield return descendant; }
        }
    }

}

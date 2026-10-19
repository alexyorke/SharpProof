using System.Collections.Immutable;
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
public sealed class CompilerSpecificationOwnerRecoveryTests
{
    [Test]
    public void BoundPostconditionCannotBeRestoredAsCompilerOnlyContinuation()
    {
        var compilation = TestCompilation.Create("BoundPostconditionOwnerRecovery", """
            using SharpProof.Attributes;
            public static class Subject
            {
                public static int Target(bool flag)
                {
                    Contract.Requires(true);
                    Contract.Ensures(flag
                        ? throw new System.NullReferenceException()
                        : throw new System.InvalidOperationException());
                    return 7;
                }
            }
            """);
        var original = compilation.SyntaxTrees.Single();
        compilation = compilation.ReplaceSyntaxTree(original, CSharpSyntaxTree.ParseText(
            original.GetText(), ((CSharpParseOptions)original.Options).WithPreprocessorSymbols(Array.Empty<string>()),
            original.FilePath));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)),
            Is.Empty);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var target = (IMethodSymbol)model.GetDeclaredSymbol(method)!;
        var ensuresSyntax = method.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Single(invocation => invocation is null ? throw new System.ArgumentNullException(nameof(invocation)) : (invocation.Expression.ToString() == "Contract.Ensures"));
        var ensures = (IInvocationOperation)model.GetOperation(ensuresSyntax)!;
        var graph = ControlFlowGraph.Create(method, model)!;
        Assert.That(graph.OriginalOperation.SemanticModel!.Compilation, Is.SameAs(compilation));
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, target);
        Assert.That(context.OwnsBody(graph.OriginalOperation), Is.True);
        var requires = new ContractBinder(compilation, factory).BindTotalRequires(context);
        Assert.That(requires.IsSuccess, Is.True, requires.Failure.ToString());
        Assert.That(requires.Clauses, Has.Length.EqualTo(1));
        Assert.That(requires.Clauses[0].Kind, Is.EqualTo(BoundContractKind.Requires));
        Assert.That(context.IsExcludedSpecificationCall(ensures), Is.True);

        var emission = new CSharpInvocationEmissionPolicy(compilation);
        Assert.That(ensures.TargetMethod.IsConditional, Is.True);
        Assert.That(emission.IsElided(ensures), Is.True);
        var source = graph.Blocks.Single(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.ConditionKind != ControlFlowConditionKind.None));
        Assert.That(source.BranchValue, Is.InstanceOf<IParameterReferenceOperation>());
        Assert.That(context.IsSpecificationOperation(source.BranchValue!), Is.True);
        var owner = emission.OwningInvocation(source.BranchValue!);
        Assert.That(owner, Is.Not.Null);
        Assert.That(owner!.Syntax.SyntaxTree, Is.SameAs(ensuresSyntax.SyntaxTree));
        Assert.That(owner.Syntax.Span, Is.EqualTo(ensuresSyntax.Span));
        var candidate = graph.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Operations.Select((operation, ordinal) =>
            operation is null ? throw new System.ArgumentNullException(nameof(operation)) : ((Block: block, Ordinal: ordinal,
                Expression: operation is IExpressionStatementOperation statement ? statement.Operation : operation)))))
            .Single(item => item.Expression is IInvocationOperation invocation &&
                ReferenceEquals(invocation.Syntax.SyntaxTree, ensuresSyntax.SyntaxTree) &&
                invocation.Syntax.Span == ensuresSyntax.Span);
        var ownerBlock = candidate.Block;
        Assert.That(ownerBlock.IsReachable, Is.False);
        var prefix = ownerBlock.Operations.Take(candidate.Ordinal + 1)
            .Select(operation => operation is null ? throw new System.ArgumentNullException(nameof(operation)) : (operation is IExpressionStatementOperation statement ? statement.Operation : operation))
            .ToImmutableArray();
        Assert.That(prefix, Is.Not.Empty);
        Assert.That(prefix.All(context.IsSpecificationOperation), Is.True);
        Assert.That(context.IsSpecificationOperation(candidate.Expression), Is.True);
        var sourceBoundary = source.EnclosingRegion;
        while (sourceBoundary.Kind == ControlFlowRegionKind.LocalLifetime)
        {
            sourceBoundary = sourceBoundary.EnclosingRegion!;
        }
        var ownerBoundary = ownerBlock.EnclosingRegion;
        while (ownerBoundary.Kind == ControlFlowRegionKind.LocalLifetime)
        {
            ownerBoundary = ownerBoundary.EnclosingRegion!;
        }
        Assert.That(sourceBoundary, Is.SameAs(ownerBoundary));
        var work = new TotalRegionWork(TestContext.CurrentContext.CancellationToken);
        var planner = new CompilerElisionContinuationPlanner(graph, context, emission, work, compilation);
        Assert.That(planner.TryRecoverCompilerElidedOwner(source, [], out var continuation), Is.False);
        Assert.That(continuation, Is.Null);
        Assert.That(planner.IsRestored(ownerBlock), Is.False);
        Assert.That(planner.TryGetPlan(source, out _), Is.False);
        Assert.That(work.ConstructionLimitExceeded, Is.False);
        var lowering = new RoslynProgramLowerer(factory).LowerCandidate(graph, context);
        Assert.That(lowering.IsExact, Is.False);
        Assert.That(lowering.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedControlFlow));
        Assert.That(lowering.ConstructionLimitExceeded, Is.False);

        var runtime = new AssemblyLoadContext("BoundPostconditionOwnerRecovery", isCollectible: true);
        try
        {
            image.Position = 0;
            var runtimeTarget = runtime.LoadFromStream(image).GetType("Subject")!.GetMethod("Target")!;
            Assert.That(runtimeTarget.Invoke(null, [false]), Is.EqualTo(7));
            Assert.That(runtimeTarget.Invoke(null, [true]), Is.EqualTo(7));
        }
        finally
        {
            runtime.Unload();
        }
    }
}

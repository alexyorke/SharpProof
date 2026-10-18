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
public sealed class RequiresOnlyTerminalSpecificationTests
{
    private static readonly ImmutableArray<bool> RuntimeInputs = [false, true];

    [TestCase(false)]
    [TestCase(true)]
    public void PublicRequiresBindingPreservesSkippedPostconditionBoundary(bool terminalArm)
    {
        var predicate = terminalArm ? "flag ? true : throw new System.InvalidOperationException()" : "flag ? true : false";
        var compilation = TestCompilation.Create("RequiresOnlyTerminalSpecification", $$"""
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
        var thrown = graph.Blocks.Where(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.FallThroughSuccessor?.Semantics == ControlFlowBranchSemantics.Throw)).ToArray();
        Assert.That(thrown, Has.Length.EqualTo(terminalArm ? 1 : 0));
        if (terminalArm)
        {
            Assert.That(thrown[0].IsReachable, Is.True);
            Assert.That(thrown[0].BranchValue, Is.Not.Null);
            Assert.That(ensuresSyntax.Span.Contains(thrown[0].BranchValue!.Syntax.Span), Is.True);
            Assert.That(emission.IsElided(thrown[0].BranchValue!), Is.True);
        }

        var factory = new IrFactory(IrExecutionSemantics.Total);
        var fullContext = new TotalLoweringContext(factory, target);
        var binder = new ContractBinder(compilation, factory);
        var full = binder.BindTotal(fullContext);
        Assert.That(full.IsSuccess, Is.EqualTo(!terminalArm));
        Assert.That(full.Failure, Is.EqualTo(terminalArm ? ContractBindingFailure.UnsupportedExpression : ContractBindingFailure.None));
        Assert.That(full.Clauses, Has.Length.EqualTo(terminalArm ? 0 : 2));
        var context = new TotalLoweringContext(factory, target);
        Assert.That(context.HasScalarSignature, Is.True);
        Assert.That(context.OwnsBody(graph.OriginalOperation), Is.True);
        var requires = binder.BindTotalRequires(context);
        Assert.That(requires.IsSuccess, Is.True, requires.Failure.ToString());
        Assert.That(requires.Clauses, Has.Length.EqualTo(1));
        Assert.That(requires.Clauses[0].Kind, Is.EqualTo(BoundContractKind.Requires));
        Assert.That(context.IsExcludedSpecificationCall(ensures), Is.True);
        Assert.That(context.IsSpecificationOperation(conditionals[0].BranchValue!), Is.True);
        var lowering = new RoslynProgramLowerer(factory).LowerCandidate(graph, context);
        Assert.That(lowering.ConstructionLimitExceeded, Is.False);
        Assert.That(lowering.IsExact, Is.EqualTo(!terminalArm));
        Assert.That(lowering.Classification.Abstention, Is.EqualTo(terminalArm
            ? FrontendAbstention.UnsupportedControlFlow : FrontendAbstention.None));
        if (!terminalArm)
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
        var load = new AssemblyLoadContext("RequiresOnlyTerminalSpecification", isCollectible: true);
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
}

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.Attributes;
using SharpProof.Contracts;
using SharpProof.Frontend;
using SharpProof.Ir;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class RecursiveRequiresDefaultWorkContractTests
{
    [TestCase(1, false)]
    [TestCase(400, true)]
    public void ActiveRecursiveRequiresRetainTheDefaultWorkBoundary(int clauses, bool limited)
    {
        var requirements = string.Join(" ", Enumerable.Repeat("Contract.Requires(true);", clauses));
        var source = "using SharpProof.Attributes; public static class Subject { public static int Target() { " +
            requirements + " return Target(); } }";
        var options = new CSharpParseOptions(LanguageVersion.CSharp12);
        Assert.That(options.PreprocessorSymbolNames, Does.Not.Contain(Contract.ConditionalSymbol));
        var tree = CSharpSyntaxTree.ParseText(source, options, "recursive-requires-work.cs");
        var compilation = CSharpCompilation.Create("RecursiveRequiresWork", [tree],
            TestMetadataReferences.WithSharpProof,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release));
        Assert.That(compilation.GetDiagnostics().Where(
            diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        Assert.That(image.Length, Is.GreaterThan(0));

        var declaration = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        Assert.That(declaration.DescendantNodesAndSelf().Count(), Is.EqualTo(8 * clauses + 8));
        var model = compilation.GetSemanticModel(tree);
        var target = (IMethodSymbol)model.GetDeclaredSymbol(declaration)!;
        var type = compilation.GetTypeByMetadataName("Subject")!;
        Assert.That(type.IsStatic, Is.True);
        Assert.That(type.GetMembers(), Has.Length.EqualTo(1));
        Assert.That(SymbolEqualityComparer.Default.Equals(type.GetMembers().Single(), target), Is.True);
        Assert.That(type.StaticConstructors, Is.Empty);
        Assert.That(target.IsStatic, Is.True);
        Assert.That(target.Parameters, Is.Empty);
        Assert.That(target.DeclaringSyntaxReferences, Has.Length.EqualTo(1));
        Assert.That(target.ReturnType.SpecialType, Is.EqualTo(SpecialType.System_Int32));
        var graph = ControlFlowGraph.Create(declaration, model)!;
        Assert.That(graph, Is.Not.Null);
        Assert.That(graph.Blocks, Has.Length.EqualTo(3));
        Assert.That(graph.Blocks[0].Kind, Is.EqualTo(BasicBlockKind.Entry));
        Assert.That(graph.Blocks[1].Kind, Is.EqualTo(BasicBlockKind.Block));
        Assert.That(graph.Blocks[2].Kind, Is.EqualTo(BasicBlockKind.Exit));
        Assert.That(graph.Root.Kind, Is.EqualTo(ControlFlowRegionKind.Root));
        Assert.That(graph.Root.NestedRegions, Is.Empty);
        Assert.That(graph.Root.Locals, Is.Empty);
        Assert.That(graph.Blocks.All(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (ReferenceEquals(block.EnclosingRegion, graph.Root))), Is.True);
        Assert.That(graph.Blocks.All(block => block.IsReachable &&
            block.ConditionKind == ControlFlowConditionKind.None && block.ConditionalSuccessor == null), Is.True);
        var body = graph.Blocks[1];
        Assert.That(body.Operations, Has.Length.EqualTo(clauses));
        Assert.That(body.FallThroughSuccessor!.Semantics, Is.EqualTo(ControlFlowBranchSemantics.Return));
        Assert.That(body.BranchValue, Is.InstanceOf<IInvocationOperation>());
        var recursive = (IInvocationOperation)body.BranchValue!;
        Assert.That(SymbolEqualityComparer.Default.Equals(recursive.TargetMethod, target), Is.True);
        Assert.That(recursive.Instance, Is.Null);
        Assert.That(recursive.Arguments, Is.Empty);

        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, target);
        var binding = new ContractBinder(compilation, factory).BindTotal(context);
        Assert.That(binding.IsSuccess, Is.True, binding.Failure.ToString());
        Assert.That(binding.Clauses, Has.Length.EqualTo(clauses));
        Assert.That(binding.Clauses.All(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Kind == BoundContractKind.Requires &&
            clause.Value is IrBooleanTerm { Value: true } &&
            clause.SafeCondition is IrBooleanTerm { Value: true })), Is.True);
        Assert.That(graph.OriginalOperation.SemanticModel!.Compilation, Is.SameAs(compilation));
        Assert.That(context.OwnsBody(graph.OriginalOperation), Is.True);
        Assert.That(context.HasScalarSignature, Is.True);
        Assert.That(context.Parameters, Is.Empty);
        var emission = new CSharpInvocationEmissionPolicy(compilation);
        foreach (var operation in body.Operations)
        {
            Assert.That(operation, Is.InstanceOf<IExpressionStatementOperation>());
            var expression = ((IExpressionStatementOperation)operation).Operation;
            Assert.That(expression, Is.InstanceOf<IInvocationOperation>());
            var requires = (IInvocationOperation)expression;
            Assert.That(requires.TargetMethod.Name, Is.EqualTo("Requires"));
            Assert.That(requires.TargetMethod.ContainingType.ToDisplayString(),
                Is.EqualTo("SharpProof.Attributes.Contract"));
            Assert.That(requires.Instance, Is.Null);
            Assert.That(requires.Arguments, Has.Length.EqualTo(1));
            Assert.That(requires.Arguments[0].Value.ConstantValue.HasValue, Is.True);
            Assert.That(requires.Arguments[0].Value.ConstantValue.Value, Is.True);
            Assert.That(requires.TargetMethod.GetAttributes().Any(attribute =>
                attribute is null ? throw new System.ArgumentNullException(nameof(attribute)) : (attribute.AttributeClass?.ToDisplayString() == "System.Diagnostics.ConditionalAttribute" &&
                attribute.ConstructorArguments.Length == 1 &&
                Equals(attribute.ConstructorArguments[0].Value, Contract.ConditionalSymbol))), Is.True);
            Assert.That(context.IsExcludedSpecificationCall(requires), Is.True);
            Assert.That(emission.IsElided(requires), Is.True);
        }
        Assert.That(emission.IsElided(recursive), Is.False);
        Assert.That(TotalBodyLowering.MaximumWork, Is.EqualTo(4096));
        Assert.That(RoslynTotalProgramLowerer.MaximumRegionSteps, Is.EqualTo(4096));

        // The production callback creates the active recursive contract-only
        // frame. No test callback or work-limit override participates.
        var lowered = TotalBodyLowering.Lower(compilation, graph, context,
            TestContext.CurrentContext.CancellationToken, opaqueCalls: true);
        Assert.That(context.Compilation, Is.SameAs(compilation));
        Assert.That(lowered.ConstructionLimitExceeded, Is.EqualTo(limited));
        Assert.That(lowered.IsExact, Is.EqualTo(!limited));
        if (limited)
        {
            Assert.That(lowered.Classification.Abstention,
                Is.EqualTo(FrontendAbstention.UnsupportedControlFlow));
        }
        else
        {
            Assert.That(lowered.Classification.Abstention, Is.EqualTo(FrontendAbstention.None));
            Assert.That(lowered.CallPreconditions, Has.Count.EqualTo(1));
            var precondition = lowered.CallPreconditions.Values.Single().Value;
            Assert.That(precondition, Is.InstanceOf<IrBooleanTerm>());
            Assert.That(((IrBooleanTerm)precondition).Value, Is.True);
            Assert.That(lowered.Program.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions))
                .OfType<IrCallInstruction>().Count(), Is.EqualTo(1));
            Assert.That(lowered.Program.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions))
                .OfType<IrHavocInstruction>().Any(havoc => havoc is null ? throw new System.ArgumentNullException(nameof(havoc)) : (havoc.Origin == IrHavocOrigin.Approximation)), Is.True);
        }
        // The source compiles, but the recursive CLR method and opaque IR are
        // deliberately not executed: neither has a completion guarantee.
    }
}

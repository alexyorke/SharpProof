using System.Numerics;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class BulkFrontendCallsConditionalFinallyMutationControls
{
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void ConditionalArgumentMutationsAndFinallyKeepTheirCompiledEffects(bool partial, bool enabled)
    {
        const string targetSource = "public static int Target(bool flag) { int result = 17; " +
            "try { Log(flag ? ++result : --result); result += 2; } finally { result *= 3; } return result; }";
        var declaration = partial
            ? "static partial void Log(int argument);" + (enabled ? "static partial void Log(int argument) { }" : "")
            : "[System.Diagnostics.Conditional(\"ABSENT\")] static void Log(int argument) { }";
        var source = "public static partial class Subject { " + targetSource + declaration + " }";
        var tree = CSharpSyntaxTree.ParseText(source,
            new CSharpParseOptions(LanguageVersion.CSharp12, preprocessorSymbols: enabled ? ["ABSENT"] : []), "conditional-finally-mutation.cs");
        var compilation = CSharpCompilation.Create("ConditionalFinallyMutation_" + partial + "_" + enabled,
            [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        var model = compilation.GetSemanticModel(tree);
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method is null ? throw new System.ArgumentNullException(nameof(method)) : (method.Identifier.ValueText == "Target"));
        var target = (IMethodSymbol)model.GetDeclaredSymbol(syntax)!;
        var graph = ControlFlowGraph.Create(syntax, model)!;
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, target);
        var runtime = new AssemblyLoadContext("ConditionalFinallyMutation_" + partial + "_" + enabled, isCollectible: true);
        try
        {
            image.Position = 0;
            var method = runtime.LoadFromStream(image).GetType("Subject")!.GetMethod("Target")!;
            // Establish both compiled outcomes before asserting CFG geometry.
            foreach (var flag in new[] { false, true })
            { Assert.That(method.Invoke(null, [flag]), Is.EqualTo(enabled ? flag ? 60 : 54 : 57)); }

            Assert.That(context.OwnsBody(graph.OriginalOperation), Is.True);
            Assert.That(context.HasScalarSignature, Is.True);
            Assert.That(graph.OriginalOperation.SemanticModel!.Compilation, Is.SameAs(compilation));
            var regions = new List<ControlFlowRegion>();
            var pending = new Stack<ControlFlowRegion>();
            pending.Push(graph.Root);
            while (pending.Count != 0)
            {
                var region = pending.Pop();
                regions.Add(region);
                foreach (var child in region.NestedRegions)
                { pending.Push(child); }
            }
            Assert.That(regions.Any(region => region is null ? throw new System.ArgumentNullException(nameof(region)) : (region.Kind == ControlFlowRegionKind.TryAndFinally)), Is.True);
            Assert.That(graph.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (new[] { block.FallThroughSuccessor, block.ConditionalSuccessor }))
                .Any(edge => edge != null && !edge.FinallyRegions.IsEmpty), Is.True);
            Assert.That(graph.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (new[] { block.FallThroughSuccessor, block.ConditionalSuccessor }))
                .Any(edge => edge?.Semantics is ControlFlowBranchSemantics.Throw or ControlFlowBranchSemantics.Rethrow), Is.False);
            var ownerSyntax = syntax.DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
            var owner = (IInvocationOperation)model.GetOperation(ownerSyntax)!;
            var emission = new CSharpInvocationEmissionPolicy(compilation);
            Assert.That(owner.TargetMethod.IsConditional, Is.EqualTo(!partial));
            if (partial)
            { Assert.That(owner.TargetMethod.PartialImplementationPart != null, Is.EqualTo(enabled)); }
            Assert.That(emission.IsElided(owner, requireOwningInvocation: true), Is.EqualTo(!enabled));
            Assert.That(owner.Syntax.SyntaxTree, Is.SameAs(tree));
            Assert.That(owner.Syntax.Span, Is.EqualTo(ownerSyntax.Span));
            var outer = (ConditionalExpressionSyntax)ownerSyntax.ArgumentList.Arguments.Single().Expression;
            var conditionalBlock = graph.Blocks.Single(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.ConditionKind != ControlFlowConditionKind.None &&
                block.BranchValue is { } branch && ReferenceEquals(branch.Syntax.SyntaxTree, tree) && branch.Syntax.Span == outer.Condition.Span));
            Assert.That(conditionalBlock.BranchValue, Is.Not.Null);
            var conditionalValue = conditionalBlock.BranchValue!;
            Assert.That(emission.IsElided(conditionalValue), Is.EqualTo(!enabled));
            var conditionalOwner = emission.OwningInvocation(conditionalValue);
            Assert.That(conditionalOwner, Is.Not.Null);
            Assert.That(conditionalOwner!.Syntax.SyntaxTree, Is.SameAs(tree));
            Assert.That(conditionalOwner.Syntax.Span, Is.EqualTo(ownerSyntax.Span));
            var mutations = syntax.DescendantNodes().OfType<PrefixUnaryExpressionSyntax>().ToArray();
            Assert.That(mutations, Has.Length.EqualTo(2));
            foreach (var mutation in mutations)
            {
                var operation = model.GetOperation(mutation);
                Assert.That(operation, Is.InstanceOf<IIncrementOrDecrementOperation>());
                var actual = emission.OwningInvocation(operation!);
                Assert.That(actual, Is.Not.Null);
                Assert.That(actual!.Syntax.SyntaxTree, Is.SameAs(tree));
                Assert.That(actual.Syntax.Span, Is.EqualTo(ownerSyntax.Span));
                Assert.That(emission.IsElided(operation!), Is.EqualTo(!enabled));
            }
            var assignments = syntax.DescendantNodes().OfType<AssignmentExpressionSyntax>().ToArray();
            Assert.That(assignments, Has.Length.EqualTo(2));
            foreach (var assignment in assignments)
            {
                var operation = model.GetOperation(assignment);
                Assert.That(operation, Is.InstanceOf<ICompoundAssignmentOperation>());
                Assert.That(emission.IsElided(operation!), Is.False);
            }

            var lowering = new RoslynProgramLowerer(factory).LowerCandidate(graph, context,
                static _ => true, null, CancellationToken.None);
            if (partial && enabled)
            {
                Assert.That(lowering.IsExact, Is.False);
                Assert.That(lowering.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedOperationKind));
                Assert.That(lowering.ConstructionLimitExceeded, Is.False);
                return;
            }
            Assert.That(lowering.IsExact, Is.True, lowering.Classification.Abstention.ToString());
            Assert.That(lowering.ConstructionLimitExceeded, Is.False);
            foreach (var flag in new[] { false, true })
            {
                var replay = new IrProgramInterpreter(factory).Execute(lowering.Program,
                    new Dictionary<IrVarId, IrValue> { [context.Parameters.Single().Entry] = factory.CreateBooleanValue(flag) });
                Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
                Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new BigInteger(enabled ? flag ? 60 : 54 : 57)));
                Assert.That(replay.ConsumedApproximation, Is.False);
                Assert.That(replay.ApproximationVariables, Is.Empty);
            }
        }
        finally
        { runtime.Unload(); }
    }
}

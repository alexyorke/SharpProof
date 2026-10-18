using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class NestedLocalConditionalErrorCoverageTests
{
    private static readonly ImmutableArray<string> ExpectedErrorIds = ["CS0159"];
    private const string OuterLabelSource = """
        public static class Subject
        {
            public static int Target(bool flag)
            {
                int Local(bool input)
                {
                    try
                    {
                        if (input) { } else { goto marker; }
                        return 7;
                    }
                    finally { input = !input; }
                }
            marker:
                return Local(flag);
            }
        }
        """;

    private const string LocalLabelSource = """
        public static class Subject
        {
            public static int Target(bool flag)
            {
                int Local(bool input)
                {
                    try
                    {
                        if (input) { } else { goto marker; }
                    marker:
                        return 7;
                    }
                    finally { input = !input; }
                }
                return Local(flag);
            }
        }
        """;

    [Test]
    public void OuterLabelInElseProducesOwnedConditionalErrorAndRefusesControlFlow()
    {
        var compilation = CreateCompilation(OuterLabelSource);
        var errors = compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)).ToArray();
        Assert.That(errors.Select(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Id)), Is.EqualTo(ExpectedErrorIds));
        using var image = new MemoryStream();
        Assert.That(compilation.Emit(image).Success, Is.False);
        var (graph, context, factory) = CreateLocalCandidate(compilation);
        Assert.That(graph.Blocks.Any(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.IsReachable &&
            block.ConditionalSuccessor?.Semantics == ControlFlowBranchSemantics.Error)), Is.True);
        var lowering = new RoslynProgramLowerer(factory).LowerCandidate(graph, context);
        Assert.That(lowering.IsExact, Is.False);
        Assert.That(lowering.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedControlFlow));
        Assert.That(lowering.ConstructionLimitExceeded, Is.False);
    }

    [Test]
    public void LocalLabelInElseEmitsAndReplaysSeven()
    {
        var compilation = CreateCompilation(LocalLabelSource);
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        var runtime = new AssemblyLoadContext("LocalConditionalErrorControl", isCollectible: true);
        try
        {
            image.Position = 0;
            var target = runtime.LoadFromStream(image).GetType("Subject")!.GetMethod("Target")!;
            var (graph, context, factory) = CreateLocalCandidate(compilation);
            Assert.That(graph.Blocks.Any(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.ConditionalSuccessor?.Semantics == ControlFlowBranchSemantics.Error ||
                block.FallThroughSuccessor?.Semantics == ControlFlowBranchSemantics.Error)), Is.False);
            var lowering = new RoslynProgramLowerer(factory).LowerCandidate(graph, context);
            Assert.That(lowering.IsExact, Is.True, lowering.Classification.Abstention.ToString());
            Assert.That(lowering.ConstructionLimitExceeded, Is.False);
            foreach (var input in new[] { false, true })
            {
                Assert.That(target.Invoke(null, [input]), Is.EqualTo(7));
                var parameter = context.Parameters.Single();
                var execution = new IrProgramInterpreter(factory).Execute(lowering.Program,
                    new Dictionary<IrVarId, IrValue> { [parameter.Entry] = factory.CreateBooleanValue(input) });
                Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
                Assert.That(execution.ConsumedApproximation, Is.False);
                Assert.That(execution.ReturnValue!.IntegerNumericValue, Is.EqualTo(new BigInteger(7)));
            }
        }
        finally { runtime.Unload(); }
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        var tree = CSharpSyntaxTree.ParseText(source, path: "nested-local-conditional-error.cs");
        return CSharpCompilation.Create("LocalConditionalError", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));
    }

    private static (ControlFlowGraph Graph, TotalLoweringContext Context, IrFactory Factory)
        CreateLocalCandidate(CSharpCompilation compilation)
    {
        System.ArgumentNullException.ThrowIfNull(compilation);
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        var targetSyntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var target = model.GetDeclaredSymbol(targetSyntax)!;
        var outer = ControlFlowGraph.Create(targetSyntax, model)!;
        var local = outer.LocalFunctions.Single();
        Assert.That(local.MethodKind, Is.EqualTo(MethodKind.LocalFunction));
        Assert.That(local.Name, Is.EqualTo("Local"));
        Assert.That(SymbolEqualityComparer.Default.Equals(local.ContainingSymbol.OriginalDefinition, target.OriginalDefinition), Is.True);
        var graph = outer.GetLocalFunctionControlFlowGraph(local);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, local);
        Assert.That(graph.OriginalOperation.SemanticModel!.Compilation, Is.SameAs(compilation));
        Assert.That(context.OwnsBody(graph.OriginalOperation), Is.True);
        Assert.That(context.HasScalarSignature, Is.True);
        Assert.That(ContainsFinally(graph.Root), Is.True);
        return (graph, context, factory);
    }

    private static bool ContainsFinally(ControlFlowRegion region)
    {
        System.ArgumentNullException.ThrowIfNull(region);
        return region.Kind == ControlFlowRegionKind.Finally || region.NestedRegions.Any(ContainsFinally);
    }
}

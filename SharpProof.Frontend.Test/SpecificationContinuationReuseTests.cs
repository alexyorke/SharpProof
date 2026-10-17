using System.Numerics;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using NUnit.Framework;
using SharpProof.Contracts;
using SharpProof.Frontend;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class SpecificationContinuationReuseTests
{
    [TestCase(false, 2)]
    [TestCase(false, 9)]
    [TestCase(true, 2)]
    [TestCase(true, 9)]
    public void BoundPrologueRetainsBodySemanticsAcrossRepeatedOwnedGraphs(bool local, int input)
    {
        var declaration = local ? "int saved = value;" : "";
        var returned = local ? "saved" : "value";
        var compilation = TestCompilation.Create("SpecificationContinuationReuse", $$"""
            using SharpProof.Attributes;
            public static class Subject {
                public static int Target(int value) {
                    Contract.Requires(value > 0 && value < 10);
                    value = unchecked(value + 1);
                    {{declaration}}
                    try { return unchecked({{returned}} - 1); }
                    finally { value = unchecked(value + 1); }
                }
            }
            """);
        var tree = compilation.SyntaxTrees.Single();
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var model = compilation.GetSemanticModel(tree);
        var method = (IMethodSymbol)model.GetDeclaredSymbol(syntax)!;
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, method);
        var binding = new ContractBinder(compilation, factory).BindTotal(context);
        Assert.That(binding.IsSuccess, Is.True, binding.Failure.ToString());
        Assert.That(binding.Clauses, Has.Length.EqualTo(1));
        var first = ControlFlowGraph.Create(syntax, model)!;
        var second = ControlFlowGraph.Create(syntax, model)!;
        Assert.That(second, Is.Not.SameAs(first));
        foreach (var graph in new[] { first, second })
        {
            Assert.That(graph.OriginalOperation.SemanticModel!.Compilation, Is.SameAs(compilation));
            Assert.That(SymbolEqualityComparer.Default.Equals(
                model.GetDeclaredSymbol(graph.OriginalOperation.Syntax), method), Is.True);
            Assert.That(graph.Blocks.Any(block => block.ConditionKind != ControlFlowConditionKind.None), Is.True);
            var regions = Regions(graph.Root).ToArray();
            Assert.That(regions.Any(region => region.Kind == ControlFlowRegionKind.Finally), Is.True);
            Assert.That(regions.SelectMany(region => region.Locals).Any(symbol => symbol.Name == "saved"), Is.EqualTo(local));
        }

        var loadContext = new AssemblyLoadContext("SpecificationContinuationReuse", isCollectible: true);
        try
        {
            using var image = new MemoryStream();
            var emitted = compilation.Emit(image);
            Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
            image.Position = 0;
            var runtime = loadContext.LoadFromStream(image).GetType("Subject")!.GetMethod("Target")!;
            Assert.That(runtime.Invoke(null, [input]), Is.EqualTo(input));

            // Reuse one owned context, then switch to another authentic graph and back.
            foreach (var graph in new[] { first, first, second, first })
            {
                var lowering = new RoslynProgramLowerer(factory).LowerCandidate(graph, context);
                Assert.That(lowering.IsExact, Is.True, lowering.Classification.Abstention.ToString());
                Assert.That(lowering.ConstructionLimitExceeded, Is.False);
                var parameter = context.Parameters.Single();
                var execution = new IrProgramInterpreter(factory).Execute(lowering.Program,
                    new Dictionary<IrVarId, IrValue>
                    {
                        [parameter.Entry] = factory.CreateIntegerValue(factory.GetVariableInfo(parameter.Entry).Type, input)
                    });
                Assert.That(execution.ConsumedApproximation, Is.False);
                Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
                Assert.That(execution.ReturnValue!.IntegerNumericValue, Is.EqualTo(new BigInteger(input)));
                Assert.That(execution.Values[parameter.Current].IntegerNumericValue, Is.EqualTo(new BigInteger(input + 2)));
            }
        }
        finally
        { loadContext.Unload(); }
    }

    private static IEnumerable<ControlFlowRegion> Regions(ControlFlowRegion region)
    {
        yield return region;
        foreach (var nested in region.NestedRegions)
        {
            foreach (var descendant in Regions(nested))
            { yield return descendant; }
        }
    }
}

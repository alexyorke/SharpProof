using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class InterpolatedStringHandlerNeighborRegressionTests
{
    private const string Source = """
        using System.Runtime.CompilerServices;
        using SharpProof.Attributes;
        [InterpolatedStringHandler]
        public ref struct Handler {
            public Handler(int literalLength, int formattedCount, out bool shouldAppend) { shouldAppend = false; }
            public void AppendFormatted(int value) { }
        }
        public static class Subject {
            public static int State;
            private static int Touch() { return ++State; }
            private static void Consume(Handler value) { }
            [EnforcePure, ZeroAllocations]
            public static void Target() { Consume($"{Touch()}"); }
            public static int Run() { State = 0; Target(); return State; }
        }
        """;

    [TestCase("finally", 10)]
    [TestCase("filter", 0)]
    public async Task ConditionalHandlerInRegionsReportsAnalysisGapWithoutCrashing(string route, int expectedState)
    {
        var source = route == "finally"
            ? Source.Replace("Consume($\"{Touch()}\");", "try { Consume($\"{Touch()}\"); } finally { State += 10; }", StringComparison.Ordinal)
            : Source.Replace("private static void Consume(Handler value) { }", "private static bool Consume(Handler value) => true;", StringComparison.Ordinal)
                .Replace("Consume($\"{Touch()}\");", "try { throw new System.Exception(); } catch (System.Exception) when (Consume($\"{Touch()}\")) { }", StringComparison.Ordinal);
        var compilation = AnalyzerTestHost.CreateCompilation(source, ["SP0002", "SP0045", "SP0047"], filePath: "HandlerSubject.cs");
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        RuntimeAssemblyTestHost.WithRuntimeAssembly("HandlerEffectsBoundaryRuntime", AnalyzerTestHost.EmitImage(compilation), assembly =>
        {
            var run = assembly.GetType("Subject")!.GetMethod("Run")!.CreateDelegate<Func<int>>();
            Assert.That(run(), Is.EqualTo(expectedState), "Only an enabled handler may evaluate the state-changing hole.");
        });
        var tree = compilation.SyntaxTrees.Single();
        var method = (await tree.GetRootAsync()).DescendantNodes().OfType<MethodDeclarationSyntax>().Single(node => node.Identifier.ValueText == "Target");
        var graph = ControlFlowGraph.Create(method, compilation.GetSemanticModel(tree))!;
        foreach (var block in graph.Blocks.Where(block => block.ConditionKind != ControlFlowConditionKind.None))
        {
            TestContext.WriteLine($"CFG condition: kind={block.BranchValue!.Kind}; type={block.BranchValue.Type?.ToDisplayString() ?? "<null>"}; syntax={block.BranchValue.Syntax}");
        }
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(compilation, mode: null, analyzer: new SharpProofAnalyzer(factory),
            profile: "advisory", features: "effects");
        TestContext.WriteLine($"runtime_state={expectedState}; diagnostics={string.Join(",", diagnostics.Select(diagnostic => diagnostic.Id))}; " +
            $"target_outcome={(factory.Outcomes.TryGetValue("Target", out var outcome) ? outcome.ToString() : "<unrecorded>")}");
        AnalyzerTestHost.AssertIds(diagnostics, "SP0047");
        Assert.That(factory.Outcomes["Target"], Is.EqualTo(AnalyzerSemanticOutcome.Abstained));
    }
}

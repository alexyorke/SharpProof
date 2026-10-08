using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class InterpolatedStringHandlerCrashRegressionTests
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

    [TestCase(false, 0)]
    [TestCase(true, 1)]
    public async Task ConditionalHandlerReportsAnalysisGapWithoutCrashing(bool shouldAppend, int expectedState)
    {
        var source = shouldAppend ? Source.Replace("shouldAppend = false", "shouldAppend = true", StringComparison.Ordinal) : Source;
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

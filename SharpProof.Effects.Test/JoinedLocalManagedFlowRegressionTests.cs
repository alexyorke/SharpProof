using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class JoinedLocalManagedFlowRegressionTests
{
    [Test]
    public void GuardOnJoinedLocalDoesNotEscapeAsAnAnalyzerException()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                public static long Guarded(long input) {
                    long value = 1;
                    if (input > 0) { value = 0; }
                    return value != 0 ? value / value : 0;
                }
            }
            """);
        var method = EffectTestHost.SampleMethod(compilation, "Guarded");
        var root = EffectTestHost.RootOperation(compilation, method);
        var graph = ControlFlowGraph.Create((IMethodBodyOperation)root);
        ManagedFlowAnalysis? analysis = null;

        Assert.DoesNotThrow((Action)(() => analysis = ManagedAbstractFlow
            .ForCompilation(compilation)
            .Analyze(method, graph, null, default)));

        Assert.That(analysis, Is.Not.Null);
        Assert.That(analysis!.Status, Is.EqualTo(ManagedFlowStatus.Complete));
    }

    [Test]
    public void EffectSummaryRetainsCompleteResultForJoinedLocalGuard()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                public static long Guarded(long input) {
                    long value = 1;
                    if (input > 0) { value = 0; }
                    return value != 0 ? 1 : 100 / value;
                }
            }
            """);

        var result = EffectTestHost.AnalyzeSample(compilation, "Guarded");

        Assert.That(result.Summary.Completeness, Is.EqualTo(EffectCompleteness.Complete));
    }
}

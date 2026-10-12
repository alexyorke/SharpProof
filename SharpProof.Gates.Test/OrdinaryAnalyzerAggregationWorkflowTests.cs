using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class OrdinaryAnalyzerAggregationWorkflowTests
{
    [Test]
    public async Task TwoActualAnalysesCombineTheirGenuineCallbackOutcomeForOneSourceMethod()
    {
        var compilation = AnalyzerGateHost.CreateCompilation("""
            using SharpProof.Attributes;
            public static class C {
                [DoesNotThrow]
                public static int Target() => 1;
            }
            """, "OrdinaryRepeatedAnalysis", includeExternalEffectsFixture: false);
        var factory = new ActualOutcomeFactory();
        var first = await AnalyzerGateHost.AnalyzeAsync(compilation, new SharpProofAnalyzer(factory),
            "effects", concurrentAnalysis: false);
        var second = await AnalyzerGateHost.AnalyzeAsync(compilation, new SharpProofAnalyzer(factory),
            "effects", concurrentAnalysis: false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.Empty);
            Assert.That(second, Is.Empty);
            Assert.That(factory.CallbackCount, Is.EqualTo(2));
            Assert.That(factory.UniqueMethodCount, Is.EqualTo(1));
            Assert.That(factory.Outcome, Is.EqualTo(AnalyzerSemanticOutcome.Proven));
        }
    }

    private sealed class ActualOutcomeFactory() : AnalyzerSessionFactoryBase<string>(StringComparer.Ordinal)
    {
        private int _callbackCount;

        internal int CallbackCount => Volatile.Read(ref _callbackCount);
        internal int UniqueMethodCount => Outcomes.Count;
        internal AnalyzerSemanticOutcome Outcome => Outcomes.Single().Value;

        protected override void Record(IMethodSymbol method, AnalyzerSemanticOutcome outcome)
        {
            Interlocked.Increment(ref _callbackCount);
            RecordOutcome(method.GetDocumentationCommentId()!, outcome);
        }
    }
}

using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class OrdinaryRepeatedAnalyzerWorkflowTests
{
    [Test]
    public async Task RepeatedRealAnalysisCombinesTheSameCompilerMethodOutcome()
    {
        var compilation = AnalyzerGateHost.CreateCompilation("""
            using SharpProof.Attributes;
            public static class RepeatedAnalysis {
                [EnforcePure] public static int Identity(int value) => value;
            }
            """, "OrdinaryRepeatedAnalysis", includeExternalEffectsFixture: false);
        Assert.That(compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var factory = new RecordingFactory();
        var first = await AnalyzerGateHost.AnalyzeAsync(compilation, new SharpProofAnalyzer(factory),
            "effects", concurrentAnalysis: false);
        var initial = factory.Snapshot();
        var second = await AnalyzerGateHost.AnalyzeAsync(compilation, new SharpProofAnalyzer(factory),
            "effects", concurrentAnalysis: false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.Empty);
            Assert.That(second, Is.Empty);
            Assert.That(initial, Has.Length.EqualTo(1));
            Assert.That(factory.Snapshot(), Is.EqualTo(initial));
            Assert.That(factory.RecordCount, Is.EqualTo(2));
        }
    }

    private sealed class RecordingFactory()
        : AnalyzerSessionFactoryBase<MethodOutcomeKey>(MethodOutcomeKeyComparer.Instance)
    {
        internal int RecordCount { get; private set; }

        internal AnalyzerSemanticOutcome[] Snapshot()
        {
            return [.. Outcomes.Values];
        }

        protected override void Record(IMethodSymbol method, AnalyzerSemanticOutcome outcome)
        {
            RecordCount++;
            var location = method.Locations.Single(static location => location.IsInSource);
            RecordOutcome(new(method, location.SourceTree, location.SourceTree!.FilePath, 0,
                method.MetadataName, method.DeclaredAccessibility, location.SourceSpan.Start), outcome);
        }
    }
}

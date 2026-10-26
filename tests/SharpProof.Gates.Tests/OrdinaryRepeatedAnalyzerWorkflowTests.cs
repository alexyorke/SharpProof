using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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
        Assert.That(compilation.GetDiagnostics().Where(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        var factory = new RecordingFactory(compilation);
        var first = await AnalyzerGateHost.AnalyzeAsync(compilation, new SharpProofAnalyzer(factory),
            "effects", concurrentAnalysis: false, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var initial = factory.Snapshot();
        var second = await AnalyzerGateHost.AnalyzeAsync(compilation, new SharpProofAnalyzer(factory),
            "effects", concurrentAnalysis: false, cancellationToken: TestContext.CurrentContext.CancellationToken);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.Empty);
            Assert.That(second, Is.Empty);
            Assert.That(initial, Has.Length.EqualTo(1));
            Assert.That(factory.Snapshot(), Is.EqualTo(initial));
            Assert.That(factory.RecordCount, Is.EqualTo(2));
        }
    }

    private sealed class RecordingFactory(CSharpCompilation compilation)
        : AnalyzerSessionFactoryBase<MethodOutcomeKey>(MethodOutcomeKeyComparer.Instance)
    {
        private readonly CSharpCompilation _guarded_compilation = compilation ?? throw new System.ArgumentNullException(nameof(compilation));
        internal int RecordCount { get; private set; }

        internal ImmutableArray<AnalyzerSemanticOutcome> Snapshot()
        {
            return [.. Outcomes.Values];
        }

        protected override void Record(IMethodSymbol method, AnalyzerSemanticOutcome outcome)
        {
            System.ArgumentNullException.ThrowIfNull(method);
            RecordCount++;
            // Separate Roslyn analyzer runs own separate symbol instances. Resolve
            // their unchanged source declarations through the original compiler model.
            var declaration = method.DeclaringSyntaxReferences.Single().GetSyntax();
            var canonical = (IMethodSymbol)_guarded_compilation.GetSemanticModel(declaration.SyntaxTree).GetDeclaredSymbol(declaration)!;
            var location = method.Locations.Single(static location => location is null ? throw new System.ArgumentNullException(nameof(location)) : (location.IsInSource));
            var canonicalLocation = canonical.Locations.Single(static location => location is null ? throw new System.ArgumentNullException(nameof(location)) : (location.IsInSource));
            Assert.That(canonicalLocation.SourceTree, Is.SameAs(location.SourceTree));
            Assert.That(canonicalLocation.SourceSpan, Is.EqualTo(location.SourceSpan));
            Assert.That(canonical.GetDocumentationCommentId(), Is.EqualTo(method.GetDocumentationCommentId()));
            RecordOutcome(new(canonical, canonicalLocation.SourceTree, canonicalLocation.SourceTree!.FilePath, 0,
                canonical.MetadataName, canonical.DeclaredAccessibility, canonicalLocation.SourceSpan.Start), outcome);
        }
    }
}

using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Gates.Corpus;
using SharpProof.Worker.Protocol;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class OrdinaryExceptionBatchWorkflowTests
{
    [Test]
    public async Task IndependentSourceRefutationsReplayFromTheSameCompiledAssembly()
    {
        const string source = """
            public static class C {
                public static int First(int value) => 10 / value;
                public static int Second(int value) => 20 / value;
            }
            """;
        var root = CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot();
        var file = new OpenSourceCorpusFile("fixture", "ordinary-batch.cs",
            OpenSourceCorpusCatalog.ComputeNormalizedSha256(source), source);
        var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Select(declaration =>
        {
            var span = declaration.GetLocation().GetLineSpan();
            return new OpenSourceCorpusMethod("ordinary-" + declaration.Identifier.ValueText, "fixture", file.Path,
                span.StartLinePosition.Line + 1, span.EndLinePosition.Line + 1,
                OpenSourceCorpusCatalog.ComputeNormalizedSha256(OpenSourceCorpusCatalog.GetDeclaration(declaration)),
                declaration.Identifier.ValueText, "effects", CorpusVerdict.Refuted, CorpusSupport.Supported);
        }).ToImmutableArray();
        var document = new OpenSourceCorpusDocument(2, [], [file], methods);
        var compilation = OpenSourceCorpusRunner.PrepareExceptionProbe(document, CancellationToken.None);

        var report = await NativeEffectOracleGate.ObserveAsync(compilation,
            methods.Select(static method => method.Id).ToImmutableArray(), RepositoryLayout.FindRoot(), "ordinary-batch", 2);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(report.Rows, Has.Length.EqualTo(2));
            Assert.That(report.NativeRefuted, Is.EqualTo(2));
            Assert.That(report.Rows.Select(static row => row.NativeOutcome), Is.All.EqualTo(WorkerClaimOutcome.Refuted));
            Assert.That(report.Rows.Select(static row => row.RuntimeOracle), Is.All.EqualTo("Confirmed"));
            Assert.That(report.RuntimeWitnesses, Is.EqualTo(2));
            Assert.That(report.RuntimeContradictions, Is.Zero);
            Assert.That(report.Passed, Is.True);
        }
    }
}

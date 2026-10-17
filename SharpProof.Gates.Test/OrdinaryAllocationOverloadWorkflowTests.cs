using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Gates.Corpus;
using SharpProof.Worker.Protocol;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class OrdinaryAllocationOverloadWorkflowTests
{
    [Test]
    public void ActualGenericOverloadsThatCoincideAfterClosureReportAmbiguousReplay()
    {
        const string source = """
            public static class C {
                private static int Target<T>(T value) => 0;
                public static int Target<T>(int value) => value;
            }
            """;
        var root = CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot();
        var declaration = root.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(static method => method is null ? throw new System.ArgumentNullException(nameof(method)) : (method.Modifiers.Any(static modifier => modifier.RawKind == (int)SyntaxKind.PublicKeyword)));
        var span = declaration.GetLocation().GetLineSpan();
        var file = new OpenSourceCorpusFile("fixture", "ordinary-generic-overloads.cs",
            OpenSourceCorpusCatalog.ComputeNormalizedSha256(source), source);
        var method = new OpenSourceCorpusMethod("sample", "fixture", file.Path,
            span.StartLinePosition.Line + 1, span.EndLinePosition.Line + 1,
            OpenSourceCorpusCatalog.ComputeNormalizedSha256(OpenSourceCorpusCatalog.GetDeclaration(declaration)),
            "Target", "effects", CorpusVerdict.Proven, CorpusSupport.Supported);
        var document = new OpenSourceCorpusDocument(2, [], [file], [method]);
        var compilation = OpenSourceCorpusRunner.PrepareExceptionProbe(document, CancellationToken.None, allocations: true);
        Assert.That(compilation.GetDiagnostics().Where(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)), Is.Empty);

        var error = Assert.ThrowsAsync<InvalidDataException>((Func<Task>)(async () =>
            await NativeEffectOracleGate.ObserveAsync(compilation, ["sample"],
                RepositoryLayout.FindRoot(), "ordinary-generic-overloads", 1, allocations: true)));

        Assert.That(error!.Message, Is.EqualTo("A source allocation witness has an ambiguous emitted method."));
    }

    [TestCase("private static int Target(string value) => 0;")]
    [TestCase("private static int Target<T>(int value) => 0;")]
    public async Task AllocationReplaySelectsTheActualPublicScalarOverload(string otherOverload)
    {
        System.ArgumentNullException.ThrowIfNull(otherOverload);
        var source = $$"""
            public static class C {
                {{otherOverload}}
                public static int Target(int value) => value;
            }
            """;
        var root = CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot();
        var declaration = root.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(static method => method is null ? throw new System.ArgumentNullException(nameof(method)) : (method.Modifiers.Any(static modifier => modifier.RawKind == (int)SyntaxKind.PublicKeyword)));
        var span = declaration.GetLocation().GetLineSpan();
        var file = new OpenSourceCorpusFile("fixture", "ordinary-overloads.cs",
            OpenSourceCorpusCatalog.ComputeNormalizedSha256(source), source);
        var method = new OpenSourceCorpusMethod("sample", "fixture", file.Path,
            span.StartLinePosition.Line + 1, span.EndLinePosition.Line + 1,
            OpenSourceCorpusCatalog.ComputeNormalizedSha256(OpenSourceCorpusCatalog.GetDeclaration(declaration)),
            "Target", "effects", CorpusVerdict.Proven, CorpusSupport.Supported);
        var document = new OpenSourceCorpusDocument(2, [], [file], [method]);
        var compilation = OpenSourceCorpusRunner.PrepareExceptionProbe(document, CancellationToken.None, allocations: true);

        var report = await NativeEffectOracleGate.ObserveAsync(compilation, ["sample"],
            RepositoryLayout.FindRoot(), "ordinary-overloads", 1, allocations: true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(report.NativeProven, Is.EqualTo(1));
            Assert.That(report.Rows.Single().NativeOutcome, Is.EqualTo(WorkerClaimOutcome.Proven));
            Assert.That(report.Rows.Single().RuntimeOracle, Is.EqualTo("Confirmed"));
            Assert.That(report.Rows.Single().RuntimeChecks, Is.EqualTo(1));
            Assert.That(report.Rows.Single().AllocatedBytes, Is.Zero);
            Assert.That(report.RuntimeContradictions, Is.Zero);
            Assert.That(report.Passed, Is.True);
        }
    }
}

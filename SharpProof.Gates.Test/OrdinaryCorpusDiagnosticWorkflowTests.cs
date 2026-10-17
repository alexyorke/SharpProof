using System.Text;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.Gates.Corpus;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class OrdinaryCorpusDiagnosticWorkflowTests
{
    private static readonly string[] ExpectedDiagnosticIds = ["SP0002", "SP0045"];

    [Test]
    public async Task ActualAllocationAndStaticWriteProduceTwoOrderedCorpusDiagnostics()
    {
        const string source = """
            public static class C {
                private static object? State;
                [SharpProof.Attributes.ZeroAllocations]
                public static int Target(int value) {
                    State = new object();
                    return value;
                }
            }
            """;
        var root = CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot();
        var declaration = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var span = declaration.GetLocation().GetLineSpan();
        var file = new OpenSourceCorpusFile("fixture", "ordinary-two-diagnostics.cs",
            OpenSourceCorpusCatalog.ComputeNormalizedSha256(source), source);
        var method = new OpenSourceCorpusMethod("ordinary-two-diagnostics", file.SourceId, file.Path,
            span.StartLinePosition.Line + 1, span.EndLinePosition.Line + 1,
            OpenSourceCorpusCatalog.ComputeNormalizedSha256(OpenSourceCorpusCatalog.GetDeclaration(declaration)),
            declaration.Identifier.ValueText, "effects", CorpusVerdict.Refuted, CorpusSupport.Supported);

        var observations = await OpenSourceCorpusRunner.ObserveAsync(
            new OpenSourceCorpusDocument(2, [], [file], [method]), CancellationToken.None);

        var observation = observations.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(observation.CaseId, Is.EqualTo("ordinary-two-diagnostics.baseline"));
            Assert.That(observation.Verdict, Is.EqualTo(CorpusVerdict.Refuted));
            Assert.That(observation.SemanticOutcome, Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
            Assert.That(observation.Diagnostics, Has.Length.EqualTo(2));
            Assert.That(observation.Diagnostics.Select(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Split('@')[0])),
                Is.EqualTo(ExpectedDiagnosticIds));
            Assert.That(observation.Diagnostics,
                Is.EqualTo(observation.Diagnostics.OrderBy(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic), StringComparer.Ordinal)));
            foreach (var diagnostic in observation.Diagnostics)
            {
                var fields = diagnostic.Split('@');
                Assert.That(fields, Has.Length.EqualTo(4));
                Assert.That(fields[1], Is.EqualTo("Warning"));
                Assert.That(fields[2], Does.StartWith(file.Path + ":"));
                Assert.That(Encoding.UTF8.GetString(Convert.FromBase64String(fields[3])), Does.Contain("Target"));
            }
        }
    }
}

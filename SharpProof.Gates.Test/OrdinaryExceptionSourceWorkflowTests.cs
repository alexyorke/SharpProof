using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Gates.Corpus;
using SharpProof.Worker.Protocol;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class OrdinaryExceptionSourceWorkflowTests
{
    [TestCase("sbyte", "return 10 / value;")]
    [TestCase("byte", "return 10 / value;")]
    [TestCase("short", "return 10 / value;")]
    [TestCase("ushort", "return 10 / value;")]
    [TestCase("char", "return 10 / value;")]
    [TestCase("uint", "return (int)(10U / value);")]
    [TestCase("long", "return (int)(10L / value);")]
    [TestCase("string", "if (value == \"ordinary\") throw new System.DivideByZeroException(); return 1;")]
    [TestCase("object", "if (value == null) throw new System.NullReferenceException(); return 1;")]
    public async Task RealSourceRefutationsReplayWithTheDeclaredParameterType(string parameterType, string body)
    {
        var source = $"public static class C {{ public static int Target({parameterType} value) {{ {body} }} }}";
        var root = CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot();
        var declaration = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var span = declaration.GetLocation().GetLineSpan();
        var file = new OpenSourceCorpusFile("fixture", "ordinary-exception.cs",
            OpenSourceCorpusCatalog.ComputeNormalizedSha256(source), source);
        var method = new OpenSourceCorpusMethod("sample", "fixture", file.Path,
            span.StartLinePosition.Line + 1, span.EndLinePosition.Line + 1,
            OpenSourceCorpusCatalog.ComputeNormalizedSha256(OpenSourceCorpusCatalog.GetDeclaration(declaration)),
            "Target", "effects", CorpusVerdict.Refuted, CorpusSupport.Supported);
        var document = new OpenSourceCorpusDocument(2, [], [file], [method]);
        var compilation = OpenSourceCorpusRunner.PrepareExceptionProbe(document, CancellationToken.None);

        var report = await NativeEffectOracleGate.ObserveAsync(
            compilation, ["sample"], RepositoryLayout.FindRoot(), "ordinary-source", 1);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(report.NativeRefuted, Is.EqualTo(1));
            Assert.That(report.Rows.Single().NativeOutcome, Is.EqualTo(WorkerClaimOutcome.Refuted));
            Assert.That(report.Rows.Single().RuntimeOracle, Is.EqualTo("Confirmed"));
            Assert.That(report.RuntimeWitnesses, Is.EqualTo(1));
            Assert.That(report.RuntimeContradictions, Is.Zero);
            Assert.That(report.Passed, Is.True);
        }
    }
}

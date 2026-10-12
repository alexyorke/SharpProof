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
    [TestCase("ulong", "return (int)(10UL / value);")]
    [TestCase("bool", "if (value) throw new System.DivideByZeroException(); return 1;")]
    [TestCase("string", "if (value == \"ordinary\") throw new System.DivideByZeroException(); return 1;")]
    [TestCase("object", "if (value == null) throw new System.NullReferenceException(); return 1;")]
    [TestCase("int", "System.Exception exception = new System.DivideByZeroException(); throw exception;")]
    public async Task RealSourceRefutationsReplayWithTheDeclaredParameterType(string parameterType, string body)
    {
        var source = $"public static class C {{ public static int Target({parameterType} value) {{ {body} }} }}";
        var report = await ObserveSourceAsync(source);

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

    [TestCase("public class C { public int Target(int value) => 10 / value; }", "UnsupportedInvocationShape")]
    [TestCase("public static class C { public static int Target<T>(int value) => 10 / value; }", "UnsupportedInvocationShape")]
    [TestCase("public static class C<T> { public static int Target(int value) => 10 / value; }", "UnboundGenericOwner")]
    public async Task RealSourceRefutationsKeepUnsupportedRuntimeInvocationShapesExplicit(string source, string expected)
    {
        var report = await ObserveSourceAsync(source);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(report.NativeRefuted, Is.EqualTo(1));
            Assert.That(report.Rows.Single().NativeOutcome, Is.EqualTo(WorkerClaimOutcome.Refuted));
            Assert.That(report.Rows.Single().RuntimeOracle, Is.EqualTo(expected));
            Assert.That(report.RuntimeWitnesses, Is.Zero);
            Assert.That(report.RuntimeContradictions, Is.Zero);
            Assert.That(report.Passed, Is.True);
        }
    }

    [Test]
    public async Task ExplicitSourceExceptionUsesTheCompilationAssemblyIdentity()
    {
        var report = await ObserveSourceAsync("""
            public sealed class OrdinaryException : System.Exception { }
            public static class C {
                public static int Target() { throw new OrdinaryException(); }
            }
            """);

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

    private static async Task<NativeEffectOracleReport> ObserveSourceAsync(string source)
    {
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

        return await NativeEffectOracleGate.ObserveAsync(
            compilation, ["sample"], RepositoryLayout.FindRoot(), "ordinary-source", 1);
    }
}

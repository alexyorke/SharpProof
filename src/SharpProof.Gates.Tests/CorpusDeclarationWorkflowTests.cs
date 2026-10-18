using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Gates.Corpus;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class CorpusDeclarationWorkflowTests
{
    [TestCase("public static class C { public static int Value() => 1; }", 2, 0)]
    [TestCase("public static class C { public static int Value() => 1; public static int Other() => 2; }", 1, 2)]
    public void SourceLineSelectionRejectsMissingAndAmbiguousDeclarations(string source, int line, int count)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        var document = SourceDocument(source);
        var root = CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot();
        var selected = document.Methods.Single() with { StartLine = line, EndLine = line };
        var error = Assert.Throws<InvalidDataException>((Action)(() =>
            OpenSourceCorpusCatalog.FindDeclaration(OpenSourceCorpusCatalog.BuildDeclarationIndex(root), selected)));
        Assert.That(error!.Message,
            Is.EqualTo($"OSS corpus method sample resolves to {count} declarations at ordinary.cs:{line}-{line}."));
    }

    [Test]
    public void ProbePreparationRejectsASelectedMethodWhoseSourceFileIsMissing()
    {
        var document = SourceDocument("public static class C { public static int Value() => 1; }") with { Files = [] };
        var error = Assert.Throws<InvalidDataException>((Action)(() =>
            OpenSourceCorpusRunner.PrepareExceptionProbe(document, TestContext.CurrentContext.CancellationToken)));
        Assert.That(error!.Message, Is.EqualTo("Instrumented 0 OSS methods, expected 1."));
    }

    [Test]
    public void ProbePreparationReportsOrdinaryCompilerErrors()
    {
        var document = SourceDocument("public static class C { public static int Value() => MissingValue; }");
        var error = Assert.Throws<InvalidDataException>((Action)(() =>
            OpenSourceCorpusRunner.PrepareExceptionProbe(document, TestContext.CurrentContext.CancellationToken)));
        Assert.That(error!.Message, Does.StartWith("The pinned OSS corpus did not compile:"));
        Assert.That(error.Message, Does.Contain("CS0103"));
        Assert.That(error.Message, Does.Contain("MissingValue"));
    }

    [Test]
    public void CatalogRejectsAnEmptyManifest()
    {
        using var temporary = new TempDirectory("SharpProof-empty-corpus-manifest-");
        var directory = OpenSourceCorpusCatalog.GetCorpusDirectory(temporary.FullName);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "oss-methods.json"), "null");
        var error = Assert.Throws<InvalidDataException>((Action)(() => OpenSourceCorpusCatalog.Load(temporary.FullName)));
        Assert.That(error!.Message, Is.EqualTo("The OSS corpus manifest is empty."));
    }

    [Test]
    public void VariantNamingRejectsUnknownEnumValues()
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>((Action)(() =>
            CorpusCatalog.VariantKey((CorpusVariant)(-1))));
        Assert.That(error!.ParamName, Is.EqualTo("variant"));
    }

    private static OpenSourceCorpusDocument SourceDocument(string source)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        var root = CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot();
        var declaration = root.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(static method => method is null ? throw new System.ArgumentNullException(nameof(method)) : (method.Identifier.ValueText == "Value"));
        var span = declaration.GetLocation().GetLineSpan();
        var file = new OpenSourceCorpusFile("fixture", "ordinary.cs",
            OpenSourceCorpusCatalog.ComputeNormalizedSha256(source), source);
        var method = new OpenSourceCorpusMethod("sample", "fixture", file.Path,
            span.StartLinePosition.Line + 1, span.EndLinePosition.Line + 1,
            OpenSourceCorpusCatalog.ComputeNormalizedSha256(OpenSourceCorpusCatalog.GetDeclaration(declaration)),
            "Value", "effects", CorpusVerdict.SilentUnknown, CorpusSupport.Unspecified);
        return new OpenSourceCorpusDocument(2, [], [file], [method]);
    }
}

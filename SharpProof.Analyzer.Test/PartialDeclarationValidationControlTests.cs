using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class PartialDeclarationValidationControlTests
{
    [Test]
    public async Task PartialValidationUsesBothDeclarations(
        [Values] bool generatedDefinition,
        [Values] bool generatedImplementation,
        [Values] bool implementationFirst,
        [Values] bool attributeOnDefinition,
        [Values] bool valid)
    {
        var compilation = CreateCompilation(generatedDefinition, generatedImplementation,
            implementationFirst, attributeOnDefinition, valid);
        await AssertValidation(compilation,
            expected: !valid && !(generatedDefinition && generatedImplementation),
            attributeOnDefinition);
    }

    [TestCase(GeneratedKind.MarkedGenerated, GeneratedKind.MarkedGenerated, false)]
    [TestCase(GeneratedKind.NotGenerated, GeneratedKind.MarkedGenerated, true)]
    [TestCase(GeneratedKind.MarkedGenerated, GeneratedKind.NotGenerated, true)]
    [TestCase(GeneratedKind.NotGenerated, GeneratedKind.NotGenerated, true)]
    public async Task ExplicitTreeClassificationControlsLogicalMethod(
        GeneratedKind definitionKind, GeneratedKind implementationKind, bool expected)
    {
        var compilation = CreateCompilation(true, true, false, true, false);
        compilation = compilation.WithOptions(compilation.Options.WithSyntaxTreeOptionsProvider(
            new PerDeclarationGeneratedProvider(definitionKind, implementationKind)));
        await AssertValidation(compilation, expected, attributeOnDefinition: true);
    }

    [TestCase(false, false)]
    [TestCase(true, true)]
    public async Task GeneratedSymbolRespectsExplicitHandwrittenTree(
        bool definitionNotGenerated, bool expected)
    {
        var compilation = CreateCompilation(false, false, false, true, false, generatedSymbol: true);
        if (definitionNotGenerated)
        {
            compilation = compilation.WithOptions(compilation.Options.WithSyntaxTreeOptionsProvider(
                new PerDeclarationGeneratedProvider(GeneratedKind.NotGenerated, GeneratedKind.Unknown)));
        }
        await AssertValidation(compilation, expected, attributeOnDefinition: true);
    }

    [TestCase(false, true)]
    [TestCase(true, false)]
    public async Task NonPartialGeneratedValidationPolicyIsUnchanged(bool generated, bool expected)
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            "using SharpProof.Attributes; public static class Subject { " +
            "[EffectContract((SharpProofEffect)(1L << 40), Complete = true)] public static void Run() { } }",
            ["SP0024"], filePath: generated ? "Subject.Definition.g.cs" : "Subject.Definition.cs");
        await AssertValidation(compilation, expected, attributeOnDefinition: true);
    }

    private static CSharpCompilation CreateCompilation(bool generatedDefinition,
        bool generatedImplementation, bool implementationFirst, bool attributeOnDefinition,
        bool valid, bool generatedSymbol = false)
    {
        var attribute = valid
            ? "[EffectContract(SharpProofEffect.None, Complete = true)]"
            : "[EffectContract((SharpProofEffect)(1L << 40), Complete = true)]";
        var symbolAttribute = generatedSymbol
            ? "[System.CodeDom.Compiler.GeneratedCode(\"audit\", \"1\")]"
            : string.Empty;
        var definition = "using SharpProof.Attributes; public static partial class Subject { " +
            symbolAttribute + (attributeOnDefinition ? attribute : string.Empty) +
            " public static partial void Run(); }";
        var implementation = "using SharpProof.Attributes; public static partial class Subject { " +
            (attributeOnDefinition ? string.Empty : attribute) + " public static partial void Run() { } }";
        var definitionPath = generatedDefinition ? "Subject.Definition.g.cs" : "Subject.Definition.cs";
        var implementationPath = generatedImplementation ? "Subject.Implementation.g.cs" : "Subject.Implementation.cs";
        var compilation = AnalyzerTestHost.CreateCompilation(
            implementationFirst ? implementation : definition, ["SP0024"],
            filePath: implementationFirst ? implementationPath : definitionPath);
        return compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(
            implementationFirst ? definition : implementation,
            new CSharpParseOptions(LanguageVersion.Preview),
            implementationFirst ? definitionPath : implementationPath));
    }

    private static async Task AssertValidation(CSharpCompilation compilation, bool expected,
        bool attributeOnDefinition)
    {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(compilation, mode: null,
            profile: "advisory", features: "effects");
        if (!expected)
        {
            Assert.That(diagnostics, Is.Empty);
            return;
        }
        AnalyzerTestHost.AssertIds(diagnostics, "SP0024");
        var expectedTree = compilation.SyntaxTrees.Single(tree => tree.FilePath.Contains(
            attributeOnDefinition ? "Definition" : "Implementation", StringComparison.Ordinal));
        Assert.That(diagnostics.Single().Location.SourceTree, Is.SameAs(expectedTree));
    }

    private sealed class PerDeclarationGeneratedProvider(
        GeneratedKind definitionKind, GeneratedKind implementationKind) : SyntaxTreeOptionsProvider
    {
        public override GeneratedKind IsGenerated(SyntaxTree tree, CancellationToken cancellationToken)
        {
            return tree.FilePath.Contains("Definition", StringComparison.Ordinal)
                ? definitionKind : implementationKind;
        }

        public override bool TryGetDiagnosticValue(SyntaxTree tree, string diagnosticId,
            CancellationToken cancellationToken, out ReportDiagnostic severity)
        {
            severity = ReportDiagnostic.Default;
            return false;
        }

        public override bool TryGetGlobalDiagnosticValue(string diagnosticId,
            CancellationToken cancellationToken, out ReportDiagnostic severity)
        {
            severity = ReportDiagnostic.Default;
            return false;
        }
    }
}

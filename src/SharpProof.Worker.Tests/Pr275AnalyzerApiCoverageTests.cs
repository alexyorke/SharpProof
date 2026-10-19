using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.Compiler.Semantics;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class Pr275AnalyzerApiCoverageTests
{
    private static readonly CSharpCompilation Compilation = TestCompilation.Create(
        "Pr275AnalyzerApi", ("Subject.cs", """
            using System;
            class Subject {
                public Subject() { }
                public int Value { get => 0; set { } }
                public int this[int index] { get => 0; set { } }
                public event Action Changed { add { } remove { } }
                public static Subject operator +(Subject left, Subject right) => left;
                public static implicit operator int(Subject value) => 0;
                public void Read() { }
            }
            """));

    [TestCase("method", "Read")]
    [TestCase("constructor", "Subject")]
    [TestCase("property", "Value")]
    [TestCase("indexer", "this")]
    [TestCase("property getter", "Value")]
    [TestCase("indexer getter", "this")]
    [TestCase("event add", "add")]
    [TestCase("operator", "+")]
    [TestCase("conversion", "implicit")]
    public void CallableLocationsSelectTheirUserVisibleDeclarationToken(string declarationKind, string expectedToken)
    {
        System.ArgumentNullException.ThrowIfNull(expectedToken);
        var tree = Compilation.SyntaxTrees.Single();
        var nodes = tree.GetRoot().DescendantNodes().ToImmutableArray();
        SyntaxNode declaration = declarationKind switch
        {
            "method" => nodes.OfType<MethodDeclarationSyntax>().Single(),
            "constructor" => nodes.OfType<ConstructorDeclarationSyntax>().Single(),
            "property" => nodes.OfType<PropertyDeclarationSyntax>().Single(),
            "indexer" => nodes.OfType<IndexerDeclarationSyntax>().Single(),
            "property getter" => nodes.OfType<AccessorDeclarationSyntax>().Single(node =>
                node is null ? throw new System.ArgumentNullException(nameof(node)) : (node.IsKind(SyntaxKind.GetAccessorDeclaration) && node.Parent?.Parent is PropertyDeclarationSyntax)),
            "indexer getter" => nodes.OfType<AccessorDeclarationSyntax>().Single(node =>
                node is null ? throw new System.ArgumentNullException(nameof(node)) : (node.IsKind(SyntaxKind.GetAccessorDeclaration) && node.Parent?.Parent is IndexerDeclarationSyntax)),
            "event add" => nodes.OfType<AccessorDeclarationSyntax>().Single(node => node is null ? throw new System.ArgumentNullException(nameof(node)) : (node.IsKind(SyntaxKind.AddAccessorDeclaration))),
            "operator" => nodes.OfType<OperatorDeclarationSyntax>().Single(),
            "conversion" => nodes.OfType<ConversionOperatorDeclarationSyntax>().Single(),
            _ => throw new ArgumentOutOfRangeException(nameof(declarationKind))
        };

        var location = AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(declaration);

        Assert.That(location.SourceTree, Is.SameAs(tree));
        Assert.That(tree.GetText().ToString(location.SourceSpan), Is.EqualTo(expectedToken));
    }

    [Test]
    public void MissingAnalyzerApiInputsPreserveTheirParameterNames()
    {
        var tree = Compilation.SyntaxTrees.Single();
        var nodeError = Assert.Throws<ArgumentNullException>((Action)(() =>
            _ = AnalyzerSyntaxHelpers.GetCallableDeclarationLocation((SyntaxNode)null!)));
        var methodError = Assert.Throws<ArgumentNullException>((Action)(() =>
            _ = AnalyzerSyntaxHelpers.GetCallableDeclarationLocation((IMethodSymbol)null!, default)));
        var optionsError = Assert.Throws<ArgumentNullException>((Action)(() =>
            _ = CompilerConfiguration.FromOptions((AnalyzerOptions)null!)));
        var symbolError = Assert.Throws<ArgumentNullException>((Action)(() =>
            _ = AnalyzerGeneratedCodePolicy.IsGenerated(null!, tree, Compilation, default)));

        Assert.That(nodeError!.ParamName, Is.EqualTo("node"));
        Assert.That(methodError!.ParamName, Is.EqualTo("method"));
        Assert.That(optionsError!.ParamName, Is.EqualTo("options"));
        Assert.That(symbolError!.ParamName, Is.EqualTo("symbol"));
    }

    [Test]
    public void StandardSdkOptionsSelectTheDocumentedDefaultProfile()
    {
        var configuration = CompilerConfiguration.FromOptions(new AnalyzerOptions([]));

        Assert.That(configuration.Profile, Is.EqualTo(SharpProofProfile.Advisory));
        Assert.That(configuration.InvalidConfigurationValues, Is.Empty);
        Assert.That(configuration.EffectsEnabled, Is.True);
        Assert.That(configuration.ContractsEnabled, Is.True);
    }

    [Test]
    public void ARealMetadataMethodHasNoSourceDeclarationLocation()
    {
        var method = Compilation.GetSpecialType(SpecialType.System_String).GetMembers("Concat")
            .OfType<IMethodSymbol>().First();

        var location = AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(method, default);

        Assert.That(location.IsInSource, Is.False);
    }
}

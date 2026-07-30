using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class AnalyzerSyntaxHelpersTests
{
    [Test]
    public void CallableLocationsIdentifyEverySupportedDeclarationShape()
    {
        const string source = """
            using System;

            class Fixture
            {
                int Property { get; set; }
                int this[int index] { get => index; set { } }
                Fixture() { }
                void Method() { }
                event Action Changed { add { } remove { } }
                public static Fixture operator +(Fixture left, Fixture right) => left;
                public static implicit operator int(Fixture value) => 0;
            }
            """;
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        var method = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var property = root.DescendantNodes().OfType<PropertyDeclarationSyntax>().Single();
        var indexer = root.DescendantNodes().OfType<IndexerDeclarationSyntax>().Single();
        var constructor = root.DescendantNodes().OfType<ConstructorDeclarationSyntax>().Single();
        var operation = root.DescendantNodes().OfType<OperatorDeclarationSyntax>().Single();
        var conversion = root.DescendantNodes().OfType<ConversionOperatorDeclarationSyntax>().Single();
        var accessors = root.DescendantNodes().OfType<AccessorDeclarationSyntax>().ToArray();
        var eventAccessors = accessors.Where(
            static accessor =>
                accessor.Parent?.Parent is EventDeclarationSyntax).ToArray();
        var declaration = root.DescendantNodes().OfType<ClassDeclarationSyntax>().Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(method).SourceSpan,
                Is.EqualTo(method.Identifier.Span));
            Assert.That(
                AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(property).SourceSpan,
                Is.EqualTo(property.Identifier.Span));
            Assert.That(
                AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(indexer).SourceSpan,
                Is.EqualTo(indexer.ThisKeyword.Span));
            Assert.That(
                AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(constructor).SourceSpan,
                Is.EqualTo(constructor.Identifier.Span));
            Assert.That(
                AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(operation).SourceSpan,
                Is.EqualTo(operation.OperatorToken.Span));
            Assert.That(
                AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(conversion).SourceSpan,
                Is.EqualTo(conversion.ImplicitOrExplicitKeyword.Span));
            Assert.That(
                AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(declaration).SourceSpan,
                Is.EqualTo(declaration.Span));
        }

        foreach (var accessor in eventAccessors)
        {
            Assert.That(
                AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(accessor).SourceSpan,
                Is.EqualTo(accessor.Keyword.Span));
        }

        foreach (var accessor in accessors.Where(
                     static accessor =>
                         accessor.Parent?.Parent is PropertyDeclarationSyntax))
        {
            Assert.That(
                AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(accessor).SourceSpan,
                Is.EqualTo(property.Identifier.Span));
        }

        foreach (var accessor in accessors.Where(
                     static accessor =>
                         accessor.Parent?.Parent is IndexerDeclarationSyntax))
        {
            Assert.That(
                AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(accessor).SourceSpan,
                Is.EqualTo(indexer.ThisKeyword.Span));
        }
    }
}

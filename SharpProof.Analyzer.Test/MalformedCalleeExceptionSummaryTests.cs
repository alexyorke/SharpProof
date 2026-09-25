using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.CompilerArtifact;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class MalformedCalleeExceptionSummaryTests
{
    [Test]
    public async Task ErrorTypedCalleeThrowBecomesUnsupportedUnknown()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            """
            using System;
            using SharpProof.Attributes;

            public static class E3
            {
                public static int H()
                {
                    throw new InvalidOperationException()(;
                }
            }

            public static class Fixture
            {
                [DoesNotThrow]
                public static int C() => E3.H();
            }
            """,
            ["SP0046", "SP0047"]);

        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            compilation,
            "effects",
            allowCompilationErrors: true);

        Assert.That(
            compilation.GetDiagnostics().Any(static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error),
            Is.True,
            "The fixture must keep the callee in the malformed-source state.");
        Assert.That(
            diagnostics.Any(static diagnostic => diagnostic.Id == "AD0001"),
            Is.False);
        AnalyzerTestHost.AssertIds(diagnostics, "SP0046");
        AnalyzerTestHost.AssertMessageContains(diagnostics.Single(), "ExceptionSetUnknown");
        AnalyzerTestHost.AssertMessageContains(diagnostics.Single(), "UnsupportedOperation");
    }

    [Test]
    public async Task EncodableCalleeThrowRemainsATypedDoesNotThrowViolation()
    {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using System;
            using SharpProof.Attributes;

            public static class Fixture
            {
                private static int H() => throw new InvalidOperationException();

                [DoesNotThrow]
                public static int C() => H();
            }
            """,
            "effects",
            ["SP0046"]);

        AnalyzerTestHost.AssertIds(diagnostics, "SP0046");
        AnalyzerTestHost.AssertMessageContains(
            diagnostics.Single(),
            "InvalidOperationException");
        Assert.That(
            diagnostics.Any(static diagnostic => diagnostic.Id == "AD0001"),
            Is.False);
    }

    [Test]
    public void FormatTypesUsesFallbackForErrorAndUnencodableSymbols()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            """
            public sealed class Fixture
            {
                MissingException Error;
                public object Anonymous() => new { Value = 1 };
            }
            """,
            []);
        var tree = compilation.SyntaxTrees.Single();
        var root = tree.GetRoot();
        var model = compilation.GetSemanticModel(tree);
        var errorSyntax = root.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Single(static syntax =>
                syntax.Identifier.ValueText == "MissingException");
        var errorType = (INamedTypeSymbol)model.GetTypeInfo(errorSyntax).Type!;
        var anonymousSyntax = root.DescendantNodes()
            .OfType<AnonymousObjectCreationExpressionSyntax>()
            .Single();
        var anonymousType = (INamedTypeSymbol)model.GetTypeInfo(anonymousSyntax).Type!;
        var validType = compilation.GetTypeByMetadataName(
            "System.InvalidOperationException")!;

        Assert.That(errorType.TypeKind, Is.EqualTo(TypeKind.Error));
        Assert.That(anonymousType.IsAnonymousType, Is.True);
        Assert.That(
            DocumentationCommentId.CreateReferenceId(anonymousType),
            Is.Null.Or.Empty);
        Assert.Throws<InvalidOperationException>((Action)(() =>
        {
            _ = CompilerExceptionTypeIdentity.Encode(anonymousType);
        }));
        var expected = string.Join(",", new[]
            {
                "<error-type>",
                CompilerExceptionTypeIdentity.Encode(validType)
            }
            .OrderBy(static value => value, StringComparer.Ordinal));
        Assert.That(
            EffectContractDiagnostics.FormatTypes(
                [errorType, anonymousType, validType]),
            Is.EqualTo(expected));
    }
}

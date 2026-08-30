using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class AnalyzerGateHostTests
{
    [Test]
    public async Task SemanticOutcomesKeepSamePositionMethodsInDistinctTrees()
    {
        const string pureSource =
            """
            using SharpProof.Attributes;

            public static class A
            {
                [EnforcePure]
                public static int Evaluate()
                {
                    return 1;
                }
            }
            """;
        const string impureSource =
            """
            using SharpProof.Attributes;

            public static class B
            {
                [EnforcePure]
                public static int Evaluate()
                {
                    State = 1;
                    return State;
                }

                private static int State;
            }
            """;
        var secondTree = CSharpSyntaxTree.ParseText(
            impureSource,
            AnalyzerGateHost.ParseOptions,
            "input.cs");
        var compilation = AnalyzerGateHost.CreateCompilation(pureSource)
            .AddSyntaxTrees(secondTree);
        var methods = compilation.SyntaxTrees
            .Select(tree => compilation.GetSemanticModel(tree)
                .GetDeclaredSymbol(tree.GetRoot()
                    .DescendantNodes()
                    .OfType<MethodDeclarationSyntax>()
                    .Single()))
            .OfType<IMethodSymbol>()
            .ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                compilation.GetDiagnostics()
                    .Where(static diagnostic =>
                        diagnostic.Severity == DiagnosticSeverity.Error),
                Is.Empty);
            Assert.That(methods, Has.Length.EqualTo(2));
            Assert.That(
                methods.Select(static method => method.MetadataName),
                Is.All.EqualTo("Evaluate"));
            Assert.That(
                methods.Select(static method => method.DeclaredAccessibility),
                Is.All.EqualTo(Accessibility.Public));
            Assert.That(
                methods.Select(static method =>
                    method.Locations.Single().SourceSpan.Start),
                Is.All.EqualTo(methods[0].Locations.Single().SourceSpan.Start));
            Assert.That(
                methods.Select(static method =>
                    method.Locations.Single().SourceTree?.FilePath),
                Is.All.EqualTo("input.cs"));
            Assert.That(
                methods[0].Locations.Single().SourceTree,
                Is.Not.SameAs(methods[1].Locations.Single().SourceTree));
            Assert.That(
                SymbolEqualityComparer.Default.Equals(methods[0], methods[1]),
                Is.False);
        }

        var analysis = await AnalyzerGateHost.AnalyzeWithSemanticOutcomesAsync(
            compilation,
            mode: "effects",
            concurrentAnalysis: true);

        Assert.That(analysis.SemanticOutcomes, Has.Length.EqualTo(2));
    }
}

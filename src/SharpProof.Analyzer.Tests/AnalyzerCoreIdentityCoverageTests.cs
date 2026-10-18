using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.CompilerArtifact;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class AnalyzerCoreIdentityCoverageTests
{
    [Test]
    public void GenericExceptionIdentityRecordsArrayAndContainingTypeArguments()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            """
            public class Outer<T> {
                public sealed class Failure<U> : System.Exception { }
            }
            public static class Fixture {
                public static void Target(Outer<int>.Failure<string[]> failure) { }
            }
            """, []);
        var tree = compilation.SyntaxTrees.Single();
        var declaration = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var method = (IMethodSymbol)compilation.GetSemanticModel(tree).GetDeclaredSymbol(declaration)!;
        var exception = (INamedTypeSymbol)method.Parameters[0].Type;
        var encoded = CompilerExceptionTypeIdentity.Encode(exception);
        var hierarchy = CompilerExceptionTypeIdentity.EncodeHierarchy(exception);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(encoded, Does.Contain("generic-argument-assemblies["));
            Assert.That(encoded, Does.Contain(compilation.GetSpecialType(SpecialType.System_Int32).ContainingAssembly.Identity.ToString()));
            Assert.That(encoded, Does.Contain(compilation.GetSpecialType(SpecialType.System_String).ContainingAssembly.Identity.ToString()));
            Assert.That(hierarchy, Does.Contain(encoded));
            Assert.That(hierarchy, Does.Contain(CompilerExceptionTypeIdentity.Encode(compilation.GetTypeByMetadataName("System.Exception")!)));
            Assert.That(hierarchy, Is.EqualTo(hierarchy.OrderBy(static value => value is null ? throw new System.ArgumentNullException(nameof(value)) : (value), StringComparer.Ordinal).ToArray()));
        }
    }

    [Test]
    public async Task AllowedBaseExceptionIncludesNestedGenericArrayArgumentIdentity()
    {
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;
            public class Outer<T> {
                public sealed class Failure<U> : System.Exception { }
            }
            public static class Fixture {
                [AllowedExceptions(typeof(System.Exception))]
                public static int Target() => throw new Outer<int>.Failure<string[]>();
            }
            """, "effects", ["SP0046"], new SharpProofAnalyzer(factory));
        Assert.That(diagnostics, Is.Empty);
        Assert.That(factory.Outcomes["Target"], Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
    }

    [Test]
    public void OperationAncestorsFollowTheSemanticParentChain()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            "public static class Fixture { public static int Target(int x) { return x + 1; } }", []);
        var tree = compilation.SyntaxTrees.Single();
        var binary = tree.GetRoot().DescendantNodes().OfType<BinaryExpressionSyntax>().Single();
        var operation = compilation.GetSemanticModel(tree).GetOperation(binary)!;
        var ancestors = OperationAncestors.Of(operation).ToArray();
        Assert.That(ancestors, Is.Not.Empty);
        Assert.That(ancestors[0], Is.SameAs(operation.Parent));
        for (var index = 1; index < ancestors.Length; index++)
        {
            Assert.That(ancestors[index], Is.SameAs(ancestors[index - 1].Parent));
        }
        Assert.That(ancestors[^1].Parent, Is.Null);
        Assert.That(OperationAncestors.Of(ancestors[^1]), Is.Empty);
    }
}

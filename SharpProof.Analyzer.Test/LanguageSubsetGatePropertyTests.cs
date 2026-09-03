using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class LanguageSubsetGatePropertyTests
{
    [TestCase("Read", MethodKind.PropertyGet)]
    [TestCase("Write", MethodKind.PropertySet)]
    public void GenericPropertyAccessRequiresOnlyMatchingAccessorApiSpec(
        string methodName,
        MethodKind expectedKind)
    {
        var requests = new List<IMethodSymbol>();
        var decision = Classify(
            methodName,
            accessor =>
            {
                requests.Add(accessor);
                return accessor.MethodKind == expectedKind;
            });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(decision.IsSupported, Is.True);
            Assert.That(
                requests.Select(static accessor => accessor.MethodKind),
                Is.EqualTo([expectedKind]));
        }
    }

    private static LanguageSubsetDecision Classify(
        string methodName,
        Func<IMethodSymbol, bool> hasResolvedGenericApiSpec)
    {
        const string source = """
            public sealed class GenericBox<T>
            {
                public T Value { get; set; } = default!;
            }

            public static class Fixture
            {
                public static int Read(GenericBox<int> box) => box.Value;

                public static void Write(GenericBox<int> box, int value) =>
                    box.Value = value;
            }
            """;
        var compilation = AnalyzerTestHost.CreateCompilation(
            source,
            enabledIds: []);
        var tree = compilation.SyntaxTrees.Single();
        var declaration = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == methodName);
        var semanticModel = compilation.GetSemanticModel(tree);
        var method = semanticModel.GetDeclaredSymbol(declaration) as IMethodSymbol ??
            throw new InvalidOperationException("Method symbol is unavailable.");
        var operation = semanticModel.GetOperation(declaration) ??
            throw new InvalidOperationException("Method operation is unavailable.");

        Assert.That(
            operation.DescendantsAndSelf().OfType<IPropertyReferenceOperation>(),
            Has.Exactly(1).Items);

        return LanguageSubsetGate.ClassifyEffects(
            method,
            declaration,
            semanticModel,
            [operation],
            hasResolvedGenericApiSpec,
            CancellationToken.None);
    }
}

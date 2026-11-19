using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class CachedInitializerClassificationTests
{
    [TestCase("default")]
    [TestCase("default!")]
    [TestCase("default(T)")]
    public void DefaultAssignmentDoesNotRunGenericTypeCode(string value)
    {
        System.ArgumentNullException.ThrowIfNull(value);
        var source = """
            public sealed class Cache<T>
            {
                private readonly T _value;
                private readonly System.Func<int> _failure;
                public bool CanContinue { get; }
                private Cache(System.Func<int> failure, bool canContinue)
                {
                    _failure = failure;
                    CanContinue = canContinue;
                    _value = VALUE;
                }
                public static Cache<T> Empty { get; } = new Cache<T>(() => System.Environment.ProcessorCount, false);
            }
            """.Replace("VALUE", value, StringComparison.Ordinal);
        var token = TestContext.CurrentContext.CancellationToken;
        var tree = CSharpSyntaxTree.ParseText(source, cancellationToken: token);
        var compilation = CSharpCompilation.Create("CachedInitializer", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var symbol = compilation.GetTypeByMetadataName("Cache`1")!;
        var operation = compilation.GetSemanticModel(tree).GetOperation(tree.GetRoot(token)
            .DescendantNodes().OfType<ConstructorDeclarationSyntax>().Single(), token)!;
        var values = operation.Descendants().OfType<ISimpleAssignmentOperation>().Select(assignment =>
            $"{assignment.Value.Syntax}: {assignment.Value.Kind} / {assignment.Value.Type} / {assignment.Value.ConstantValue.HasValue}");
        Assert.That(TypeInitialization.Classify(compilation, symbol, token), Is.EqualTo(InitializationEffect.FreshValues),
            string.Join(", ", values));
    }
}

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Symbolic;

namespace SharpProof.Test;

[TestFixture]
public sealed class SymbolMutationFactsTests {
    [Test]
    public void ExpressionReferencesSymbol_TracksFieldsButNotNestedCallableBodies() {
        const string source = """
                              class Sample
                              {
                                  private int _value;

                                  private void Use(System.Func<int> callback) { }

                                  private void Update()
                                  {
                                      _value = _value + 1;
                                      Use(() => _value);
                                  }
                              }
                              """;
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            "SymbolMutationFactsTests",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) });
        var semanticModel = compilation.GetSemanticModel(tree);
        var root = tree.GetRoot();
        var fieldDeclaration = root.DescendantNodes().OfType<VariableDeclaratorSyntax>().Single();
        var field = semanticModel.GetDeclaredSymbol(fieldDeclaration)!;
        var assignments = root.DescendantNodes().OfType<AssignmentExpressionSyntax>().ToArray();
        var invocation = root.DescendantNodes().OfType<InvocationExpressionSyntax>().Single();

        Assert.Multiple(() => {
            Assert.That(SymbolMutationFacts.ExpressionReferencesSymbol(
                assignments[0].Right,
                field,
                semanticModel,
                CancellationToken.None), Is.True);
            Assert.That(SymbolMutationFacts.ExpressionReferencesSymbol(
                invocation,
                field,
                semanticModel,
                CancellationToken.None), Is.False);
        });
    }
}

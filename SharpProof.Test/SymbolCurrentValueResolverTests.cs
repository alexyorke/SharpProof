using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Symbolic;

namespace SharpProof.Test;

[TestFixture]
public sealed class SymbolCurrentValueResolverTests
{
    [Test]
    public void ExactRuntimeType_LaterLoopMutationInvalidatesPriorIterationValue()
    {
        const string source = """
                              public sealed class TestClass
                              {
                                  public int Length(bool repeat)
                                  {
                                      object value = "text";
                                      while (repeat)
                                      {
                                          var length = ((string)value).Length;
                                          value = new object();
                                      }

                                      return 0;
                                  }
                              }
                              """;
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var compilation = CSharpCompilation.Create(
            "LoopRuntimeTypeMutation",
            new[] { syntaxTree },
            AnalyzerTestHost.GetTrustedPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var cast = syntaxTree.GetRoot().DescendantNodes().OfType<CastExpressionSyntax>().Single();

        var resolved = SymbolicRuntimeTypeFacts.TryGetExactRuntimeType(
            cast.Expression,
            cast,
            semanticModel,
            CancellationToken.None,
            out _);

        Assert.That(resolved, Is.False);
    }
}

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Analyzer.Engine.Rules;
using SharpProof.Symbolic;

namespace SharpProof.Test;

[TestFixture]
public sealed class OperationClassificationTests
{
    [Test]
    public void WriteOnlyAssignmentTarget_DistinguishesWritesFromReadModifyWrite()
    {
        const string source = """
                              sealed class C
                              {
                                  private int P { get; set; }
                                  private readonly int[] _values = new int[1];

                                  private void M()
                                  {
                                      P = 1;
                                      P += 1;
                                      P++;
                                      _values[0] = 1;
                                      _values[0] += 1;
                                  }
                              }
                              """;

        var model = CreateSemanticModel(source, out var root);
        var assignments = root.DescendantNodes().OfType<AssignmentExpressionSyntax>().ToArray();
        var increment = root.DescendantNodes().OfType<PostfixUnaryExpressionSyntax>().Single();

        Assert.Multiple(() =>
        {
            Assert.That(IsWriteOnlyTarget(model, assignments[0].Left), Is.True);
            Assert.That(IsWriteOnlyTarget(model, assignments[1].Left), Is.False);
            Assert.That(IsWriteOnlyTarget(model, increment.Operand), Is.False);
            Assert.That(IsWriteOnlyTarget(model, assignments[2].Left), Is.True);
            Assert.That(IsWriteOnlyTarget(model, assignments[3].Left), Is.False);
        });
    }

    [Test]
    public void BaseReference_UsesTheCanonicalDispatchClassifier()
    {
        const string source = """
                              class B
                              {
                                  protected virtual void M() { }
                              }

                              sealed class C : B
                              {
                                  protected override void M() => base.M();
                              }
                              """;

        var model = CreateSemanticModel(source, out var root);
        var invocation = root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Select(node => model.GetOperation(node))
            .OfType<IInvocationOperation>()
            .Single();

        Assert.That(SymbolicDispatchFacts.IsBaseReference(invocation.Instance), Is.True);
    }

    private static bool IsWriteOnlyTarget(SemanticModel model, ExpressionSyntax expression)
    {
        var operation = model.GetOperation(expression);
        Assert.That(operation, Is.Not.Null);
        return RuleAnalysisHelper.IsWriteOnlyAssignmentTarget(operation!);
    }

    private static SemanticModel CreateSemanticModel(string source, out SyntaxNode root)
    {
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var compilation = CSharpCompilation.Create(
            "OperationClassificationTests",
            new[] { tree },
            AnalyzerTestHost.GetTrustedPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var diagnostics = compilation.GetDiagnostics();
        Assert.That(diagnostics, Is.Empty, string.Join(Environment.NewLine, diagnostics));
        root = tree.GetRoot();
        return compilation.GetSemanticModel(tree);
    }
}

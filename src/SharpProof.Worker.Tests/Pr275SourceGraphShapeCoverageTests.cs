using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Frontend;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class Pr275SourceGraphShapeCoverageTests
{
    [TestCase("static int Local(int value) { return value; }", true)]
    [TestCase("int Local(int value) { return value + input; }", false)]
    [TestCase("int Peer(int value) { return input + value; } int Local(int value) { return Peer(value); }", false)]
    [TestCase("static int Peer(int value) { return value; } static int Local(int value) { return Peer(value); }", true)]
    [TestCase("static int Local(int value) { static int Nested(int other) { return other; } return Nested(value); }", false)]
    public void LocalFunctionGraphsRequireAnIndependentParameterOnlyBody(string declarations, bool admitted)
    {
        System.ArgumentNullException.ThrowIfNull(declarations);
        var compilation = TestCompilation.Create("Pr275LocalGraph",
            "internal static class Subject { internal static int Target(int input) { " +
            declarations + " return Local(input); } }");
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        var local = tree.GetRoot().DescendantNodes().OfType<LocalFunctionStatementSyntax>()
            .Single(node => node is null ? throw new System.ArgumentNullException(nameof(node)) : (node.Identifier.Text == "Local"));
        var method = (IMethodSymbol)model.GetDeclaredSymbol(local)!;

        var graph = TotalSourceCallSession.CreateGraph(local, method, model, default);

        Assert.That(graph != null, Is.EqualTo(admitted));
        if (graph != null)
        {
            Assert.That(local.Span.Contains(graph.OriginalOperation.Syntax.Span), Is.True);
            Assert.That(graph.Blocks, Is.Not.Empty);
        }
    }

    [TestCase("internal Subject(int input) { }", true)]
    [TestCase("private int _value = 5; internal Subject(int input) { _value = input; }", false)]
    [TestCase("internal Subject() { } internal Subject(int input) : this() { }", false)]
    public void ConstructorGraphsExcludeInitializationAndChainingOutsideTheWrittenBody(
        string declarations, bool admitted)
    {
        System.ArgumentNullException.ThrowIfNull(declarations);
        var compilation = TestCompilation.Create("Pr275ConstructorGraph",
            "internal sealed class Subject { " + declarations + " }");
        var method = compilation.GetTypeByMetadataName("Subject")!.InstanceConstructors
            .Single(constructor => constructor is null ? throw new System.ArgumentNullException(nameof(constructor)) : (constructor.Parameters.Length == 1));
        var declaration = method.DeclaringSyntaxReferences.Single().GetSyntax();

        var graph = TotalSourceCallSession.CreateGraph(declaration, method,
            compilation.GetSemanticModel(declaration.SyntaxTree), default);

        Assert.That(graph != null, Is.EqualTo(admitted));
        if (graph != null)
        {
            Assert.That(graph.Blocks, Is.Not.Empty);
        }
    }
}

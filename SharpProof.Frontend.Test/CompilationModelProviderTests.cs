using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Frontend.Host;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class CompilationModelProviderTests
{
    [Test]
    public void ResolvesTreesFromNestedSourceCompilationReferences()
    {
        var leafTree = CSharpSyntaxTree.ParseText(
            "internal static class Leaf { internal static int Value => 1; }");
        var leaf = CreateCompilation("Leaf", leafTree);
        var middleTree = CSharpSyntaxTree.ParseText(
            "internal static class Middle { internal static int Value => 2; }");
        var middle = CreateCompilation(
            "Middle",
            middleTree,
            leaf.ToMetadataReference());
        var rootTree = CSharpSyntaxTree.ParseText(
            "internal static class Root { internal static int Value => 3; }");
        var root = CreateCompilation(
            "Root",
            rootTree,
            middle.ToMetadataReference());

        var rootModel = CompilationModelProvider.GetSemanticModel(
            root,
            rootTree);
        var leafModel = CompilationModelProvider.GetSemanticModel(
            root,
            leafTree);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(rootModel.Compilation, Is.SameAs(root));
            Assert.That(leafModel.Compilation, Is.SameAs(leaf));
        }
    }

    [Test]
    public void RejectsTreeOutsideCompilationReferenceClosure()
    {
        var root = CreateCompilation(
            "Root",
            CSharpSyntaxTree.ParseText(
                "internal static class Root { }"));
        var unrelated = CSharpSyntaxTree.ParseText(
            "internal static class Unrelated { }");

        var exception = Assert.Throws<ArgumentException>(
            (Action)(() =>
                CompilationModelProvider.GetSemanticModel(root, unrelated)));

        Assert.That(exception!.ParamName, Is.EqualTo("tree"));
    }

    [Test]
    public void RejectsTreeOwnedByMultipleSourceCompilations()
    {
        var sharedTree = CSharpSyntaxTree.ParseText(
            "internal static class Shared { }");
        var firstOwner = CreateCompilation("FirstOwner", sharedTree);
        var secondOwner = CreateCompilation("SecondOwner", sharedTree);
        var root = CreateCompilation(
            "Root",
            CSharpSyntaxTree.ParseText("internal static class Root { }"),
            firstOwner.ToMetadataReference(),
            secondOwner.ToMetadataReference());

        var exception = Assert.Throws<ArgumentException>(
            (Action)(() =>
                CompilationModelProvider.GetSemanticModel(root, sharedTree)));

        Assert.That(exception!.ParamName, Is.EqualTo("tree"));
    }

    private static CSharpCompilation CreateCompilation(
        string name,
        SyntaxTree tree,
        params MetadataReference[] references)
    {
        return CSharpCompilation.Create(
            name,
            [tree],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
    }
}

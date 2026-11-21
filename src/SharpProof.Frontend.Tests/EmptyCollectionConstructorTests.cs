using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;

namespace SharpProof.Frontend.Test;

// Only an empty `[]` whose target class has a parameterless constructor
// stands for `new T()`.
public sealed class EmptyCollectionConstructorTests
{
    [TestCase("List<int> x = [];", "List")]
    [TestCase("HashSet<string> x = [];", "HashSet")]
    [TestCase("Dictionary<int, int> x = [];", "Dictionary")]
    [TestCase("List<int> x = [1];", null)]
    [TestCase("int[] x = [];", null)]
    [TestCase("IReadOnlyList<int> x = [];", null)]
    [TestCase("IList<int> x = [];", null)]
    [TestCase("List<int> x = new List<int>();", null)]
    public void OnlyEmptyClassTargetsAreConstructions(string declaration, string? owner)
    {
        System.ArgumentNullException.ThrowIfNull(declaration);
        var tree = CSharpSyntaxTree.ParseText("using System.Collections.Generic; public static class C { public static void M() { " + declaration + " } }");
        var compilation = CSharpCompilation.Create("EmptyCollections", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.That(compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var model = compilation.GetSemanticModel(tree);
        var value = tree.GetRoot().DescendantNodes().OfType<EqualsValueClauseSyntax>().Single().Value;
        var operation = model.GetOperation(value)!;
        var constructor = CSharpOperationSemantics.EmptyCollectionConstructor(operation.Parent is Microsoft.CodeAnalysis.Operations.IConversionOperation conversion ? conversion : operation);
        Assert.That(constructor?.ContainingType.Name, Is.EqualTo(owner));
        if (constructor != null)
        { Assert.That(constructor.Parameters, Is.Empty); }
    }
}

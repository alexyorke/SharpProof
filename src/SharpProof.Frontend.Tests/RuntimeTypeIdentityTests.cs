using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class RuntimeTypeIdentityTests
{
    [TestCase("int", "int", true)]
    [TestCase("int", "uint", false)]
    [TestCase("Box<dynamic>", "Box<object>", true)]
    [TestCase("Box<(int x, int y)>", "Box<(int a, int b)>", true)]
    [TestCase("Box<(int x, int y)>", "Box<System.ValueTuple<int, int>>", true)]
    [TestCase("Box<string?>", "Box<string>", true)]
    [TestCase("Box<nint>", "Box<System.IntPtr>", true)]
    [TestCase("Box<T>", "Box<U>", false)]
    [TestCase("Box<string[]>", "Box<object[]>", false)]
    [TestCase("int[,]", "int[]", false)]
    [TestCase("Box<(int x, int y)[]>", "Box<(int a, int b)[]>", true)]
    [TestCase("Outer<int>.Inner<string>", "Outer<long>.Inner<string>", false)]
    public void RuntimeTypeInterningErasesSpellingWithoutErasingDistinctTypes(string first, string second, bool same)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        var (left, right) = Types(first, second);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        Assert.That(CompilerIdentityBridge.SameRuntimeType(left, right), Is.EqualTo(same));
        var identity = CompilerIdentityBridge.InternType(factory, left);
        Assert.That(CompilerIdentityBridge.InternSymbol(factory, left), Is.EqualTo(identity));
        Assert.That(CompilerIdentityBridge.InternType(factory, right) == identity, Is.EqualTo(same));
    }

    [TestCase(70, false)]
    [TestCase(12, true)]
    public void IncompleteRuntimeComparisonCannotCertifyDisjointness(int depth, bool binary)
    {
        var first = "(int x, int y)";
        var second = "(int a, int b)";
        for (var index = 0; index < depth; index++)
        {
            first = binary ? "Pair<" + first + "," + first + ">" : "Box<" + first + ">";
            second = binary ? "Pair<" + second + "," + second + ">" : "Box<" + second + ">";
        }
        var (left, right) = Types(first, second);
        Assert.That(CompilerIdentityBridge.SameRuntimeType(left, right), Is.True);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        Assert.That(CompilerIdentityBridge.InternType(factory, left), Is.Not.EqualTo(CompilerIdentityBridge.InternType(factory, right)));
    }

    private static (ITypeSymbol First, ITypeSymbol Second) Types(string first, string second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        var tree = CSharpSyntaxTree.ParseText("public class Box<T> { } public class Pair<T, U> { } public class Outer<T> { public class Inner<U> { } } " +
            "public class Subject<T, U> { public " + first + " First; public " + second + " Second; }");
        var compilation = CSharpCompilation.Create("RuntimeIdentity", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new ArgumentNullException(nameof(diagnostic)) : diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var fields = tree.GetRoot().DescendantNodes().OfType<FieldDeclarationSyntax>().ToArray();
        var model = compilation.GetSemanticModel(tree);
        return (model.GetTypeInfo(fields[0].Declaration.Type).Type!, model.GetTypeInfo(fields[1].Declaration.Type).Type!);
    }
}

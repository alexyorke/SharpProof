using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.CompilerArtifact;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class AnalyzerCorePointerArrayTypeFormattingTests
{
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void ActualPointerArrayArgumentsRetainWholeTypeFormatting(int pointerDepth)
    {
        var element = "Leaf" + new string('*', pointerDepth);
        var source = "public struct Leaf { public int Value; } " +
            "public sealed class Failure<T> : System.Exception { } " +
            "public static unsafe class Fixture { public static void Target(Failure<" + element + "[]> value) { } }";
        var compilation = AnalyzerTestHost.CreateCompilation(source, []);
        compilation = compilation.WithOptions(compilation.Options.WithAllowUnsafe(true));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Diagnostics.Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        Assert.That(emitted.Success, Is.True);
        Assert.That(image.Length, Is.GreaterThan(0));

        var tree = compilation.SyntaxTrees.Single();
        var declaration = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var method = (IMethodSymbol)compilation.GetSemanticModel(tree).GetDeclaredSymbol(declaration)!;
        Assert.That(SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, compilation.Assembly), Is.True);
        var failure = method.Parameters.Single().Type as INamedTypeSymbol;
        Assert.That(failure, Is.Not.Null);
        Assert.That(SymbolEqualityComparer.Default.Equals(failure!.ContainingAssembly, compilation.Assembly), Is.True);
        Assert.That(failure.TypeArguments, Has.Length.EqualTo(1));
        var array = failure.TypeArguments.Single() as IArrayTypeSymbol;
        Assert.That(array, Is.Not.Null);
        Assert.That(array!.IsSZArray, Is.True);
        Assert.That(array.Rank, Is.EqualTo(1));
        ITypeSymbol leaf = array.ElementType;
        for (var depth = 0; depth < pointerDepth; depth++)
        {
            Assert.That(leaf, Is.AssignableTo<IPointerTypeSymbol>());
            Assert.That(leaf.TypeKind, Is.EqualTo(TypeKind.Pointer));
            leaf = ((IPointerTypeSymbol)leaf).PointedAtType;
        }
        Assert.That(leaf, Is.AssignableTo<INamedTypeSymbol>());
        Assert.That(leaf.TypeKind, Is.EqualTo(TypeKind.Struct));
        Assert.That(leaf.Name, Is.EqualTo("Leaf"));
        Assert.That(SymbolEqualityComparer.Default.Equals(leaf.ContainingAssembly, compilation.Assembly), Is.True);
        Assert.That(leaf.DeclaringSyntaxReferences, Has.Length.EqualTo(1));

        var reference = DocumentationCommentId.CreateReferenceId(failure);
        Assert.That(reference, Is.Not.Null.And.Not.Empty);
        var assembly = compilation.Assembly.Identity.ToString();
        var expected = assembly + "::" + reference + "::generic-argument-assemblies[" +
            assembly.Length.ToString(CultureInfo.InvariantCulture) + ":" + assembly + "]";
        var encoded = CompilerExceptionTypeIdentity.Encode(failure);
        Assert.That(encoded, Is.EqualTo(expected));

        var baseException = compilation.GetTypeByMetadataName("System.Exception")!;
        var baseDisplay = baseException.ContainingAssembly.Identity + "::" + DocumentationCommentId.CreateReferenceId(baseException);
        var objectType = compilation.GetSpecialType(SpecialType.System_Object);
        var objectDisplay = objectType.ContainingAssembly.Identity + "::" + DocumentationCommentId.CreateReferenceId(objectType);
        Assert.That(SymbolEqualityComparer.Default.Equals(failure.BaseType, baseException), Is.True);
        Assert.That(CompilerExceptionTypeIdentity.Encode(baseException), Is.EqualTo(baseDisplay));
        string[] expectedHierarchy = [expected, baseDisplay, objectDisplay];
        Array.Sort(expectedHierarchy, StringComparer.Ordinal);
        Assert.That(CompilerExceptionTypeIdentity.EncodeHierarchy(failure), Is.EqualTo(expectedHierarchy));
    }
}

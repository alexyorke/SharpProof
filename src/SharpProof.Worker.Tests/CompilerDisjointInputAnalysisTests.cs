using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.CompilerArtifact;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class CompilerDisjointInputAnalysisTests
{
    [TestCase(0, 0)]
    [TestCase(2, 1)]
    [TestCase(46, 1024)]
    [TestCase(129, 0)]
    public void RealEmittedSiblingTypesRetainOnlyBoundedImpossibleAliases(int count, int expectedPairs)
    {
        var declarations = string.Concat(Enumerable.Range(0, count).Select(index => $"public sealed class A{index} {{ }} "));
        var parameters = string.Join(", ", Enumerable.Range(0, count).Select(index => $"A{index} p{index}"));
        var source = declarations + $"public static class Subject {{ public static int Root({parameters}) => {count}; }}";
        var (compilation, method, image) = Compile(source);
        Assert.That(method.Parameters, Has.Length.EqualTo(count));
        foreach (var parameter in method.Parameters)
        {
            Assert.That(parameter.Type, Is.InstanceOf<INamedTypeSymbol>());
            Assert.That(parameter.Type.IsSealed, Is.True);
            Assert.That(SymbolEqualityComparer.Default.Equals(parameter.Type.ContainingAssembly, compilation.Assembly), Is.True);
        }
        var runtime = new System.Runtime.Loader.AssemblyLoadContext("OwnedDisjointInputs", isCollectible: true);
        try
        {
            using var stream = new MemoryStream(image.ToArray());
            var root = runtime.LoadFromStream(stream).GetType("Subject")!.GetMethod("Root")!;
            Assert.That(root.GetParameters(), Has.Length.EqualTo(count));
            Assert.That(root.Invoke(null, new object?[count]), Is.EqualTo(count));
        }
        finally { runtime.Unload(); }

        var pairs = CompilerDisjointInputAnalysis.Collect(method, TestContext.CurrentContext.CancellationToken);
        Assert.That(pairs.Length, Is.LessThanOrEqualTo(CompilerArtifactLimits.MaximumInstructions));
        var expected = (from left in Enumerable.Range(0, count)
                        from right in Enumerable.Range(left + 1, count - left - 1)
                        select (left, right)).Take(expectedPairs).ToImmutableArray();
        Assert.That(pairs.Select(pair => pair is null ? throw new System.ArgumentNullException(nameof(pair)) : ((pair.Left, pair.Right))), Is.EqualTo(expected));
        foreach (var pair in pairs)
        {
            Assert.That(compilation.ClassifyConversion(method.Parameters[pair.Left].Type, method.Parameters[pair.Right].Type).Exists, Is.False);
            Assert.That(compilation.ClassifyConversion(method.Parameters[pair.Right].Type, method.Parameters[pair.Left].Type).Exists, Is.False);
        }
    }

    [Test]
    public void RelatedInputsRemainPossibleAliasesAndScalarsDoNotReceiveReferenceCertificates()
    {
        var (compilation, method, image) = Compile("public sealed class A { } public sealed class B { } " +
            "public static class Subject { public static int Root(A first, A repeated, B different, int scalar) " +
            "=> object.ReferenceEquals(first, repeated) ? 1 : 0; }");
        Assert.That(compilation.ClassifyConversion(method.Parameters[0].Type, method.Parameters[1].Type).IsIdentity, Is.True);
        var runtime = new System.Runtime.Loader.AssemblyLoadContext("RelatedDisjointInputs", isCollectible: true);
        try
        {
            using var stream = new MemoryStream(image.ToArray());
            var assembly = runtime.LoadFromStream(stream);
            var first = Activator.CreateInstance(assembly.GetType("A")!);
            var second = Activator.CreateInstance(assembly.GetType("A")!);
            var different = Activator.CreateInstance(assembly.GetType("B")!);
            var root = assembly.GetType("Subject")!.GetMethod("Root")!;
            Assert.That(root.Invoke(null, [first, first, different, 3]), Is.EqualTo(1));
            Assert.That(root.Invoke(null, [first, second, different, 3]), Is.EqualTo(0));
        }
        finally { runtime.Unload(); }
        var pairs = CompilerDisjointInputAnalysis.Collect(method, TestContext.CurrentContext.CancellationToken);
        Assert.That(pairs.Select(pair => pair is null ? throw new System.ArgumentNullException(nameof(pair)) : ((pair.Left, pair.Right))), Is.EqualTo(ImmutableArray.Create((0, 2), (1, 2))));
        foreach (var pair in pairs)
        {
            Assert.That(compilation.ClassifyConversion(method.Parameters[pair.Left].Type, method.Parameters[pair.Right].Type).Exists, Is.False);
        }
    }

    [Test]
    public void RealNonemptyInputAnalysisPreservesCancellation()
    {
        var (_, method, _) = Compile("public sealed class A { } public sealed class B { } " +
            "public static class Subject { public static int Root(A first, B second) => 1; }");
        Assert.Throws<OperationCanceledException>((Action)(() => CompilerDisjointInputAnalysis.Collect(method, new CancellationToken(true))));
        Assert.That(CompilerDisjointInputAnalysis.Collect(method, TestContext.CurrentContext.CancellationToken), Has.Length.EqualTo(1));
    }

    private static (CSharpCompilation Compilation, IMethodSymbol Method, ImmutableArray<byte> Image) Compile(string source)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        var compilation = TestCompilation.Create("OwnedDisjointInputAnalysis", ("disjoint-analysis.cs", source));
        TestCompilation.AssertNoErrors(compilation);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        var tree = compilation.SyntaxTrees.Single();
        var declaration = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var method = (IMethodSymbol)compilation.GetSemanticModel(tree).GetDeclaredSymbol(declaration)!;
        return (compilation, method, ImmutableArray.Create<byte>(image.GetBuffer().AsSpan(0, checked((int)image.Length))));
    }
}

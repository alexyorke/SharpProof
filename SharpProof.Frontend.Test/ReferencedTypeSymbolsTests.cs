using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class ReferencedTypeSymbolsTests
{
    private static readonly string[] ExpectedTraversalOrder =
    [
        "ConsumerAssembly:ConsumerRoot",
        "ConsumerAssembly:ConsumerRoot.Nested",
        "ConsumerAssembly:Consumer.First.Outer",
        "ConsumerAssembly:Consumer.First.Outer.Nested",
        "ConsumerAssembly:Consumer.First.Inner.Leaf",
        "ConsumerAssembly:Consumer.Second.Last",
        "ReferencedAssembly:ReferencedRoot",
        "ReferencedAssembly:ReferencedRoot.Nested",
        "ReferencedAssembly:Referenced.First.Outer",
        "ReferencedAssembly:Referenced.First.Outer.Nested",
        "ReferencedAssembly:Referenced.First.Inner.Leaf",
        "ReferencedAssembly:Referenced.Second.Last"
    ];

    [Test]
    public void PreCanceledTraversalStopsInTypeFreeNamespaceTree()
    {
        var compilation = CreateCompilation(
            "Cancellation",
            "namespace A.B.C.D { }");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var exception = Assert.Throws<OperationCanceledException>(
            (Action)(() => ReferencedTypeSymbols.GetAll(
                compilation,
                cancellation.Token).ToArray()));

        Assert.That(
            exception!.CancellationToken,
            Is.EqualTo(cancellation.Token));
    }

    [Test]
    public void CancellationAfterTypeStopsBeforeTypeFreeNamespaceDescent()
    {
        var compilation = CreateCompilation(
            "CancellationDuringTraversal",
            """
            namespace A
            {
                internal sealed class Marker { }
                namespace B.C.D { }
            }
            """);
        using var cancellation = new CancellationTokenSource();
        using var types = ReferencedTypeSymbols.GetAll(
            compilation,
            cancellation.Token).GetEnumerator();
        Assert.That(types.MoveNext(), Is.True);
        Assert.That(types.Current.Name, Is.EqualTo("Marker"));
        cancellation.Cancel();

        var exception = Assert.Throws<OperationCanceledException>(
            (Action)(() => types.MoveNext()));

        Assert.That(
            exception!.CancellationToken,
            Is.EqualTo(cancellation.Token));
    }

    [Test]
    public void DeepTypeFreeNamespaceTraversalIsStackSafe()
    {
        const int namespaceDepth = 4_096;
        var namespaceName = string.Join(
            ".",
            Enumerable.Range(0, namespaceDepth)
                .Select(static index =>
                    "N" + index.ToString(CultureInfo.InvariantCulture)));
        var compilation = CreateCompilation(
            "DeepNamespaces",
            "namespace " + namespaceName + " { }");

        var types = ReferencedTypeSymbols.GetAll(compilation).ToArray();

        Assert.That(types, Is.Empty);
    }

    [Test]
    public void TraversalPreservesDepthFirstTypeAndAssemblyOrder()
    {
        var referenced = CreateCompilation(
            "ReferencedAssembly",
            """
            namespace Referenced.First
            {
                internal sealed class Outer
                {
                    internal sealed class Nested { }
                }

                namespace Inner
                {
                    internal sealed class Leaf { }
                }
            }

            namespace Referenced.Second
            {
                internal sealed class Last { }
            }

            internal sealed class ReferencedRoot
            {
                internal sealed class Nested { }
            }
            """);
        var compilation = CreateCompilation(
            "ConsumerAssembly",
            """
            namespace Consumer.First
            {
                internal sealed class Outer
                {
                    internal sealed class Nested { }
                }

                namespace Inner
                {
                    internal sealed class Leaf { }
                }
            }

            namespace Consumer.Second
            {
                internal sealed class Last { }
            }

            internal sealed class ConsumerRoot
            {
                internal sealed class Nested { }
            }
            """,
            referenced.ToMetadataReference());

        var identities = ReferencedTypeSymbols.GetAll(compilation)
            .Select(static type =>
                type.ContainingAssembly.Name + ":" + type.ToDisplayString())
            .ToArray();

        Assert.That(
            identities,
            Is.EqualTo(ExpectedTraversalOrder));
    }

    private static CSharpCompilation CreateCompilation(
        string assemblyName,
        string source,
        params MetadataReference[] references)
    {
        return CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source)],
            references,
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
    }
}

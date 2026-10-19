using SharpProof.Ir;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.VisualBasic;
using NUnit.Framework;
using SharpProof.CompilerArtifact;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class ClaimIdentitySymbolOwnershipTests
{
    private const string CSharpTypes = """
        public class Outer<T> {
            public class Middle<U> {
                public class Leaf<V> { }
                public class Sibling<V> { }
            }
        }
        public class Other<T> {
            public class Middle<U> { public class Leaf<V> { } }
        }
        """;

    private const string VisualBasicTypes = """
        Public Class Outer(Of T)
            Public Class Middle(Of U)
                Public Class Leaf(Of V)
                End Class
                Public Class Sibling(Of V)
                End Class
            End Class
        End Class
        Public Class Other(Of T)
            Public Class Middle(Of U)
                Public Class Leaf(Of V)
                End Class
            End Class
        End Class
        """;

    private const string CSharpMethods = """
        public partial class HostContainer {
            private partial void Host<T>();
            private partial void Host<T>() {
                void Parent<U>() {
                    void Leaf<V>() { }
                    void Sibling<V>() { }
                }
            }
        }
        """;

    private const string VisualBasicMethods = """
        Public Class HostContainer
            Public Shared Sub Host(Of T)()
            End Sub
            Public Shared Sub Other(Of U)()
            End Sub
        End Class
        """;

    [TestCase("CSharp", "self", true, 0)]
    [TestCase("CSharp", "parent", true, 1)]
    [TestCase("CSharp", "outer", true, 2)]
    [TestCase("CSharp", "sibling", false, 3)]
    [TestCase("CSharp", "descendant", false, 1)]
    [TestCase("CSharp", "unrelated", false, 3)]
    [TestCase("CSharp", "constructed-original", true, 0)]
    [TestCase("VisualBasic", "self", true, 0)]
    [TestCase("VisualBasic", "parent", true, 1)]
    [TestCase("VisualBasic", "outer", true, 2)]
    [TestCase("VisualBasic", "sibling", false, 3)]
    [TestCase("VisualBasic", "descendant", false, 1)]
    [TestCase("VisualBasic", "unrelated", false, 3)]
    [TestCase("VisualBasic", "constructed-original", true, 0)]
    public void TypeParameterOwnerDepthUsesSemanticDefinitions(string language, string relation, bool expected, int expectedDepth)
    {
        System.ArgumentNullException.ThrowIfNull(relation);
        System.ArgumentNullException.ThrowIfNull(language);
        var compilation = CreateCompilation(language, language == "CSharp" ? CSharpTypes : VisualBasicTypes);
        var outer = TypeOwner(compilation.GetTypeByMetadataName("Outer`1")!);
        var middle = TypeOwner(compilation.GetTypeByMetadataName("Outer`1+Middle`1")!);
        var leaf = TypeOwner(compilation.GetTypeByMetadataName("Outer`1+Middle`1+Leaf`1")!);
        var sibling = TypeOwner(compilation.GetTypeByMetadataName("Outer`1+Middle`1+Sibling`1")!);
        var unrelated = TypeOwner(compilation.GetTypeByMetadataName("Other`1+Middle`1+Leaf`1")!);
        var source = relation == "descendant" ? outer : leaf;
        var candidate = relation switch
        {
            "parent" => middle,
            "outer" => outer,
            "sibling" => sibling,
            "unrelated" => unrelated,
            _ => leaf
        };
        if (relation == "constructed-original")
        {
            source = leaf.Construct(compilation.GetSpecialType(SpecialType.System_Int32));
            Assert.That(source.IsDefinition, Is.False);
            Assert.That(source.TypeArguments.Single().SpecialType, Is.EqualTo(SpecialType.System_Int32));
            Assert.That(SymbolEqualityComparer.Default.Equals(source.OriginalDefinition, leaf), Is.True);
        }
        if (relation == "unrelated")
        {
            Assert.That(candidate.Name, Is.EqualTo(source.Name));
            Assert.That(SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, source.OriginalDefinition), Is.False);
        }

        var actual = ClaimIdentitySymbolOwnership.TryGetContainingTypeDepth(source, candidate, out var depth);
        Assert.That(actual, Is.EqualTo(expected));
        Assert.That(depth, Is.EqualTo(expectedDepth));
    }

    [TestCase("parent-original", true, 1)]
    [TestCase("parent-constructed", true, 1)]
    [TestCase("partial-definition", true, 2)]
    [TestCase("partial-implementation", true, 2)]
    [TestCase("self", false, 3)]
    [TestCase("sibling", false, 3)]
    public void LocalMethodOwnerDepthPreservesPartialAndGenericNormalization(string relation, bool expected, int expectedDepth)
    {
        System.ArgumentNullException.ThrowIfNull(relation);
        var compilation = CreateCompilation("CSharp", CSharpMethods);
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        var locals = ImmutableDictionaryCapture.Create(tree.GetRoot().DescendantNodes().OfType<LocalFunctionStatementSyntax>(), syntax => syntax is null ? throw new System.ArgumentNullException(nameof(syntax)) : (syntax.Identifier.ValueText), syntax => syntax is null ? throw new System.ArgumentNullException(nameof(syntax)) : (MethodOwner((IMethodSymbol)model.GetDeclaredSymbol(syntax)!)));
        var source = locals["Leaf"];
        var parent = locals["Parent"];
        var definition = MethodOwner(compilation.GetTypeByMetadataName("HostContainer")!.GetMembers("Host")
            .OfType<IMethodSymbol>().Single(method => method.PartialImplementationPart != null));
        var implementation = definition.PartialImplementationPart!;
        Assert.That(SymbolEqualityComparer.Default.Equals(source.ContainingSymbol, parent), Is.True);
        Assert.That(SymbolEqualityComparer.Default.Equals(parent.ContainingSymbol, implementation), Is.True);
        Assert.That(SymbolEqualityComparer.Default.Equals(definition, implementation), Is.False);
        var candidate = relation switch
        {
            "partial-definition" => definition,
            "partial-implementation" => MethodOwner(implementation),
            "self" => source,
            "sibling" => locals["Sibling"],
            _ => parent
        };
        if (relation == "parent-constructed")
        {
            candidate = parent.Construct(compilation.GetSpecialType(SpecialType.System_Int32));
            Assert.That(candidate.IsGenericMethod, Is.True);
            Assert.That(candidate.IsDefinition, Is.False);
            Assert.That(candidate.TypeArguments.Single().SpecialType, Is.EqualTo(SpecialType.System_Int32));
            Assert.That(SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, parent), Is.True);
        }

        var actual = ClaimIdentitySymbolOwnership.TryGetContainingMethodDepth(source, candidate, out var depth);
        Assert.That(actual, Is.EqualTo(expected));
        Assert.That(depth, Is.EqualTo(expectedDepth));
    }

    [TestCase("self")]
    [TestCase("unrelated")]
    public void VisualBasicOrdinaryMethodsHaveNoMethodAncestor(string relation)
    {
        System.ArgumentNullException.ThrowIfNull(relation);
        var compilation = CreateCompilation("VisualBasic", VisualBasicMethods);
        var type = compilation.GetTypeByMetadataName("HostContainer")!;
        var source = MethodOwner(type.GetMembers("Host").OfType<IMethodSymbol>().Single());
        var candidate = relation == "self" ? source : MethodOwner(type.GetMembers("Other").OfType<IMethodSymbol>().Single());
        Assert.That(source.ContainingSymbol, Is.InstanceOf<INamedTypeSymbol>());
        Assert.That(ClaimIdentitySymbolOwnership.TryGetContainingMethodDepth(source, candidate, out var depth), Is.False);
        Assert.That(depth, Is.EqualTo(1));
    }

    private static INamedTypeSymbol TypeOwner(INamedTypeSymbol type)
    {
        System.ArgumentNullException.ThrowIfNull(type);
        var parameter = type.TypeParameters.Single();
        Assert.That(parameter.TypeParameterKind, Is.EqualTo(TypeParameterKind.Type));
        Assert.That(SymbolEqualityComparer.Default.Equals(parameter.ContainingSymbol, type), Is.True);
        return (INamedTypeSymbol)parameter.ContainingSymbol;
    }

    private static IMethodSymbol MethodOwner(IMethodSymbol method)
    {
        System.ArgumentNullException.ThrowIfNull(method);
        var parameter = method.TypeParameters.Single();
        Assert.That(parameter.TypeParameterKind, Is.EqualTo(TypeParameterKind.Method));
        Assert.That(SymbolEqualityComparer.Default.Equals(parameter.ContainingSymbol, method), Is.True);
        return (IMethodSymbol)parameter.ContainingSymbol;
    }

    private static Compilation CreateCompilation(string language, string source)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        System.ArgumentNullException.ThrowIfNull(language);
        Compilation compilation = language == "CSharp"
            ? CSharpCompilation.Create("ClaimIdentityOwnership", [CSharpSyntaxTree.ParseText(source,
                new CSharpParseOptions(Microsoft.CodeAnalysis.CSharp.LanguageVersion.CSharp12))], TestMetadataReferences.Platform,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release))
            : VisualBasicCompilation.Create("ClaimIdentityOwnership", [VisualBasicSyntaxTree.ParseText(source)], TestMetadataReferences.Platform,
                new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        return compilation;
    }
}

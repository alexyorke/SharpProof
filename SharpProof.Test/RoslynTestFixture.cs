using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.Test;

internal static class RoslynTestFixture
{
    private static readonly MetadataReference ObjectReference =
        MetadataReference.CreateFromFile(typeof(object).Assembly.Location);

    internal static CompilationFixture CreateCompilation(
        string source,
        string assemblyName,
        IEnumerable<MetadataReference>? references = null,
        CSharpParseOptions? parseOptions = null,
        CSharpCompilationOptions? compilationOptions = null)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);
        return CreateCompilation(syntaxTree, assemblyName, references, compilationOptions);
    }

    internal static CompilationFixture CreateCompilation(
        SyntaxTree syntaxTree,
        string assemblyName,
        IEnumerable<MetadataReference>? references = null,
        CSharpCompilationOptions? compilationOptions = null)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { syntaxTree },
            references ?? new[] { ObjectReference },
            compilationOptions ?? new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        return new CompilationFixture(
            syntaxTree,
            compilation,
            compilation.GetSemanticModel(syntaxTree),
            syntaxTree.GetCompilationUnitRoot());
    }

    internal static NodeFixture<TNode> CreateSingleNode<TNode>(
        string source,
        string assemblyName,
        IEnumerable<MetadataReference>? references = null,
        CSharpParseOptions? parseOptions = null,
        CSharpCompilationOptions? compilationOptions = null,
        Func<IEnumerable<TNode>, TNode>? selectNode = null)
        where TNode : SyntaxNode
    {
        var compilation = CreateCompilation(
            source,
            assemblyName,
            references,
            parseOptions,
            compilationOptions);
        var candidates = compilation.Root.DescendantNodesAndSelf().OfType<TNode>();
        var node = selectNode == null ? candidates.Single() : selectNode(candidates);
        return new NodeFixture<TNode>(compilation, node);
    }

    internal readonly record struct CompilationFixture(
        SyntaxTree SyntaxTree,
        CSharpCompilation Compilation,
        SemanticModel SemanticModel,
        CompilationUnitSyntax Root);

    internal readonly record struct NodeFixture<TNode>(
        CompilationFixture Fixture,
        TNode Node)
        where TNode : SyntaxNode
    {
        internal SyntaxTree SyntaxTree => Fixture.SyntaxTree;

        internal CSharpCompilation Compilation => Fixture.Compilation;

        internal SemanticModel SemanticModel => Fixture.SemanticModel;

        internal CompilationUnitSyntax Root => Fixture.Root;
    }
}

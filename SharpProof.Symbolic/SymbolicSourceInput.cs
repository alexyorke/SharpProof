using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace SharpProof.Symbolic;

internal sealed class SymbolicSourceInput
{
    internal const string DefaultFilePath = "SharpProof.Symbolic.Query.cs";

    private SymbolicSourceInput(
        SymbolicSourceInputKind kind,
        string? filePath = null,
        string? sourceText = null,
        SyntaxTree? syntaxTree = null,
        Compilation? compilation = null,
        SyntaxNode? node = null,
        SemanticModel? semanticModel = null,
        SymbolicSourceCompilationProfile? compilationProfile = null,
        SymbolicSourceMap? sourceMap = null)
    {
        Kind = kind;
        FilePath = filePath;
        SourceText = sourceText;
        SyntaxTree = syntaxTree;
        Compilation = compilation;
        Node = node;
        SemanticModel = semanticModel;
        CompilationProfile = compilationProfile;
        SourceMap = sourceMap;
    }

    public SymbolicSourceInputKind Kind { get; }

    public string? FilePath { get; }

    public string? SourceText { get; }

    public SyntaxTree? SyntaxTree { get; }

    public Compilation? Compilation { get; }

    public SyntaxNode? Node { get; }

    public SemanticModel? SemanticModel { get; }

    public SymbolicSourceCompilationProfile? CompilationProfile { get; }

    public SymbolicSourceMap? SourceMap { get; }

    public static SymbolicSourceInput FromFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path is required.", nameof(filePath));

        return FromFile(filePath, SymbolicSourceCompilationProfile.Default);
    }

    public static SymbolicSourceInput FromFile(
        string filePath,
        SymbolicSourceCompilationProfile compilationProfile)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path is required.", nameof(filePath));

        return new SymbolicSourceInput(
            SymbolicSourceInputKind.File,
            filePath,
            compilationProfile: compilationProfile ??
                                throw new ArgumentNullException(nameof(compilationProfile)));
    }

    public static SymbolicSourceInput FromText(string sourceText, string? filePath = null)
    {
        if (sourceText == null) throw new ArgumentNullException(nameof(sourceText));

        return FromTextWithProfile(sourceText, SymbolicSourceCompilationProfile.Default, filePath);
    }

    public static SymbolicSourceInput FromTextWithProfile(
        string sourceText,
        SymbolicSourceCompilationProfile compilationProfile,
        string? filePath = null)
    {
        if (sourceText == null) throw new ArgumentNullException(nameof(sourceText));

        return new SymbolicSourceInput(
            SymbolicSourceInputKind.Text,
            string.IsNullOrWhiteSpace(filePath) ? DefaultFilePath : filePath,
            sourceText,
            compilationProfile: compilationProfile ??
                                throw new ArgumentNullException(nameof(compilationProfile)));
    }

    public static SymbolicSourceInput FromSyntaxTree(SyntaxTree syntaxTree, Compilation compilation)
    {
        return new SymbolicSourceInput(
            SymbolicSourceInputKind.SyntaxTree,
            syntaxTree?.FilePath,
            syntaxTree: syntaxTree ?? throw new ArgumentNullException(nameof(syntaxTree)),
            compilation: compilation ?? throw new ArgumentNullException(nameof(compilation)));
    }

    public static SymbolicSourceInput FromNode(SyntaxNode node, SemanticModel semanticModel)
    {
        return new SymbolicSourceInput(
            SymbolicSourceInputKind.Node,
            node?.SyntaxTree.FilePath,
            node: node ?? throw new ArgumentNullException(nameof(node)),
            semanticModel: semanticModel ?? throw new ArgumentNullException(nameof(semanticModel)));
    }

    public SymbolicSourceInput WithSourceMap(SymbolicSourceMap sourceMap)
    {
        return new SymbolicSourceInput(
            Kind,
            FilePath,
            SourceText,
            SyntaxTree,
            Compilation,
            Node,
            SemanticModel,
            CompilationProfile,
            sourceMap ?? throw new ArgumentNullException(nameof(sourceMap)));
    }
}

internal enum SymbolicSourceInputKind
{
    File,
    Text,
    SyntaxTree,
    Node
}

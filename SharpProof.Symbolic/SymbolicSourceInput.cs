namespace SharpProof.Symbolic;

internal sealed record SymbolicSourceInput(
    SymbolicSourceInputKind Kind,
    string? FilePath = null,
    string? SourceText = null,
    SyntaxTree? SyntaxTree = null,
    Compilation? Compilation = null,
    SyntaxNode? Node = null,
    SemanticModel? SemanticModel = null,
    SymbolicSourceCompilationProfile? CompilationProfile = null,
    SymbolicSourceMap? SourceMap = null) {
    internal const string DefaultFilePath = "SharpProof.Symbolic.Query.cs";

    public static SymbolicSourceInput FromFile(string filePath) {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path is required.", nameof(filePath));

        return FromFile(filePath, SymbolicSourceCompilationProfile.Default);
    }

    public static SymbolicSourceInput FromFile(
        string filePath,
        SymbolicSourceCompilationProfile compilationProfile) {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path is required.", nameof(filePath));

        return new SymbolicSourceInput(
            SymbolicSourceInputKind.File,
            filePath,
            CompilationProfile: compilationProfile ??
                                throw new ArgumentNullException(nameof(compilationProfile)));
    }

    public static SymbolicSourceInput FromText(string sourceText, string? filePath = null) {
        if (sourceText == null) throw new ArgumentNullException(nameof(sourceText));

        return FromTextWithProfile(sourceText, SymbolicSourceCompilationProfile.Default, filePath);
    }

    public static SymbolicSourceInput FromTextWithProfile(
        string sourceText,
        SymbolicSourceCompilationProfile compilationProfile,
        string? filePath = null) {
        if (sourceText == null) throw new ArgumentNullException(nameof(sourceText));

        return new SymbolicSourceInput(
            SymbolicSourceInputKind.Text,
            string.IsNullOrWhiteSpace(filePath) ? DefaultFilePath : filePath,
            sourceText,
            CompilationProfile: compilationProfile ??
                                throw new ArgumentNullException(nameof(compilationProfile)));
    }

    public static SymbolicSourceInput FromSyntaxTree(SyntaxTree syntaxTree, Compilation compilation) {
        return new SymbolicSourceInput(
            SymbolicSourceInputKind.SyntaxTree,
            syntaxTree?.FilePath,
            SyntaxTree: syntaxTree ?? throw new ArgumentNullException(nameof(syntaxTree)),
            Compilation: compilation ?? throw new ArgumentNullException(nameof(compilation)));
    }

    public static SymbolicSourceInput FromNode(SyntaxNode node, SemanticModel semanticModel) {
        return new SymbolicSourceInput(
            SymbolicSourceInputKind.Node,
            node?.SyntaxTree.FilePath,
            Node: node ?? throw new ArgumentNullException(nameof(node)),
            SemanticModel: semanticModel ?? throw new ArgumentNullException(nameof(semanticModel)));
    }

    public SymbolicSourceInput WithSourceMap(SymbolicSourceMap sourceMap) =>
        this with { SourceMap = sourceMap ?? throw new ArgumentNullException(nameof(sourceMap)) };
}

internal enum SymbolicSourceInputKind {
    File,
    Text,
    SyntaxTree,
    Node
}

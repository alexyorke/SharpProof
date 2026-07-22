namespace SharpProof.Symbolic;
internal sealed record SymbolicSourceInput(SyntaxTree SyntaxTree, Compilation Compilation) {
    internal static SymbolicSourceInput FromSyntaxTree(SyntaxTree syntaxTree, Compilation compilation) =>
        new(
            syntaxTree ?? throw new ArgumentNullException(nameof(syntaxTree)),
            compilation ?? throw new ArgumentNullException(nameof(compilation)));
}

using Microsoft.CodeAnalysis;

namespace SharpProof.Symbolic;

internal sealed class SymbolicMethodAnalysisInput
{
    private SymbolicMethodAnalysisInput(
        IMethodSymbol methodSymbol,
        SyntaxNode declaration,
        SemanticModel semanticModel)
    {
        MethodSymbol = methodSymbol;
        Declaration = declaration;
        SemanticModel = semanticModel;
        Source = SymbolicSourceInput.FromNode(declaration, semanticModel);
    }

    internal IMethodSymbol MethodSymbol { get; }

    internal SyntaxNode Declaration { get; }

    internal SemanticModel SemanticModel { get; }

    internal SymbolicSourceInput Source { get; }

    internal static SymbolicMethodAnalysisInput Create(
        IMethodSymbol methodSymbol,
        SyntaxNode declaration,
        SemanticModel semanticModel)
    {
        if (methodSymbol == null) throw new ArgumentNullException(nameof(methodSymbol));
        if (declaration == null) throw new ArgumentNullException(nameof(declaration));
        if (semanticModel == null) throw new ArgumentNullException(nameof(semanticModel));
        if (declaration.SyntaxTree != semanticModel.SyntaxTree)
            throw new ArgumentException(
                "The method declaration and semantic model must belong to the same syntax tree.",
                nameof(semanticModel));

        return new SymbolicMethodAnalysisInput(methodSymbol, declaration, semanticModel);
    }

    internal SymbolicQueryContext CreateNodeQuery(SymbolicQueryOptions? options = null) =>
        new(Source, SymbolicQueryTarget.Node(), options);
}

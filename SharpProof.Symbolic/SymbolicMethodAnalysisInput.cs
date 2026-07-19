namespace SharpProof.Symbolic;

internal sealed class SymbolicMethodAnalysisInput(
    IMethodSymbol methodSymbol,
    SyntaxNode declaration,
    SemanticModel semanticModel)
{
    internal IMethodSymbol MethodSymbol { get; } = methodSymbol;

    internal SyntaxNode Declaration { get; } = declaration;

    internal SemanticModel SemanticModel { get; } = semanticModel;

    internal SymbolicSourceInput Source { get; } = SymbolicSourceInput.FromNode(declaration, semanticModel);

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
        new(Source, SharpProofTarget.Node(), options);

    internal SymbolicOperationResult<SymbolicConditionProofResult> TryProveAtNode(
        SymbolicQueryExecutor queryExecutor,
        SyntaxNode node,
        string condition,
        SmtAnalysisService smtAnalysis,
        bool includeCurrentStatementCompletionFacts,
        CancellationToken cancellationToken)
    {
        ValidateNode(node);
        return queryExecutor.TryProveAtSyntaxNode(
            SemanticModel,
            node,
            condition,
            smtAnalysis,
            includeCurrentStatementCompletionFacts,
            cancellationToken);
    }

    internal SymbolicOperationResult<SymbolicConditionProofResult> TryProveAtNode(
        SymbolicQueryExecutor queryExecutor,
        SyntaxNode node,
        string condition,
        SymbolicCondition symbolicCondition,
        SymbolicState initialState,
        SmtAnalysisService smtAnalysis,
        bool includeCurrentStatementCompletionFacts,
        CancellationToken cancellationToken)
    {
        ValidateNode(node);
        return queryExecutor.TryProveAtSyntaxNode(
            SemanticModel,
            node,
            condition,
            symbolicCondition,
            initialState,
            smtAnalysis,
            includeCurrentStatementCompletionFacts,
            cancellationToken);
    }

    private void ValidateNode(SyntaxNode node)
    {
        if (node == null) throw new ArgumentNullException(nameof(node));
        if (node.SyntaxTree != Declaration.SyntaxTree)
            throw new ArgumentException(
                "The proof node must belong to the analyzed method syntax tree.",
                nameof(node));
    }
}

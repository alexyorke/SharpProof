namespace SharpProof.Analyzer;

internal sealed record MethodAnalysisSnapshot(
    IMethodSymbol MethodSymbol,
    SyntaxNode Declaration,
    SemanticModel SemanticModel,
    ImmutableArray<IOperation> OperationBlocks,
    IOperation? RootOperation,
    ImmutableArray<IOperation> VisibleOperations) {
    internal static MethodAnalysisSnapshot Create(
        IMethodSymbol methodSymbol,
        SyntaxNode declaration,
        SemanticModel semanticModel,
        ImmutableArray<IOperation> operationBlocks,
        CancellationToken cancellationToken) {
        if (methodSymbol == null) throw new ArgumentNullException(nameof(methodSymbol));
        if (declaration == null) throw new ArgumentNullException(nameof(declaration));
        if (semanticModel == null) throw new ArgumentNullException(nameof(semanticModel));
        if (declaration.SyntaxTree != semanticModel.SyntaxTree)
            throw new ArgumentException(
                "The method declaration and semantic model must belong to the same syntax tree.",
                nameof(semanticModel));
        cancellationToken.ThrowIfCancellationRequested();
        var blocks = operationBlocks.IsDefault ? [] : operationBlocks;
        var root = MethodBodyOperationResolver.GetMethodBodyRootOperation(declaration, semanticModel, cancellationToken) ??
                   SelectRootOperation(blocks);
        var visibleOperations = root == null
            ? ImmutableArray<IOperation>.Empty
            : [.. ExecutionVisibility.VisibleDescendants(root)];
        return new MethodAnalysisSnapshot(methodSymbol, declaration, semanticModel, blocks, root, visibleOperations);
    }
    private static IOperation? SelectRootOperation(ImmutableArray<IOperation> operationBlocks) {
        if (operationBlocks.IsDefaultOrEmpty) return null;

        return operationBlocks
            .OrderByDescending(static operation => operation.Syntax.Span.Length)
            .First();
    }
}

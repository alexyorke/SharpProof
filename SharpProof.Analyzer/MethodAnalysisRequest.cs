namespace SharpProof.Analyzer;

internal sealed class MethodAnalysisRequest(
    SymbolicMethodAnalysisInput symbolicInput,
    ImmutableArray<IOperation> operationBlocks,
    IOperation? fallbackRootOperation)
{
    internal SymbolicMethodAnalysisInput SymbolicInput { get; } = symbolicInput;

    internal ImmutableArray<IOperation> OperationBlocks { get; } = operationBlocks;

    internal IOperation? FallbackRootOperation { get; } = fallbackRootOperation;

    internal static MethodAnalysisRequest Create(
        IMethodSymbol methodSymbol,
        SyntaxNode declaration,
        SemanticModel semanticModel,
        ImmutableArray<IOperation> operationBlocks,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var blocks = operationBlocks.IsDefault ? ImmutableArray<IOperation>.Empty : operationBlocks;
        return new MethodAnalysisRequest(
            SymbolicMethodAnalysisInput.Create(methodSymbol, declaration, semanticModel),
            blocks,
            MethodBodyOperationResolver.GetMethodBodyRootOperation(
                declaration,
                semanticModel,
                cancellationToken));
    }
}

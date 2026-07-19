namespace SharpProof.Analyzer;

internal sealed record MethodAnalysisSnapshot(
    SymbolicMethodAnalysisInput Input,
    ImmutableArray<IOperation> OperationBlocks,
    IOperation? RootOperation,
    ImmutableArray<IOperation> VisibleOperations,
    MethodBodySemanticFacts SemanticFacts)
{
    internal IMethodSymbol MethodSymbol => Input.MethodSymbol;

    internal SyntaxNode Declaration => Input.Declaration;

    internal SemanticModel SemanticModel => Input.SemanticModel;

    internal SymbolicSourceInput Source => Input.Source;

    internal static MethodAnalysisSnapshot Create(MethodAnalysisRequest request)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        var blocks = request.OperationBlocks;
        var root = request.FallbackRootOperation ?? SelectRootOperation(blocks);
        var visibleOperations = root == null
            ? ImmutableArray<IOperation>.Empty
            : ExecutionVisibility.VisibleDescendants(root).ToImmutableArray();
        var semanticFacts = new MethodBodySemanticFacts(
            blocks.Length,
            visibleOperations.Length,
            visibleOperations.Count(static operation => operation is IReturnOperation),
            visibleOperations.Any(static operation => operation is ILocalFunctionOperation),
            root != null);
        return new MethodAnalysisSnapshot(
            request.SymbolicInput,
            blocks,
            root,
            visibleOperations,
            semanticFacts);
    }

    private static IOperation? SelectRootOperation(ImmutableArray<IOperation> operationBlocks)
    {
        if (operationBlocks.IsDefaultOrEmpty) return null;

        return operationBlocks
            .OrderByDescending(static operation => operation.Syntax.Span.Length)
            .First();
    }
}

internal sealed record MethodBodySemanticFacts(
    int OperationBlockCount,
    int VisibleOperationCount,
    int ReturnOperationCount,
    bool ContainsLocalFunction,
    bool HasRootOperation);

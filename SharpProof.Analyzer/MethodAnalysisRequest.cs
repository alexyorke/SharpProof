using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Symbolic;

namespace SharpProof.Analyzer;

internal sealed class MethodAnalysisRequest
{
    private MethodAnalysisRequest(
        SymbolicMethodAnalysisInput symbolicInput,
        ImmutableArray<IOperation> operationBlocks,
        IOperation? fallbackRootOperation)
    {
        SymbolicInput = symbolicInput;
        OperationBlocks = operationBlocks;
        FallbackRootOperation = fallbackRootOperation;
    }

    internal SymbolicMethodAnalysisInput SymbolicInput { get; }

    internal ImmutableArray<IOperation> OperationBlocks { get; }

    internal IOperation? FallbackRootOperation { get; }

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

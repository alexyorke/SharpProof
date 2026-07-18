using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Analyzer.Engine;
using SharpProof.Symbolic;

namespace SharpProof.Analyzer;

internal sealed class MethodAnalysisSnapshot
{
    private MethodAnalysisSnapshot(
        SymbolicMethodAnalysisInput input,
        ImmutableArray<IOperation> operationBlocks,
        IOperation? rootOperation,
        ImmutableArray<IOperation> visibleOperations,
        MethodBodySemanticFacts semanticFacts)
    {
        Input = input;
        OperationBlocks = operationBlocks;
        RootOperation = rootOperation;
        VisibleOperations = visibleOperations;
        SemanticFacts = semanticFacts;
    }

    internal SymbolicMethodAnalysisInput Input { get; }

    internal IMethodSymbol MethodSymbol => Input.MethodSymbol;

    internal SyntaxNode Declaration => Input.Declaration;

    internal SemanticModel SemanticModel => Input.SemanticModel;

    internal ImmutableArray<IOperation> OperationBlocks { get; }

    internal IOperation? RootOperation { get; }

    internal ImmutableArray<IOperation> VisibleOperations { get; }

    internal MethodBodySemanticFacts SemanticFacts { get; }

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

internal sealed class MethodBodySemanticFacts
{
    internal MethodBodySemanticFacts(
        int operationBlockCount,
        int visibleOperationCount,
        int returnOperationCount,
        bool containsLocalFunction,
        bool hasRootOperation)
    {
        OperationBlockCount = operationBlockCount;
        VisibleOperationCount = visibleOperationCount;
        ReturnOperationCount = returnOperationCount;
        ContainsLocalFunction = containsLocalFunction;
        HasRootOperation = hasRootOperation;
    }

    internal int OperationBlockCount { get; }

    internal int VisibleOperationCount { get; }

    internal int ReturnOperationCount { get; }

    internal bool ContainsLocalFunction { get; }

    internal bool HasRootOperation { get; }
}

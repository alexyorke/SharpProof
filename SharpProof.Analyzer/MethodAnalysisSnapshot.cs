using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Analyzer.Engine;
using SharpProof.Symbolic;

namespace SharpProof.Analyzer;

internal sealed class MethodAnalysisSnapshot
{
    private MethodAnalysisSnapshot(
        IMethodSymbol methodSymbol,
        SyntaxNode declaration,
        SemanticModel semanticModel,
        ImmutableArray<IOperation> operationBlocks,
        IOperation? rootOperation,
        ImmutableArray<IOperation> visibleOperations,
        MethodBodySemanticFacts semanticFacts,
        SymbolicSourceInput source)
    {
        MethodSymbol = methodSymbol;
        Declaration = declaration;
        SemanticModel = semanticModel;
        OperationBlocks = operationBlocks;
        RootOperation = rootOperation;
        VisibleOperations = visibleOperations;
        SemanticFacts = semanticFacts;
        Source = source;
    }

    internal IMethodSymbol MethodSymbol { get; }

    internal SyntaxNode Declaration { get; }

    internal SemanticModel SemanticModel { get; }

    internal ImmutableArray<IOperation> OperationBlocks { get; }

    internal IOperation? RootOperation { get; }

    internal ImmutableArray<IOperation> VisibleOperations { get; }

    internal MethodBodySemanticFacts SemanticFacts { get; }

    internal SymbolicSourceInput Source { get; }

    internal static MethodAnalysisSnapshot Create(
        IMethodSymbol methodSymbol,
        SyntaxNode declaration,
        SemanticModel semanticModel,
        ImmutableArray<IOperation> operationBlocks,
        IOperation? fallbackRootOperation)
    {
        if (methodSymbol == null) throw new ArgumentNullException(nameof(methodSymbol));
        if (declaration == null) throw new ArgumentNullException(nameof(declaration));
        if (semanticModel == null) throw new ArgumentNullException(nameof(semanticModel));

        var blocks = operationBlocks.IsDefault ? ImmutableArray<IOperation>.Empty : operationBlocks;
        var root = fallbackRootOperation ?? SelectRootOperation(blocks);
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
            methodSymbol,
            declaration,
            semanticModel,
            blocks,
            root,
            visibleOperations,
            semanticFacts,
            SymbolicSourceInput.FromNode(declaration, semanticModel));
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

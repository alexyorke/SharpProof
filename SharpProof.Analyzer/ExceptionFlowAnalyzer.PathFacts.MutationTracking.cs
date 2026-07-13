using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.Analyzer.Engine;
using SharpProof.Symbolic;

namespace SharpProof.Analyzer;

internal static partial class ExceptionFlowAnalyzer
{
    private static bool AnySymbolAssignedBeforeUse(
        SyntaxNode branchRoot,
        int useSpanStart,
        IReadOnlyCollection<ISymbol> symbols,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return AnySymbolAssignedBetween(branchRoot, branchRoot.SpanStart - 1, useSpanStart, symbols, semanticModel,
            cancellationToken);
    }

    private static bool AnyReferencedSymbolAssignedBeforeUse(
        SyntaxNode condition,
        SyntaxNode branchRoot,
        int useSpanStart,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var referencedSymbols = GetReferencedLocalAndParameterSymbols(condition, semanticModel, cancellationToken);
        return referencedSymbols.Count != 0 &&
               AnySymbolAssignedBeforeUse(branchRoot, useSpanStart, referencedSymbols, semanticModel,
                   cancellationToken);
    }

    private static bool AnyReferencedSymbolAssignedBetween(
        SyntaxNode condition,
        SyntaxNode root,
        int afterSpanStart,
        int beforeSpanStart,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var referencedSymbols = GetReferencedLocalAndParameterSymbols(condition, semanticModel, cancellationToken);
        return referencedSymbols.Count != 0 &&
               AnySymbolAssignedBetween(root, afterSpanStart, beforeSpanStart, referencedSymbols, semanticModel,
                   cancellationToken);
    }

    private static bool IsSymbolAssignedBetween(
        SyntaxNode root,
        int afterSpanStart,
        int beforeSpanStart,
        ISymbol symbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var node in CSharpSyntaxFacts.DescendantNodesInExecution(root, includeSelf: false))
        {
            if (node.SpanStart <= afterSpanStart || node.SpanStart >= beforeSpanStart) continue;

            if (SymbolMutationFacts.MutatesSymbol(node, symbol, semanticModel, cancellationToken))
                return true;
        }

        return false;
    }

    private static bool AnySymbolAssignedBetween(
        SyntaxNode root,
        int afterSpanStart,
        int beforeSpanStart,
        IReadOnlyCollection<ISymbol> symbols,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (symbols.Count == 0) return false;

        foreach (var symbol in symbols)
            if (IsSymbolAssignedBetween(root, afterSpanStart, beforeSpanStart, symbol, semanticModel,
                    cancellationToken))
                return true;

        return false;
    }

    private static bool StatementDefinitelyExits(
        StatementSyntax statement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (statement is ReturnStatementSyntax or
            ThrowStatementSyntax or
            ContinueStatementSyntax or
            BreakStatementSyntax)
            return true;
        if (statement is YieldStatementSyntax yieldStatement)
            return yieldStatement.IsKind(SyntaxKind.YieldBreakStatement);

        try
        {
            var controlFlow = semanticModel.AnalyzeControlFlow(statement);
            return controlFlow is { Succeeded: true } && !controlFlow.EndPointIsReachable;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SearchLib.Smt;
using SharpProof.Analyzer.Engine;
using SharpProof.Symbolic;

namespace SharpProof.Analyzer;

internal static partial class ExceptionFlowAnalyzer
{
    private static IReadOnlyCollection<ISymbol> CollectLocalAndParameterSymbols(
        SyntaxNode root,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var symbols = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        foreach (var node in root.DescendantNodesAndSelf(candidate =>
                     !ExecutionVisibility.IsNestedCallableBoundary(candidate)))
        {
            if (node is not ExpressionSyntax expression) continue;

            var symbol = GetLocalOrParameterSymbol(expression, semanticModel, cancellationToken);
            if (symbol != null) symbols.Add(symbol);
        }

        return symbols;
    }

    private static HashSet<ISymbol> CollectRelevantSymbols(
        SyntaxNode primaryRoot,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return new HashSet<ISymbol>(
            CollectLocalAndParameterSymbols(primaryRoot, semanticModel, cancellationToken),
            SymbolEqualityComparer.Default);
    }

    private static HashSet<ISymbol> CollectRelevantSymbols(
        SyntaxNode primaryRoot,
        SyntaxNode? additionalRoot,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var symbols = CollectRelevantSymbols(primaryRoot, semanticModel, cancellationToken);
        if (additionalRoot != null && !ReferenceEquals(additionalRoot, primaryRoot))
            AddRelevantSymbols(symbols, additionalRoot, semanticModel, cancellationToken);

        return symbols;
    }

    private static void AddRelevantSymbols(
        ICollection<ISymbol> symbols,
        SyntaxNode root,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var symbol in CollectLocalAndParameterSymbols(root, semanticModel, cancellationToken))
            symbols.Add(symbol);
    }

    private static void AddPriorAssignmentPathConditions(
        SyntaxNode useNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        ICollection<SmtFormula> pathConditions)
    {
        foreach (var fact in SymbolicReachabilityService.CollectPriorAssignmentFacts(useNode, semanticModel,
                     cancellationToken)) pathConditions.Add(fact);

        AddPriorCoalesceAssignmentThrowFacts(useNode, semanticModel, cancellationToken, pathConditions);
        AddPriorSelfThrowGuardedAssignmentFacts(useNode, semanticModel, cancellationToken, pathConditions);
    }

    private static void AddPriorCoalesceAssignmentThrowFacts(
        SyntaxNode useNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        ICollection<SmtFormula> pathConditions)
    {
        foreach (var (block, containingStatement) in EnumerateContainingBlocks(useNode).Reverse())
            foreach (var statement in block.Statements)
            {
                if (ReferenceEquals(statement, containingStatement)) break;

                if (statement is not ExpressionStatementSyntax
                    {
                        Expression: AssignmentExpressionSyntax assignment
                    } ||
                    !assignment.IsKind(SyntaxKind.CoalesceAssignmentExpression) ||
                    UnwrapFactExpression(assignment.Right) is not ThrowExpressionSyntax ||
                    GetLocalOrParameterSymbol(assignment.Left, semanticModel, cancellationToken) is not
                    { } assignedSymbol ||
                    IsSymbolAssignedBetween(block, assignment.Span.End, useNode.SpanStart, assignedSymbol, semanticModel,
                        cancellationToken))
                    continue;

                AddSymbolNonNullFact(assignedSymbol, pathConditions);
            }
    }

    private static void AddPriorSelfThrowGuardedAssignmentFacts(
        SyntaxNode useNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        ICollection<SmtFormula> pathConditions)
    {
        foreach (var (block, containingStatement) in EnumerateContainingBlocks(useNode).Reverse())
            foreach (var statement in block.Statements)
            {
                if (ReferenceEquals(statement, containingStatement)) break;

                if (statement is not ExpressionStatementSyntax
                    {
                        Expression: AssignmentExpressionSyntax assignment
                    } ||
                    !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
                    GetLocalOrParameterSymbol(assignment.Left, semanticModel, cancellationToken) is not
                    { } assignedSymbol ||
                    !TryGetThrowGuardedValue(
                        assignment.Right,
                        out var effectiveValueExpression,
                        out var guardExpression,
                        out var guardBranchWhenTrue,
                        out var requiresNonNullValue) ||
                    !ExpressionMatchesSymbol(effectiveValueExpression, assignedSymbol, semanticModel, cancellationToken) ||
                    IsSymbolAssignedBetween(block, assignment.Span.End, useNode.SpanStart, assignedSymbol, semanticModel,
                        cancellationToken))
                    continue;

                if (guardExpression != null)
                {
                    if (AnyReferencedSymbolAssignedBetween(guardExpression, block, assignment.Span.End, useNode.SpanStart,
                            semanticModel, cancellationToken)) continue;

                    TryAddPathCondition(guardExpression, guardBranchWhenTrue, semanticModel, cancellationToken,
                        pathConditions);
                }
                else if (requiresNonNullValue)
                {
                    AddSymbolNonNullFact(assignedSymbol, pathConditions);
                }
            }
    }
}
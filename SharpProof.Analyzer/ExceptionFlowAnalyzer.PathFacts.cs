using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SearchLib.Smt;
using SharpProof.Analyzer.Engine;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Analyzer;

internal static partial class ExceptionFlowAnalyzer
{
    private static bool IsKnownByDominatingIf(
        ExpressionSyntax expression,
        SyntaxNode useNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        PathFactKind factKind,
        SmtAnalysisService smtAnalysis)
    {
        var invalidatedSymbols = CollectLocalAndParameterSymbols(expression, semanticModel, cancellationToken);
        if (invalidatedSymbols.Count == 0) return false;

        if (!TryCreateFactFormula(expression, factKind, semanticModel, cancellationToken, out var factFormula) ||
            factFormula == null)
            return false;

        var pathConditions = CollectPathConditionsForUse(
            useNode,
            invalidatedSymbols,
            semanticModel,
            cancellationToken);
        return pathConditions.Count > 0 &&
               SymbolicReachabilityService.PathConditionsAllowAndImply(
                   pathConditions,
                   factFormula,
                   smtAnalysis);
    }

    private static bool IsKnownByPriorAssignment(
        ExpressionSyntax expression,
        SyntaxNode useNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        PathFactKind factKind)
    {
        var symbol = GetLocalOrParameterSymbol(expression, semanticModel, cancellationToken);
        if (symbol == null) return false;

        var containingStatement = useNode
            .AncestorsAndSelf()
            .OfType<StatementSyntax>()
            .FirstOrDefault(statement => statement.Parent is BlockSyntax);
        if (containingStatement?.Parent is not BlockSyntax block) return false;

        var matchedAssignment = false;
        foreach (var statement in block.Statements)
        {
            if (ReferenceEquals(statement, containingStatement)) break;

            foreach (var candidate in statement.DescendantNodesAndSelf(node =>
                         !ExecutionVisibility.IsNestedCallableBoundary(node)))
                if (candidate is AssignmentExpressionSyntax assignment &&
                    ExpressionMatchesSymbol(assignment.Left, symbol, semanticModel, cancellationToken))
                {
                    if (!ExpressionMatchesFact(assignment.Right, factKind, semanticModel, cancellationToken))
                        return false;

                    matchedAssignment = true;
                }
                else if (candidate is VariableDeclaratorSyntax declarator &&
                         declarator.Initializer != null &&
                         semanticModel.GetDeclaredSymbol(declarator, cancellationToken) is ILocalSymbol localSymbol &&
                         SymbolEqualityComparer.Default.Equals(localSymbol.OriginalDefinition, symbol))
                {
                    if (!ExpressionMatchesFact(declarator.Initializer.Value, factKind, semanticModel,
                            cancellationToken)) return false;

                    matchedAssignment = true;
                }
                else if (MutatesSymbol(candidate, symbol, semanticModel, cancellationToken))
                {
                    return false;
                }
        }

        return matchedAssignment;
    }

    private static List<SmtFormula> CollectPathConditionsForUse(
        SyntaxNode useNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return CollectPathConditionsForUse(
            useNode,
            CollectLocalAndParameterSymbols(useNode, semanticModel, cancellationToken),
            semanticModel,
            cancellationToken);
    }

    private static List<SmtFormula> CollectPathConditionsForUse(
        SyntaxNode useNode,
        IReadOnlyCollection<ISymbol> invalidatedSymbols,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var pathConditions = new List<SmtFormula>();
        AddMethodEntryRequiresPathConditions(useNode, semanticModel, cancellationToken, pathConditions);
        AddPriorAssignmentPathConditions(useNode, semanticModel, cancellationToken, pathConditions);
        AddSharedAncestorPathConditions(useNode, semanticModel, cancellationToken, pathConditions);

        foreach (var ifStatement in useNode.Ancestors().OfType<IfStatementSyntax>())
        {
            if (ifStatement.Statement.Span.Contains(useNode.SpanStart) &&
                !AnySymbolAssignedBeforeUse(ifStatement.Statement, useNode.SpanStart, invalidatedSymbols, semanticModel,
                    cancellationToken) &&
                !AnyReferencedSymbolAssignedBeforeUse(ifStatement.Condition, ifStatement.Statement, useNode.SpanStart,
                    semanticModel, cancellationToken))
                TryAddPathCondition(ifStatement.Condition, true, semanticModel, cancellationToken, pathConditions);

            if (ifStatement.Else?.Statement is { } elseStatement &&
                elseStatement.Span.Contains(useNode.SpanStart) &&
                !AnySymbolAssignedBeforeUse(elseStatement, useNode.SpanStart, invalidatedSymbols, semanticModel,
                    cancellationToken) &&
                !AnyReferencedSymbolAssignedBeforeUse(ifStatement.Condition, elseStatement, useNode.SpanStart,
                    semanticModel, cancellationToken))
                TryAddPathCondition(ifStatement.Condition, false, semanticModel, cancellationToken, pathConditions);
        }

        AddSwitchPathConditions(useNode, invalidatedSymbols, semanticModel, cancellationToken, pathConditions);
        AddLoopPathConditions(useNode, invalidatedSymbols, semanticModel, cancellationToken, pathConditions);
        AddCatchFilterPathConditions(useNode, invalidatedSymbols, semanticModel, cancellationToken, pathConditions);
        AddExpressionBranchPathConditions(useNode, invalidatedSymbols, semanticModel, cancellationToken,
            pathConditions);
        AddPrecedingGuardConditions(invalidatedSymbols, useNode, semanticModel, cancellationToken, pathConditions);
        return pathConditions;
    }

    private static void AddMethodEntryRequiresPathConditions(
        SyntaxNode useNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        List<SmtFormula> pathConditions)
    {
        var methodNode = useNode
            .AncestorsAndSelf()
            .FirstOrDefault(IsMethodLikeDeclaration);
        if (methodNode == null ||
            semanticModel.GetDeclaredSymbol(methodNode, cancellationToken) is not IMethodSymbol methodSymbol)
            return;

        var contracts = RequiresContractHelpers.ValidContracts(
            methodSymbol,
            ActiveAttributePolicy,
            cancellationToken);
        if (contracts.Length == 0) return;

        var position = RequiresContractHelpers.GetMethodEntrySpeculativePosition(methodNode);
        foreach (var contract in contracts)
        {
            if (!RequiresContractHelpers.TryCreateConditionFormula(
                    semanticModel,
                    position,
                    contract.Condition,
                    cancellationToken,
                    out var conditionExpression,
                    out var conditionSemanticModel,
                    out var formula,
                    out _) ||
                RequiresContractHelpers.ContainsResultReference(conditionExpression))
                continue;

            var referencedSymbols =
                CollectLocalAndParameterSymbols(conditionExpression, conditionSemanticModel, cancellationToken);
            if (referencedSymbols.Count != 0 &&
                AnySymbolAssignedBeforeUse(methodNode, useNode.SpanStart, referencedSymbols, semanticModel,
                    cancellationToken))
                continue;

            pathConditions.Add(formula);
        }
    }

    private static bool IsMethodLikeDeclaration(SyntaxNode node)
    {
        return node is MethodDeclarationSyntax ||
               node is AccessorDeclarationSyntax ||
               node is ConstructorDeclarationSyntax ||
               node is ConversionOperatorDeclarationSyntax ||
               node is OperatorDeclarationSyntax ||
               node is LocalFunctionStatementSyntax;
    }

    internal static bool IsExceptionPathReachable(
        SyntaxNode useNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        var pathConditions = CollectPathConditionsForUse(useNode, semanticModel, cancellationToken);

        return SymbolicPathConditionsAreSatisfiable(
            pathConditions,
            useNode,
            smtAnalysis);
    }

    internal static bool IsMethodCallCandidatePathReachable(
        MethodCallCandidate candidate,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        var relevantSymbols = CollectRelevantSymbols(candidate.CallSite, semanticModel, cancellationToken);
        if (candidate.UsingDisposeGuard?.ResourceExpression is { } resourceExpression)
            AddRelevantSymbols(relevantSymbols, resourceExpression, semanticModel, cancellationToken);

        var pathConditions = CollectPathConditionsForUse(
            candidate.CallSite,
            relevantSymbols,
            semanticModel,
            cancellationToken);

        if (candidate.UsingDisposeGuard?.ResourceExpression is { } disposeReceiver)
            TryAddReferenceNullCondition(
                disposeReceiver,
                false,
                semanticModel,
                cancellationToken,
                pathConditions);

        return SymbolicPathConditionsAreSatisfiable(
            pathConditions,
            candidate.CallSite,
            smtAnalysis);
    }

    internal static List<SmtFormula> CollectExceptionSitePathConditions(
        SyntaxNode exceptionSite,
        SyntaxNode? relevantRoot,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var relevantSymbols = CollectRelevantSymbols(
            exceptionSite,
            relevantRoot,
            semanticModel,
            cancellationToken);

        return CollectPathConditionsForUse(
            exceptionSite,
            relevantSymbols,
            semanticModel,
            cancellationToken);
    }

    private static bool SymbolicPathConditionsAreSatisfiable(
        IReadOnlyCollection<SmtFormula> pathConditions,
        SyntaxNode sourceNode,
        SmtAnalysisService smtAnalysis)
    {
        return SymbolicReachabilityService.IsSatisfiable(
            pathConditions,
            smtAnalysis);
    }
}

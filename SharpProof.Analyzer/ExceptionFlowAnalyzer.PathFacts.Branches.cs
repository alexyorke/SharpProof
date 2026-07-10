using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SearchLib.Smt;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Analyzer;

internal static partial class ExceptionFlowAnalyzer
{
    private static void AddSharedAncestorPathConditions(
        SyntaxNode useNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        ICollection<SmtFormula> pathConditions)
    {
        foreach (var condition in SymbolicReachabilityService.CollectAncestorReachabilityConditions(useNode,
                     semanticModel, cancellationToken)) pathConditions.Add(condition);
    }

    private static void AddExpressionBranchPathConditions(
        SyntaxNode useNode,
        IReadOnlyCollection<ISymbol> invalidatedSymbols,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        ICollection<SmtFormula> pathConditions)
    {
        foreach (var conditionalExpression in useNode.Ancestors().OfType<ConditionalExpressionSyntax>())
        {
            if (AnySymbolMutatedInSyntax(conditionalExpression.Condition, invalidatedSymbols, semanticModel,
                    cancellationToken)) continue;

            if (conditionalExpression.WhenTrue.Span.Contains(useNode.SpanStart) &&
                !AnySymbolAssignedBeforeUse(conditionalExpression.WhenTrue, useNode.SpanStart, invalidatedSymbols,
                    semanticModel, cancellationToken))
                TryAddPathCondition(conditionalExpression.Condition, true, semanticModel, cancellationToken,
                    pathConditions);
            else if (conditionalExpression.WhenFalse.Span.Contains(useNode.SpanStart) &&
                     !AnySymbolAssignedBeforeUse(conditionalExpression.WhenFalse, useNode.SpanStart, invalidatedSymbols,
                         semanticModel, cancellationToken))
                TryAddPathCondition(conditionalExpression.Condition, false, semanticModel, cancellationToken,
                    pathConditions);
        }

        foreach (var binaryExpression in useNode.Ancestors().OfType<BinaryExpressionSyntax>())
        {
            if (!binaryExpression.Right.Span.Contains(useNode.SpanStart) ||
                AnySymbolMutatedInSyntax(binaryExpression.Left, invalidatedSymbols, semanticModel, cancellationToken) ||
                AnySymbolAssignedBeforeUse(binaryExpression.Right, useNode.SpanStart, invalidatedSymbols, semanticModel,
                    cancellationToken))
                continue;

            if (binaryExpression.IsKind(SyntaxKind.LogicalAndExpression))
                TryAddPathCondition(binaryExpression.Left, true, semanticModel, cancellationToken, pathConditions);
            else if (binaryExpression.IsKind(SyntaxKind.LogicalOrExpression))
                TryAddPathCondition(binaryExpression.Left, false, semanticModel, cancellationToken, pathConditions);
            else if (binaryExpression.IsKind(SyntaxKind.CoalesceExpression))
                TryAddCoalesceRightPathCondition(binaryExpression.Left, semanticModel, cancellationToken,
                    pathConditions);
        }

        foreach (var conditionalAccess in useNode.Ancestors().OfType<ConditionalAccessExpressionSyntax>())
        {
            if (!conditionalAccess.WhenNotNull.Span.Contains(useNode.SpanStart) ||
                AnySymbolMutatedInSyntax(conditionalAccess.Expression, invalidatedSymbols, semanticModel,
                    cancellationToken) ||
                AnySymbolAssignedBeforeUse(conditionalAccess.WhenNotNull, useNode.SpanStart, invalidatedSymbols,
                    semanticModel, cancellationToken))
                continue;

            TryAddReferenceNullCondition(conditionalAccess.Expression, false, semanticModel, cancellationToken,
                pathConditions);
        }
    }

    private static void AddLoopPathConditions(
        SyntaxNode useNode,
        IReadOnlyCollection<ISymbol> invalidatedSymbols,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        ICollection<SmtFormula> pathConditions)
    {
        foreach (var whileStatement in useNode.Ancestors().OfType<WhileStatementSyntax>())
        {
            if (!whileStatement.Statement.Span.Contains(useNode.SpanStart) ||
                AnySymbolAssignedBeforeUse(whileStatement.Statement, useNode.SpanStart, invalidatedSymbols,
                    semanticModel, cancellationToken) ||
                AnyReferencedSymbolAssignedBeforeUse(whileStatement.Condition, whileStatement.Statement,
                    useNode.SpanStart, semanticModel, cancellationToken))
                continue;

            TryAddPathCondition(whileStatement.Condition, true, semanticModel, cancellationToken, pathConditions);
            foreach (var loopFact in SymbolicReachabilityService.CollectLoopBodyInvariantFacts(whileStatement,
                         semanticModel, cancellationToken)) pathConditions.Add(loopFact);
        }

        foreach (var doStatement in useNode.Ancestors().OfType<DoStatementSyntax>())
        {
            if (!doStatement.Statement.Span.Contains(useNode.SpanStart) ||
                AnySymbolAssignedBeforeUse(doStatement.Statement, useNode.SpanStart, invalidatedSymbols, semanticModel,
                    cancellationToken))
                continue;

            foreach (var loopFact in SymbolicReachabilityService.CollectLoopBodyInvariantFacts(doStatement,
                         semanticModel, cancellationToken)) pathConditions.Add(loopFact);
        }

        foreach (var forStatement in useNode.Ancestors().OfType<ForStatementSyntax>())
        {
            if (forStatement.Condition == null ||
                !forStatement.Statement.Span.Contains(useNode.SpanStart) ||
                AnySymbolAssignedBeforeUse(forStatement.Statement, useNode.SpanStart, invalidatedSymbols, semanticModel,
                    cancellationToken) ||
                AnyReferencedSymbolAssignedBeforeUse(forStatement.Condition, forStatement.Statement, useNode.SpanStart,
                    semanticModel, cancellationToken))
                continue;

            TryAddPathCondition(forStatement.Condition, true, semanticModel, cancellationToken, pathConditions);
            foreach (var loopFact in SymbolicReachabilityService.CollectLoopBodyInvariantFacts(forStatement,
                         semanticModel, cancellationToken)) pathConditions.Add(loopFact);
        }
    }

    private static void AddSwitchPathConditions(
        SyntaxNode useNode,
        IReadOnlyCollection<ISymbol> invalidatedSymbols,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        ICollection<SmtFormula> pathConditions)
    {
        foreach (var switchStatement in useNode.Ancestors().OfType<SwitchStatementSyntax>())
        {
            var matchingSection = switchStatement.Sections
                .FirstOrDefault(section => section.Span.Contains(useNode.SpanStart));
            if (matchingSection == null ||
                AnySymbolMutatedInSyntax(switchStatement.Expression, invalidatedSymbols, semanticModel,
                    cancellationToken) ||
                AnySymbolAssignedBeforeUse(matchingSection, useNode.SpanStart, invalidatedSymbols, semanticModel,
                    cancellationToken) ||
                AnyReferencedSymbolAssignedBeforeUse(switchStatement.Expression, matchingSection, useNode.SpanStart,
                    semanticModel, cancellationToken))
                continue;

            if (SwitchPathConditionBuilder.TryCreateSwitchStatementSectionCondition(
                    switchStatement.Expression,
                    matchingSection,
                    semanticModel,
                    cancellationToken,
                    out var sectionCondition))
                pathConditions.Add(sectionCondition);
        }

        foreach (var switchExpression in useNode.Ancestors().OfType<SwitchExpressionSyntax>())
        {
            var matchingArm = switchExpression.Arms
                .FirstOrDefault(arm => arm.Expression.Span.Contains(useNode.SpanStart));
            if (matchingArm == null ||
                AnySymbolMutatedInSyntax(switchExpression.GoverningExpression, invalidatedSymbols, semanticModel,
                    cancellationToken) ||
                AnySymbolAssignedBeforeUse(matchingArm, useNode.SpanStart, invalidatedSymbols, semanticModel,
                    cancellationToken) ||
                AnyReferencedSymbolAssignedBeforeUse(switchExpression.GoverningExpression, matchingArm,
                    useNode.SpanStart, semanticModel, cancellationToken))
                continue;

            if (SwitchPathConditionBuilder.TryCreateSwitchExpressionArmCondition(
                    switchExpression.GoverningExpression,
                    matchingArm,
                    semanticModel,
                    cancellationToken,
                    out var armCondition))
                pathConditions.Add(armCondition);
        }
    }

    private static void AddCatchFilterPathConditions(
        SyntaxNode useNode,
        IReadOnlyCollection<ISymbol> invalidatedSymbols,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        ICollection<SmtFormula> pathConditions)
    {
        foreach (var catchClause in useNode.Ancestors().OfType<CatchClauseSyntax>())
        {
            if (catchClause.Filter?.FilterExpression is not { } filterExpression ||
                !catchClause.Block.Span.Contains(useNode.SpanStart) ||
                AnySymbolMutatedInSyntax(filterExpression, invalidatedSymbols, semanticModel, cancellationToken) ||
                AnySymbolAssignedBeforeUse(catchClause.Block, useNode.SpanStart, invalidatedSymbols, semanticModel,
                    cancellationToken) ||
                AnyReferencedSymbolAssignedBeforeUse(filterExpression, catchClause.Block, useNode.SpanStart,
                    semanticModel, cancellationToken))
                continue;

            AddCatchFilterPreTryPathConditions(
                catchClause,
                filterExpression,
                semanticModel,
                cancellationToken,
                pathConditions);
            TryAddPathCondition(filterExpression, true, semanticModel, cancellationToken, pathConditions);
        }
    }

    private static void AddCatchFilterPreTryPathConditions(
        CatchClauseSyntax catchClause,
        ExpressionSyntax filterExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        ICollection<SmtFormula> pathConditions)
    {
        if (catchClause.Parent is not TryStatementSyntax tryStatement ||
            tryStatement.Parent is not BlockSyntax outerBlock)
            return;

        TryGetSimpleBooleanFilterAliasSymbol(
            filterExpression,
            semanticModel,
            cancellationToken,
            out var filterAliasSymbol);

        var preTryFacts = new List<SmtFormula>();
        foreach (var statement in outerBlock.Statements)
        {
            if (ReferenceEquals(statement, tryStatement)) break;

            if (AnyConditionSymbolMutatedInStatement(filterExpression, statement, semanticModel, cancellationToken))
                preTryFacts.Clear();

            AddPriorStatementFacts(statement, semanticModel, cancellationToken, preTryFacts);
            AddSimpleBooleanAliasConditionFacts(
                statement,
                filterAliasSymbol,
                semanticModel,
                cancellationToken,
                preTryFacts);
        }

        foreach (var fact in preTryFacts) pathConditions.Add(fact);
    }

    private static bool TryGetSimpleBooleanFilterAliasSymbol(
        ExpressionSyntax filterExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out ISymbol? symbol)
    {
        symbol = null;
        if (GetExpressionType(filterExpression, semanticModel, cancellationToken)?.SpecialType !=
            SpecialType.System_Boolean ||
            GetLocalOrParameterSymbol(filterExpression, semanticModel, cancellationToken) is not { } candidate ||
            SymbolicFactFactory.GetTrackedSymbolType(candidate)?.SpecialType != SpecialType.System_Boolean)
            return false;

        symbol = candidate;
        return true;
    }

    private static void AddSimpleBooleanAliasConditionFacts(
        StatementSyntax statement,
        ISymbol? filterAliasSymbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        ICollection<SmtFormula> pathConditions)
    {
        if (filterAliasSymbol == null) return;

        if (statement is LocalDeclarationStatementSyntax localDeclaration)
        {
            foreach (var declarator in localDeclaration.Declaration.Variables)
            {
                if (declarator.Initializer == null ||
                    semanticModel.GetDeclaredSymbol(declarator, cancellationToken) is not ILocalSymbol localSymbol ||
                    !SymbolEqualityComparer.Default.Equals(localSymbol.OriginalDefinition, filterAliasSymbol))
                    continue;

                TryAddPathCondition(
                    declarator.Initializer.Value,
                    true,
                    semanticModel,
                    cancellationToken,
                    pathConditions);
            }

            return;
        }

        if (statement is not ExpressionStatementSyntax
            {
                Expression: AssignmentExpressionSyntax assignment
            } ||
            !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
            GetLocalOrParameterSymbol(assignment.Left, semanticModel, cancellationToken) is not { } assignedSymbol ||
            !SymbolEqualityComparer.Default.Equals(assignedSymbol, filterAliasSymbol) ||
            ExpressionReferencesSymbol(assignment.Right, filterAliasSymbol, semanticModel, cancellationToken))
            return;

        TryAddPathCondition(
            assignment.Right,
            true,
            semanticModel,
            cancellationToken,
            pathConditions);
    }

    private static void AddPrecedingGuardConditions(
        IReadOnlyCollection<ISymbol> invalidatedSymbols,
        SyntaxNode useNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        ICollection<SmtFormula> pathConditions)
    {
        var containingStatement = useNode
            .AncestorsAndSelf()
            .OfType<StatementSyntax>()
            .FirstOrDefault(statement => statement.Parent is BlockSyntax);
        if (containingStatement?.Parent is not BlockSyntax block) return;

        foreach (var statement in block.Statements)
        {
            if (ReferenceEquals(statement, containingStatement)) break;

            if (statement is IfStatementSyntax ifStatement &&
                !AnySymbolAssignedBetween(block, ifStatement.Span.End, useNode.SpanStart, invalidatedSymbols,
                    semanticModel, cancellationToken) &&
                !AnyReferencedSymbolAssignedBetween(ifStatement.Condition, block, ifStatement.Span.End,
                    useNode.SpanStart, semanticModel, cancellationToken))
                AddCompletedIfStatementFacts(ifStatement, invalidatedSymbols, semanticModel, cancellationToken,
                    pathConditions);
        }
    }
}
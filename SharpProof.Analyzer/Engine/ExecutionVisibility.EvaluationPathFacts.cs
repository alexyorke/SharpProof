using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SearchLib.Smt;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Analyzer.Engine;

internal static partial class ExecutionVisibility
{
    private static SymbolicState AddEvaluationPathState(
        SymbolicState pathState,
        SyntaxNode syntaxNode,
        SyntaxNode ancestor,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        Func<ISymbol, int>? getSymbolVersion)
    {
        if (TryGetEvaluationBranch(ancestor, syntaxNode.SpanStart, out var condition, out var branchWhenTrue) &&
            SymbolicReachabilityService.TryCollectBranchState(
                pathState,
                condition,
                branchWhenTrue,
                semanticModel,
                cancellationToken,
                out var branchState,
                getSymbolVersion))
            return branchState;

        if (ancestor is BinaryExpressionSyntax binaryExpression &&
            binaryExpression.Right.Span.Contains(syntaxNode.SpanStart) &&
            binaryExpression.IsKind(SyntaxKind.CoalesceExpression))
            return AddReferenceNullStateCondition(
                pathState,
                binaryExpression.Left,
                true,
                semanticModel,
                cancellationToken,
                getSymbolVersion);

        if (ancestor is ConditionalAccessExpressionSyntax conditionalAccessExpression &&
            conditionalAccessExpression.WhenNotNull.Span.Contains(syntaxNode.SpanStart))
            return AddReferenceNullStateCondition(
                pathState,
                conditionalAccessExpression.Expression,
                false,
                semanticModel,
                cancellationToken,
                getSymbolVersion);

        if (ancestor is SwitchStatementSyntax switchStatement)
        {
            var section = switchStatement.Sections.FirstOrDefault(candidate =>
                candidate.Statements.Any(statement => statement.Span.Contains(syntaxNode.SpanStart)));
            if (section != null &&
                !IsReachableConstantSwitchGotoTarget(section, switchStatement, semanticModel, cancellationToken) &&
                SwitchPathConditionBuilder.TryCreateSwitchStatementSectionSymbolicCondition(
                    switchStatement.Expression,
                    section,
                    semanticModel,
                    cancellationToken,
                    out var sectionCondition,
                    getSymbolVersion))
                return AddSwitchEvaluationState(
                    pathState,
                    switchStatement.Expression,
                    sectionCondition,
                    semanticModel,
                    cancellationToken,
                    getSymbolVersion);
        }

        if (ancestor is SwitchExpressionSyntax switchExpression)
        {
            var arm = switchExpression.Arms.FirstOrDefault(candidate =>
                candidate.Expression.Span.Contains(syntaxNode.SpanStart));
            if (arm != null &&
                SwitchPathConditionBuilder.TryCreateSwitchExpressionArmSymbolicCondition(
                    switchExpression.GoverningExpression,
                    arm,
                    semanticModel,
                    cancellationToken,
                    out var armCondition,
                    getSymbolVersion))
                return AddSwitchEvaluationState(
                    pathState,
                    switchExpression.GoverningExpression,
                    armCondition,
                    semanticModel,
                    cancellationToken,
                    getSymbolVersion);
        }

        return pathState;
    }

    private static bool TryGetEvaluationBranch(
        SyntaxNode ancestor,
        int position,
        out ExpressionSyntax condition,
        out bool branchWhenTrue)
    {
        switch (ancestor)
        {
            case IfStatementSyntax ifStatement when ifStatement.Statement.Span.Contains(position):
                condition = ifStatement.Condition;
                branchWhenTrue = true;
                return true;
            case IfStatementSyntax ifStatement when ifStatement.Else?.Statement.Span.Contains(position) == true:
                condition = ifStatement.Condition;
                branchWhenTrue = false;
                return true;
            case ConditionalExpressionSyntax conditional when conditional.WhenTrue.Span.Contains(position):
                condition = conditional.Condition;
                branchWhenTrue = true;
                return true;
            case ConditionalExpressionSyntax conditional when conditional.WhenFalse.Span.Contains(position):
                condition = conditional.Condition;
                branchWhenTrue = false;
                return true;
            case BinaryExpressionSyntax binary when binary.Right.Span.Contains(position) &&
                                                    binary.IsKind(SyntaxKind.LogicalAndExpression):
                condition = binary.Left;
                branchWhenTrue = true;
                return true;
            case BinaryExpressionSyntax binary when binary.Right.Span.Contains(position) &&
                                                    binary.IsKind(SyntaxKind.LogicalOrExpression):
                condition = binary.Left;
                branchWhenTrue = false;
                return true;
            default:
                condition = null!;
                branchWhenTrue = false;
                return false;
        }
    }

    private static SymbolicState AddReferenceNullStateCondition(
        SymbolicState pathState,
        ExpressionSyntax expression,
        bool equalToNull,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        Func<ISymbol, int>? getSymbolVersion)
    {
        var lowering = SymbolicSemanticPipeline.LowerReferenceTerm(
            expression,
            new SymbolicLoweringContext(semanticModel, cancellationToken, getSymbolVersion));
        if (lowering is not { IsExact: true, Value: { } reference }) return pathState;

        var fact = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                equalToNull ? SymbolicRelationOperator.Equal : SymbolicRelationOperator.NotEqual,
                reference,
                new SymbolicNullTerm()),
            expression,
            equalToNull ? "analyzer.evaluation.null" : "analyzer.evaluation.non-null");
        return pathState.AddPathCondition(new SymbolicFactCondition(fact));
    }

    private static SymbolicState AddSwitchEvaluationState(
        SymbolicState pathState,
        ExpressionSyntax governingExpression,
        SymbolicCondition selectionCondition,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        Func<ISymbol, int>? getSymbolVersion)
    {
        if (SymbolicReachabilityService.TryCreateArrayLengthCountAliasCondition(
                governingExpression,
                semanticModel,
                cancellationToken,
                out var aliasCondition,
                getSymbolVersion))
            pathState = pathState.AddPathCondition(aliasCondition);

        return pathState.AddPathCondition(selectionCondition);
    }

    private static void AddEvaluationPathFacts(
        SyntaxNode syntaxNode,
        SyntaxNode ancestor,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        ICollection<SmtFormula> pathConditions,
        Func<ISymbol, int>? getSymbolVersion)
    {
        if (ancestor is IfStatementSyntax ifStatement)
        {
            if (ifStatement.Statement.Span.Contains(syntaxNode.SpanStart))
                AddBranchConditionFact(
                    ifStatement.Condition,
                    true,
                    semanticModel,
                    cancellationToken,
                    pathConditions,
                    getSymbolVersion);
            else if (ifStatement.Else?.Statement.Span.Contains(syntaxNode.SpanStart) == true)
                AddBranchConditionFact(
                    ifStatement.Condition,
                    false,
                    semanticModel,
                    cancellationToken,
                    pathConditions,
                    getSymbolVersion);

            return;
        }

        if (ancestor is ConditionalExpressionSyntax conditionalExpression)
        {
            if (conditionalExpression.WhenTrue.Span.Contains(syntaxNode.SpanStart))
                AddBranchConditionFact(
                    conditionalExpression.Condition,
                    true,
                    semanticModel,
                    cancellationToken,
                    pathConditions,
                    getSymbolVersion);
            else if (conditionalExpression.WhenFalse.Span.Contains(syntaxNode.SpanStart))
                AddBranchConditionFact(
                    conditionalExpression.Condition,
                    false,
                    semanticModel,
                    cancellationToken,
                    pathConditions,
                    getSymbolVersion);

            return;
        }

        if (ancestor is BinaryExpressionSyntax binaryExpression)
        {
            if (!binaryExpression.Right.Span.Contains(syntaxNode.SpanStart)) return;

            if (binaryExpression.IsKind(SyntaxKind.LogicalAndExpression))
                AddBranchConditionFact(
                    binaryExpression.Left,
                    true,
                    semanticModel,
                    cancellationToken,
                    pathConditions,
                    getSymbolVersion);
            else if (binaryExpression.IsKind(SyntaxKind.LogicalOrExpression))
                AddBranchConditionFact(
                    binaryExpression.Left,
                    false,
                    semanticModel,
                    cancellationToken,
                    pathConditions,
                    getSymbolVersion);
            else if (binaryExpression.IsKind(SyntaxKind.CoalesceExpression))
                AddReferenceNullStateFact(
                    binaryExpression.Left,
                    true,
                    semanticModel,
                    cancellationToken,
                    pathConditions,
                    getSymbolVersion);

            return;
        }

        if (ancestor is ConditionalAccessExpressionSyntax conditionalAccessExpression &&
            conditionalAccessExpression.WhenNotNull.Span.Contains(syntaxNode.SpanStart))
        {
            AddReferenceNullStateFact(
                conditionalAccessExpression.Expression,
                false,
                semanticModel,
                cancellationToken,
                pathConditions,
                getSymbolVersion);

            return;
        }

        if (ancestor is SwitchStatementSyntax switchStatement)
        {
            var section = switchStatement.Sections.FirstOrDefault(candidate =>
                candidate.Statements.Any(statement => statement.Span.Contains(syntaxNode.SpanStart)));
            if (section != null &&
                !IsReachableConstantSwitchGotoTarget(section, switchStatement, semanticModel, cancellationToken) &&
                SwitchPathConditionBuilder.TryCreateSwitchStatementSectionCondition(
                    switchStatement.Expression,
                    section,
                    semanticModel,
                    cancellationToken,
                    out var sectionCondition))
            {
                AddArrayLengthCountAliasFact(
                    switchStatement.Expression,
                    semanticModel,
                    cancellationToken,
                    pathConditions,
                    getSymbolVersion);
                pathConditions.Add(sectionCondition);
            }

            return;
        }

        if (ancestor is SwitchExpressionSyntax switchExpression)
        {
            var arm = switchExpression.Arms.FirstOrDefault(candidate =>
                candidate.Expression.Span.Contains(syntaxNode.SpanStart));
            if (arm != null &&
                SwitchPathConditionBuilder.TryCreateSwitchExpressionArmCondition(
                    switchExpression.GoverningExpression,
                    arm,
                    semanticModel,
                    cancellationToken,
                    out var armCondition))
            {
                AddArrayLengthCountAliasFact(
                    switchExpression.GoverningExpression,
                    semanticModel,
                    cancellationToken,
                    pathConditions,
                    getSymbolVersion);
                pathConditions.Add(armCondition);
            }
        }
    }

    private static void AddBranchConditionFact(
        ExpressionSyntax expression,
        bool branchWhenTrue,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        ICollection<SmtFormula> pathConditions,
        Func<ISymbol, int>? getSymbolVersion)
    {
        SymbolicReachabilityService.TryAddBranchConditionFacts(
            expression,
            branchWhenTrue,
            semanticModel,
            cancellationToken,
            pathConditions,
            getSymbolVersion,
            true);
    }

    private static void AddReferenceNullStateFact(
        ExpressionSyntax expression,
        bool equalToNull,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        ICollection<SmtFormula> pathConditions,
        Func<ISymbol, int>? getSymbolVersion)
    {
        if (SymbolicReachabilityService.TryCreateReferenceNullComparison(
                expression,
                semanticModel,
                cancellationToken,
                equalToNull,
                out var formula,
                getSymbolVersion))
            pathConditions.Add(formula);
    }

    private static void AddArrayLengthCountAliasFact(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        ICollection<SmtFormula> pathConditions,
        Func<ISymbol, int>? getSymbolVersion)
    {
        if (SymbolicReachabilityService.TryCreateArrayLengthCountAliasFact(
                expression,
                semanticModel,
                cancellationToken,
                out var aliasFact,
                getSymbolVersion))
            pathConditions.Add(aliasFact);
    }
}

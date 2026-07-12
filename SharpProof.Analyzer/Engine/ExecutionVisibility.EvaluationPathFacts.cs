using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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
            SymbolicReachabilityService.ApplyBranchFacts(
                pathState,
                condition,
                branchWhenTrue,
                semanticModel,
                cancellationToken,
                getSymbolVersion) is { IsExact: true, Value: { } branchState })
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
        if (SymbolicSemanticPipeline.LowerArrayLengthCountAliasCondition(
                governingExpression,
                new SymbolicLoweringContext(semanticModel, cancellationToken, getSymbolVersion)) is
            { IsExact: true, Value: { } aliasCondition })
            pathState = pathState.AddPathCondition(aliasCondition);

        return pathState.AddPathCondition(selectionCondition);
    }

}

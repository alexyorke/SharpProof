using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
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
            !IsLoopGuardInvalidatedBeforeUse(
                ancestor,
                condition,
                syntaxNode,
                semanticModel,
                cancellationToken) &&
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
            case WhileStatementSyntax whileStatement when whileStatement.Statement.Span.Contains(position):
                condition = whileStatement.Condition;
                branchWhenTrue = true;
                return true;
            case ForStatementSyntax { Condition: { } forCondition } forStatement
                when forStatement.Statement.Span.Contains(position):
                condition = forCondition;
                branchWhenTrue = true;
                return true;
            default:
                condition = null!;
                branchWhenTrue = false;
                return false;
        }
    }

    private static bool IsLoopGuardInvalidatedBeforeUse(
        SyntaxNode ancestor,
        ExpressionSyntax condition,
        SyntaxNode use,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        StatementSyntax? body = ancestor switch
        {
            WhileStatementSyntax whileStatement => whileStatement.Statement,
            ForStatementSyntax forStatement => forStatement.Statement,
            _ => null
        };
        if (body == null) return false;

        var dependencies = condition.DescendantNodesAndSelf()
            .OfType<ExpressionSyntax>()
            .Select(expression => semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol?.OriginalDefinition)
            .OfType<ISymbol>()
            .Where(static symbol => symbol is ILocalSymbol or IParameterSymbol)
            .ToImmutableHashSet(SymbolEqualityComparer.Default);
        if (dependencies.Count == 0) return false;

        foreach (var candidate in body.DescendantNodesAndSelf(static node =>
                     !IsCallableBoundary(node)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (candidate.SpanStart >= use.SpanStart) continue;

            if (candidate is AssignmentExpressionSyntax assignment &&
                ReferencesAnyDependency(assignment.Left, dependencies, semanticModel, cancellationToken))
                return true;

            if (candidate is PrefixUnaryExpressionSyntax or PostfixUnaryExpressionSyntax &&
                semanticModel.GetOperation(candidate, cancellationToken) is IIncrementOrDecrementOperation increment &&
                ReferencesAnyDependency(increment.Target.Syntax, dependencies, semanticModel, cancellationToken))
                return true;

            if (candidate is InvocationExpressionSyntax &&
                semanticModel.GetOperation(candidate, cancellationToken) is IInvocationOperation invocation &&
                invocation.Arguments.Any(argument =>
                    argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out &&
                    ReferencesAnyDependency(
                        argument.Value.Syntax,
                        dependencies,
                        semanticModel,
                        cancellationToken)))
                return true;
        }

        return false;
    }

    private static bool ReferencesAnyDependency(
        SyntaxNode syntax,
        ImmutableHashSet<ISymbol> dependencies,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return syntax.DescendantNodesAndSelf()
            .OfType<ExpressionSyntax>()
            .Any(expression =>
                semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol is { } symbol &&
                dependencies.Contains(symbol.OriginalDefinition));
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

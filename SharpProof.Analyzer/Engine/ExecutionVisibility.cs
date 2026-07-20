namespace SharpProof.Analyzer.Engine;

internal static partial class ExecutionVisibility
{
    public static bool IsInStaticallyUnreachableBranchUsingSmt(
        SyntaxNode syntaxNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService? smtAnalysis = null)
    {
        foreach (var ancestor in syntaxNode.Ancestors())
        {
            if (CSharpSyntaxFacts.IsNestedLocalCallableBoundary(ancestor)) break;

            if (ancestor is not ForStatementSyntax &&
                TryGetEvaluationBranch(
                    ancestor,
                    syntaxNode.SpanStart,
                    out var condition,
                    out var branchWhenTrue,
                    out _) &&
                IsConditionTruthAt(
                    condition,
                    !branchWhenTrue,
                    ancestor,
                    semanticModel,
                    cancellationToken,
                    smtAnalysis))
                return true;

            if (ancestor is ConditionalAccessExpressionSyntax conditionalAccessExpression)
            {
                if (conditionalAccessExpression.WhenNotNull.Span.Contains(syntaxNode.SpanStart) &&
                    IsReferenceKnownNullStateAt(
                        conditionalAccessExpression.Expression,
                        expectedNull: true,
                        conditionalAccessExpression,
                        semanticModel,
                        cancellationToken,
                        smtAnalysis))
                    return true;
            }
            else if (ancestor is BinaryExpressionSyntax binaryExpression)
            {
                if (binaryExpression.IsKind(SyntaxKind.CoalesceExpression) &&
                    binaryExpression.Right.Span.Contains(syntaxNode.SpanStart))
                    if (IsReferenceKnownNullStateAt(
                            binaryExpression.Left,
                            expectedNull: false,
                            binaryExpression,
                            semanticModel,
                            cancellationToken,
                            smtAnalysis))
                        return true;
            }
            else if (ancestor is ForStatementSyntax forStatement)
            {
                if (forStatement.Condition != null &&
                    forStatement.Statement.Span.Contains(syntaxNode.SpanStart) &&
                    SymbolicReachabilityService.IsForInitialEntryConditionAlwaysFalse(
                        forStatement,
                        semanticModel,
                        cancellationToken,
                        smtAnalysis))
                    return true;
            }
            else if (ancestor is SwitchStatementSyntax switchStatement &&
                     IsInUnreachableSwitchStatementSection(syntaxNode, switchStatement, semanticModel,
                         cancellationToken, smtAnalysis))
            {
                return true;
            }
            else if (ancestor is SwitchExpressionSyntax switchExpression &&
                     IsInUnreachableSwitchExpressionArm(syntaxNode, switchExpression, semanticModel, cancellationToken,
                         smtAnalysis))
            {
                return true;
            }
        }

        return IsProgramPointUnreachableUsingSharedFacts(
            syntaxNode, semanticModel, cancellationToken, smtAnalysis);
    }

    public static bool IsEvaluationPathUnsatisfiableUsingSymbolicState(
        SyntaxNode syntaxNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SymbolicState basePathState,
        Func<ISymbol, int>? getSymbolVersion,
        SmtAnalysisService smtAnalysis)
    {
        var pathState = basePathState;
        foreach (var ancestor in syntaxNode.Ancestors())
        {
            var nextState = AddEvaluationPathState(
                pathState,
                syntaxNode,
                ancestor,
                semanticModel,
                cancellationToken,
                getSymbolVersion);
            if (ReferenceEquals(nextState, pathState)) continue;

            pathState = nextState;
            if (new SymbolicProofService(smtAnalysis).ClassifyReachability(pathState).Status ==
                SymbolicProofStatus.Unreachable)
                return true;
        }

        return false;
    }
}

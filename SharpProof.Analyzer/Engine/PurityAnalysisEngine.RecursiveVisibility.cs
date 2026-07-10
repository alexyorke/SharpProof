using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Analyzer.Engine;

internal partial class PurityAnalysisEngine
{
    private static bool ShouldSkipPostCfgDirectPurityProbe(
        IOperation operation,
        SemanticModel semanticModel,
        SmtAnalysisService smtAnalysis,
        CancellationToken cancellationToken)
    {
        if (operation.Syntax == null) return false;

        foreach (var syntax in GetOperationVisibilitySyntaxCandidates(operation.Syntax))
            if (ExecutionVisibility.IsInStaticallyUnreachableBranchUsingSmt(
                    syntax,
                    semanticModel,
                    cancellationToken,
                    smtAnalysis))
                return true;

        return false;
    }

    private static bool IsImpurityProvenUnreachable(
        PurityAnalysisResult result,
        SemanticModel semanticModel,
        SmtAnalysisService smtAnalysis,
        CancellationToken cancellationToken)
    {
        if (result.IsPure ||
            result.ImpureSyntaxNode == null)
            return false;

        foreach (var syntax in GetOperationVisibilitySyntaxCandidates(result.ImpureSyntaxNode))
            if (ExecutionVisibility.IsInStaticallyUnreachableBranchUsingSmt(
                    syntax,
                    semanticModel,
                    cancellationToken,
                    smtAnalysis))
                return true;

        return false;
    }

    private static IEnumerable<SyntaxNode> GetOperationVisibilitySyntaxCandidates(SyntaxNode syntax)
    {
        yield return syntax;

        foreach (var ancestor in syntax.Ancestors())
        {
            if (ancestor is ConditionalAccessExpressionSyntax conditionalAccess &&
                conditionalAccess.WhenNotNull.Span.Contains(syntax.SpanStart))
            {
                yield return conditionalAccess.WhenNotNull;
                continue;
            }

            if (ancestor is BinaryExpressionSyntax binaryExpression &&
                binaryExpression.IsKind(SyntaxKind.CoalesceExpression) &&
                binaryExpression.Right.Span.Contains(syntax.SpanStart))
            {
                yield return binaryExpression.Right;
                continue;
            }

            if (IsNestedCallableBoundary(ancestor)) yield break;
        }
    }

    private static bool IsNestedCallableBoundary(SyntaxNode syntax)
    {
        return syntax is MethodDeclarationSyntax or
            ConstructorDeclarationSyntax or
            OperatorDeclarationSyntax or
            AccessorDeclarationSyntax or
            LocalFunctionStatementSyntax or
            ParenthesizedLambdaExpressionSyntax or
            SimpleLambdaExpressionSyntax or
            AnonymousMethodExpressionSyntax;
    }
}
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Analyzer;

internal static partial class ExceptionFlowQuery
{
    private static bool IsCaughtWithinMethod(
        SyntaxNode throwNode,
        ITypeSymbol? exceptionType,
        SyntaxNode methodNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        foreach (var tryStatement in throwNode.Ancestors().OfType<TryStatementSyntax>())
        {
            if (!tryStatement.Span.Contains(throwNode.SpanStart)) continue;

            if (!tryStatement.Block.Span.Contains(throwNode.SpanStart)) continue;

            if (tryStatement.Catches.Any(catchClause => CatchesException(catchClause, exceptionType, throwNode,
                    semanticModel, cancellationToken, smtAnalysis))) return true;

            if (ReferenceEquals(tryStatement, methodNode)) break;
        }

        return false;
    }

    private static bool IsInStaticallyUnreachableBranch(
        SyntaxNode node,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        return !ExceptionFlowAnalyzer.IsExceptionPathReachable(node, semanticModel, cancellationToken, smtAnalysis);
    }

    private static bool IsShadowedByThrowingFinally(
        SyntaxNode node,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        return ExceptionFlowAnalyzer.IsShadowedByDefinitelyThrowingFinally(node) ||
               ExceptionFlowAnalyzer.IsShadowedByPathSensitiveThrowingFinally(
                   node,
                   semanticModel,
                   cancellationToken,
                   smtAnalysis);
    }

    private static bool CatchesException(
        CatchClauseSyntax catchClause,
        ITypeSymbol? exceptionType,
        SyntaxNode exceptionSite,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        if (!CatchDeclarationMatches(catchClause, exceptionType, semanticModel, cancellationToken)) return false;

        return IsCatchFilterProvenTrueAtSite(catchClause, exceptionSite, semanticModel, cancellationToken, smtAnalysis);
    }

    private static bool CatchDeclarationMatches(
        CatchClauseSyntax catchClause,
        ITypeSymbol? exceptionType,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (catchClause.Declaration == null) return true;

        if (exceptionType == null) return false;

        var catchType = semanticModel.GetTypeInfo(catchClause.Declaration.Type, cancellationToken).Type;
        return catchType != null && IsSameOrDerivedFrom(exceptionType, catchType);
    }

    private static bool IsCatchFilterProvenTrueAtSite(
        CatchClauseSyntax catchClause,
        SyntaxNode exceptionSite,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        if (catchClause.Filter?.FilterExpression is not { } filterExpression) return true;

        var constantValue = semanticModel.GetConstantValue(filterExpression, cancellationToken);
        if (constantValue.HasValue && constantValue.Value is bool booleanValue) return booleanValue;

        var pathConditions = ExceptionFlowAnalyzer.CollectExceptionSitePathConditions(
            exceptionSite,
            filterExpression,
            semanticModel,
            cancellationToken);
        return SymbolicReachabilityService.EvaluateConditionTruth(
            filterExpression,
            semanticModel,
            cancellationToken,
            smtAnalysis,
            pathConditions) ?? SymbolicReachabilityService.PathConditionsImplyBranchWithIrFirst(
            pathConditions,
            filterExpression,
            true,
            semanticModel,
            cancellationToken,
            smtAnalysis,
            "exception.path.condition",
            "exception.path.condition");
    }

    private static bool IsSameOrDerivedFrom(ITypeSymbol exceptionType, ITypeSymbol catchType)
    {
        for (var current = exceptionType; current != null; current = current.BaseType)
            if (SymbolEqualityComparer.Default.Equals(current, catchType))
                return true;

        return false;
    }

    private static bool IsRethrow(SyntaxNode throwNode)
    {
        return throwNode is ThrowStatementSyntax statement && statement.Expression == null;
    }
}
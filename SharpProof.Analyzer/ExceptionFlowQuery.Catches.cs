using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.Analyzer.Engine.Analysis;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Ir;
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
        // The CLR treats an exception raised while evaluating a catch filter as a false filter
        // result. The filter exception is swallowed and handler search continues for the original
        // exception, so the filter's own exception never escapes the method.
        if (throwNode.Ancestors().OfType<CatchFilterClauseSyntax>().Any()) return true;

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
        return !ExceptionPathStateService.IsExceptionPathReachable(
            node,
            semanticModel,
            cancellationToken,
            smtAnalysis);
    }

    private static bool IsShadowedByThrowingFinally(
        SyntaxNode node,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        return ExceptionSiteClassifier.IsShadowedByDefinitelyThrowingFinally(
                   node,
                   semanticModel,
                   cancellationToken) ||
               ExceptionPathStateService.IsShadowedByPathSensitiveThrowingFinally(
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
        return catchType != null && TypeHierarchyEnumeration.IsSameOrDerivedFrom(exceptionType, catchType);
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

        var pathState = ExceptionPathStateService.CollectExceptionSitePathState(
            exceptionSite,
            filterExpression,
            semanticModel,
            cancellationToken);
        var lowering = SymbolicSemanticPipeline.LowerCondition(
            filterExpression,
            new SymbolicLoweringContext(semanticModel, cancellationToken));
        return lowering is { IsExact: true, Value: { } condition } &&
               SymbolicReachabilityService.ClassifyStateConditionTruth(pathState, condition, smtAnalysis)
                   .Info.Status == SymbolicProofStatus.ProvenTrue;
    }

}

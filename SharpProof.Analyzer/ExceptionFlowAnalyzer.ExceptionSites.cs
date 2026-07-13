using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Analyzer;

internal static partial class ExceptionFlowAnalyzer
{
    internal static IEnumerable<SyntaxNode> GetThrowNodes(SyntaxNode methodNode)
    {
        return GetRelevantDescendants<SyntaxNode>(methodNode)
            .Where(node => node is ThrowStatementSyntax || node is ThrowExpressionSyntax);
    }

    internal static IEnumerable<BinaryExpressionSyntax> GetDefiniteDivideByZeroNodes(
        SyntaxNode methodNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        return GetDefiniteReachableDescendants<BinaryExpressionSyntax>(
            methodNode,
            semanticModel,
            cancellationToken,
            smtAnalysis,
            binaryExpression =>
                (binaryExpression.IsKind(SyntaxKind.DivideExpression) ||
                 binaryExpression.IsKind(SyntaxKind.ModuloExpression)) &&
                IsThrowingDivideByZeroExpression(binaryExpression.Right, semanticModel, cancellationToken) &&
                IsDefinitelyZeroExpression(binaryExpression.Right, binaryExpression, semanticModel, cancellationToken,
                    smtAnalysis));
    }

    internal static IEnumerable<SyntaxNode> GetDefiniteCheckedIntegralOverflowNodes(
        SyntaxNode methodNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        return GetDefiniteReachableDescendants(
            methodNode,
            semanticModel,
            cancellationToken,
            smtAnalysis,
            node => node switch
            {
                BinaryExpressionSyntax binaryExpression =>
                    IsDefinitelyCheckedIntegralOverflow(binaryExpression, semanticModel, cancellationToken,
                        smtAnalysis),
                PrefixUnaryExpressionSyntax unaryExpression =>
                    IsDefinitelyCheckedIntegralOverflow(unaryExpression, semanticModel, cancellationToken,
                        smtAnalysis),
                PostfixUnaryExpressionSyntax unaryExpression =>
                    IsDefinitelyCheckedIntegralOverflow(unaryExpression, semanticModel, cancellationToken,
                        smtAnalysis),
                CastExpressionSyntax castExpression =>
                    IsDefinitelyCheckedIntegralOverflow(castExpression, semanticModel, cancellationToken,
                        smtAnalysis),
                _ => false
            });
    }

    internal static IEnumerable<ArrayCreationExpressionSyntax> GetDefiniteNegativeArrayLengthNodes(
        SyntaxNode methodNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        return GetDefiniteReachableDescendants<ArrayCreationExpressionSyntax>(
            methodNode,
            semanticModel,
            cancellationToken,
            smtAnalysis,
            arrayCreation =>
                IsDefinitelyNegativeArrayLength(arrayCreation, semanticModel, cancellationToken, smtAnalysis));
    }

    internal static IEnumerable<SyntaxNode> GetDefiniteNullDereferenceNodes(
        SyntaxNode methodNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        foreach (var node in GetRelevantDescendants<SyntaxNode>(methodNode))
            if (node is MemberAccessExpressionSyntax memberAccess &&
                IsReferenceDereferenceReceiver(memberAccess.Expression, semanticModel, cancellationToken) &&
                IsDefinitelyNullExpression(memberAccess.Expression, memberAccess, semanticModel, cancellationToken,
                    smtAnalysis) &&
                IsExceptionPathReachable(memberAccess, semanticModel, cancellationToken, smtAnalysis))
                yield return memberAccess;
            else if (node is ElementAccessExpressionSyntax elementAccess &&
                     IsReferenceDereferenceReceiver(elementAccess.Expression, semanticModel, cancellationToken) &&
                     IsDefinitelyNullExpression(elementAccess.Expression, elementAccess, semanticModel,
                         cancellationToken, smtAnalysis) &&
                     IsExceptionPathReachable(elementAccess, semanticModel, cancellationToken, smtAnalysis))
                yield return elementAccess;
            else if (node is InvocationExpressionSyntax invocation &&
                     !IsDynamicExpression(invocation.Expression, semanticModel, cancellationToken) &&
                     IsDefinitelyNullExpression(invocation.Expression, invocation, semanticModel, cancellationToken,
                         smtAnalysis) &&
                     IsExceptionPathReachable(invocation, semanticModel, cancellationToken, smtAnalysis))
                yield return invocation;
            else if (node is AwaitExpressionSyntax awaitExpression &&
                     IsReferenceDereferenceReceiver(awaitExpression.Expression, semanticModel, cancellationToken) &&
                     IsDefinitelyNullExpression(awaitExpression.Expression, awaitExpression, semanticModel,
                         cancellationToken, smtAnalysis) &&
                     IsExceptionPathReachable(awaitExpression, semanticModel, cancellationToken, smtAnalysis))
                yield return awaitExpression;
    }

    internal static IEnumerable<LockStatementSyntax> GetDefiniteLockNullNodes(
        SyntaxNode methodNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        return GetDefiniteReachableDescendants<LockStatementSyntax>(
            methodNode,
            semanticModel,
            cancellationToken,
            smtAnalysis,
            lockStatement =>
                IsReferenceDereferenceReceiver(lockStatement.Expression, semanticModel, cancellationToken) &&
                IsDefinitelyNullExpression(lockStatement.Expression, lockStatement, semanticModel, cancellationToken,
                    smtAnalysis));
    }

    internal static IEnumerable<DynamicNullBindingSite> GetDefiniteDynamicNullBindingSites(
        SyntaxNode methodNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        foreach (var node in GetRelevantDescendants<SyntaxNode>(methodNode))
            if (SymbolicDynamicNullBindingFacts.TryGetDynamicNullBindingShape(
                    node,
                    UnwrapFactExpression,
                    out var site,
                    out var receiver,
                    out var category,
                    out var source) &&
                IsDefiniteDynamicNullReceiver(receiver, site, semanticModel, cancellationToken, smtAnalysis))
                yield return new DynamicNullBindingSite(
                    site,
                    category,
                    source);
    }

    private static bool IsDefiniteDynamicNullReceiver(
        ExpressionSyntax receiver,
        SyntaxNode site,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        return IsDynamicExpression(receiver, semanticModel, cancellationToken) &&
               IsDefinitelyNullExpression(receiver, site, semanticModel, cancellationToken, smtAnalysis) &&
               IsExceptionPathReachable(site, semanticModel, cancellationToken, smtAnalysis);
    }

    private static bool IsReferenceDereferenceReceiver(
        ExpressionSyntax receiver,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return !IsDynamicExpression(receiver, semanticModel, cancellationToken) &&
               IsReferenceType(GetExpressionType(receiver, semanticModel, cancellationToken));
    }

    private static IEnumerable<TNode> GetDefiniteReachableDescendants<TNode>(
        SyntaxNode methodNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis,
        Func<TNode, bool> isDefinite)
        where TNode : SyntaxNode
    {
        return GetDefiniteDescendants<TNode>(
            methodNode,
            node =>
                isDefinite(node) &&
                IsExceptionPathReachable(node, semanticModel, cancellationToken, smtAnalysis));
    }

    private static IEnumerable<SyntaxNode> GetDefiniteReachableDescendants(
        SyntaxNode methodNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis,
        Func<SyntaxNode, bool> isDefinite)
    {
        return GetDefiniteDescendants(
            methodNode,
            node =>
                isDefinite(node) &&
                IsExceptionPathReachable(node, semanticModel, cancellationToken, smtAnalysis));
    }

    private static IEnumerable<TNode> GetDefiniteDescendants<TNode>(
        SyntaxNode methodNode,
        Func<TNode, bool> isDefinite)
        where TNode : SyntaxNode
    {
        foreach (var node in GetRelevantDescendants<TNode>(methodNode))
            if (isDefinite(node))
                yield return node;
    }

    private static IEnumerable<SyntaxNode> GetDefiniteDescendants(
        SyntaxNode methodNode,
        Func<SyntaxNode, bool> isDefinite)
    {
        foreach (var node in GetRelevantDescendants<SyntaxNode>(methodNode))
            if (isDefinite(node))
                yield return node;
    }

    internal static IEnumerable<MemberAccessExpressionSyntax> GetDefiniteNullableValueAccessNodes(
        SyntaxNode methodNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        return GetDefiniteDescendants<MemberAccessExpressionSyntax>(
            methodNode,
            memberAccess =>
                IsNullableValueAccess(memberAccess, semanticModel, cancellationToken) &&
                IsDefinitelyMissingNullableValue(memberAccess, semanticModel, cancellationToken, smtAnalysis));
    }

    internal static IEnumerable<CastExpressionSyntax> GetDefiniteUnboxNullCastNodes(
        SyntaxNode methodNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        return GetDefiniteReachableDescendants<CastExpressionSyntax>(
            methodNode,
            semanticModel,
            cancellationToken,
            smtAnalysis,
            castExpression => IsDefinitelyUnboxNullCast(castExpression, semanticModel, cancellationToken, smtAnalysis));
    }

    internal static IEnumerable<CastExpressionSyntax> GetDefiniteInvalidCastNodes(
        SyntaxNode methodNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        return GetDefiniteReachableDescendants<CastExpressionSyntax>(
            methodNode,
            semanticModel,
            cancellationToken,
            smtAnalysis,
            castExpression => IsDefinitelyInvalidCast(castExpression, semanticModel, cancellationToken, smtAnalysis));
    }

    internal static IEnumerable<AssignmentExpressionSyntax> GetDefiniteArrayTypeMismatchStoreNodes(
        SyntaxNode methodNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        return GetDefiniteReachableDescendants<AssignmentExpressionSyntax>(
            methodNode,
            semanticModel,
            cancellationToken,
            smtAnalysis,
            assignment =>
                IsDefinitelyArrayTypeMismatchStore(assignment, semanticModel, cancellationToken, smtAnalysis));
    }

    internal static IEnumerable<ElementAccessExpressionSyntax> GetDefiniteIndexOutOfRangeNodes(
        SyntaxNode methodNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        return GetDefiniteDescendants<ElementAccessExpressionSyntax>(
            methodNode,
            elementAccess => IsDefinitelyOutOfRangeBuiltInElementAccess(
                elementAccess,
                semanticModel,
                cancellationToken,
                smtAnalysis,
                false));
    }

    internal static IEnumerable<InvocationExpressionSyntax> GetDefiniteArrayGetValueIndexOutOfRangeNodes(
        SyntaxNode methodNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        return GetDefiniteDescendants<InvocationExpressionSyntax>(
            methodNode,
            invocation =>
                IsDefinitelyOutOfRangeArrayGetValueCall(invocation, semanticModel, cancellationToken, smtAnalysis));
    }

    internal static IEnumerable<SyntaxNode> GetDefiniteArgumentOutOfRangeNodes(
        SyntaxNode methodNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        return GetDefiniteDescendants(
            methodNode,
            node => node switch
            {
                ElementAccessExpressionSyntax elementAccess => IsDefinitelyOutOfRangeBuiltInElementAccess(
                    elementAccess,
                    semanticModel,
                    cancellationToken,
                    smtAnalysis,
                    true),
                InvocationExpressionSyntax invocation =>
                    IsDefinitelyOutOfRangeBuiltInSliceCall(invocation, semanticModel, cancellationToken, smtAnalysis),
                _ => false
            });
    }

    internal static ITypeSymbol? GetThrownExceptionType(
        SyntaxNode throwNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (throwNode is not ThrowStatementSyntax and not ThrowExpressionSyntax) return null;

        return SymbolicRuntimeExceptionFacts.GetThrownExceptionType(
            throwNode,
            semanticModel,
            cancellationToken,
            true);
    }

    internal static bool IsDefinitelyThrowNull(
        SyntaxNode throwNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        return SymbolicRuntimeExceptionFacts.TryGetThrowExpression(throwNode, out var expression) &&
               IsDefinitelyNullExpression(expression, throwNode, semanticModel, cancellationToken, smtAnalysis);
    }

    internal static bool IsShadowedByDefinitelyThrowingFinally(
        SyntaxNode site,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var tryStatement in site.Ancestors().OfType<TryStatementSyntax>())
        {
            if (!tryStatement.Span.Contains(site.SpanStart)) continue;

            if (tryStatement.Finally == null ||
                !StatementDefinitelyExits(tryStatement.Finally.Block, semanticModel, cancellationToken))
                continue;

            if (tryStatement.Finally.Block.Span.Contains(site.SpanStart)) continue;

            if (tryStatement.Block.Span.Contains(site.SpanStart) ||
                tryStatement.Catches.Any(catchClause =>
                    catchClause.Block.Span.Contains(site.SpanStart) ||
                    catchClause.Filter?.Span.Contains(site.SpanStart) == true))
                return true;
        }

        return false;
    }

    internal readonly struct DynamicNullBindingSite
    {
        public DynamicNullBindingSite(SyntaxNode site, string category, string source)
        {
            Site = site;
            Category = category;
            Source = source;
        }

        public SyntaxNode Site { get; }

        public string Category { get; }

        public string Source { get; }
    }
}

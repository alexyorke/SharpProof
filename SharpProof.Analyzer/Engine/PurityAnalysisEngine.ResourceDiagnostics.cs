using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Symbolic.Ir;

namespace SharpProof.Analyzer.Engine;

internal partial class PurityAnalysisEngine
{
    internal static bool IsParameterlessDisposeInvocation(IInvocationOperation invocationOperation)
    {
        var targetMethod = invocationOperation.TargetMethod?.ReducedFrom ?? invocationOperation.TargetMethod;
        return targetMethod != null &&
               targetMethod.Parameters.Length == 0 &&
               targetMethod.Name is nameof(IDisposable.Dispose) or "DisposeAsync";
    }

    internal static bool HasDisposedResourceFact(PurityAnalysisState currentState, ISymbol resourceSymbol)
    {
        var term = CreateSymbolicReferenceTerm(resourceSymbol, currentState);
        return HasDisposedResourceFactForTerm(
            term,
            currentState,
            null,
            new HashSet<SymbolicTerm>());
    }

    private static bool HasDisposedResourceFactBefore(
        PurityAnalysisState currentState,
        ISymbol resourceSymbol,
        SyntaxNode observationSyntax)
    {
        var term = CreateSymbolicReferenceTerm(resourceSymbol, currentState);
        return HasDisposedResourceFactForTerm(
            term,
            currentState,
            observationSyntax,
            new HashSet<SymbolicTerm>());
    }

    internal static bool TryCreateUseAfterDisposeEvidence(
        IOperation useOperation,
        IOperation? resourceOperation,
        ISymbol usedMemberSymbol,
        PurityAnalysisState currentState,
        string ruleName,
        out PurityEvidence evidence)
    {
        evidence = PurityEvidence.None;
        if (TryResolveTrackedSymbol(resourceOperation, currentState) is not { } resourceSymbol ||
            !HasDisposedResourceFact(currentState, resourceSymbol))
            return false;

        evidence = PurityEvidence.Create(
            "resource_use_after_dispose",
            ruleName,
            useOperation,
            useOperation.Syntax,
            usedMemberSymbol,
            "symbolic_resource_lifetime");
        return true;
    }

    internal static bool TryCreateUseAfterDisposeEvidence(
        IOperation useOperation,
        IOperation? resourceOperation,
        ISymbol usedMemberSymbol,
        PurityAnalysisState currentState,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string ruleName,
        out PurityEvidence evidence)
    {
        if (TryCreateUseAfterDisposeEvidence(
                useOperation,
                resourceOperation,
                usedMemberSymbol,
                currentState,
                ruleName,
                out evidence))
            return true;

        if (TryResolveTrackedSymbol(resourceOperation, currentState) is not { } resourceSymbol ||
            (!WasResourceDisposedByEarlierUsingStatement(
                 resourceSymbol,
                 useOperation.Syntax,
                 currentState,
                 semanticModel,
                 cancellationToken) &&
             !WasResourceDisposedByEarlierRelatedLocal(
                 resourceSymbol,
                 useOperation.Syntax,
                 semanticModel,
                 cancellationToken)))
        {
            evidence = PurityEvidence.None;
            return false;
        }

        evidence = PurityEvidence.Create(
            "resource_use_after_dispose",
            ruleName,
            useOperation,
            useOperation.Syntax,
            usedMemberSymbol,
            "symbolic_resource_lifetime");
        return true;
    }

    internal static bool TryCreateDoubleDisposeEvidence(
        IInvocationOperation invocationOperation,
        IMethodSymbol invokedMethodSymbol,
        PurityAnalysisState currentState,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string ruleName,
        out PurityEvidence evidence)
    {
        evidence = PurityEvidence.None;
        if (!IsParameterlessDisposeInvocation(invocationOperation) ||
            invocationOperation.Instance == null ||
            TryResolveTrackedSymbol(invocationOperation.Instance, currentState) is not { } resourceSymbol ||
            (!HasDisposedResourceFact(currentState, resourceSymbol) &&
             !HasDisposedResourceFactBefore(currentState, resourceSymbol, invocationOperation.Syntax) &&
             !WasResourceDisposedByEarlierUsingStatement(
                 resourceSymbol,
                 invocationOperation.Syntax,
                 currentState,
                 semanticModel,
                 cancellationToken) &&
             !WasResourceDisposedByEarlierRelatedLocal(
                 resourceSymbol,
                 invocationOperation.Syntax,
                 semanticModel,
                 cancellationToken)))
            return false;

        evidence = PurityEvidence.Create(
            "resource_double_dispose",
            ruleName,
            invocationOperation,
            symbol: invokedMethodSymbol,
            catalogSource: "symbolic_resource_lifetime");
        return true;
    }

    private static bool WasResourceDisposedByEarlierUsingStatement(
        ISymbol resourceSymbol,
        SyntaxNode useSyntax,
        PurityAnalysisState currentState,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var containingBlock = useSyntax.FirstAncestorOrSelf<BlockSyntax>();
        if (containingBlock == null) return false;

        foreach (var usingStatement in containingBlock.DescendantNodes().OfType<UsingStatementSyntax>())
        {
            if (usingStatement.Span.End > useSyntax.SpanStart ||
                usingStatement.Statement == null)
                continue;

            if (usingStatement.Expression is { } usingExpression &&
                semanticModel.GetSymbolInfo(usingExpression, cancellationToken).Symbol is { } usingSymbol &&
                AreSymbolsSameOrAliased(resourceSymbol, usingSymbol, currentState))
                return true;

            if (usingStatement.Declaration == null) continue;

            foreach (var variable in usingStatement.Declaration.Variables)
                if (semanticModel.GetDeclaredSymbol(variable, cancellationToken) is { } declaredUsingSymbol &&
                    AreSymbolsSameOrAliased(resourceSymbol, declaredUsingSymbol, currentState))
                    return true;
        }

        foreach (var usingDeclaration in containingBlock.DescendantNodes().OfType<LocalDeclarationStatementSyntax>())
        {
            if (!usingDeclaration.UsingKeyword.IsKind(SyntaxKind.UsingKeyword)) continue;

            var declarationBlock = usingDeclaration.FirstAncestorOrSelf<BlockSyntax>();
            if (declarationBlock == null ||
                declarationBlock.Span.End > useSyntax.SpanStart)
                continue;

            foreach (var variable in usingDeclaration.Declaration.Variables)
                if (semanticModel.GetDeclaredSymbol(variable, cancellationToken) is { } declaredUsingSymbol &&
                    AreSymbolsSameOrAliased(resourceSymbol, declaredUsingSymbol, currentState))
                    return true;
        }

        return false;
    }

    private static bool WasResourceDisposedByEarlierRelatedLocal(
        ISymbol resourceSymbol,
        SyntaxNode useSyntax,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var containingBlock = useSyntax.FirstAncestorOrSelf<BlockSyntax>();
        if (containingBlock == null) return false;

        var relatedSymbols = GetRelatedLocalAliases(
            resourceSymbol,
            useSyntax,
            containingBlock,
            semanticModel,
            cancellationToken);
        foreach (var invocation in containingBlock.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.SpanStart >= useSyntax.SpanStart ||
                invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
                memberAccess.Name.Identifier.ValueText is not (nameof(IDisposable.Dispose) or "DisposeAsync") ||
                semanticModel.GetSymbolInfo(memberAccess.Expression, cancellationToken).Symbol is not
                { } disposedSymbol ||
                !relatedSymbols.Contains(disposedSymbol) ||
                !IsPriorDisposalSpanOnCompatiblePath(invocation.SpanStart, useSyntax) ||
                IsStaleRelatedLocalDisposal(
                    resourceSymbol,
                    disposedSymbol,
                    invocation.SpanStart,
                    useSyntax.SpanStart,
                    containingBlock,
                    semanticModel,
                    cancellationToken))
                continue;

            return true;
        }

        return false;
    }

    private static bool IsStaleRelatedLocalDisposal(
        ISymbol usedResourceSymbol,
        ISymbol disposedSymbol,
        int disposeSpanStart,
        int useSpanStart,
        BlockSyntax containingBlock,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (SymbolEqualityComparer.Default.Equals(usedResourceSymbol, disposedSymbol))
            return HasLocalReassignmentBetween(
                disposedSymbol,
                disposeSpanStart,
                useSpanStart,
                containingBlock,
                semanticModel,
                cancellationToken);

        foreach (var declarator in containingBlock.DescendantNodes().OfType<VariableDeclaratorSyntax>())
        {
            if (declarator.SpanStart >= disposeSpanStart ||
                declarator.Initializer?.Value == null ||
                semanticModel.GetDeclaredSymbol(declarator, cancellationToken) is not { } declaredSymbol ||
                !SymbolEqualityComparer.Default.Equals(declaredSymbol, usedResourceSymbol) ||
                semanticModel.GetSymbolInfo(declarator.Initializer.Value, cancellationToken).Symbol is not
                { } initializerSymbol ||
                !SymbolEqualityComparer.Default.Equals(initializerSymbol, disposedSymbol))
                continue;

            return HasLocalReassignmentBetween(
                disposedSymbol,
                declarator.SpanStart,
                disposeSpanStart,
                containingBlock,
                semanticModel,
                cancellationToken);
        }

        return false;
    }

    private static HashSet<ISymbol> GetRelatedLocalAliases(
        ISymbol resourceSymbol,
        SyntaxNode useSyntax,
        BlockSyntax containingBlock,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var relatedSymbols = new HashSet<ISymbol>(SymbolEqualityComparer.Default)
        {
            resourceSymbol
        };

        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var declarator in containingBlock.DescendantNodes().OfType<VariableDeclaratorSyntax>())
            {
                if (declarator.SpanStart >= useSyntax.SpanStart ||
                    declarator.Initializer?.Value == null ||
                    semanticModel.GetDeclaredSymbol(declarator, cancellationToken) is not { } declaredSymbol ||
                    semanticModel.GetSymbolInfo(declarator.Initializer.Value, cancellationToken).Symbol is not
                    { } initializerSymbol)
                    continue;

                if (relatedSymbols.Contains(declaredSymbol) && relatedSymbols.Add(initializerSymbol)) changed = true;

                if (relatedSymbols.Contains(initializerSymbol) &&
                    !HasLocalReassignmentBetween(
                        initializerSymbol,
                        declarator.SpanStart,
                        useSyntax.SpanStart,
                        containingBlock,
                        semanticModel,
                        cancellationToken) &&
                    relatedSymbols.Add(declaredSymbol))
                    changed = true;
            }
        }

        return relatedSymbols;
    }

    private static bool HasLocalReassignmentBetween(
        ISymbol symbol,
        int start,
        int end,
        BlockSyntax containingBlock,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var assignment in containingBlock.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            if (assignment.SpanStart <= start ||
                assignment.SpanStart >= end ||
                semanticModel.GetSymbolInfo(assignment.Left, cancellationToken).Symbol is not { } assignedSymbol)
                continue;

            if (SymbolEqualityComparer.Default.Equals(assignedSymbol, symbol)) return true;
        }

        return false;
    }

    private static bool AreSymbolsSameOrAliased(
        ISymbol first,
        ISymbol second,
        PurityAnalysisState currentState)
    {
        if (SymbolEqualityComparer.Default.Equals(first, second)) return true;

        var firstTerm = CreateSymbolicReferenceTerm(first, currentState);
        var secondTerm = CreateSymbolicReferenceTerm(second, currentState);
        return EnumerateSymbolicAliasTerms(firstTerm, currentState).Any(aliasTerm => Equals(aliasTerm, secondTerm)) ||
               EnumerateSymbolicAliasTerms(secondTerm, currentState).Any(aliasTerm => Equals(aliasTerm, firstTerm));
    }

    private static bool HasDisposedResourceFactForTerm(
        SymbolicTerm resourceTerm,
        PurityAnalysisState currentState,
        SyntaxNode? observationSyntax,
        HashSet<SymbolicTerm> visitedTerms)
    {
        if (!visitedTerms.Add(resourceTerm)) return false;

        foreach (var fact in currentState.PathState.Facts)
        {
            if (!fact.Polarity ||
                fact.Confidence != SymbolicFactConfidence.Exact ||
                (observationSyntax != null && !IsPriorDisposalFactOnCompatiblePath(fact, observationSyntax)))
                continue;

            if (IsDisposedResourceFactForTerm(fact, resourceTerm)) return true;
        }

        foreach (var aliasTerm in EnumerateSymbolicAliasTerms(resourceTerm, currentState))
            if (HasDisposedResourceFactForTerm(
                    aliasTerm,
                    currentState,
                    observationSyntax,
                    visitedTerms))
                return true;

        return false;
    }

    private static bool IsDisposedResourceFactForTerm(
        SymbolicFact fact,
        SymbolicTerm resourceTerm)
    {
        switch (fact.Atom)
        {
            case SymbolicDisposalAtom { State: SymbolicDisposalState.Disposed } disposal
                when Equals(disposal.Resource, resourceTerm):
                return true;
            case SymbolicResourceLifetimeAtom { State: SymbolicResourceLifetimeState.Released } lifetime
                when Equals(lifetime.Resource, resourceTerm) &&
                     IsMergedAllPathReleaseFact(fact):
                return true;
            default:
                return false;
        }
    }

    private static bool IsPriorDisposalFactOnCompatiblePath(
        SymbolicFact fact,
        SyntaxNode observationSyntax)
    {
        return IsPriorDisposalSpanOnCompatiblePath(fact.SourceSpan.Start, observationSyntax);
    }

    private static bool IsMergedAllPathReleaseFact(SymbolicFact fact)
    {
        return string.Equals(
            fact.Provenance,
            "analyzer.resource.merge.all-path-release",
            StringComparison.Ordinal);
    }

    private static bool IsPriorDisposalSpanOnCompatiblePath(
        int sourceSpanStart,
        SyntaxNode observationSyntax)
    {
        if (sourceSpanStart >= observationSyntax.SpanStart) return false;

        var observationSection = observationSyntax.FirstAncestorOrSelf<SwitchSectionSyntax>();
        if (observationSection == null) return true;

        var containingSwitch = observationSection.FirstAncestorOrSelf<SwitchStatementSyntax>();
        if (containingSwitch == null ||
            !containingSwitch.Span.Contains(sourceSpanStart))
            return true;

        return observationSection.Span.Contains(sourceSpanStart);
    }
}
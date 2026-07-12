using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SearchLib.Smt;
using SharpProof.Analyzer.Engine.Rules;
using SharpProof.Symbolic.Ir;

namespace SharpProof.Analyzer.Engine;

internal partial class PurityAnalysisEngine
{
    private static PurityAnalysisState AddReturnedOwnedResourceFacts(
        PurityAnalysisState nextState,
        IReturnOperation returnOperation,
        PurityAnalysisState currentState)
    {
        return returnOperation.ReturnedValue == null
            ? nextState
            : AddReturnedOwnedResourceFacts(nextState, returnOperation.ReturnedValue, currentState);
    }

    private static PurityAnalysisState AddReturnedOwnedResourceFacts(
        PurityAnalysisState nextState,
        IOperation returnedValue,
        PurityAnalysisState currentState)
    {
        if (TryResolveTrackedSymbol(returnedValue, currentState) is not { } resourceSymbol ||
            !HasSymbolicOwnedFactForSymbol(resourceSymbol, currentState))
            return nextState;

        var term = CreateSymbolicReferenceTerm(resourceSymbol, nextState);
        var returnedFact = SymbolicOwnershipFactFactory.CreateReturnedOwnership(
            term,
            returnedValue.Syntax,
            "analyzer.resource.returned",
            resourceSymbol,
            "evidence.resource.returned");
        var lifetimeFact = SymbolicOwnershipFactFactory.CreateResourceLifetime(
            term,
            SymbolicResourceLifetimeState.Returned,
            returnedValue.Syntax,
            "analyzer.resource.returned.lifetime",
            resourceSymbol,
            "evidence.resource.returned");

        var pathState = RemoveExclusiveResourceStateFacts(
            nextState.PathState,
            term,
            resourceSymbol,
            removeDisposal: false,
            removeLifetime: true);
        return nextState.WithPathState(pathState.AddFact(returnedFact).AddFact(lifetimeFact));
    }

    private static PurityAnalysisState AddDisposeInvocationFacts(
        PurityAnalysisState nextState,
        IInvocationOperation invocationOperation,
        PurityAnalysisState currentState)
    {
        if (!IsParameterlessDisposeInvocation(invocationOperation) ||
            invocationOperation.Instance == null ||
            TryResolveTrackedSymbol(invocationOperation.Instance, currentState) is not { } resourceSymbol)
            return nextState;

        var term = CreateSymbolicReferenceTerm(resourceSymbol, nextState);
        return AddResourceDisposedFacts(
            nextState,
            term,
            resourceSymbol,
            invocationOperation.Syntax,
            "analyzer.resource.dispose",
            "evidence.resource.dispose");
    }

    private static PurityAnalysisState AddUsingStatementDisposeFacts(
        PurityAnalysisState nextState,
        IUsingOperation usingOperation,
        PurityAnalysisState currentState)
    {
        foreach (var resourceSymbol in EnumerateUsingStatementDisposedSymbols(usingOperation, currentState))
        {
            var term = CreateSymbolicReferenceTerm(resourceSymbol, nextState);
            nextState = AddResourceDisposedFacts(
                nextState,
                term,
                resourceSymbol,
                usingOperation.Syntax,
                "analyzer.resource.using.dispose",
                "evidence.resource.using.dispose");
        }

        return nextState;
    }

    private static PurityAnalysisState AddUsingDeclarationDisposeFacts(
        PurityAnalysisState nextState,
        IUsingDeclarationOperation usingDeclaration)
    {
        foreach (var resourceSymbol in EnumerateUsingDeclarationDisposedSymbols(usingDeclaration))
        {
            var term = CreateSymbolicReferenceTerm(resourceSymbol, nextState);
            nextState = AddResourceDisposedFacts(
                nextState,
                term,
                resourceSymbol,
                usingDeclaration.Syntax,
                "analyzer.resource.using-declaration.dispose",
                "evidence.resource.using-declaration.dispose");
        }

        return nextState;
    }

    private static IEnumerable<ISymbol> EnumerateUsingDeclarationDisposedSymbols(
        IUsingDeclarationOperation usingDeclaration)
    {
        foreach (var declaration in usingDeclaration.DeclarationGroup.Declarations)
            foreach (var declarator in declaration.Declarators)
                yield return declarator.Symbol;
    }

    private static IEnumerable<ISymbol> EnumerateUsingStatementDisposedSymbols(
        IUsingOperation usingOperation,
        PurityAnalysisState currentState)
    {
        var resourceOperation = usingOperation.Resources;
        if (TryResolveTrackedSymbol(resourceOperation, currentState) is { } resourceSymbol)
        {
            yield return resourceSymbol;
            yield break;
        }

        if (resourceOperation is IVariableDeclarationGroupOperation declarationGroup)
            foreach (var declaration in declarationGroup.Declarations)
                foreach (var declarator in declaration.Declarators)
                    yield return declarator.Symbol;
        else if (resourceOperation is IVariableDeclarationOperation variableDeclaration)
            foreach (var declarator in variableDeclaration.Declarators)
                yield return declarator.Symbol;
    }

    private static PurityAnalysisState AddResourceDisposedFacts(
        PurityAnalysisState nextState,
        SymbolicTerm term,
        ISymbol resourceSymbol,
        SyntaxNode syntax,
        string provenance,
        string evidenceKey)
    {
        var disposedFact = SymbolicOwnershipFactFactory.CreateDisposal(
            term,
            SymbolicDisposalState.Disposed,
            syntax,
            provenance,
            resourceSymbol,
            evidenceKey);
        var releasedFact = SymbolicOwnershipFactFactory.CreateResourceLifetime(
            term,
            SymbolicResourceLifetimeState.Released,
            syntax,
            provenance + ".lifetime",
            resourceSymbol,
            evidenceKey);

        var pathState = RemoveExclusiveResourceStateFacts(
            nextState.PathState,
            term,
            resourceSymbol,
            removeDisposal: true,
            removeLifetime: true);
        return nextState.WithPathState(pathState.AddFact(disposedFact).AddFact(releasedFact));
    }

    private static SymbolicState RemoveExclusiveResourceStateFacts(
        SymbolicState pathState,
        SymbolicTerm resource,
        ISymbol resourceSymbol,
        bool removeDisposal,
        bool removeLifetime)
    {
        var facts = pathState.Facts
            .Where(fact => fact.Atom switch
            {
                SymbolicDisposalAtom disposal when removeDisposal =>
                    !Equals(disposal.Resource, resource) &&
                    !SymbolEqualityComparer.Default.Equals(fact.Symbol, resourceSymbol),
                SymbolicResourceLifetimeAtom lifetime when removeLifetime =>
                    !Equals(lifetime.Resource, resource) &&
                    !SymbolEqualityComparer.Default.Equals(fact.Symbol, resourceSymbol),
                _ => true
            })
            .ToArray();
        return facts.Length == pathState.Facts.Length
            ? pathState
            : new SymbolicState(facts, pathState.PathConditions, pathState.SymbolVersions);
    }

    private static PurityAnalysisState AddCallerVisibleMutationFact(
        PurityAnalysisState nextState,
        IOperation targetOperation,
        PurityAnalysisState currentState,
        SyntaxNode syntax)
    {
        if (!TryCreateCallerVisibleMutationTerm(targetOperation, currentState, out var term, out var symbol))
            return nextState;

        var mutationFact = SymbolicOwnershipFactFactory.CreateMutation(
            term,
            true,
            syntax,
            "analyzer.mutation.caller-visible",
            symbol,
            "evidence.mutation.caller-visible");

        return nextState.WithPathState(
            nextState.PathState.AddFact(mutationFact));
    }

    internal static bool TryCreateCallerVisibleMutationEvidence(
        IOperation operation,
        IOperation targetOperation,
        PurityAnalysisState currentState,
        string ruleName,
        out PurityEvidence evidence)
    {
        if (!TryCreateCallerVisibleMutationTerm(targetOperation, currentState, out var term, out var symbol))
        {
            evidence = default;
            return false;
        }

        var mutationFact = SymbolicOwnershipFactFactory.CreateMutation(
            term,
            true,
            targetOperation.Syntax,
            "analyzer.mutation.caller-visible",
            symbol,
            "evidence.mutation.caller-visible");
        if (mutationFact.Atom is not SymbolicMutationAtom { CallerVisible: true })
        {
            evidence = default;
            return false;
        }

        evidence = PurityEvidence.Create(
            "mutable_state_write",
            ruleName,
            operation,
            operation.Syntax,
            symbol,
            mutationFact.Provenance);
        return true;
    }

    internal static bool TryCreateReturnEscapeEvidence(
        IReturnOperation returnOperation,
        SyntaxNode escapeSyntax,
        ISymbol escapeSymbol,
        PurityAnalysisState currentState,
        string ruleName,
        string fallbackCatalogSource,
        out PurityEvidence evidence)
    {
        var escapeTerm = CreateSymbolicReferenceTerm(escapeSymbol, currentState);
        var escapeFact = SymbolicOwnershipFactFactory.CreateEscape(
            escapeTerm,
            SymbolicEscapeKind.Return,
            escapeSyntax,
            "analyzer.escape.return",
            escapeSymbol,
            "evidence.escape.return");
        if (escapeFact.Atom is not SymbolicEscapeAtom { Kind: SymbolicEscapeKind.Return })
        {
            evidence = default;
            return false;
        }

        evidence = PurityEvidence.Create(
            "mutable_state_escape",
            ruleName,
            returnOperation,
            escapeSyntax,
            escapeSymbol,
            string.IsNullOrEmpty(fallbackCatalogSource)
                ? escapeFact.Provenance
                : fallbackCatalogSource);
        return true;
    }

    private static PurityEvidence CreateByRefReturnEscapeEvidence(
        IMethodSymbol methodSymbol,
        SyntaxNode escapeSyntax)
    {
        var escapeTerm = new SymbolicVariableTerm(
            methodSymbol.ToDisplayString(_signatureFormat),
            SmtValueKind.Reference);
        var escapeFact = SymbolicOwnershipFactFactory.CreateEscape(
            escapeTerm,
            SymbolicEscapeKind.Return,
            escapeSyntax,
            "analyzer.escape.return.byref",
            methodSymbol,
            "evidence.escape.return.byref");

        var catalogSource = escapeFact.Atom is SymbolicEscapeAtom { Kind: SymbolicEscapeKind.Return }
            ? escapeFact.Provenance
            : "return_by_ref";
        return PurityEvidence.Create(
            "mutable_state_escape",
            "ReturnByRefAnalysis",
            syntaxNode: escapeSyntax,
            symbol: methodSymbol,
            catalogSource: catalogSource);
    }

    internal static bool TryCreateCallerVisibleMutationTerm(
        IOperation targetOperation,
        PurityAnalysisState currentState,
        out SymbolicTerm term,
        out ISymbol? symbol)
    {
        var unwrappedTargetOperation = SkipImplicitConversions(targetOperation);
        if (unwrappedTargetOperation == null)
        {
            symbol = null;
            term = null!;
            return false;
        }

        targetOperation = unwrappedTargetOperation;
        switch (targetOperation)
        {
            case IParameterReferenceOperation parameterReference:
                symbol = parameterReference.Parameter;
                term = CreateSymbolicReferenceTerm(parameterReference.Parameter, currentState);
                return true;

            case IFieldReferenceOperation fieldReference:
                symbol = fieldReference.Field;
                term = CreateSymbolicReferenceTerm(fieldReference.Field, currentState);
                return true;

            case IPropertyReferenceOperation propertyReference:
                symbol = propertyReference.Property;
                term = CreateSymbolicReferenceTerm(propertyReference.Property, currentState);
                return true;

            case IArrayElementReferenceOperation arrayElementReference
                when TryResolveTrackedSymbol(arrayElementReference.ArrayReference, currentState) is IParameterSymbol
                    parameterSymbol:
                symbol = parameterSymbol;
                term = CreateSymbolicReferenceTerm(parameterSymbol, currentState);
                return true;

            default:
                symbol = null;
                term = null!;
                return false;
        }
    }

    private static PurityAnalysisState AddOwnedLocalArrayFacts(
        PurityAnalysisState nextState,
        ISymbol localSymbol,
        IOperation valueOperation)
    {
        var term = CreateSymbolicReferenceTerm(localSymbol, nextState);
        var pathState = nextState.PathState;
        var ownershipFacts = SymbolicOwnershipFactFactory.CreateFreshOwnedValue(
            term,
            valueOperation.Syntax,
            "analyzer.array.acquire",
            localSymbol,
            "evidence.array.acquire");
        foreach (var fact in ownershipFacts) pathState = pathState.AddFact(fact);

        return nextState.WithPathState(pathState);
    }

    private static PurityAnalysisState AddFreshMutableObjectFacts(
        PurityAnalysisState nextState,
        ISymbol localSymbol,
        IOperation valueOperation)
    {
        var unwrappedValue = SkipImplicitConversions(valueOperation);
        if (unwrappedValue is not IObjectCreationOperation objectCreationOperation ||
            !RuleAnalysisHelper.IsFreshMutableEscapingReferenceType(objectCreationOperation.Type))
            return nextState;

        var term = CreateSymbolicReferenceTerm(localSymbol, nextState);
        var pathState = nextState.PathState;
        var ownershipFacts = SymbolicOwnershipFactFactory.CreateFreshOwnedValue(
            term,
            valueOperation.Syntax,
            "analyzer.object.acquire",
            localSymbol,
            "evidence.object.acquire");
        foreach (var fact in ownershipFacts) pathState = pathState.AddFact(fact);

        return nextState.WithPathState(pathState);
    }

    private static PurityAnalysisState AddOwnedDisposableLocalFacts(
        PurityAnalysisState nextState,
        ISymbol localSymbol,
        IOperation valueOperation,
        Compilation compilation)
    {
        if (!IsOwnedDisposableObjectCreationValue(valueOperation, compilation)) return nextState;

        var term = CreateSymbolicReferenceTerm(localSymbol, nextState);
        if (HasReleasedResourceFact(term, nextState)) return nextState;

        var pathState = nextState.PathState;
        var ownershipFacts = SymbolicOwnershipFactFactory.CreateFreshOwned(
            term,
            valueOperation.Syntax,
            "analyzer.resource.acquire",
            localSymbol,
            "evidence.resource.acquire");
        foreach (var fact in ownershipFacts) pathState = pathState.AddFact(fact);

        pathState = pathState.AddFact(SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.NotEqual,
                term,
                new SymbolicNullTerm()),
            valueOperation.Syntax,
            "analyzer.resource.acquire.not-null",
            localSymbol,
            "evidence.resource.acquire.not-null"));

        pathState = pathState.AddFact(SymbolicOwnershipFactFactory.CreateDisposal(
            term,
            SymbolicDisposalState.NotDisposed,
            valueOperation.Syntax,
            "analyzer.resource.acquire.disposal",
            localSymbol,
            "evidence.resource.acquire"));

        return nextState.WithPathState(pathState);
    }

    private static bool HasReleasedResourceFact(SymbolicTerm term, PurityAnalysisState state)
    {
        var releasedResources = new HashSet<SymbolicTerm>();
        foreach (var fact in state.PathState.Facts)
            if (TryGetExactResourceRelease(fact, out var releasedResource, out _))
                releasedResources.Add(releasedResource);

        return IsResourceReleased(term, releasedResources, state, new HashSet<SymbolicTerm>());
    }

    private static bool IsOwnedDisposableObjectCreationValue(
        IOperation valueOperation,
        Compilation compilation)
    {
        var unwrappedValue = SkipImplicitConversions(valueOperation);
        return unwrappedValue is IObjectCreationOperation objectCreationOperation &&
               objectCreationOperation.Type is { } createdType &&
               IsDisposableResourceType(createdType, compilation);
    }

    private static bool IsDisposableResourceType(ITypeSymbol type, Compilation compilation)
    {
        if (type.SpecialType == SpecialType.System_IDisposable ||
            type.ToDisplayString() == "System.IAsyncDisposable")
            return true;

        return type.AllInterfaces.Any(static interfaceType =>
            interfaceType.SpecialType == SpecialType.System_IDisposable ||
            interfaceType.ToDisplayString() == "System.IAsyncDisposable");
    }

    private static bool IsUsingResourceDeclarator(IVariableDeclaratorOperation declarator)
    {
        foreach (var ancestor in declarator.Syntax.AncestorsAndSelf())
        {
            if (ancestor is UsingStatementSyntax) return true;

            if (ancestor is LocalDeclarationStatementSyntax { UsingKeyword.RawKind: not 0 }) return true;
        }

        return false;
    }
}

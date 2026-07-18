using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Analyzer.Engine.Rules;
using SharpProof.Symbolic.Ir;
using PurityAnalysisState = SharpProof.Analyzer.Engine.PurityAnalysisEngine.PurityAnalysisState;
using PurityEvidence = SharpProof.Analyzer.Engine.PurityAnalysisEngine.PurityEvidence;

namespace SharpProof.Analyzer.Engine;

internal static partial class PurityResourceStateFacts
{
    internal static PurityAnalysisState AddReturnedOwnedResourceFacts(
        PurityAnalysisState nextState,
        IReturnOperation returnOperation,
        PurityAnalysisState currentState)
    {
        return returnOperation.ReturnedValue == null
            ? nextState
            : AddReturnedOwnedResourceFacts(nextState, returnOperation.ReturnedValue, currentState);
    }

    internal static PurityAnalysisState AddReturnedOwnedResourceFacts(
        PurityAnalysisState nextState,
        IOperation returnedValue,
        PurityAnalysisState currentState)
    {
        if (PurityAnalysisEngine.TryResolveTrackedSymbol(returnedValue, currentState) is not { } resourceSymbol ||
            !PuritySymbolicStateFacts.HasSymbolicOwnedFactForSymbol(resourceSymbol, currentState))
            return nextState;

        var term = PuritySymbolicStateFacts.CreateSymbolicReferenceTerm(resourceSymbol, nextState);
        return PurityOperationTransfer.ApplyLifetime(
            nextState,
            term,
            SymbolicLifetimeOperationKind.Return,
            returnedValue.Syntax,
            "analyzer.resource.returned",
            resourceSymbol,
            "evidence.resource.returned");
    }

    internal static PurityAnalysisState AddDisposeInvocationFacts(
        PurityAnalysisState nextState,
        IInvocationOperation invocationOperation,
        PurityAnalysisState currentState)
    {
        if (!IsParameterlessDisposeInvocation(invocationOperation) ||
            invocationOperation.Instance == null ||
            PurityAnalysisEngine.TryResolveTrackedSymbol(invocationOperation.Instance, currentState) is not { } resourceSymbol)
            return nextState;

        var term = PuritySymbolicStateFacts.CreateSymbolicReferenceTerm(resourceSymbol, nextState);
        return AddResourceDisposedFacts(
            nextState,
            term,
            resourceSymbol,
            invocationOperation.Syntax,
            "analyzer.resource.dispose",
            "evidence.resource.dispose");
    }

    internal static PurityAnalysisState AddUsingStatementDisposeFacts(
        PurityAnalysisState nextState,
        IUsingOperation usingOperation,
        PurityAnalysisState currentState)
    {
        foreach (var resourceSymbol in EnumerateUsingStatementDisposedSymbols(usingOperation, currentState))
        {
            var term = PuritySymbolicStateFacts.CreateSymbolicReferenceTerm(resourceSymbol, nextState);
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

    internal static PurityAnalysisState AddUsingDeclarationDisposeFacts(
        PurityAnalysisState nextState,
        IUsingDeclarationOperation usingDeclaration)
    {
        foreach (var resourceSymbol in EnumerateUsingDeclarationDisposedSymbols(usingDeclaration))
        {
            var term = PuritySymbolicStateFacts.CreateSymbolicReferenceTerm(resourceSymbol, nextState);
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
        if (PurityAnalysisEngine.TryResolveTrackedSymbol(resourceOperation, currentState) is { } resourceSymbol)
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
        return PurityOperationTransfer.ApplyLifetime(
            nextState,
            term,
            SymbolicLifetimeOperationKind.Dispose,
            syntax,
            provenance,
            resourceSymbol,
            evidenceKey);
    }

    internal static PurityAnalysisState AddCallerVisibleMutationFact(
        PurityAnalysisState nextState,
        IOperation targetOperation,
        PurityAnalysisState currentState,
        SyntaxNode syntax)
    {
        if (!TryCreateCallerVisibleMutationTerm(targetOperation, currentState, out var term, out var symbol))
            return nextState;

        return nextState.WithPathState(SymbolicOperationTransferKernel.TransitionMutation(
            nextState.PathState,
            term,
            syntax.Span,
            "analyzer.mutation.caller-visible",
            symbol,
            "evidence.mutation.caller-visible").State);
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

        evidence = PurityEvidence.Create(
            "mutable_state_write",
            ruleName,
            operation,
            operation.Syntax,
            symbol,
            "analyzer.mutation.caller-visible");
        return true;
    }

    internal static PurityEvidence CreateReturnEscapeEvidence(
        IReturnOperation returnOperation,
        SyntaxNode escapeSyntax,
        ISymbol escapeSymbol,
        string ruleName,
        string fallbackCatalogSource)
    {
        return PurityEvidence.Create(
            "mutable_state_escape",
            ruleName,
            returnOperation,
            escapeSyntax,
            escapeSymbol,
            string.IsNullOrEmpty(fallbackCatalogSource)
                ? "analyzer.escape.return"
                : fallbackCatalogSource);
    }

    internal static PurityEvidence CreateByRefReturnEscapeEvidence(
        IMethodSymbol methodSymbol,
        SyntaxNode escapeSyntax)
    {
        return PurityEvidence.Create(
            "mutable_state_escape",
            "ReturnByRefAnalysis",
            syntaxNode: escapeSyntax,
            symbol: methodSymbol,
            catalogSource: "analyzer.escape.return.byref");
    }

    internal static bool TryCreateCallerVisibleMutationTerm(
        IOperation targetOperation,
        PurityAnalysisState currentState,
        out SymbolicTerm term,
        out ISymbol? symbol)
    {
        var unwrappedTargetOperation = PurityAnalysisEngine.SkipImplicitConversions(targetOperation);
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
                term = PuritySymbolicStateFacts.CreateSymbolicReferenceTerm(parameterReference.Parameter, currentState);
                return true;

            case IFieldReferenceOperation fieldReference:
                symbol = fieldReference.Field;
                term = PuritySymbolicStateFacts.CreateSymbolicReferenceTerm(fieldReference.Field, currentState);
                return true;

            case IPropertyReferenceOperation propertyReference:
                symbol = propertyReference.Property;
                term = PuritySymbolicStateFacts.CreateSymbolicReferenceTerm(propertyReference.Property, currentState);
                return true;

            case IArrayElementReferenceOperation arrayElementReference
                when PurityAnalysisEngine.TryResolveTrackedSymbol(arrayElementReference.ArrayReference, currentState) is IParameterSymbol
                    parameterSymbol:
                symbol = parameterSymbol;
                term = PuritySymbolicStateFacts.CreateSymbolicReferenceTerm(parameterSymbol, currentState);
                return true;

            default:
                symbol = null;
                term = null!;
                return false;
        }
    }

    internal static bool HasReleasedResourceFact(SymbolicTerm term, PurityAnalysisState state)
        => SymbolicStateMerger.HasExactResourceRelease(state.PathState, term);

    internal static bool IsOwnedDisposableObjectCreationValue(IOperation valueOperation)
    {
        var unwrappedValue = PurityAnalysisEngine.SkipImplicitConversions(valueOperation);
        return unwrappedValue is IObjectCreationOperation objectCreationOperation &&
               objectCreationOperation.Type is { } createdType &&
               IsDisposableResourceType(createdType);
    }

    private static bool IsDisposableResourceType(ITypeSymbol type)
    {
        if (type.SpecialType == SpecialType.System_IDisposable ||
            type.ToDisplayString() == "System.IAsyncDisposable")
            return true;

        return type.AllInterfaces.Any(static interfaceType =>
            interfaceType.SpecialType == SpecialType.System_IDisposable ||
            interfaceType.ToDisplayString() == "System.IAsyncDisposable");
    }
}

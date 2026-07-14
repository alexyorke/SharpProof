using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Analyzer.Engine.Rules;

internal partial class ReturnStatementPurityRule : IPurityRule
{
    private static bool TryFindReturnedInitializerArrayEscape(
        IOperation returnedValue,
        PurityAnalysisEngine.PurityAnalysisState currentState,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SyntaxNode escapeSyntax,
        out ISymbol escapeSymbol,
        out string catalogSource)
    {
        foreach (var (value, site) in EnumerateReturnedInitializerEscapeValues(
                     returnedValue,
                     semanticModel,
                     cancellationToken))
        {
            if (IsOwnedLocalArrayReturn(value, currentState, out var localSymbol))
            {
                escapeSyntax = value.Syntax;
                escapeSymbol = localSymbol;
                catalogSource = "owned_local_array_" + site + "_escape";
                return true;
            }

            if (IsKnownPureArrayFactoryReturn(value, semanticModel.Compilation, out var factoryMethod))
            {
                escapeSyntax = value.Syntax;
                escapeSymbol = factoryMethod;
                catalogSource = "array_factory_" + site + "_escape";
                return true;
            }
        }

        return NoReturnEscape(out escapeSyntax, out escapeSymbol, out catalogSource);
    }

    private static bool TryFindMutableCollectionReturnEscape(
        IOperation returnedValue,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SyntaxNode escapeSyntax,
        out ISymbol escapeSymbol,
        out string catalogSource)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var unwrappedReturnedValue = PurityAnalysisEngine.SkipImplicitConversions(returnedValue);
        if (unwrappedReturnedValue is IInvocationOperation invocationOperation &&
            PurityCalleeResolver.IsKnownMutableCollectionBoundaryType(invocationOperation.Type))
        {
            escapeSyntax = invocationOperation.Syntax;
            escapeSymbol = invocationOperation.TargetMethod.OriginalDefinition;
            catalogSource = "returned_mutable_collection_invocation";
            return true;
        }

        if (unwrappedReturnedValue is ILocalReferenceOperation localReference &&
            TryGetStableMutableCollectionLocalEscape(
                localReference.Local,
                returnedValue,
                semanticModel,
                cancellationToken,
                out escapeSyntax,
                out escapeSymbol,
                out catalogSource))
            return true;

        if (unwrappedReturnedValue is IConditionalOperation conditionalOperation)
        {
            foreach (var branch in EnumerateReachableConditionalValues(conditionalOperation))
                if (TryFindMutableCollectionReturnEscape(
                        branch,
                        semanticModel,
                        cancellationToken,
                        out escapeSyntax,
                        out escapeSymbol,
                        out catalogSource))
                    return true;

            return NoReturnEscape(out escapeSyntax, out escapeSymbol, out catalogSource);
        }

        if (unwrappedReturnedValue is ICoalesceOperation coalesceOperation)
            return TryFindMutableCollectionReturnEscape(
                       coalesceOperation.Value,
                       semanticModel,
                       cancellationToken,
                       out escapeSyntax,
                       out escapeSymbol,
                       out catalogSource) ||
                   TryFindMutableCollectionReturnEscape(
                       coalesceOperation.WhenNull,
                       semanticModel,
                       cancellationToken,
                       out escapeSyntax,
                       out escapeSymbol,
                       out catalogSource);

        return NoReturnEscape(out escapeSyntax, out escapeSymbol, out catalogSource);
    }

    private static bool TryFindReturnedInitializerMutableObjectEscape(
        IOperation returnedValue,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SyntaxNode escapeSyntax,
        out ISymbol escapeSymbol,
        out string catalogSource)
    {
        foreach (var (value, site) in EnumerateReturnedInitializerEscapeValues(
                     returnedValue,
                     semanticModel,
                     cancellationToken))
        {
            if (TryFindFreshMutableObjectReturnEscape(
                    value,
                    semanticModel,
                    null,
                    cancellationToken,
                    out escapeSyntax,
                    out escapeSymbol,
                    out _))
            {
                catalogSource = "fresh_mutable_object_" + site + "_escape";
                return true;
            }
        }

        return NoReturnEscape(out escapeSyntax, out escapeSymbol, out catalogSource);
    }

    private static IEnumerable<(IOperation Value, string Site)> EnumerateReturnedInitializerEscapeValues(
        IOperation returnedValue,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var assignment in returnedValue.DescendantsAndSelf().OfType<ISimpleAssignmentOperation>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return (assignment.Value, "initializer");
        }

        foreach (var objectCreation in returnedValue.DescendantsAndSelf().OfType<IObjectCreationOperation>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsConstructionWithEscapingParameters(objectCreation, semanticModel, cancellationToken)) continue;

            foreach (var argument in objectCreation.Arguments)
                yield return (argument.Value, "constructor");
        }
    }

    private static bool TryFindFreshMutableObjectReturnEscape(
        IOperation returnedValue,
        SemanticModel semanticModel,
        PurityAnalysisEngine.PurityAnalysisState? currentState,
        CancellationToken cancellationToken,
        out SyntaxNode escapeSyntax,
        out ISymbol escapeSymbol,
        out string catalogSource)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var unwrappedReturnedValue = PurityAnalysisEngine.SkipImplicitConversions(returnedValue);
        if (unwrappedReturnedValue is IObjectCreationOperation objectCreationOperation &&
            RuleAnalysisHelper.IsFreshMutableEscapingReferenceType(objectCreationOperation.Type))
        {
            escapeSyntax = objectCreationOperation.Syntax;
            escapeSymbol = objectCreationOperation.Constructor ?? (ISymbol)objectCreationOperation.Type!;
            catalogSource = "fresh_mutable_object_return";
            return true;
        }

        if (unwrappedReturnedValue is IInvocationOperation invocationOperation &&
            TryFindNestedCallableFreshMutableObjectReturnEscape(
                invocationOperation,
                semanticModel,
                cancellationToken,
                out escapeSyntax,
                out escapeSymbol,
                out catalogSource))
            return true;

        if (unwrappedReturnedValue is ILocalReferenceOperation localReference)
        {
            if (OwnedFreshMutableObjectClassifier.IsOwnedFreshMutableLocal(
                    localReference.Local,
                    returnedValue.Syntax,
                    semanticModel,
                    currentState,
                    cancellationToken))
            {
                escapeSyntax = returnedValue.Syntax;
                escapeSymbol = localReference.Local;
                catalogSource = "symbolic_fresh_mutable_object_return";
                return true;
            }

            if (TryGetStableMutableObjectLocalEscape(
                    localReference.Local,
                    returnedValue,
                    semanticModel,
                    cancellationToken,
                    out escapeSyntax,
                    out escapeSymbol,
                    out catalogSource))
                return true;
        }

        if (unwrappedReturnedValue is ITupleOperation tupleOperation)
            foreach (var element in tupleOperation.Elements)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (TryFindFreshMutableObjectReturnEscape(
                        element,
                        semanticModel,
                        currentState,
                        cancellationToken,
                        out escapeSyntax,
                        out escapeSymbol,
                        out catalogSource))
                {
                    catalogSource = catalogSource switch
                    {
                        "fresh_mutable_object_return" => "fresh_mutable_object_tuple_return",
                        "symbolic_fresh_mutable_object_return" => "symbolic_fresh_mutable_object_tuple_return",
                        _ => catalogSource
                    };
                    return true;
                }
            }

        if (unwrappedReturnedValue is IConditionalOperation conditionalOperation)
        {
            foreach (var branch in EnumerateReachableConditionalValues(conditionalOperation))
                if (TryFindFreshMutableObjectReturnEscape(
                        branch,
                        semanticModel,
                        currentState,
                        cancellationToken,
                        out escapeSyntax,
                        out escapeSymbol,
                        out catalogSource))
                    return true;

            return NoReturnEscape(out escapeSyntax, out escapeSymbol, out catalogSource);
        }

        if (unwrappedReturnedValue is ICoalesceOperation coalesceOperation)
            return TryFindFreshMutableObjectReturnEscape(
                       coalesceOperation.Value,
                       semanticModel,
                       currentState,
                       cancellationToken,
                       out escapeSyntax,
                       out escapeSymbol,
                       out catalogSource) ||
                   TryFindFreshMutableObjectReturnEscape(
                       coalesceOperation.WhenNull,
                       semanticModel,
                       currentState,
                       cancellationToken,
                       out escapeSyntax,
                       out escapeSymbol,
                       out catalogSource);

        return NoReturnEscape(out escapeSyntax, out escapeSymbol, out catalogSource);
    }

    private static IEnumerable<IOperation> EnumerateReachableConditionalValues(
        IConditionalOperation conditionalOperation)
    {
        if (RuleAnalysisHelper.TryGetConstantCondition(conditionalOperation, out var conditionValue))
        {
            var selectedBranch = conditionValue ? conditionalOperation.WhenTrue : conditionalOperation.WhenFalse;
            if (selectedBranch != null) yield return selectedBranch;
            yield break;
        }

        yield return conditionalOperation.WhenTrue!;
        if (conditionalOperation.WhenFalse != null) yield return conditionalOperation.WhenFalse;
    }

    private static bool TryFindNestedCallableFreshMutableObjectReturnEscape(
        IInvocationOperation invocationOperation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SyntaxNode escapeSyntax,
        out ISymbol escapeSymbol,
        out string catalogSource)
    {
        if (!PurityAnalysisEngine.TryGetSingleReturnedValueFromInvocation(
                invocationOperation,
                semanticModel,
                out var returnedOperation,
                out _,
                out var returnedSemanticModel,
                currentState: null,
                cancellationToken: cancellationToken) ||
            !TryFindFreshMutableObjectReturnEscape(
                returnedOperation,
                returnedSemanticModel,
                null,
                cancellationToken,
                out escapeSyntax,
                out escapeSymbol,
                out var nestedCatalogSource))
            return NoReturnEscape(out escapeSyntax, out escapeSymbol, out catalogSource);

        catalogSource = nestedCatalogSource.StartsWith("fresh_mutable_object_", StringComparison.Ordinal)
            ? "fresh_mutable_object_nested_callable_return"
            : nestedCatalogSource;
        return true;
    }

    private static bool TryGetStableMutableObjectLocalEscape(
        ILocalSymbol localSymbol,
        IOperation returnedValue,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SyntaxNode escapeSyntax,
        out ISymbol escapeSymbol,
        out string catalogSource)
    {
        return TryGetStableMutableObjectLocalEscape(
            localSymbol,
            returnedValue,
            semanticModel,
            new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default),
            cancellationToken,
            out escapeSyntax,
            out escapeSymbol,
            out catalogSource);
    }

    private static bool TryGetStableMutableCollectionLocalEscape(
        ILocalSymbol localSymbol,
        IOperation returnedValue,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SyntaxNode escapeSyntax,
        out ISymbol escapeSymbol,
        out string catalogSource)
    {
        if (!RuleAnalysisHelper.TryGetStableLocalInitializer(
                localSymbol,
                returnedValue.Syntax,
                semanticModel,
                new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default),
                cancellationToken,
                out _,
                out var initializerOperation))
            return NoReturnEscape(out escapeSyntax, out escapeSymbol, out catalogSource);

        initializerOperation = PurityAnalysisEngine.SkipImplicitConversions(initializerOperation);
        if (initializerOperation != null &&
            TryFindMutableCollectionReturnEscape(
                initializerOperation,
                semanticModel,
                cancellationToken,
                out escapeSyntax,
                out escapeSymbol,
                out var nestedCatalogSource))
        {
            catalogSource = nestedCatalogSource == "returned_mutable_collection_invocation"
                ? "returned_mutable_collection_local"
                : nestedCatalogSource;
            return true;
        }

        return NoReturnEscape(out escapeSyntax, out escapeSymbol, out catalogSource);
    }

    private static bool TryGetStableMutableObjectLocalEscape(
        ILocalSymbol localSymbol,
        IOperation returnedValue,
        SemanticModel semanticModel,
        HashSet<ILocalSymbol> visitedLocals,
        CancellationToken cancellationToken,
        out SyntaxNode escapeSyntax,
        out ISymbol escapeSymbol,
        out string catalogSource)
    {
        if (!visitedLocals.Add(localSymbol))
            return NoReturnEscape(out escapeSyntax, out escapeSymbol, out catalogSource);

        var declaratorSyntax = localSymbol.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax(cancellationToken))
            .OfType<VariableDeclaratorSyntax>()
            .FirstOrDefault();
        var initializerSyntax = declaratorSyntax?.Initializer?.Value;
        SyntaxNode declarationSyntax;
        if (declaratorSyntax != null && initializerSyntax != null)
        {
            declarationSyntax = declaratorSyntax;
            if (RuleAnalysisHelper.HasAssignmentToLocalBetweenDeclarationAndObservation(
                    localSymbol,
                    returnedValue.Syntax,
                    declaratorSyntax,
                    semanticModel,
                    cancellationToken))
                return NoReturnEscape(out escapeSyntax, out escapeSymbol, out catalogSource);
        }
        else if (TryGetDeconstructionElementInitializer(
                     localSymbol,
                     returnedValue.Syntax,
                     semanticModel,
                     cancellationToken,
                     out initializerSyntax,
                     out declarationSyntax))
        {
            if (RuleAnalysisHelper.HasAssignmentToLocalBetweenDeclarationAndObservation(
                    localSymbol,
                    returnedValue.Syntax,
                    declarationSyntax,
                    semanticModel,
                    cancellationToken))
                return NoReturnEscape(out escapeSyntax, out escapeSymbol, out catalogSource);
        }
        else
        {
            return NoReturnEscape(out escapeSyntax, out escapeSymbol, out catalogSource);
        }

        var initializerOperation =
            PurityAnalysisEngine.SkipImplicitConversions(semanticModel.GetOperation(initializerSyntax,
                cancellationToken));
        if (initializerOperation is IObjectCreationOperation objectCreationOperation &&
            RuleAnalysisHelper.IsFreshMutableEscapingReferenceType(objectCreationOperation.Type))
        {
            escapeSyntax = returnedValue.Syntax;
            escapeSymbol = objectCreationOperation.Constructor ?? (ISymbol)objectCreationOperation.Type!;
            catalogSource = "fresh_mutable_object_local_return";
            return true;
        }

        if (initializerOperation != null &&
            TryFindReturnedInitializerMutableObjectEscape(
                initializerOperation,
                semanticModel,
                cancellationToken,
                out escapeSyntax,
                out escapeSymbol,
                out var nestedCatalogSource))
        {
            catalogSource = nestedCatalogSource switch
            {
                "fresh_mutable_object_constructor_escape" => "fresh_mutable_object_local_constructor_escape",
                "fresh_mutable_object_initializer_escape" => "fresh_mutable_object_local_initializer_escape",
                _ => "fresh_mutable_object_local_escape"
            };
            return true;
        }

        if (initializerOperation is ILocalReferenceOperation localReference)
            return TryGetStableMutableObjectLocalEscape(
                localReference.Local,
                returnedValue,
                semanticModel,
                visitedLocals,
                cancellationToken,
                out escapeSyntax,
                out escapeSymbol,
                out catalogSource);

        return NoReturnEscape(out escapeSyntax, out escapeSymbol, out catalogSource);
    }
}

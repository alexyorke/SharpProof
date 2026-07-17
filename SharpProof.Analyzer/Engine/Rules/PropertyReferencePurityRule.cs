using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Analyzer.Engine.Rules;

internal partial class PropertyReferencePurityRule : IPurityRule
{
    public IEnumerable<OperationKind> ApplicableOperationKinds =>
        ImmutableArray.Create(OperationKind.PropertyReference);

    public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(IOperation operation, PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState)
    {
        if (!(operation is IPropertyReferenceOperation propertyReferenceOperation))
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        var propertySymbol = propertyReferenceOperation.Property;

        if (IsCompilerGeneratedArrayForeachCurrent(propertyReferenceOperation, context))
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        var argumentResult = CheckArguments(propertyReferenceOperation, context, currentState);
        if (!argumentResult.IsPure) return argumentResult;

        if (!propertySymbol.IsStatic &&
            propertyReferenceOperation.Instance != null &&
            PurityResourceStateFacts.TryCreateUseAfterDisposeEvidence(
                propertyReferenceOperation,
                propertyReferenceOperation.Instance,
                propertySymbol.GetMethod is ISymbol getterSymbolForUseAfterDispose
                    ? getterSymbolForUseAfterDispose
                    : propertySymbol,
                currentState,
                context.CancellationToken,
                nameof(PropertyReferencePurityRule),
                out var useAfterDisposeEvidence))
            return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                propertyReferenceOperation.Syntax,
                useAfterDisposeEvidence);

        if (IsArrayLengthProperty(propertyReferenceOperation)) return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        if (RuleAnalysisHelper.IsWriteOnlyAssignmentTarget(propertyReferenceOperation))
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        if (TryCheckDictionaryIndexerKeyDispatchPurity(propertyReferenceOperation, context,
                out var dictionaryIndexerResult)) return dictionaryIndexerResult;

        if (TryCheckSortedDictionaryIndexerComparisonDispatchPurity(propertyReferenceOperation, context,
                out var sortedDictionaryIndexerResult)) return sortedDictionaryIndexerResult;

        if (TryCheckFormattableStringFormatPurity(propertyReferenceOperation, context, out var formattableStringResult))
            return formattableStringResult;

        var getterSymbol = propertySymbol.GetMethod;
        var getterPolicy = getterSymbol == null
            ? null
            : PurityPolicyResolver.Resolve(
                getterSymbol,
                context.SemanticModel.Compilation,
                context.AttributePolicy);
        var hasAuthoritativeGetterPolicy = PurityPolicyResolver.IsAuthoritativeDeclaration(getterPolicy?.Winner);
        if (hasAuthoritativeGetterPolicy &&
            getterPolicy is { Decision: PurityPolicyDecision.Impure, Winner: { } impureWinner })
            return CreateImpureResult(
                propertyReferenceOperation,
                getterSymbol!,
                impureWinner.Category,
                impureWinner.CatalogSource);

        var isPureEnforcedProperty = hasAuthoritativeGetterPolicy &&
                                     getterPolicy?.Decision == PurityPolicyDecision.Pure;
        var hasTrustedGeneratedPurity = PurityAnalysisEngine.TryGetTrustedGeneratedPurityCoverage(
            getterSymbol,
            context.SemanticModel.Compilation,
            out var generatedPurity);
        var allowsKnownPureFallback = !hasTrustedGeneratedPurity;
        var requiresDispatchCheck = getterSymbol != null &&
                                    IsPotentiallyDispatchedProperty(propertySymbol, context.SemanticModel.Compilation);
        var dispatchGetterWasProvenPure = false;
        var knownImpureMemberSource = PurityCalleeResolver.GetKnownImpureMemberSource(propertySymbol);
        var hasConfiguredKnownImpureMember = string.Equals(
            knownImpureMemberSource,
            "config_known_impure",
            StringComparison.Ordinal);

        if (isPureEnforcedProperty && !requiresDispatchCheck)
        {
            if (propertyReferenceOperation.Instance != null)
            {
                var instanceResult = PurityAnalysisEngine.CheckSingleOperation(
                    propertyReferenceOperation.Instance,
                    context,
                    currentState);
                if (!instanceResult.IsPure) return instanceResult;
            }

            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }

        if (PurityCatalogSemantics.IsConfiguredKnownPureMember(propertySymbol) ||
            (getterSymbol != null && PurityCatalogSemantics.IsConfiguredKnownPureMember(getterSymbol)))
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        if (getterSymbol != null &&
            hasTrustedGeneratedPurity &&
            generatedPurity.IsPure &&
            IsTrustedGeneratedMetadataGetter(getterSymbol))
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        if (hasConfiguredKnownImpureMember)
            return CreateImpureResult(
                propertyReferenceOperation,
                propertySymbol,
                GetCatalogHitCategory(propertySymbol),
                knownImpureMemberSource!);

        if (propertySymbol.IsStatic &&
            hasTrustedGeneratedPurity &&
            generatedPurity.IsPure)
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        if (!requiresDispatchCheck &&
            getterSymbol != null &&
            PurityAnalysisEngine.IsMetadataSymbol(getterSymbol) &&
            !hasTrustedGeneratedPurity &&
            string.Equals(GetCatalogHitCategory(propertySymbol), "reflection_environment_source",
                StringComparison.Ordinal))
            return CreateReflectionEnvironmentSourceResult(
                propertySymbol,
                propertyReferenceOperation,
                knownImpureMemberSource ?? "reflection_environment_source");

        if (PurityCatalogSemantics.IsInConfiguredImpureNamespaceOrType(propertySymbol) &&
            !PurityCatalogSemantics.IsConfiguredKnownPureMember(propertySymbol) &&
            (getterSymbol == null || !PurityCatalogSemantics.IsConfiguredKnownPureMember(getterSymbol)))
            return CreateImpureResult(
                propertyReferenceOperation,
                propertySymbol,
                GetCatalogHitCategory(propertySymbol),
                "known_impure_namespace_or_type");

        if (knownImpureMemberSource != null && !hasTrustedGeneratedPurity)
            return CreateImpureResult(
                propertyReferenceOperation,
                propertySymbol,
                GetCatalogHitCategory(propertySymbol),
                knownImpureMemberSource);

        if (!requiresDispatchCheck && hasTrustedGeneratedPurity)
            if (!generatedPurity.IsPure)
                return CreateImpureResult(
                    propertyReferenceOperation,
                    getterSymbol!,
                    generatedPurity.PrimaryCategory,
                    "generated_purity_summary");

        if (!requiresDispatchCheck &&
            RuleAnalysisHelper.IsSourceAutoPropertyAccessor(
                propertySymbol,
                getter: true,
                cancellationToken: context.CancellationToken))
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        if (!requiresDispatchCheck &&
            propertySymbol.ContainingType is INamedTypeSymbol containingType &&
            containingType.IsAnonymousType &&
            propertySymbol.IsReadOnly)
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        if (requiresDispatchCheck)
        {
            var dispatchResult = CheckDispatchedGetterPurity(
                propertyReferenceOperation,
                context,
                currentState);
            if (!dispatchResult.IsPure) return dispatchResult;

            dispatchGetterWasProvenPure = true;
            if (isPureEnforcedProperty) return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }


        if (propertySymbol.IsStatic)
        {
            var cctorResult =
                PurityAnalysisEngine.CheckStaticConstructorPurity(propertySymbol.ContainingType, context);
            if (!cctorResult.IsPure)
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    cctorResult.ImpureSyntaxNode ?? propertyReferenceOperation.Syntax,
                    cctorResult.Evidence);


            var staticKnownPure = allowsKnownPureFallback &&
                                  PurityCatalogSemantics.IsKnownPureBCLMember(propertySymbol,
                                      context.SemanticModel.Compilation);

            if (staticKnownPure) return PurityAnalysisEngine.PurityAnalysisResult.Pure;

            if (allowsKnownPureFallback &&
                PurityAnalysisEngine.TryCreateBclFallbackImpurity(
                    propertySymbol,
                    propertyReferenceOperation.Syntax,
                    propertyReferenceOperation,
                    nameof(PropertyReferencePurityRule),
                    out var staticBclFallbackResult))
                return staticBclFallbackResult;

            return GetterResultOrImpure(propertySymbol, propertyReferenceOperation, context);
        }

        var instanceOperation = propertyReferenceOperation.Instance;


        if (instanceOperation == null)
            return PurityAnalysisEngine.PurityAnalysisResult.Impure(propertyReferenceOperation.Syntax);


        if (instanceOperation is IParameterReferenceOperation paramRef &&
            (paramRef.Parameter.RefKind == RefKind.In ||
             paramRef.Parameter.RefKind == RefKind.RefReadOnly ||
             paramRef.Parameter.RefKind == RefKind.RefReadOnlyParameter))
        {
            if (dispatchGetterWasProvenPure) return PurityAnalysisEngine.PurityAnalysisResult.Pure;

            return GetterResultOrImpure(propertySymbol, propertyReferenceOperation, context);
        }

        if (instanceOperation is IInstanceReferenceOperation instanceRef &&
            instanceRef.ReferenceKind == InstanceReferenceKind.ContainingTypeInstance)
        {
            if (dispatchGetterWasProvenPure) return PurityAnalysisEngine.PurityAnalysisResult.Pure;

            var isReadonlyStruct = context.ContainingMethodSymbol?.ContainingType is
            { IsReadOnly: true, IsValueType: true };

            if (isReadonlyStruct)
                return GetterResultOrImpure(propertySymbol, propertyReferenceOperation, context);

            if (propertySymbol.IsReadOnly)
                return GetterResultOrImpure(propertySymbol, propertyReferenceOperation, context);

            if (propertySymbol.GetMethod != null)
                return GetterResultOrImpure(propertySymbol, propertyReferenceOperation, context);

            return PurityAnalysisEngine.PurityAnalysisResult.Impure(propertyReferenceOperation.Syntax);
        }

        var instanceExprResult = PurityAnalysisEngine.CheckSingleOperation(instanceOperation, context, currentState);
        if (!instanceExprResult.IsPure) return instanceExprResult;

        if (dispatchGetterWasProvenPure) return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        var instanceKnownPure = allowsKnownPureFallback &&
                                PurityCatalogSemantics.IsKnownPureBCLMember(propertySymbol,
                                    context.SemanticModel.Compilation);

        if (instanceKnownPure) return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        if (allowsKnownPureFallback &&
            PurityAnalysisEngine.TryCreateBclFallbackImpurity(
                propertySymbol,
                propertyReferenceOperation.Syntax,
                propertyReferenceOperation,
                nameof(PropertyReferencePurityRule),
                out var instanceBclFallbackResult))
            return instanceBclFallbackResult;

        if (propertySymbol.GetMethod != null &&
            context.AttributePolicy.HasAttribute(propertySymbol.GetMethod, "PureAttribute"))
            return GetterResultOrImpure(propertySymbol, propertyReferenceOperation, context);

        if (propertySymbol.GetMethod != null)
            return GetterResultOrImpure(propertySymbol, propertyReferenceOperation, context);

        if (propertySymbol.GetMethod != null &&
            context.PurityCache.TryGetValue(propertySymbol.GetMethod.OriginalDefinition, out var cachedGetterResult) &&
            !cachedGetterResult.IsPure)
            return cachedGetterResult.WithCallee(propertySymbol.GetMethod, propertyReferenceOperation.Syntax);


        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
    }

    private static PurityAnalysisEngine.PurityAnalysisResult CreateImpureResult(
        IPropertyReferenceOperation operation,
        ISymbol symbol,
        string category,
        string source) =>
        PurityAnalysisEngine.PurityAnalysisResult.Impure(
            operation.Syntax,
            PurityAnalysisEngine.PurityEvidence.Create(
                category,
                nameof(PropertyReferencePurityRule),
                operation,
                operation.Syntax,
                symbol,
                source));
}

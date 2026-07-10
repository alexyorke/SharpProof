using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using SharpProof.Analyzer.Engine;
using SharpProof.Analyzer.Engine.Analysis;

namespace SharpProof.Analyzer.Engine.Rules
{

    internal partial class PropertyReferencePurityRule : IPurityRule
    {
        public IEnumerable<OperationKind> ApplicableOperationKinds => ImmutableArray.Create(OperationKind.PropertyReference);

        public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(IOperation operation, PurityAnalysisContext context, PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            if (!(operation is IPropertyReferenceOperation propertyReferenceOperation))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            IPropertySymbol propertySymbol = propertyReferenceOperation.Property;

            if (IsCompilerGeneratedArrayForeachCurrent(propertyReferenceOperation, context))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            var argumentResult = CheckArguments(propertyReferenceOperation, context, currentState);
            if (!argumentResult.IsPure)
            {
                return argumentResult;
            }

            if (!propertySymbol.IsStatic &&
                propertyReferenceOperation.Instance != null &&
                PurityAnalysisEngine.TryCreateUseAfterDisposeEvidence(
                    propertyReferenceOperation,
                    propertyReferenceOperation.Instance,
                    propertySymbol.GetMethod is ISymbol getterSymbolForUseAfterDispose
                        ? getterSymbolForUseAfterDispose
                        : propertySymbol,
                    currentState,
                    context.SemanticModel,
                    context.CancellationToken,
                    nameof(PropertyReferencePurityRule),
                    out var useAfterDisposeEvidence))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    propertyReferenceOperation.Syntax,
                    useAfterDisposeEvidence);
            }

            if (IsArrayLengthProperty(propertyReferenceOperation))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (IsPartOfAssignmentTarget(propertyReferenceOperation))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (TryCheckDictionaryIndexerKeyDispatchPurity(propertyReferenceOperation, context, out var dictionaryIndexerResult))
            {
                return dictionaryIndexerResult;
            }

            if (TryCheckSortedDictionaryIndexerComparisonDispatchPurity(propertyReferenceOperation, context, out var sortedDictionaryIndexerResult))
            {
                return sortedDictionaryIndexerResult;
            }

            if (TryCheckFormattableStringFormatPurity(propertyReferenceOperation, context, out var formattableStringResult))
            {
                return formattableStringResult;
            }

            var isPureEnforcedProperty = PurityAnalysisEngine.IsPureEnforced(
                propertySymbol,
                context.EnforcePureAttributeSymbol,
                context.PureAttributeSymbol);
            var getterSymbol = propertySymbol.GetMethod;
            var hasTrustedGeneratedPurity = PurityAnalysisEngine.TryGetTrustedGeneratedPurityCoverage(
                getterSymbol,
                context.SemanticModel.Compilation,
                out var generatedPurity);
            var allowsKnownPureFallback = !hasTrustedGeneratedPurity;
            var requiresDispatchCheck = getterSymbol != null &&
                IsPotentiallyDispatchedProperty(propertySymbol, context.SemanticModel.Compilation);
            var dispatchGetterWasProvenPure = false;
            var knownImpureMemberSource = PurityAnalysisEngine.GetKnownImpureMemberSource(propertySymbol);
            var hasConfiguredKnownImpureMember = string.Equals(
                knownImpureMemberSource,
                "config_known_impure",
                StringComparison.Ordinal);

            if (isPureEnforcedProperty && !requiresDispatchCheck)
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (PurityAnalysisEngine.IsConfiguredKnownPureMember(propertySymbol) ||
                (getterSymbol != null && PurityAnalysisEngine.IsConfiguredKnownPureMember(getterSymbol)))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (getterSymbol != null &&
                hasTrustedGeneratedPurity &&
                generatedPurity.IsPure &&
                IsTrustedGeneratedMetadataGetter(getterSymbol))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }



            string impureSig = propertySymbol.OriginalDefinition.ToDisplayString();
            if (hasConfiguredKnownImpureMember)
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    propertyReferenceOperation.Syntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        GetCatalogHitCategory(propertySymbol),
                        ruleName: nameof(PropertyReferencePurityRule),
                        operation: propertyReferenceOperation,
                        syntaxNode: propertyReferenceOperation.Syntax,
                        symbol: propertySymbol,
                        catalogSource: knownImpureMemberSource));
            }

            if (propertySymbol.IsStatic &&
                hasTrustedGeneratedPurity &&
                generatedPurity.IsPure)
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (!requiresDispatchCheck &&
                getterSymbol != null &&
                PurityAnalysisEngine.IsMetadataSymbol(getterSymbol) &&
                !hasTrustedGeneratedPurity &&
                string.Equals(GetCatalogHitCategory(propertySymbol), "reflection_environment_source", StringComparison.Ordinal))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    propertyReferenceOperation.Syntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        "reflection_environment_source",
                        ruleName: nameof(PropertyReferencePurityRule),
                        operation: propertyReferenceOperation,
                        syntaxNode: propertyReferenceOperation.Syntax,
                        symbol: propertySymbol,
                        catalogSource: knownImpureMemberSource ?? "reflection_environment_source"));
            }

            if (PurityAnalysisEngine.IsInConfiguredImpureNamespaceOrType(propertySymbol) &&
                !PurityAnalysisEngine.IsConfiguredKnownPureMember(propertySymbol) &&
                (getterSymbol == null || !PurityAnalysisEngine.IsConfiguredKnownPureMember(getterSymbol)))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    propertyReferenceOperation.Syntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        GetCatalogHitCategory(propertySymbol),
                        ruleName: nameof(PropertyReferencePurityRule),
                        operation: propertyReferenceOperation,
                        syntaxNode: propertyReferenceOperation.Syntax,
                        symbol: propertySymbol,
                        catalogSource: "known_impure_namespace_or_type"));
            }

            if (knownImpureMemberSource != null && !hasTrustedGeneratedPurity)
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    propertyReferenceOperation.Syntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        GetCatalogHitCategory(propertySymbol),
                        ruleName: nameof(PropertyReferencePurityRule),
                        operation: propertyReferenceOperation,
                        syntaxNode: propertyReferenceOperation.Syntax,
                        symbol: propertySymbol,
                        catalogSource: knownImpureMemberSource));
            }

            if (!requiresDispatchCheck && hasTrustedGeneratedPurity)
            {
                if (!generatedPurity.IsPure)
                {
                    return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                        propertyReferenceOperation.Syntax,
                        PurityAnalysisEngine.PurityEvidence.Create(
                            generatedPurity.PrimaryCategory,
                            ruleName: nameof(PropertyReferencePurityRule),
                            operation: propertyReferenceOperation,
                            syntaxNode: propertyReferenceOperation.Syntax,
                            symbol: getterSymbol,
                        catalogSource: "generated_purity_summary"));
                }
            }

            if (!requiresDispatchCheck && IsSourceAutoPropertyGetter(propertySymbol, context.CancellationToken))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (!requiresDispatchCheck &&
                propertySymbol.ContainingType is INamedTypeSymbol containingType &&
                containingType.IsAnonymousType &&
                propertySymbol.IsReadOnly)
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (requiresDispatchCheck)
            {
                var dispatchResult = CheckDispatchedGetterPurity(
                    propertyReferenceOperation,
                    context,
                    currentState);
                if (!dispatchResult.IsPure)
                {
                    return dispatchResult;
                }

                dispatchGetterWasProvenPure = true;
                if (isPureEnforcedProperty)
                {
                    return PurityAnalysisEngine.PurityAnalysisResult.Pure;
                }
            }


            if (propertySymbol.IsStatic)
            {


                var cctorResult = PurityAnalysisEngine.CheckStaticConstructorPurity(propertySymbol.ContainingType, context, currentState);
                if (!cctorResult.IsPure)
                {
                    return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                        cctorResult.ImpureSyntaxNode ?? propertyReferenceOperation.Syntax,
                        cctorResult.Evidence);
                }


                string staticPureSig = propertySymbol.OriginalDefinition.ToDisplayString();
                bool staticKnownPure = allowsKnownPureFallback &&
                    PurityAnalysisEngine.IsKnownPureBCLMember(propertySymbol, context.SemanticModel.Compilation);

                if (staticKnownPure)
                {
                    return PurityAnalysisEngine.PurityAnalysisResult.Pure;
                }

                if (allowsKnownPureFallback &&
                    PurityAnalysisEngine.TryCreateBclFallbackImpurity(
                        propertySymbol,
                        propertyReferenceOperation.Syntax,
                        propertyReferenceOperation,
                        nameof(PropertyReferencePurityRule),
                        out var staticBclFallbackResult))
                {
                    return staticBclFallbackResult;
                }

                return GetterResultOrImpure(propertySymbol, propertyReferenceOperation, context, $"static property '{propertySymbol.Name}'", $"Static property '{propertySymbol.Name}' has no accessible getter to analyze and is not a known pure BCL member");
            }
            else
            {
                IOperation? instanceOperation = propertyReferenceOperation.Instance;


                string instanceKind = instanceOperation?.Kind.ToString() ?? "null";
                string instanceSyntax = instanceOperation?.Syntax.ToString() ?? "null";

                if (instanceOperation == null)
                {

                    return PurityAnalysisEngine.PurityAnalysisResult.Impure(propertyReferenceOperation.Syntax);
                }


                if (instanceOperation is IParameterReferenceOperation paramRef &&
                    (paramRef.Parameter.RefKind == RefKind.In ||
                     paramRef.Parameter.RefKind == RefKind.RefReadOnly ||
                     paramRef.Parameter.RefKind == (RefKind)4))
                {
                    bool isValueStruct = paramRef.Parameter.Type.IsValueType && !paramRef.Parameter.Type.IsReferenceType;

                    if (dispatchGetterWasProvenPure)
                    {
                        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
                    }

                    return GetterResultOrImpure(propertySymbol, propertyReferenceOperation, context, $"property '{propertySymbol.Name}' on parameter '{paramRef.Parameter.Name}'", $"Instance '{paramRef.Parameter.Name}' has no accessible getter to analyze");
                }
                else if (instanceOperation is IInstanceReferenceOperation instanceRef && instanceRef.ReferenceKind == InstanceReferenceKind.ContainingTypeInstance)
                {
                    if (dispatchGetterWasProvenPure)
                    {
                        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
                    }

                    bool isReadonlyStruct = context.ContainingMethodSymbol?.ContainingType is { IsReadOnly: true, IsValueType: true };

                    if (isReadonlyStruct)
                    {

                        return GetterResultOrImpure(propertySymbol, propertyReferenceOperation, context, $"readonly struct property '{propertySymbol.Name}' on this", $"Instance is 'this' within a readonly struct, but property '{propertySymbol.Name}' has no accessible getter to analyze");
                    }
                    else if (propertySymbol.IsReadOnly)
                    {
                        return GetterResultOrImpure(propertySymbol, propertyReferenceOperation, context, $"readonly property '{propertySymbol.Name}' on this", $"Instance is 'this', property '{propertySymbol.Name}' has no accessible getter to analyze");
                    }
                    else if (propertySymbol.GetMethod != null)
                    {
                        return GetterResultOrImpure(propertySymbol, propertyReferenceOperation, context, $"property '{propertySymbol.Name}' on this");
                    }
                    else
                    {
                        return PurityAnalysisEngine.PurityAnalysisResult.Impure(propertyReferenceOperation.Syntax);
                    }
                }
                else
                {
                    var instanceExprResult = PurityAnalysisEngine.CheckSingleOperation(instanceOperation, context, currentState);
                    if (!instanceExprResult.IsPure)
                    {
                        return instanceExprResult;
                    }

                    if (dispatchGetterWasProvenPure)
                    {
                        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
                    }

                    string instancePureSig = propertySymbol.OriginalDefinition.ToDisplayString();
                    bool instanceKnownPure = allowsKnownPureFallback &&
                        PurityAnalysisEngine.IsKnownPureBCLMember(propertySymbol, context.SemanticModel.Compilation);

                    if (instanceKnownPure)
                    {
                        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
                    }

                    if (allowsKnownPureFallback &&
                        PurityAnalysisEngine.TryCreateBclFallbackImpurity(
                            propertySymbol,
                            propertyReferenceOperation.Syntax,
                            propertyReferenceOperation,
                            nameof(PropertyReferencePurityRule),
                            out var instanceBclFallbackResult))
                    {
                        return instanceBclFallbackResult;
                    }

                    else if (propertySymbol.GetMethod != null && context.PureAttributeSymbol != null &&
                             PurityAnalysisEngine.HasAttribute(propertySymbol.GetMethod, context.PureAttributeSymbol))
                    {
                        return GetterResultOrImpure(propertySymbol, propertyReferenceOperation, context, $"[Pure] property '{propertySymbol.Name}'");
                    }

                    else if (propertySymbol.GetMethod != null)
                    {
                        return GetterResultOrImpure(propertySymbol, propertyReferenceOperation, context, $"complex instance ({instanceKind}) property '{propertySymbol.Name}'");
                    }

                    else
                    {

                        if (propertySymbol.GetMethod != null &&
                            context.PurityCache.TryGetValue(propertySymbol.GetMethod.OriginalDefinition, out var cachedGetterResult) &&
                            !cachedGetterResult.IsPure)
                        {
                            return cachedGetterResult.WithCallee(propertySymbol.GetMethod, propertyReferenceOperation.Syntax);
                        }


                        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
                    }
                }
            }


        }
    }
}

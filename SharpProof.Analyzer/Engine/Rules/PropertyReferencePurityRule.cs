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
            PurityAnalysisEngine.LogDebug($"  [PropRefRule] Checking PropertyReference: {propertySymbol.Name} on Type: {propertySymbol.ContainingType?.ToDisplayString()}");

            if (IsCompilerGeneratedArrayForeachCurrent(propertyReferenceOperation, context))
            {
                PurityAnalysisEngine.LogDebug("    [PropRefRule] Compiler-generated array foreach Current is treated as pure.");
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
                PurityAnalysisEngine.LogDebug($"    [PropRefRule] Property '{propertySymbol.Name}' reads a resource already marked disposed by symbolic ownership facts.");
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    propertyReferenceOperation.Syntax,
                    useAfterDisposeEvidence);
            }

            if (IsArrayLengthProperty(propertyReferenceOperation))
            {
                PurityAnalysisEngine.LogDebug("    [PropRefRule] System.Array.Length is treated as a pure property read after its receiver is analyzed.");
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (IsPartOfAssignmentTarget(propertyReferenceOperation))
            {
                PurityAnalysisEngine.LogDebug($"    [PropRefRule] Skipping property read {propertySymbol.Name} as it's an assignment target.");
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
                PurityAnalysisEngine.LogDebug($"    [PropRefRule] Property {propertySymbol.Name} has [EnforcePure]. Assuming Pure.");
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (PurityAnalysisEngine.IsConfiguredKnownPureMember(propertySymbol) ||
                (getterSymbol != null && PurityAnalysisEngine.IsConfiguredKnownPureMember(getterSymbol)))
            {
                PurityAnalysisEngine.LogDebug($"    [PropRefRule] Property {propertySymbol.Name} is configured known pure. Assuming Pure.");
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (getterSymbol != null &&
                hasTrustedGeneratedPurity &&
                generatedPurity.IsPure &&
                IsTrustedGeneratedMetadataGetter(getterSymbol))
            {
                PurityAnalysisEngine.LogDebug($"    [PropRefRule] Metadata getter '{getterSymbol.ToDisplayString()}' is trusted pure from generated purity summary.");
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }



            string impureSig = propertySymbol.OriginalDefinition.ToDisplayString();
            PurityAnalysisEngine.LogDebug($"      [PropRefRule] Checking IsKnownImpure for property: '{impureSig}'");
            if (hasConfiguredKnownImpureMember)
            {
                PurityAnalysisEngine.LogDebug($"    [PropRefRule] Property {propertySymbol.Name} is configured known impure. Impure.");
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
                PurityAnalysisEngine.LogDebug($"    [PropRefRule] Getter '{getterSymbol?.ToDisplayString() ?? propertySymbol.ToDisplayString()}' is trusted pure from generated purity summary.");
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (!requiresDispatchCheck &&
                getterSymbol != null &&
                PurityAnalysisEngine.IsMetadataSymbol(getterSymbol) &&
                !hasTrustedGeneratedPurity &&
                string.Equals(GetCatalogHitCategory(propertySymbol), "reflection_environment_source", StringComparison.Ordinal))
            {
                PurityAnalysisEngine.LogDebug($"    [PropRefRule] Metadata-backed reflection-sensitive property '{propertySymbol.ToDisplayString()}' has no trusted generated summary. Classifying as reflection/environment impure.");
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
                PurityAnalysisEngine.LogDebug($"    [PropRefRule] Property {propertySymbol.Name} is in a known impure namespace or type. Impure.");
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
                PurityAnalysisEngine.LogDebug($"    [PropRefRule] Property {propertySymbol.Name} is built-in known impure. Impure.");
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
                    PurityAnalysisEngine.LogDebug($"    [PropRefRule] Getter '{getterSymbol?.ToDisplayString() ?? propertySymbol.ToDisplayString()}' is trusted impure from generated purity summary.");
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
                PurityAnalysisEngine.LogDebug($"    [PropRefRule] Property {propertySymbol.Name} is a source auto-property getter. Treating read as pure.");
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (!requiresDispatchCheck &&
                propertySymbol.ContainingType is INamedTypeSymbol containingType &&
                containingType.IsAnonymousType &&
                propertySymbol.IsReadOnly)
            {
                PurityAnalysisEngine.LogDebug($"    [PropRefRule] Property {propertySymbol.Name} is an anonymous-type readonly property. Treating read as pure.");
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (requiresDispatchCheck)
            {
                PurityAnalysisEngine.LogDebug($"    [PropRefRule] Property {propertySymbol.Name} may dispatch. Checking getter candidates.");
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
                    PurityAnalysisEngine.LogDebug($"    [PropRefRule] Property {propertySymbol.Name} has [EnforcePure] and dispatched getter candidates were pure. Assuming Pure.");
                    return PurityAnalysisEngine.PurityAnalysisResult.Pure;
                }
            }


            if (propertySymbol.IsStatic)
            {

                PurityAnalysisEngine.LogDebug($"    [PropRefRule] Static property access: {propertySymbol.Name}");

                var cctorResult = PurityAnalysisEngine.CheckStaticConstructorPurity(propertySymbol.ContainingType, context, currentState);
                if (!cctorResult.IsPure)
                {
                    PurityAnalysisEngine.LogDebug($"    [PropRefRule] Static property '{propertySymbol.Name}' access IMPURE due to impure static constructor in {propertySymbol.ContainingType?.Name}.");
                    return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                        cctorResult.ImpureSyntaxNode ?? propertyReferenceOperation.Syntax,
                        cctorResult.Evidence);
                }


                string staticPureSig = propertySymbol.OriginalDefinition.ToDisplayString();
                bool staticKnownPure = allowsKnownPureFallback &&
                    PurityAnalysisEngine.IsKnownPureBCLMember(propertySymbol, context.SemanticModel.Compilation);
                PurityAnalysisEngine.LogDebug($"      [PropRefRule] Checking IsKnownPureBCLMember for static property: '{staticPureSig}' -> {staticKnownPure}");

                if (staticKnownPure)
                {
                    PurityAnalysisEngine.LogDebug($"    [PropRefRule] Static property '{propertySymbol.Name}' is a known pure BCL member. Read is Pure.");
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
                    PurityAnalysisEngine.LogDebug($"    [PropRefRule] Static property '{propertySymbol.Name}' has no trusted purity evidence; using BCL fallback guess.");
                    return staticBclFallbackResult;
                }

                return GetterResultOrImpure(propertySymbol, propertyReferenceOperation, context, $"static property '{propertySymbol.Name}'", $"Static property '{propertySymbol.Name}' has no accessible getter to analyze and is not a known pure BCL member");
            }
            else
            {
                PurityAnalysisEngine.LogDebug($"    [PropRefRule] Instance property access: {propertySymbol.Name}");
                IOperation? instanceOperation = propertyReferenceOperation.Instance;


                string instanceKind = instanceOperation?.Kind.ToString() ?? "null";
                string instanceSyntax = instanceOperation?.Syntax.ToString() ?? "null";
                PurityAnalysisEngine.LogDebug($"      [PropRefRule] Instance Operation Kind: {instanceKind}, Syntax: {instanceSyntax}");

                if (instanceOperation == null)
                {

                    PurityAnalysisEngine.LogDebug($"    [PropRefRule] Instance operation is null for property '{propertySymbol.Name}'. Assuming Impure for safety.");
                    return PurityAnalysisEngine.PurityAnalysisResult.Impure(propertyReferenceOperation.Syntax);
                }


                if (instanceOperation is IParameterReferenceOperation paramRef &&
                    (paramRef.Parameter.RefKind == RefKind.In ||
                     paramRef.Parameter.RefKind == RefKind.RefReadOnly ||
                     paramRef.Parameter.RefKind == (RefKind)4))
                {
                    bool isValueStruct = paramRef.Parameter.Type.IsValueType && !paramRef.Parameter.Type.IsReferenceType;
                    PurityAnalysisEngine.LogDebug($"    [PropRefRule] Instance is ParameterReference '{paramRef.Parameter.Name}', RefKind={paramRef.Parameter.RefKind}, IsValueStruct={isValueStruct}");

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
                        PurityAnalysisEngine.LogDebug($"    [PropRefRule] Instance is 'this', property '{propertySymbol.Name}' is not readonly and has no accessible getter to analyze. Read is Impure.");
                        return PurityAnalysisEngine.PurityAnalysisResult.Impure(propertyReferenceOperation.Syntax);
                    }
                }
                else
                {
                    var instanceExprResult = PurityAnalysisEngine.CheckSingleOperation(instanceOperation, context, currentState);
                    if (!instanceExprResult.IsPure)
                    {
                        PurityAnalysisEngine.LogDebug($"    [PropRefRule] Instance expression for '{propertySymbol.Name}' is impure. Propagating.");
                        return instanceExprResult;
                    }

                    if (dispatchGetterWasProvenPure)
                    {
                        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
                    }

                    string instancePureSig = propertySymbol.OriginalDefinition.ToDisplayString();
                    bool instanceKnownPure = allowsKnownPureFallback &&
                        PurityAnalysisEngine.IsKnownPureBCLMember(propertySymbol, context.SemanticModel.Compilation);
                    PurityAnalysisEngine.LogDebug($"      [PropRefRule] Checking IsKnownPureBCLMember for instance property: '{instancePureSig}' -> {instanceKnownPure}");

                    if (instanceKnownPure)
                    {
                        PurityAnalysisEngine.LogDebug($"    [PropRefRule] Instance property '{propertySymbol.Name}' is known pure BCL. Read is Pure.");
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
                        PurityAnalysisEngine.LogDebug($"    [PropRefRule] Instance property '{propertySymbol.Name}' has no trusted purity evidence; using BCL fallback guess.");
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
                            PurityAnalysisEngine.LogDebug($"    [PropRefRule] Instance is complex, property {propertySymbol.Name} known pure BCL, but getter is known impure from cache. Returning Impure.");
                            return cachedGetterResult.WithCallee(propertySymbol.GetMethod, propertyReferenceOperation.Syntax);
                        }


                        PurityAnalysisEngine.LogDebug($"    [PropRefRule] Instance property '{propertySymbol.Name}' is known pure BCL. Read is Pure.");
                        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
                    }
                }
            }


        }
    }
}

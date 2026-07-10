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

    internal partial class PropertyReferencePurityRule
    {

        private static bool IsPotentiallyDispatchedProperty(IPropertySymbol propertySymbol, Compilation compilation)
        {
            return propertySymbol.ContainingType?.TypeKind == TypeKind.Interface ||
                   propertySymbol.IsAbstract ||
                   (propertySymbol.GetMethod != null && DispatchedMemberResolution.IsPotentiallyDispatchedGetter(propertySymbol.GetMethod, compilation));
        }

        private static PurityAnalysisEngine.PurityAnalysisResult CheckDispatchedGetterPurity(
            IPropertyReferenceOperation propertyReferenceOperation,
            PurityAnalysisContext context,
            PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            var hasExactReceiverType = PurityAnalysisEngine.TryResolveKnownConcreteType(
                propertyReferenceOperation.Instance,
                currentState,
                context.SemanticModel.Compilation,
                out var exactReceiverType);

            var knownReceiverType = hasExactReceiverType
                ? exactReceiverType
                : PropertyDispatchHelper.GetKnownReceiverType(propertyReferenceOperation.Instance);

            if (hasExactReceiverType && knownReceiverType != null)
            {
                var exactGetter = PurityAnalysisEngine.ResolvePropertyAccessorTargetForConcreteReceiver(
                    propertyReferenceOperation.Property,
                    knownReceiverType,
                    preferSetter: false);
                if (exactGetter != null)
                {
                    var getterResult = PurityAnalysisEngine.GetCalleePurity(exactGetter, context);
                    return getterResult.IsPure
                        ? PurityAnalysisEngine.PurityAnalysisResult.Pure
                        : getterResult.WithCallee(exactGetter, propertyReferenceOperation.Syntax);
                }
            }

            if (propertyReferenceOperation.Property.GetMethod is { } runtimeBackedGetter &&
                PurityAnalysisEngine.IsKnownSystemTypeRuntimeReceiver(propertyReferenceOperation.Instance) &&
                GeneratedPurityCatalog.Current.TryGetSystemTypeRuntimeImplementationPurity(
                    runtimeBackedGetter.OriginalDefinition,
                    context.SemanticModel.Compilation,
                    out var runtimeImplementationPurity))
            {
                if (runtimeImplementationPurity.IsPure)
                {
                    return PurityAnalysisEngine.PurityAnalysisResult.Pure;
                }

                if (runtimeImplementationPurity.IsNonPure)
                {
                    return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                        propertyReferenceOperation.Syntax,
                        PurityAnalysisEngine.PurityEvidence.Create(
                            runtimeImplementationPurity.PrimaryCategory,
                            nameof(PropertyReferencePurityRule),
                            propertyReferenceOperation,
                            symbol: runtimeBackedGetter,
                            catalogSource: "generated_purity_summary"));
                }
            }

            if (!hasExactReceiverType &&
                CanDispatchToUnknownGetterTarget(
                    propertyReferenceOperation.Property,
                    knownReceiverType,
                    context.SemanticModel.Compilation))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    propertyReferenceOperation.Syntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        "dynamic_dispatch",
                        nameof(PropertyReferencePurityRule),
                        propertyReferenceOperation,
                        symbol: propertyReferenceOperation.Property.GetMethod));
            }

            var candidates = ResolvePotentialGetterTargets(
                propertyReferenceOperation.Property,
                context.SemanticModel,
                knownReceiverType,
                hasExactReceiverType,
                context.CancellationToken);

            if (candidates.IsDefaultOrEmpty)
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    propertyReferenceOperation.Syntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        "dynamic_dispatch",
                        nameof(PropertyReferencePurityRule),
                        propertyReferenceOperation,
                        symbol: propertyReferenceOperation.Property.GetMethod));
            }

            foreach (var getter in candidates)
            {
                var getterResult = PurityAnalysisEngine.GetCalleePurity(getter, context);
                if (!getterResult.IsPure)
                {
                    return getterResult.WithCallee(getter, propertyReferenceOperation.Syntax);
                }
            }

            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }

        private static bool CanDispatchToUnknownGetterTarget(
            IPropertySymbol propertySymbol,
            INamedTypeSymbol? knownReceiverType,
            Compilation compilation)
        {
            if (knownReceiverType == null)
            {
                return true;
            }

            if (knownReceiverType.TypeKind == TypeKind.Interface)
            {
                return true;
            }

            if (knownReceiverType.TypeKind == TypeKind.Class &&
                !knownReceiverType.IsSealed &&
                IsPotentiallyDispatchedProperty(propertySymbol, compilation))
            {
                return true;
            }

            return false;
        }

        private static PurityAnalysisEngine.PurityAnalysisResult GetterResultOrPure(
            PurityAnalysisEngine.PurityAnalysisResult getterResult,
            IPropertySymbol propertySymbol,
            IMethodSymbol getterSymbol,
            IPropertyReferenceOperation propertyReferenceOperation)
        {
            if (!getterResult.IsPure &&
                string.Equals(getterResult.Evidence.Category, "unknown_external_call", StringComparison.Ordinal) &&
                string.IsNullOrEmpty(getterResult.Evidence.CatalogSource) &&
                string.Equals(GetCatalogHitCategory(propertySymbol), "reflection_environment_source", StringComparison.Ordinal))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    propertyReferenceOperation.Syntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        "reflection_environment_source",
                        ruleName: nameof(PropertyReferencePurityRule),
                        operation: propertyReferenceOperation,
                        syntaxNode: propertyReferenceOperation.Syntax,
                        symbol: propertySymbol));
            }

            return getterResult.IsPure
                ? PurityAnalysisEngine.PurityAnalysisResult.Pure
                : getterResult.WithCallee(getterSymbol, propertyReferenceOperation.Syntax);
        }

        private static PurityAnalysisEngine.PurityAnalysisResult GetterResultOrImpure(
            IPropertySymbol propertySymbol,
            IPropertyReferenceOperation propertyReferenceOperation,
            PurityAnalysisContext context,
            string getterDescription,
            string? missingGetterMessage = null)
        {
            if (propertySymbol.GetMethod is not { } getter)
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(propertyReferenceOperation.Syntax);
            }

            var getterResult = PurityAnalysisEngine.GetCalleePurity(getter, context);
            return GetterResultOrPure(getterResult, propertySymbol, getter, propertyReferenceOperation);
        }

        private static string GetCatalogHitCategory(ISymbol symbol) =>
            PurityAnalysisEngine.GetKnownImpureCatalogHitCategory(symbol);
    }
}

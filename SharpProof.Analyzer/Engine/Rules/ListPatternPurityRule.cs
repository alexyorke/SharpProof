using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace SharpProof.Analyzer.Engine.Rules
{
    internal sealed class ListPatternPurityRule : IPurityRule
    {
        public IEnumerable<OperationKind> ApplicableOperationKinds =>
            ImmutableArray.Create(OperationKind.ListPattern);

        public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(
            IOperation operation,
            PurityAnalysisContext context,
            PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            var matchedInputOperation = GetMatchedInputOperation(operation);
            if (matchedInputOperation == null)
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            var receiverType = GetKnownReceiverType(
                matchedInputOperation,
                currentState,
                context.SemanticModel.Compilation,
                out var hasStableConcreteReceiver);
            var hasBuiltInPureReceiver = IsBuiltInPureListPatternReceiver(matchedInputOperation.Type);

            if (operation is IListPatternOperation listPattern)
            {
                if (!hasBuiltInPureReceiver)
                {
                    var lengthResult = CheckMemberPurity(
                        listPattern.LengthSymbol,
                        receiverType,
                        hasStableConcreteReceiver,
                        operation,
                        context);
                    if (!lengthResult.IsPure)
                    {
                        return lengthResult;
                    }

                    var indexerResult = CheckMemberPurity(
                        listPattern.IndexerSymbol,
                        receiverType,
                        hasStableConcreteReceiver,
                        operation,
                        context);
                    if (!indexerResult.IsPure)
                    {
                        return indexerResult;
                    }
                }

                foreach (var pattern in listPattern.Patterns)
                {
                    var patternResult = pattern is ISlicePatternOperation slicePattern
                        ? CheckSlicePatternPurity(
                            slicePattern,
                            receiverType,
                            hasStableConcreteReceiver,
                            hasBuiltInPureReceiver,
                            context,
                            currentState)
                        : PurityAnalysisEngine.CheckSingleOperation(pattern, context, currentState);
                    if (!patternResult.IsPure)
                    {
                        return patternResult;
                    }
                }

                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }

        private static PurityAnalysisEngine.PurityAnalysisResult CheckSlicePatternPurity(
            ISlicePatternOperation slicePattern,
            INamedTypeSymbol? receiverType,
            bool hasStableConcreteReceiver,
            bool hasBuiltInPureReceiver,
            PurityAnalysisContext context,
            PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            if (!hasBuiltInPureReceiver)
            {
                var sliceResult = CheckMemberPurity(
                    slicePattern.SliceSymbol,
                    receiverType,
                    hasStableConcreteReceiver,
                    slicePattern,
                    context);
                if (!sliceResult.IsPure)
                {
                    return sliceResult;
                }
            }

            return slicePattern.Pattern == null
                ? PurityAnalysisEngine.PurityAnalysisResult.Pure
                : PurityAnalysisEngine.CheckSingleOperation(slicePattern.Pattern, context, currentState);
        }

        private static PurityAnalysisEngine.PurityAnalysisResult CheckMemberPurity(
            ISymbol? member,
            INamedTypeSymbol? receiverType,
            bool hasStableConcreteReceiver,
            IOperation operation,
            PurityAnalysisContext context)
        {
            if (member == null)
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (member is IPropertySymbol property)
            {
                var knownImpureMemberSource = PurityAnalysisEngine.GetKnownImpureMemberSource(property);
                if (string.Equals(
                    knownImpureMemberSource,
                    "config_known_impure",
                    StringComparison.Ordinal))
                {
                    return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                        operation.Syntax,
                        PurityAnalysisEngine.PurityEvidence.Create(
                            "catalog_hit",
                            nameof(ListPatternPurityRule),
                            operation,
                            syntaxNode: operation.Syntax,
                            symbol: property,
                            catalogSource: knownImpureMemberSource));
                }

                return CheckPropertyGetterPurity(
                    property,
                    receiverType,
                    hasStableConcreteReceiver,
                    operation,
                    context);
            }

            if (member is IMethodSymbol method)
            {
                return CheckMethodPurity(
                    method,
                    receiverType,
                    hasStableConcreteReceiver,
                    operation,
                    context);
            }

            if (PurityAnalysisEngine.IsKnownImpure(member))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    operation.Syntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        "catalog_hit",
                        nameof(ListPatternPurityRule),
                        operation,
                        syntaxNode: operation.Syntax,
                        symbol: member,
                        catalogSource: PurityAnalysisEngine.GetKnownImpureMemberSource(member) ?? "known_impure"));
            }

            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }

        private static PurityAnalysisEngine.PurityAnalysisResult CheckPropertyGetterPurity(
            IPropertySymbol propertySymbol,
            INamedTypeSymbol? receiverType,
            bool hasStableConcreteReceiver,
            IOperation operation,
            PurityAnalysisContext context)
        {
            var getter = ResolveGetter(
                propertySymbol,
                receiverType,
                hasStableConcreteReceiver,
                context.SemanticModel.Compilation);
            if (getter == null)
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    operation.Syntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        "dynamic_dispatch",
                        ruleName: nameof(ListPatternPurityRule),
                        operation: operation,
                        symbol: propertySymbol.GetMethod));
            }

            var getterPurity = PurityAnalysisEngine.GetCalleePurity(getter, context);
            return getterPurity.IsPure
                ? PurityAnalysisEngine.PurityAnalysisResult.Pure
                : getterPurity.WithCallee(getter, operation.Syntax);
        }

        private static PurityAnalysisEngine.PurityAnalysisResult CheckMethodPurity(
            IMethodSymbol? method,
            INamedTypeSymbol? receiverType,
            bool hasStableConcreteReceiver,
            IOperation operation,
            PurityAnalysisContext context)
        {
            if (method == null)
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            var targetMethod = ResolveMethod(
                method,
                receiverType,
                hasStableConcreteReceiver,
                context.SemanticModel.Compilation);
            if (targetMethod == null)
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    operation.Syntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        "dynamic_dispatch",
                        nameof(ListPatternPurityRule),
                        operation,
                        syntaxNode: operation.Syntax,
                        symbol: method));
            }

            var result = PurityAnalysisEngine.GetCalleePurity(targetMethod, context);
            return result.IsPure
                ? PurityAnalysisEngine.PurityAnalysisResult.Pure
                : result.WithCallee(targetMethod, operation.Syntax);
        }

        private static IMethodSymbol? ResolveGetter(
            IPropertySymbol propertySymbol,
            INamedTypeSymbol? receiverType,
            bool hasStableConcreteReceiver,
            Compilation compilation)
        {
            if (propertySymbol.GetMethod == null)
            {
                return null;
            }

            if (!IsPotentiallyDispatchedGetter(propertySymbol.GetMethod, compilation))
            {
                return propertySymbol.GetMethod.OriginalDefinition;
            }

            if (receiverType == null || !hasStableConcreteReceiver)
            {
                return null;
            }

            if (propertySymbol.ContainingType?.TypeKind == TypeKind.Interface)
            {
                var implementation = receiverType.FindImplementationForInterfaceMember(propertySymbol) ??
                    receiverType.FindImplementationForInterfaceMember(propertySymbol.GetMethod);
                return implementation switch
                {
                    IPropertySymbol implementationProperty when implementationProperty.GetMethod != null =>
                        implementationProperty.GetMethod.OriginalDefinition,
                    IMethodSymbol implementationMethod => implementationMethod.OriginalDefinition,
                    _ => null
                };
            }

            var rootProperty = GetRootOverriddenProperty(propertySymbol);
            for (var current = receiverType; current != null; current = current.BaseType)
            {
                foreach (var member in current.GetMembers(rootProperty.Name))
                {
                    if (member is IPropertySymbol candidate &&
                        (SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, rootProperty.OriginalDefinition) ||
                         OverridesProperty(candidate, rootProperty)) &&
                        candidate.GetMethod != null)
                    {
                        return candidate.GetMethod.OriginalDefinition;
                    }
                }
            }

            return propertySymbol.GetMethod.IsAbstract ? null : propertySymbol.GetMethod.OriginalDefinition;
        }

        private static IMethodSymbol? ResolveMethod(
            IMethodSymbol methodSymbol,
            INamedTypeSymbol? receiverType,
            bool hasStableConcreteReceiver,
            Compilation compilation)
        {
            if (!IsPotentiallyDispatchedMethod(methodSymbol, compilation))
            {
                return methodSymbol.OriginalDefinition;
            }

            if (receiverType == null || !hasStableConcreteReceiver)
            {
                return null;
            }

            if (methodSymbol.ContainingType?.TypeKind == TypeKind.Interface)
            {
                return receiverType.FindImplementationForInterfaceMember(methodSymbol) as IMethodSymbol;
            }

            var rootMethod = GetRootOverriddenMethod(methodSymbol);
            for (var current = receiverType; current != null; current = current.BaseType)
            {
                foreach (var member in current.GetMembers(rootMethod.Name))
                {
                    if (member is IMethodSymbol candidate &&
                        candidate.Parameters.Length == rootMethod.Parameters.Length &&
                        (SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, rootMethod.OriginalDefinition) ||
                         OverridesMethod(candidate, rootMethod)))
                    {
                        return candidate.OriginalDefinition;
                    }
                }
            }

            return methodSymbol.IsAbstract ? null : methodSymbol.OriginalDefinition;
        }

        private static IOperation? GetMatchedInputOperation(IOperation operation)
        {
            for (var current = operation; current != null; current = current.Parent)
            {
                if (current is IIsPatternOperation isPatternOperation)
                {
                    return isPatternOperation.Value;
                }
            }

            return null;
        }

        private static bool IsBuiltInPureListPatternReceiver(ITypeSymbol? receiverType)
        {
            return receiverType is IArrayTypeSymbol { Rank: 1 } ||
                receiverType?.SpecialType == SpecialType.System_String;
        }

        private static INamedTypeSymbol? GetKnownReceiverType(
            IOperation? instanceOperation,
            PurityAnalysisEngine.PurityAnalysisState currentState,
            Compilation compilation,
            out bool hasStableConcreteReceiver)
        {
            if (PurityAnalysisEngine.TryResolveKnownConcreteType(instanceOperation, currentState, compilation, out var concreteType))
            {
                hasStableConcreteReceiver = true;
                return concreteType;
            }

            var receiverType = PurityAnalysisEngine.SkipImplicitConversions(instanceOperation)?.Type as INamedTypeSymbol;
            hasStableConcreteReceiver = receiverType != null &&
                                        (receiverType.TypeKind == TypeKind.Struct || receiverType.IsSealed);
            return receiverType;
        }

        private static bool IsPotentiallyDispatchedGetter(IMethodSymbol getterSymbol, Compilation compilation)
        {
            if (getterSymbol.ContainingType?.TypeKind == TypeKind.Interface ||
                getterSymbol.IsAbstract)
            {
                return true;
            }

            if (!getterSymbol.IsVirtual && !getterSymbol.IsOverride)
            {
                return false;
            }

            if (GeneratedPurityCatalog.TryCanMetadataMethodBeOverridden(getterSymbol, compilation, out var canBeOverridden))
            {
                return canBeOverridden;
            }

            return !getterSymbol.IsSealed;
        }

        private static bool IsPotentiallyDispatchedMethod(IMethodSymbol methodSymbol, Compilation compilation)
        {
            if (methodSymbol.ContainingType?.TypeKind == TypeKind.Interface ||
                methodSymbol.IsAbstract)
            {
                return true;
            }

            if (!methodSymbol.IsVirtual && !methodSymbol.IsOverride)
            {
                return false;
            }

            if (GeneratedPurityCatalog.TryCanMetadataMethodBeOverridden(methodSymbol, compilation, out var canBeOverridden))
            {
                return canBeOverridden;
            }

            return !methodSymbol.IsSealed;
        }

        private static IPropertySymbol GetRootOverriddenProperty(IPropertySymbol propertySymbol)
        {
            var current = propertySymbol;
            while (current.OverriddenProperty != null)
            {
                current = current.OverriddenProperty;
            }

            return current.OriginalDefinition;
        }

        private static IMethodSymbol GetRootOverriddenMethod(IMethodSymbol methodSymbol)
        {
            var current = methodSymbol;
            while (current.OverriddenMethod != null)
            {
                current = current.OverriddenMethod;
            }

            return current.OriginalDefinition;
        }

        private static bool OverridesProperty(IPropertySymbol property, IPropertySymbol target)
        {
            for (var current = property; current != null; current = current.OverriddenProperty)
            {
                if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, target.OriginalDefinition))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool OverridesMethod(IMethodSymbol method, IMethodSymbol target)
        {
            for (var current = method; current != null; current = current.OverriddenMethod)
            {
                if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, target.OriginalDefinition))
                {
                    return true;
                }
            }

            return false;
        }
    }
}

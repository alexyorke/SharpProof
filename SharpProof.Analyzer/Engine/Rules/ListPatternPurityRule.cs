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

            var receiverType = DispatchedMemberResolution.GetKnownReceiverType(
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
            var getter = DispatchedMemberResolution.ResolveGetter(
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

            var targetMethod = DispatchedMemberResolution.ResolveMethod(
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

    }
}

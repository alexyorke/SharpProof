using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Analyzer.Engine.Rules;

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
        if (matchedInputOperation == null) return PurityAnalysisEngine.PurityAnalysisResult.Pure;

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
                if (!lengthResult.IsPure) return lengthResult;

                var indexerResult = CheckMemberPurity(
                    listPattern.IndexerSymbol,
                    receiverType,
                    hasStableConcreteReceiver,
                    operation,
                    context);
                if (!indexerResult.IsPure) return indexerResult;
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
                if (!patternResult.IsPure) return patternResult;
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
            if (!sliceResult.IsPure) return sliceResult;
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
        if (member == null) return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        if (member is IPropertySymbol property)
        {
            var knownImpureMemberSource = PurityCalleeResolver.GetKnownImpureMemberSource(property);
            if (string.Equals(
                    knownImpureMemberSource,
                    "config_known_impure",
                    StringComparison.Ordinal))
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    operation.Syntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        "catalog_hit",
                        nameof(ListPatternPurityRule),
                        operation,
                        operation.Syntax,
                        property,
                        knownImpureMemberSource));

            return DispatchedMemberResolution.CheckGetterPurity(
                property,
                receiverType,
                hasStableConcreteReceiver,
                operation,
                context,
                nameof(ListPatternPurityRule));
        }

        if (member is IMethodSymbol method)
            return DispatchedMemberResolution.CheckMethodPurity(
                method,
                receiverType,
                hasStableConcreteReceiver,
                operation,
                context,
                nameof(ListPatternPurityRule));

        if (PurityCatalogSemantics.IsKnownImpure(member))
            return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                operation.Syntax,
                PurityAnalysisEngine.PurityEvidence.Create(
                    "catalog_hit",
                    nameof(ListPatternPurityRule),
                    operation,
                    operation.Syntax,
                    member,
                    PurityCalleeResolver.GetKnownImpureMemberSource(member) ?? "known_impure"));

        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
    }

    private static IOperation? GetMatchedInputOperation(IOperation operation)
    {
        for (var current = operation; current != null; current = current.Parent)
        {
            if (current is IIsPatternOperation isPatternOperation)
                return isPatternOperation.Value;
            if (current is ISwitchOperation switchOperation)
                return switchOperation.Value;
            if (current is ISwitchExpressionOperation switchExpressionOperation)
                return switchExpressionOperation.Value;
        }

        return null;
    }

    private static bool IsBuiltInPureListPatternReceiver(ITypeSymbol? receiverType)
    {
        return receiverType is IArrayTypeSymbol { Rank: 1 } ||
               receiverType?.SpecialType == SpecialType.System_String;
    }
}

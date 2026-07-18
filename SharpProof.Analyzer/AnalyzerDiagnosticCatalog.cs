using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using SharpProof.Analyzer.Configuration;

namespace SharpProof.Analyzer;

internal sealed record AnalyzerDiagnosticDefinition(
    DiagnosticDescriptor Descriptor,
    AnalyzerFeatures OwningFeature,
    string? ConfigurationKey,
    string DocumentationUri);

internal static class AnalyzerDiagnosticCatalog
{
    internal static readonly ImmutableArray<AnalyzerDiagnosticDefinition> All = CreateDefinitions();

    internal static readonly ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =
        All.Select(static definition => definition.Descriptor).ToImmutableArray();

    private static ImmutableArray<AnalyzerDiagnosticDefinition> CreateDefinitions()
    {
        var descriptors = ImmutableArray.Create(
            SharpProofDiagnostics.PurityNotVerifiedRule,
            SharpProofDiagnostics.MisplacedAttributeRule,
            SharpProofDiagnostics.MissingEnforcePureAttributeRule,
            SharpProofDiagnostics.ConflictingPurityAttributesRule,
            SharpProofDiagnostics.AllowSynchronizationWithoutPurityAttributeRule,
            SharpProofDiagnostics.MisplacedAllowSynchronizationAttributeRule,
            SharpProofDiagnostics.RedundantAllowSynchronizationRule,
            SharpProofDiagnostics.PurityExplanationRule,
            SharpProofDiagnostics.ExceptionSummaryRule,
            SharpProofDiagnostics.UncaughtExceptionSiteRule,
            SharpProofDiagnostics.UnknownRuntimeHazardRule,
            SharpProofDiagnostics.BclFallbackGuessRule,
            SharpProofDiagnostics.AllocationInZeroAllocationMethodRule,
            SharpProofDiagnostics.MisplacedZeroAllocationsAttributeRule,
            SharpProofDiagnostics.CapabilityViolationRule,
            SharpProofDiagnostics.CapabilityUnknownRule,
            SharpProofDiagnostics.MisplacedAllowedCapabilitiesAttributeRule,
            SharpProofDiagnostics.EnsuresNotProvenRule,
            SharpProofDiagnostics.EnsuresUnsupportedRule,
            SharpProofDiagnostics.MisplacedEnsuresAttributeRule,
            SharpProofDiagnostics.ComplexityExceededRule,
            SharpProofDiagnostics.ComplexityCouldNotBeVerifiedRule,
            SharpProofDiagnostics.MisplacedExpectedComplexityAttributeRule,
            SharpProofDiagnostics.InvalidContractArgumentRule,
            SharpProofDiagnostics.InvalidAnalyzerConfigurationRule,
            SharpProofDiagnostics.InvalidAdditionalFileRule,
            SharpProofDiagnostics.UnrecognizedAttributeIdentityRule,
            SharpProofDiagnostics.RequiresNotProvenRule,
            SharpProofDiagnostics.RequiresUnsupportedRule,
            SharpProofDiagnostics.MisplacedRequiresAttributeRule,
            SharpProofDiagnostics.ExceptionContractViolationRule,
            SharpProofDiagnostics.MisplacedExceptionContractAttributeRule,
            SharpProofDiagnostics.SuggestZeroAllocationsRule,
            SharpProofDiagnostics.SuggestAllowedCapabilitiesRule,
            SharpProofDiagnostics.SuggestExpectedComplexityRule,
            SharpProofDiagnostics.SuggestExceptionContractRule,
            SharpProofDiagnostics.SuggestEnsuresRule,
            SharpProofDiagnostics.SuggestRequiresRule,
            SharpProofDiagnostics.TrustedBoundaryReviewRule,
            SharpProofDiagnostics.NullableReturnContractViolationRule,
            SharpProofDiagnostics.NullableParameterPostconditionViolationRule,
            SharpProofDiagnostics.NullableMemberContractViolationRule,
            SharpProofDiagnostics.UnsafeNullForgivingOperatorRule,
            SharpProofDiagnostics.UnnecessaryNullForgivingOperatorRule,
            SharpProofDiagnostics.SuggestNullableContractRule,
            SharpProofDiagnostics.NullableVerificationInconclusiveRule,
            SharpProofDiagnostics.AwaitNullConditionalRule,
            SharpProofDiagnostics.TaskConvertedToStringRule,
            SharpProofDiagnostics.TaskCompletionSourceContinuationsRule,
            SharpProofDiagnostics.AsyncVoidRule,
            SharpProofDiagnostics.BlockingAsyncRule,
            SharpProofDiagnostics.NullTaskReturnRule,
            SharpProofDiagnostics.TaskUsedAsDisposableRule,
            SharpProofDiagnostics.AsyncValidationDeferredRule,
            SharpProofDiagnostics.CollectionMutationDuringEnumerationRule,
            SharpProofDiagnostics.CapturedLoopVariableRule,
            SharpProofDiagnostics.MutableStructRule,
            SharpProofDiagnostics.OwnedDisposableFieldRule,
            SharpProofDiagnostics.HttpClientInLoopRule,
            SharpProofDiagnostics.UnsynchronizedSharedMutationRule,
            SharpProofDiagnostics.ConcurrentCollectionEnumerationRule,
            SharpProofDiagnostics.BoxingInLoopRule,
            SharpProofDiagnostics.MaybeNullResultDereferenceRule,
            SharpProofDiagnostics.PrematureQueryMaterializationRule,
            SharpProofDiagnostics.DeferredQuerySideEffectRule,
            SharpProofDiagnostics.QueryTranslationRiskRule,
            SharpProofDiagnostics.SerializationCycleRiskRule,
            SharpProofDiagnostics.SerializerAttributeMismatchRule,
            SharpProofDiagnostics.IneffectiveRequiredAttributeRule,
            SharpProofDiagnostics.UncheckedAllocationArithmeticRule,
            SharpProofDiagnostics.SuppressionWithoutJustificationRule,
            SharpProofDiagnostics.NullableAnalysisDisabledRule,
            SharpProofDiagnostics.IdenticalOperandsRule,
            SharpProofDiagnostics.ContainerOwnedServiceDisposedRule,
            SharpProofDiagnostics.UnconsumedDeferredQueryRule);

        return descriptors.Select(CreateDefinition).ToImmutableArray();
    }

    private static AnalyzerDiagnosticDefinition CreateDefinition(DiagnosticDescriptor descriptor)
    {
        if (string.IsNullOrWhiteSpace(descriptor.HelpLinkUri))
            throw new InvalidOperationException($"Diagnostic {descriptor.Id} has no documentation URI.");

        return new AnalyzerDiagnosticDefinition(
            descriptor,
            GetOwningFeature(descriptor.Id),
            GetConfigurationKey(descriptor.Id),
            descriptor.HelpLinkUri);
    }

    private static AnalyzerFeatures GetOwningFeature(string id)
    {
        if (id is SharpProofDiagnostics.PurityNotVerifiedId or
            SharpProofDiagnostics.MissingEnforcePureAttributeId or
            SharpProofDiagnostics.ConflictingPurityAttributesId or
            SharpProofDiagnostics.AllowSynchronizationWithoutPurityAttributeId or
            SharpProofDiagnostics.RedundantAllowSynchronizationId or
            SharpProofDiagnostics.PurityExplanationId or
            SharpProofDiagnostics.BclFallbackGuessId or
            SharpProofDiagnostics.TrustedBoundaryReviewId)
            return AnalyzerFeatures.Purity;
        if (id is SharpProofDiagnostics.AllocationInZeroAllocationMethodId)
            return AnalyzerFeatures.Allocation;
        if (id is SharpProofDiagnostics.CapabilityViolationId or SharpProofDiagnostics.CapabilityUnknownId)
            return AnalyzerFeatures.Capability;
        if (id is SharpProofDiagnostics.RequiresNotProvenId or SharpProofDiagnostics.RequiresUnsupportedId)
            return AnalyzerFeatures.Requires;
        if (id is SharpProofDiagnostics.EnsuresNotProvenId or SharpProofDiagnostics.EnsuresUnsupportedId)
            return AnalyzerFeatures.Ensures;
        if (id is SharpProofDiagnostics.ComplexityExceededId or
            SharpProofDiagnostics.ComplexityCouldNotBeVerifiedId)
            return AnalyzerFeatures.Complexity;
        if (id is SharpProofDiagnostics.ExceptionSummaryId or
            SharpProofDiagnostics.UncaughtExceptionSiteId or
            SharpProofDiagnostics.UnknownRuntimeHazardId or
            SharpProofDiagnostics.ExceptionContractViolationId)
            return AnalyzerFeatures.Exceptions;
        if (id is SharpProofDiagnostics.SuggestZeroAllocationsId or
            SharpProofDiagnostics.SuggestAllowedCapabilitiesId or
            SharpProofDiagnostics.SuggestExpectedComplexityId or
            SharpProofDiagnostics.SuggestExceptionContractId or
            SharpProofDiagnostics.SuggestEnsuresId or
            SharpProofDiagnostics.SuggestRequiresId)
            return AnalyzerFeatures.Suggestions;
        if (id is SharpProofDiagnostics.NullableReturnContractViolationId or
            SharpProofDiagnostics.NullableParameterPostconditionViolationId or
            SharpProofDiagnostics.NullableMemberContractViolationId or
            SharpProofDiagnostics.UnsafeNullForgivingOperatorId or
            SharpProofDiagnostics.UnnecessaryNullForgivingOperatorId or
            SharpProofDiagnostics.SuggestNullableContractId or
            SharpProofDiagnostics.NullableVerificationInconclusiveId)
            return AnalyzerFeatures.Nullability;
        if (IsCommonBug(id)) return AnalyzerFeatures.CommonBugs;
        return AnalyzerFeatures.Placement;
    }

    private static bool IsCommonBug(string id)
    {
        return int.TryParse(id.Substring(2), out var number) && number is >= 48 and <= 76;
    }

    private static string? GetConfigurationKey(string id)
    {
        return id switch
        {
            SharpProofDiagnostics.MissingEnforcePureAttributeId => ConfigKeys.SuggestMissingEnforcePure,
            SharpProofDiagnostics.PurityExplanationId => ConfigKeys.EmitExplanations,
            SharpProofDiagnostics.ExceptionSummaryId => ConfigKeys.ReportExceptions,
            SharpProofDiagnostics.UncaughtExceptionSiteId or SharpProofDiagnostics.UnknownRuntimeHazardId =>
                ConfigKeys.RuntimeHazardMode,
            SharpProofDiagnostics.BclFallbackGuessId => ConfigKeys.ReportBclFallbackGuesses,
            SharpProofDiagnostics.SuggestZeroAllocationsId or
                SharpProofDiagnostics.SuggestAllowedCapabilitiesId or
                SharpProofDiagnostics.SuggestExpectedComplexityId or
                SharpProofDiagnostics.SuggestExceptionContractId or
                SharpProofDiagnostics.SuggestEnsuresId or
                SharpProofDiagnostics.SuggestRequiresId => ConfigKeys.SuggestInferredContracts,
            SharpProofDiagnostics.TrustedBoundaryReviewId => ConfigKeys.TrustedBoundaryReviewMode,
            SharpProofDiagnostics.NullableVerificationInconclusiveId => ConfigKeys.ReportNullableInconclusive,
            _ => null
        };
    }
}

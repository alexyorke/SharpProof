using Microsoft.CodeAnalysis;
using SharpProof.Symbolic;

namespace SharpProof.Analyzer;

public static partial class SharpProofDiagnostics
{
    public const string EvidenceSchemaVersionProperty = SharpProofEvidenceSchema.DiagnosticVersionPropertyName;
    public const string EvidenceSchemaCompatibilityProperty =
        SharpProofEvidenceSchema.DiagnosticCompatibilityPropertyName;
    public const string BaselineSymbolProperty = "sharpproof.baseline.symbol";
    public const string BaselineSymbolAliasesProperty = "sharpproof.baseline.symbol_aliases";
    public const string BaselinePathProperty = "sharpproof.baseline.path";
    public const string BaselineOperationKindProperty = "sharpproof.baseline.operation_kind";
    public const string BaselineContractProperty = "sharpproof.baseline.contract";
    public const string BaselineEvidenceKeyProperty = "sharpproof.baseline.evidence_key";
    public const string ExplainFileProperty = "sharpproof.explain.file";
    public const string ExplainLineProperty = "sharpproof.explain.line";
    public const string ExplainColumnProperty = "sharpproof.explain.column";
    public const string ExplainQueryProperty = "sharpproof.explain.query";
    public const string ExplainContractProperty = "sharpproof.explain.contract";
    public const string ExplainProofStatusProperty = "sharpproof.explain.proof_status";
    public const string ExplainUnknownReasonProperty = "sharpproof.explain.unknown_reason";
    public const string UnknownReasonCodeProperty = "sharpproof.unknown.code";
    public const string UnknownReasonCategoryProperty = "sharpproof.unknown.category";
    public const string UnknownReasonSourceProperty = "sharpproof.unknown.source";
    public const string UnknownReasonRawProperty = "sharpproof.unknown.raw_reason";
    public const string UnknownReasonRetryableProperty = "sharpproof.unknown.retryable";
    public const string UnknownReasonConfigurationRelatedProperty = "sharpproof.unknown.configuration_related";
    public const string AnalysisTruncatedProperty = "sharpproof.analysis.truncated";
    public const string AnalysisLimitCodesProperty = "sharpproof.analysis.limit_codes";
    public const string AnalysisLimitEventsProperty = "sharpproof.analysis.limit_events";

    public const string PurityNotVerifiedId = "SP0002";
    public const string ImpurityCategoryProperty = "sharpproof.impurity.category";
    public const string ImpurityRuleProperty = "sharpproof.impurity.rule";
    public const string ImpurityOperationKindProperty = "sharpproof.impurity.operation_kind";
    public const string ImpuritySymbolProperty = "sharpproof.impurity.symbol";
    public const string ImpurityCatalogSourceProperty = "sharpproof.impurity.catalog_source";
    public const string ImpurityCalleeChainProperty = "sharpproof.impurity.callee_chain";
    public const string BclFallbackGuessProperty = "sharpproof.bcl_fallback.guess";
    public const string BclFallbackConfidenceProperty = "sharpproof.bcl_fallback.confidence";
    public const string BclFallbackReasonProperty = "sharpproof.bcl_fallback.reason";

    public const string BclFallbackGuessId = "SP0012";

    public const string AllocationInZeroAllocationMethodId = "SP0013";
    public const string AllocationKindProperty = "sharpproof.allocation.kind";
    public const string AllocationOperationKindProperty = "sharpproof.allocation.operation_kind";
    public const string AllocationSymbolProperty = "sharpproof.allocation.symbol";

    public const string PurityExplanationId = "SP0009";

    public const string ExceptionSummaryId = "SP0010";
    public const string ExceptionTypesProperty = "sharpproof.exceptions.types";
    public const string ExceptionCategoriesProperty = "sharpproof.exceptions.categories";
    public const string ExceptionSourcesProperty = "sharpproof.exceptions.sources";
    public const string ExceptionSymbolProperty = "sharpproof.exceptions.symbol";
    public const string ExceptionEdgesProperty = "sharpproof.exceptions.edges";

    public const string UncaughtExceptionSiteId = "SP0011";
    public const string UnknownRuntimeHazardId = "SP0033";
    public const string RuntimeHazardKindProperty = "sharpproof.runtime_hazard.kind";
    public const string RuntimeHazardStatusProperty = "sharpproof.runtime_hazard.status";
    public const string RuntimeHazardStatusReasonProperty = "sharpproof.runtime_hazard.status_reason";
    public const string RuntimeHazardTriggerProperty = "sharpproof.runtime_hazard.trigger";
    public const string RuntimeHazardProofBackendProperty = "sharpproof.runtime_hazard.proof_backend";
    public const string RuntimeHazardUnknownReasonProperty = "sharpproof.runtime_hazard.unknown_reason";

    public const string SuggestZeroAllocationsId = "SP0034";
    public const string SuggestAllowedCapabilitiesId = "SP0035";
    public const string SuggestExpectedComplexityId = "SP0036";
    public const string SuggestExceptionContractId = "SP0037";
    public const string SuggestEnsuresId = "SP0038";
    public const string SuggestRequiresId = "SP0039";
    public const string SuggestedContractKindProperty = "sharpproof.suggestion.contract_kind";
    public const string SuggestedContractAttributeProperty = "sharpproof.suggestion.attribute";
    public const string SuggestedContractConfidenceProperty = "sharpproof.suggestion.confidence";
    public const string SuggestedContractEvidenceProperty = "sharpproof.suggestion.evidence";

    public const string TrustedBoundaryReviewId = "SP0040";
    public const string TrustedBoundarySymbolProperty = "sharpproof.trusted_boundary.symbol";
    public const string TrustedBoundarySourceProperty = "sharpproof.trusted_boundary.source";
    public const string TrustedBoundaryValueProperty = "sharpproof.trusted_boundary.value";
    public const string TrustedBoundaryDispositionProperty = "sharpproof.trusted_boundary.disposition";
    public const string TrustedBoundaryOverriddenByProperty = "sharpproof.trusted_boundary.overridden_by";
    public const string TrustedBoundaryOverrideValueProperty = "sharpproof.trusted_boundary.override_value";
    public const string TrustedBoundaryClassificationProperty = "sharpproof.trusted_boundary.classification";

    public const string NullableReturnContractViolationId = "SP0041";
    public const string NullableParameterPostconditionViolationId = "SP0042";
    public const string NullableMemberContractViolationId = "SP0043";
    public const string UnsafeNullForgivingOperatorId = "SP0044";
    public const string UnnecessaryNullForgivingOperatorId = "SP0045";
    public const string SuggestNullableContractId = "SP0046";
    public const string NullableVerificationInconclusiveId = "SP0047";
    public const string AwaitNullConditionalId = "SP0048";
    public const string TaskConvertedToStringId = "SP0049";
    public const string TaskCompletionSourceContinuationsId = "SP0050";
    public const string AsyncVoidId = "SP0051";
    public const string BlockingAsyncId = "SP0052";
    public const string NullTaskReturnId = "SP0053";
    public const string TaskUsedAsDisposableId = "SP0054";
    public const string AsyncValidationDeferredId = "SP0055";
    public const string CollectionMutationDuringEnumerationId = "SP0056";
    public const string CapturedLoopVariableId = "SP0057";
    public const string MutableStructId = "SP0058";
    public const string OwnedDisposableFieldId = "SP0059";
    public const string HttpClientInLoopId = "SP0060";
    public const string UnsynchronizedSharedMutationId = "SP0061";
    public const string ConcurrentCollectionEnumerationId = "SP0062";
    public const string BoxingInLoopId = "SP0063";
    public const string MaybeNullResultDereferenceId = "SP0064";
    public const string PrematureQueryMaterializationId = "SP0065";
    public const string DeferredQuerySideEffectId = "SP0066";
    public const string QueryTranslationRiskId = "SP0067";
    public const string SerializationCycleRiskId = "SP0068";
    public const string SerializerAttributeMismatchId = "SP0069";
    public const string IneffectiveRequiredAttributeId = "SP0070";
    public const string UncheckedAllocationArithmeticId = "SP0071";
    public const string SuppressionWithoutJustificationId = "SP0072";
    public const string NullableAnalysisDisabledId = "SP0073";
    public const string IdenticalOperandsId = "SP0074";
    public const string ContainerOwnedServiceDisposedId = "SP0075";
    public const string UnconsumedDeferredQueryId = "SP0076";
    public const string CommonBugKindProperty = "sharpproof.common_bug.kind";
    public const string CommonBugSymbolProperty = "sharpproof.common_bug.symbol";
    public const string NullableContractKindProperty = "sharpproof.nullable.contract_kind";
    public const string NullableContractConditionProperty = "sharpproof.nullable.condition";
    public const string NullableContractTargetProperty = "sharpproof.nullable.target";
    public const string NullableProofStatusProperty = "sharpproof.nullable.proof_status";
    public const string NullableProofReasonProperty = "sharpproof.nullable.proof_reason";

    public const string MisplacedZeroAllocationsAttributeId = "SP0014";

    public const string CapabilityViolationId = "SP0015";
    public const string CapabilityProperty = "sharpproof.capability.flags";
    public const string CapabilityOperationKindProperty = "sharpproof.capability.operation_kind";
    public const string CapabilitySymbolProperty = "sharpproof.capability.symbol";
    public const string CapabilityUnknownReasonProperty = "sharpproof.capability.unknown_reason";

    public const string CapabilityUnknownId = "SP0016";

    public const string MisplacedAllowedCapabilitiesAttributeId = "SP0017";

    public const string EnsuresNotProvenId = "SP0018";
    public const string EnsuresConditionProperty = "sharpproof.ensures.condition";
    public const string EnsuresProofStatusProperty = "sharpproof.ensures.proof_status";
    public const string EnsuresUnknownReasonProperty = "sharpproof.ensures.unknown_reason";
    public const string EnsuresFailureReasonProperty = "sharpproof.ensures.failure_reason";

    public const string EnsuresUnsupportedId = "SP0019";

    public const string MisplacedEnsuresAttributeId = "SP0020";

    public const string ComplexityExceededId = "SP0021";
    public const string ExpectedComplexityProperty = "sharpproof.complexity.expected";
    public const string ActualComplexityProperty = "sharpproof.complexity.actual";
    public const string ComplexityUnknownReasonProperty = "sharpproof.complexity.unknown_reason";

    public const string ComplexityCouldNotBeVerifiedId = "SP0022";

    public const string MisplacedExpectedComplexityAttributeId = "SP0023";

    public const string InvalidContractArgumentId = "SP0024";
    public const string ContractAttributeProperty = "sharpproof.contract.attribute";
    public const string ContractArgumentProperty = "sharpproof.contract.argument";
    public const string ContractInvalidReasonProperty = "sharpproof.contract.invalid_reason";

    public const string InvalidAnalyzerConfigurationId = "SP0025";
    public const string ConfigurationKeyProperty = "sharpproof.config.key";
    public const string ConfigurationValueProperty = "sharpproof.config.value";
    public const string ConfigurationInvalidReasonProperty = "sharpproof.config.invalid_reason";

    public const string InvalidAdditionalFileId = "SP0032";
    public const string AdditionalFilePathProperty = "sharpproof.additional_file.path";
    public const string AdditionalFileReasonProperty = "sharpproof.additional_file.reason";
    public const string AdditionalFileReasonCodeProperty = "sharpproof.additional_file.reason_code";

    public const string UnrecognizedAttributeIdentityId = "SP0026";
    public const string AttributeIdentityNameProperty = "sharpproof.attribute_identity.name";
    public const string AttributeIdentityNamespaceProperty = "sharpproof.attribute_identity.namespace";

    public const string AttributeIdentityAcceptedNamespacesProperty =
        "sharpproof.attribute_identity.accepted_namespaces";

    public const string RequiresNotProvenId = "SP0027";
    public const string RequiresConditionProperty = "sharpproof.requires.condition";
    public const string RequiresProofStatusProperty = "sharpproof.requires.proof_status";
    public const string RequiresUnknownReasonProperty = "sharpproof.requires.unknown_reason";
    public const string RequiresFailureReasonProperty = "sharpproof.requires.failure_reason";
    public const string RequiresCalleeProperty = "sharpproof.requires.callee";

    public const string RequiresUnsupportedId = "SP0028";

    public const string MisplacedRequiresAttributeId = "SP0029";

    public const string ExceptionContractViolationId = "SP0030";
    public const string ExceptionContractAttributeProperty = "sharpproof.exception_contract.attribute";
    public const string ExceptionContractAllowedTypesProperty = "sharpproof.exception_contract.allowed_types";
    public const string ExceptionContractDisallowedTypesProperty = "sharpproof.exception_contract.disallowed_types";

    public const string MisplacedExceptionContractAttributeId = "SP0031";

    public const string MisplacedAttributeId = "SP0003";

    public const string MissingEnforcePureAttributeId = "SP0004";

    public const string ConflictingPurityAttributesId = "SP0005";

    public const string AllowSynchronizationWithoutPurityAttributeId = "SP0006";

    public const string MisplacedAllowSynchronizationAttributeId = "SP0007";

    public const string RedundantAllowSynchronizationId = "SP0008";
    private static DiagnosticDescriptor CreateDescriptor(
        string id,
        LocalizableString title,
        LocalizableString messageFormat,
        string category,
        DiagnosticSeverity defaultSeverity,
        LocalizableString description)
    {
        return CreateDescriptor(id, title, messageFormat, category, defaultSeverity, true, description);
    }

    private static DiagnosticDescriptor CreateDescriptor(
        string id,
        LocalizableString title,
        LocalizableString messageFormat,
        string category,
        DiagnosticSeverity defaultSeverity,
        bool isEnabledByDefault,
        LocalizableString description)
    {
        return new DiagnosticDescriptor(
            id,
            title,
            messageFormat,
            category,
            defaultSeverity,
            isEnabledByDefault,
            description,
            "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#" + id.ToLowerInvariant(),
            new[] { WellKnownDiagnosticTags.Telemetry });
    }

    private static DiagnosticDescriptor CreateCommonBugDescriptor(
        string id,
        LocalizableString title,
        LocalizableString messageFormat,
        string category,
        DiagnosticSeverity defaultSeverity,
        LocalizableString description)
    {
        return CreateDescriptor(id, title, messageFormat, category, defaultSeverity, false, description);
    }

    private static DiagnosticDescriptor CreateMisplacedGetterAliasingAttributeDescriptor(
        string id,
        string attributeName,
        string analysisKind)
    {
        return CreateDescriptor(
            id,
            $"Misplaced [{attributeName}] Attribute",
            $"The [{attributeName}] attribute can only be applied to method-like declarations or getter-bearing properties and indexers",
            "Usage",
            DiagnosticSeverity.Error,
            $"[{attributeName}] configures {analysisKind} analysis for a method-like declaration or aliases the getter of a getter-bearing property or indexer.");
    }

    private static DiagnosticDescriptor CreateInferredContractDescriptor(string id, string title)
    {
        return CreateDescriptor(
            id,
            title,
            InferredContractSuggestionMessageFormat,
            "Suggestions",
            DiagnosticSeverity.Info,
            InferredContractSuggestionDescription);
    }
}

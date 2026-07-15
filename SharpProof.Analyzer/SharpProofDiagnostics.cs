using Microsoft.CodeAnalysis;
using SharpProof.Symbolic;

namespace SharpProof.Analyzer;

public static class SharpProofDiagnostics
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

    public static readonly DiagnosticDescriptor PurityNotVerifiedRule = CreateDescriptor(
        PurityNotVerifiedId,
        "Purity Not Proven",
        "Method '{0}' is marked [EnforcePure]/[Pure], but its body contains operations the analyzer cannot prove pure",
        "Purity",
        DiagnosticSeverity.Error,
        "Methods marked with [EnforcePure] require analysis. This diagnostic indicates the analysis rules did not determine the method's purity status. Use `SharpProof.SymbolicCli explain --file <path> --line <number>` to inspect the symbolic proof evidence.");

    public static readonly DiagnosticDescriptor NullableReturnContractViolationRule = CreateDescriptor(
        NullableReturnContractViolationId,
        "Nullable return contract violated",
        "Method '{0}' can return null despite contract '{1}'",
        "Nullability",
        DiagnosticSeverity.Warning,
        "Reports a reachable normal return that violates the declared nullable return contract.");

    public static readonly DiagnosticDescriptor NullableParameterPostconditionViolationRule = CreateDescriptor(
        NullableParameterPostconditionViolationId,
        "Nullable parameter postcondition violated",
        "Method '{0}' can complete with parameter '{1}' null despite contract '{2}'",
        "Nullability",
        DiagnosticSeverity.Warning,
        "Reports a reachable normal completion that violates a nullable parameter postcondition.");

    public static readonly DiagnosticDescriptor NullableMemberContractViolationRule = CreateDescriptor(
        NullableMemberContractViolationId,
        "Nullable member contract violated",
        "Method '{0}' can complete with member '{1}' null despite contract '{2}'",
        "Nullability",
        DiagnosticSeverity.Warning,
        "Reports a reachable normal completion that violates a member-not-null contract.");

    public static readonly DiagnosticDescriptor UnsafeNullForgivingOperatorRule = CreateDescriptor(
        UnsafeNullForgivingOperatorId,
        "Null-forgiving operator is unsafe",
        "Null-forgiving operator can suppress a feasible null value for '{0}'",
        "Nullability",
        DiagnosticSeverity.Warning,
        "Reports a null-forgiving operator reached by a proven null execution.");

    public static readonly DiagnosticDescriptor UnnecessaryNullForgivingOperatorRule = CreateDescriptor(
        UnnecessaryNullForgivingOperatorId,
        "Null-forgiving operator is unnecessary",
        "Null-forgiving operator is unnecessary because '{0}' is proven non-null",
        "Nullability",
        DiagnosticSeverity.Info,
        "Reports a null-forgiving operator whose operand is already proven non-null.");

    public static readonly DiagnosticDescriptor SuggestNullableContractRule = CreateDescriptor(
        SuggestNullableContractId,
        "Nullable contract can be declared",
        "Method '{0}' satisfies nullable contract '{1}'",
        "Nullability",
        DiagnosticSeverity.Info,
        "Suggests a nullable contract proved by every relevant completion path.");

    public static readonly DiagnosticDescriptor NullableVerificationInconclusiveRule = CreateDescriptor(
        NullableVerificationInconclusiveId,
        "Nullable verification was inconclusive",
        "Nullable contract '{1}' on '{0}' could not be verified: {2}",
        "Nullability",
        DiagnosticSeverity.Info,
        "Reports bounded nullable proofs that ended as unsupported or unknown.");

    public static readonly DiagnosticDescriptor BclFallbackGuessRule = CreateDescriptor(
        BclFallbackGuessId,
        "BCL Purity Fallback Guess",
        "BCL purity fallback for '{0}': {1} ({2})",
        "Purity",
        DiagnosticSeverity.Info,
        "Reports a non-authoritative purity guess for a metadata BCL member when no stronger analyzer, attribute, generated summary, or user configuration evidence was available. Enable with sharpproof_emit_explanations or sharpproof_report_bcl_fallback_guesses.");

    public static readonly DiagnosticDescriptor AllocationInZeroAllocationMethodRule = CreateDescriptor(
        AllocationInZeroAllocationMethodId,
        "Allocation In [ZeroAllocations] Method",
        "Method '{1}' is marked [ZeroAllocations], but operation '{0}' allocates",
        "Allocation",
        DiagnosticSeverity.Warning,
        "Reports direct source-visible allocation sites inside methods annotated with [ZeroAllocations].");

    public static readonly DiagnosticDescriptor PurityExplanationRule = CreateDescriptor(
        PurityExplanationId,
        "Purity Diagnostic Explanation",
        "Purity analysis for '{0}': {1}",
        "Purity",
        DiagnosticSeverity.Info,
        "Provides structured explanation data for a purity diagnostic.");

    public static readonly DiagnosticDescriptor ExceptionSummaryRule = CreateDescriptor(
        ExceptionSummaryId,
        "Method May Throw Exceptions",
        "Method '{0}' can throw: {1}",
        "ExceptionFlow",
        DiagnosticSeverity.Info,
        "Reports exception types that can escape a method. Enable with sharpproof_report_exceptions = true or sharpproof_runtime_hazard_mode = summaries/all/all-and-unknowns. Use `SharpProof.SymbolicCli explain --file <path> --line <number>` to inspect the exception proof evidence.");

    public static readonly DiagnosticDescriptor UncaughtExceptionSiteRule = CreateDescriptor(
        UncaughtExceptionSiteId,
        "Operation May Throw Uncaught Exceptions",
        "Operation '{0}' may throw uncaught exceptions: {1}",
        "ExceptionFlow",
        DiagnosticSeverity.Warning,
        "Reports uncaught exceptions and proven runtime hazards at specific operations. Enable with sharpproof_checked_exceptions = true or sharpproof_runtime_hazard_mode = sites/all/sites-and-unknowns/all-and-unknowns. Use `SharpProof.SymbolicCli explain --file <path> --line <number>` to inspect the runtime hazard evidence.");

    public static readonly DiagnosticDescriptor UnknownRuntimeHazardRule = CreateDescriptor(
        UnknownRuntimeHazardId,
        "Runtime Hazard Candidate Could Not Be Proven",
        "Runtime hazard candidate '{0}' at operation '{1}' could not be proven: {2}",
        "ExceptionFlow",
        DiagnosticSeverity.Info,
        "Reports bounded runtime-hazard candidates whose trigger could not be proven or rejected. " +
        "Enable with sharpproof_runtime_hazard_mode = unknowns, sites-and-unknowns, or all-and-unknowns. " +
        "The diagnostic is informational by default and carries stable proof, reason, trigger, and baseline metadata.");

    private static readonly LocalizableString InferredContractSuggestionMessageFormat =
        "Method '{0}' has {1}; consider adding {2} ({3} confidence)";

    private static readonly LocalizableString InferredContractSuggestionDescription =
        "Reports opt-in inferred contract candidates backed by bounded analyzer evidence. " +
        "Enable with sharpproof_suggest_inferred_contracts and filter by family, visibility, and confidence.";

    public static readonly DiagnosticDescriptor SuggestZeroAllocationsRule = CreateInferredContractDescriptor(
        SuggestZeroAllocationsId,
        "Inferred [ZeroAllocations] Contract");

    public static readonly DiagnosticDescriptor SuggestAllowedCapabilitiesRule = CreateInferredContractDescriptor(
        SuggestAllowedCapabilitiesId,
        "Inferred [AllowedCapabilities] Contract");

    public static readonly DiagnosticDescriptor SuggestExpectedComplexityRule = CreateInferredContractDescriptor(
        SuggestExpectedComplexityId,
        "Inferred [ExpectedComplexity] Contract");

    public static readonly DiagnosticDescriptor SuggestExceptionContractRule = CreateInferredContractDescriptor(
        SuggestExceptionContractId,
        "Inferred Exception Contract");

    public static readonly DiagnosticDescriptor SuggestEnsuresRule = CreateInferredContractDescriptor(
        SuggestEnsuresId,
        "Inferred [Ensures] Contract");

    public static readonly DiagnosticDescriptor SuggestRequiresRule = CreateInferredContractDescriptor(
        SuggestRequiresId,
        "Inferred [Requires] Contract");

    public static readonly DiagnosticDescriptor MisplacedZeroAllocationsAttributeRule =
        CreateMisplacedGetterAliasingAttributeDescriptor(
        MisplacedZeroAllocationsAttributeId,
        "ZeroAllocations",
        "allocation");

    public static readonly DiagnosticDescriptor CapabilityViolationRule = CreateDescriptor(
        CapabilityViolationId,
        "Disallowed Capability Use",
        "Method '{1}' is marked [AllowedCapabilities], but operation '{0}' requires capabilities: {2}",
        "Capabilities",
        DiagnosticSeverity.Warning,
        "Reports source-visible operations or proven transitive callees that exceed the method's declared allowed capability set.");

    public static readonly DiagnosticDescriptor CapabilityUnknownRule = CreateDescriptor(
        CapabilityUnknownId,
        "Capability Contract Not Proven",
        "Method '{1}' is marked [AllowedCapabilities], but operation '{0}' could not be capability-verified: {2}",
        "Capabilities",
        DiagnosticSeverity.Warning,
        "Reports operations whose capability set could not be conservatively proven under the current capability analysis. Use `SharpProof.SymbolicCli explain --file <path> --line <number>` to inspect the capability proof evidence.");

    public static readonly DiagnosticDescriptor MisplacedAllowedCapabilitiesAttributeRule =
        CreateMisplacedGetterAliasingAttributeDescriptor(
        MisplacedAllowedCapabilitiesAttributeId,
        "AllowedCapabilities",
        "capability");

    public static readonly DiagnosticDescriptor EnsuresNotProvenRule = CreateDescriptor(
        EnsuresNotProvenId,
        "Postcondition Not Proven",
        "Method '{1}' is marked [Ensures], but return site '{0}' does not prove postcondition '{2}'",
        "Contracts",
        DiagnosticSeverity.Warning,
        "Reports return sites that contradict a declared [Ensures] postcondition. Use `SharpProof.SymbolicCli explain --file <path> --line <number>` to inspect the proof evidence for each return site.");

    public static readonly DiagnosticDescriptor EnsuresUnsupportedRule = CreateDescriptor(
        EnsuresUnsupportedId,
        "Postcondition Could Not Be Verified",
        "Method '{1}' is marked [Ensures], but postcondition '{0}' could not be verified: {2}",
        "Contracts",
        DiagnosticSeverity.Warning,
        "Reports [Ensures] contracts that could not be parsed, lowered, or proven within the supported bounded proof surface. Use `SharpProof.SymbolicCli explain --file <path> --line <number>` to inspect the unprovable contract details.");

    public static readonly DiagnosticDescriptor MisplacedEnsuresAttributeRule =
        CreateMisplacedGetterAliasingAttributeDescriptor(
        MisplacedEnsuresAttributeId,
        "Ensures",
        "symbolic postcondition");

    public static readonly DiagnosticDescriptor ComplexityExceededRule = CreateDescriptor(
        ComplexityExceededId,
        "Declared Complexity Exceeded",
        "Method '{0}' is marked [ExpectedComplexity({1})], but inferred complexity '{2}' exceeds the declared bound",
        "Complexity",
        DiagnosticSeverity.Warning,
        "Reports methods whose inferred bounded complexity is stronger than the declared [ExpectedComplexity] contract allows. Use `SharpProof.SymbolicCli explain --file <path> --line <number>` to inspect the complexity proof evidence.");

    public static readonly DiagnosticDescriptor ComplexityCouldNotBeVerifiedRule = CreateDescriptor(
        ComplexityCouldNotBeVerifiedId,
        "Declared Complexity Could Not Be Verified",
        "Method '{0}' is marked [ExpectedComplexity({1})], but the declared bound could not be verified conservatively: {2}",
        "Complexity",
        DiagnosticSeverity.Warning,
        "Reports [ExpectedComplexity] contracts that could not be verified because the inferred complexity is unknown, unsupported, or incomparable with the declared bound. Use `SharpProof.SymbolicCli explain --file <path> --line <number>` to inspect the complexity analysis details.");

    public static readonly DiagnosticDescriptor MisplacedExpectedComplexityAttributeRule =
        CreateMisplacedGetterAliasingAttributeDescriptor(
        MisplacedExpectedComplexityAttributeId,
        "ExpectedComplexity",
        "complexity");

    public static readonly DiagnosticDescriptor InvalidContractArgumentRule = CreateDescriptor(
        InvalidContractArgumentId,
        "Invalid SharpProof Contract Argument",
        "SharpProof contract '{0}' has invalid argument '{1}': {2}",
        "Usage",
        DiagnosticSeverity.Error,
        "Reports malformed SharpProof contract arguments, such as empty [Ensures] conditions, undefined [ExpectedComplexity] values, unknown [AllowedCapabilities] bits, and non-exception [AllowedExceptions] types.");

    public static readonly DiagnosticDescriptor InvalidAnalyzerConfigurationRule = CreateDescriptor(
        InvalidAnalyzerConfigurationId,
        "Invalid SharpProof Analyzer Configuration",
        "SharpProof analyzer option '{0}' has invalid value '{1}': {2}",
        "Configuration",
        DiagnosticSeverity.Warning,
        "Reports invalid sharpproof_* analyzer configuration values that would otherwise fall back to defaults silently.");

    public static readonly DiagnosticDescriptor InvalidAdditionalFileRule = CreateDescriptor(
        InvalidAdditionalFileId,
        "Invalid SharpProof Analyzer Additional File",
        "SharpProof analyzer input file '{0}' is invalid: {1}",
        "Configuration",
        DiagnosticSeverity.Warning,
        "Reports malformed, empty, unsupported, or partially ignored SharpProof analyzer AdditionalFiles instead of silently dropping their data.");

    public static readonly DiagnosticDescriptor TrustedBoundaryReviewRule = CreateDescriptor(
        TrustedBoundaryReviewId,
        "Trusted Purity Boundary Review",
        "Purity trust source '{0}' for '{1}' was {2}{3}",
        "Review",
        DiagnosticSeverity.Info,
        "Reports structured, opt-in audit evidence for applied and overridden purity trust shortcuts.");

    public static readonly DiagnosticDescriptor UnrecognizedAttributeIdentityRule = CreateDescriptor(
        UnrecognizedAttributeIdentityId,
        "Unrecognized SharpProof Attribute Identity",
        "Attribute '{0}' looks like a SharpProof contract, but type '{1}' is not in an accepted SharpProof attribute namespace",
        "Usage",
        DiagnosticSeverity.Warning,
        "Reports attributes whose simple name matches a SharpProof contract attribute but whose containing namespace is neither SharpProof.Attributes nor an opt-in source-stub namespace.");

    public static readonly DiagnosticDescriptor RequiresNotProvenRule = CreateDescriptor(
        RequiresNotProvenId,
        "Precondition Not Proven",
        "Call to '{0}' does not prove precondition '{1}'",
        "Contracts",
        DiagnosticSeverity.Warning,
        "Reports calls whose current path facts contradict a declared [Requires] precondition. Use `SharpProof.SymbolicCli explain --file <path> --line <number>` to inspect the proof evidence.");

    public static readonly DiagnosticDescriptor RequiresUnsupportedRule = CreateDescriptor(
        RequiresUnsupportedId,
        "Precondition Could Not Be Verified",
        "Precondition '{1}' for '{0}' could not be verified: {2}",
        "Contracts",
        DiagnosticSeverity.Warning,
        "Reports [Requires] contracts that could not be parsed, lowered, or proven within the supported bounded proof surface. Use `SharpProof.SymbolicCli explain --file <path> --line <number>` to inspect the unprovable contract details.");

    public static readonly DiagnosticDescriptor MisplacedRequiresAttributeRule = CreateDescriptor(
        MisplacedRequiresAttributeId,
        "Misplaced [Requires] Attribute",
        "The [Requires] attribute can only be applied to method-like declarations",
        "Usage",
        DiagnosticSeverity.Error,
        "[Requires] configures symbolic call-site precondition analysis for a method-like declaration. On a property or indexer, place it on the explicit get accessor.");

    public static readonly DiagnosticDescriptor ExceptionContractViolationRule = CreateDescriptor(
        ExceptionContractViolationId,
        "Exception Contract Violated",
        "Method '{0}' is marked {1}, but operation '{2}' can throw disallowed exceptions: {3}",
        "ExceptionFlow",
        DiagnosticSeverity.Warning,
        "Reports operations whose escaping exceptions violate [DoesNotThrow] or [AllowedExceptions] contracts. Use `SharpProof.SymbolicCli explain --file <path> --line <number>` to inspect the exception proof evidence.");

    public static readonly DiagnosticDescriptor MisplacedExceptionContractAttributeRule = CreateDescriptor(
        MisplacedExceptionContractAttributeId,
        "Misplaced Exception Contract Attribute",
        "The [DoesNotThrow] and [AllowedExceptions] attributes can only be applied to method-like declarations or getter-bearing properties and indexers",
        "Usage",
        DiagnosticSeverity.Error,
        "[DoesNotThrow] and [AllowedExceptions] configure exception analysis for method-like declarations or alias the getter of a getter-bearing property or indexer.");

    public static readonly DiagnosticDescriptor MisplacedAttributeRule = CreateDescriptor(
        MisplacedAttributeId,
        "Misplaced [EnforcePure] Attribute",
        "The [EnforcePure]/[Pure] attributes can only be applied to method-like declarations or getter-bearing properties and indexers",
        "Usage",
        DiagnosticSeverity.Error,
        "[EnforcePure] and [Pure] configure purity analysis for a method-like declaration or alias the getter of a getter-bearing property or indexer.");

    public static readonly DiagnosticDescriptor MissingEnforcePureAttributeRule = CreateDescriptor(
        MissingEnforcePureAttributeId,
        "Missing [EnforcePure] Attribute",
        "Method '{0}' appears to be pure but is not marked with [EnforcePure]. Consider adding the attribute to enforce and document its purity.",
        "Purity",
        DiagnosticSeverity.Warning,
        "This method seems to only contain operations considered pure, but it lacks the [EnforcePure] attribute. Adding the attribute helps ensure its purity is maintained and communicates intent.");

    public static readonly DiagnosticDescriptor ConflictingPurityAttributesRule = CreateDescriptor(
        ConflictingPurityAttributesId,
        "Conflicting purity attributes",
        "Method '{0}' has conflicting purity attributes applied",
        "Usage",
        DiagnosticSeverity.Warning,
        "Apply one purity contract to a method. Combining enforcing, trusted-external, and explicit-impure attributes is contradictory or confusing.");

    public static readonly DiagnosticDescriptor AllowSynchronizationWithoutPurityAttributeRule = CreateDescriptor(
        AllowSynchronizationWithoutPurityAttributeId,
        "[AllowSynchronization] requires a purity attribute",
        "Method '{0}' is marked with [AllowSynchronization] but is not marked with [EnforcePure] or [Pure]",
        "Usage",
        DiagnosticSeverity.Warning,
        "[AllowSynchronization] only affects methods participating in purity analysis. Apply [EnforcePure] or [Pure] for it to have effect.");

    public static readonly DiagnosticDescriptor MisplacedAllowSynchronizationAttributeRule = CreateDescriptor(
        MisplacedAllowSynchronizationAttributeId,
        "Misplaced [AllowSynchronization] Attribute",
        "The [AllowSynchronization] attribute can only be applied to method declarations",
        "Usage",
        DiagnosticSeverity.Error,
        "[AllowSynchronization] configures analyzer behavior for a method and should not be used on non-method declarations.");

    public static readonly DiagnosticDescriptor RedundantAllowSynchronizationRule = CreateDescriptor(
        RedundantAllowSynchronizationId,
        "Redundant [AllowSynchronization]",
        "Method '{0}' is marked with [AllowSynchronization] but contains no synchronization constructs",
        "Usage",
        DiagnosticSeverity.Info,
        "Remove [AllowSynchronization] when the method does not use synchronization (e.g., lock).");

    public static readonly DiagnosticDescriptor AwaitNullConditionalRule = CreateCommonBugDescriptor(
        AwaitNullConditionalId,
        "Awaiting a null-conditional expression",
        "Awaiting null-conditional expression '{0}' can dereference a null awaitable",
        "AsyncCorrectness",
        DiagnosticSeverity.Warning,
        "A null-conditional invocation produces a null awaitable when its receiver is null. Coalesce to a non-null awaitable or guard the receiver before awaiting.");

    public static readonly DiagnosticDescriptor TaskConvertedToStringRule = CreateCommonBugDescriptor(
        TaskConvertedToStringId,
        "Task converted to text without awaiting",
        "Task expression '{0}' is converted to text instead of awaiting its result",
        "AsyncCorrectness",
        DiagnosticSeverity.Warning,
        "String concatenation and interpolation call ToString on Task objects; they do not use the asynchronous result.");

    public static readonly DiagnosticDescriptor TaskCompletionSourceContinuationsRule = CreateCommonBugDescriptor(
        TaskCompletionSourceContinuationsId,
        "TaskCompletionSource may run continuations synchronously",
        "TaskCompletionSource construction '{0}' does not prove RunContinuationsAsynchronously",
        "AsyncCorrectness",
        DiagnosticSeverity.Warning,
        "TaskCompletionSource should normally include TaskCreationOptions.RunContinuationsAsynchronously to prevent completing threads from running arbitrary continuations inline.");

    public static readonly DiagnosticDescriptor AsyncVoidRule = CreateCommonBugDescriptor(
        AsyncVoidId,
        "Async void method is not an event handler",
        "Async void method '{0}' is not an event handler; return Task so callers can observe completion and exceptions",
        "AsyncCorrectness",
        DiagnosticSeverity.Warning,
        "Async void prevents callers from awaiting completion and routes exceptions through the current synchronization context. It is reserved for event-handler-shaped methods.");

    public static readonly DiagnosticDescriptor BlockingAsyncRule = CreateCommonBugDescriptor(
        BlockingAsyncId,
        "Async method blocks on asynchronous work",
        "Async method '{0}' synchronously blocks on '{1}'",
        "AsyncCorrectness",
        DiagnosticSeverity.Warning,
        "Calling Task.Result, Task.Wait, or GetAwaiter().GetResult() inside async code can deadlock or starve worker threads. Await the operation instead.");

    public static readonly DiagnosticDescriptor NullTaskReturnRule = CreateCommonBugDescriptor(
        NullTaskReturnId,
        "Task-returning method returns null",
        "Task-returning method '{0}' returns null; callers that await it will throw",
        "AsyncCorrectness",
        DiagnosticSeverity.Warning,
        "A non-async method whose declared return type is Task or Task<T> must return a task object, not null.");

    public static readonly DiagnosticDescriptor TaskUsedAsDisposableRule = CreateCommonBugDescriptor(
        TaskUsedAsDisposableId,
        "Task used as a disposable resource",
        "Task expression '{0}' is disposed by using instead of awaiting its result",
        "AsyncCorrectness",
        DiagnosticSeverity.Warning,
        "Using a Task disposes the task object; it does not await the asynchronous operation or manage the resource produced by that operation.");

    public static readonly DiagnosticDescriptor AsyncValidationDeferredRule = CreateCommonBugDescriptor(
        AsyncValidationDeferredId,
        "Public async parameter validation is deferred",
        "Validation in async method '{0}' is captured by the returned task; use a synchronous wrapper when fail-fast argument validation is required",
        "AsyncCorrectness",
        DiagnosticSeverity.Info,
        "Exceptions thrown from an async Task method, including validation before its first await, are stored in the returned task. A synchronous wrapper is required when callers must observe argument errors at invocation time.");

    public static readonly DiagnosticDescriptor CollectionMutationDuringEnumerationRule = CreateCommonBugDescriptor(
        CollectionMutationDuringEnumerationId,
        "Collection mutated during enumeration",
        "Collection '{0}' is mutated by '{1}' while it is being enumerated",
        "CollectionSafety",
        DiagnosticSeverity.Warning,
        "Mutating the same ordinary mutable collection inside its foreach body invalidates the active enumerator and commonly throws InvalidOperationException.");

    public static readonly DiagnosticDescriptor CapturedLoopVariableRule = CreateCommonBugDescriptor(
        CapturedLoopVariableId,
        "For-loop variable captured by an escaping closure",
        "For-loop variable '{0}' is captured by a closure that can observe a later iteration value",
        "Correctness",
        DiagnosticSeverity.Warning,
        "For-loop iteration variables are shared across iterations. Copy the value into a local inside the loop before capturing it in a lambda that can escape the iteration.");

    public static readonly DiagnosticDescriptor MutableStructRule = CreateCommonBugDescriptor(
        MutableStructId,
        "Struct exposes mutable instance state",
        "Struct '{0}' has mutable instance state; copies can be modified independently",
        "Design",
        DiagnosticSeverity.Info,
        "Mutable value types are frequently modified through accidental copies. Prefer readonly struct, record struct with immutable members, or a class when shared mutation is intended.");

    public static readonly DiagnosticDescriptor OwnedDisposableFieldRule = CreateCommonBugDescriptor(
        OwnedDisposableFieldId,
        "Owned disposable field has no owner disposal contract",
        "Type '{0}' creates disposable field '{1}' but does not implement '{2}'",
        "ResourceLifetime",
        DiagnosticSeverity.Warning,
        "A field initialized or assigned from a local allocation is owned by its containing type. The owner must expose the matching deterministic disposal lifecycle.");

    public static readonly DiagnosticDescriptor HttpClientInLoopRule = CreateCommonBugDescriptor(
        HttpClientInLoopId,
        "HttpClient created repeatedly inside a loop",
        "HttpClient is created inside loop '{0}'; reuse a client or use IHttpClientFactory",
        "ResourceLifetime",
        DiagnosticSeverity.Warning,
        "Repeated HttpClient construction creates avoidable connection pools and can exhaust sockets under sustained load.");

    public static readonly DiagnosticDescriptor UnsynchronizedSharedMutationRule = CreateCommonBugDescriptor(
        UnsynchronizedSharedMutationId,
        "Shared state mutated by a parallel callback",
        "Shared state '{0}' is mutated in '{1}' without visible synchronization",
        "Concurrency",
        DiagnosticSeverity.Warning,
        "Captured locals and fields mutated by Task, Thread, timer, or Parallel callbacks require synchronization such as Interlocked or lock, or should be replaced with isolated state.");

    public static readonly DiagnosticDescriptor ConcurrentCollectionEnumerationRule = CreateCommonBugDescriptor(
        ConcurrentCollectionEnumerationId,
        "Concurrent collection enumerated through LINQ",
        "LINQ operator '{0}' enumerates concurrent collection '{1}' without snapshot guarantees",
        "Concurrency",
        DiagnosticSeverity.Info,
        "Concurrent collection members have documented concurrency behavior, but interface and LINQ extension methods do not necessarily provide an atomic snapshot.");

    public static readonly DiagnosticDescriptor BoxingInLoopRule = CreateCommonBugDescriptor(
        BoxingInLoopId,
        "Value boxed inside a loop",
        "Value of type '{0}' is boxed inside loop '{1}'",
        "Performance",
        DiagnosticSeverity.Info,
        "Repeated boxing allocates objects and adds GC pressure. Prefer generic APIs or strongly typed interfaces in repeated paths.");

    public static readonly DiagnosticDescriptor MaybeNullResultDereferenceRule = CreateCommonBugDescriptor(
        MaybeNullResultDereferenceId,
        "Maybe-null query result is dereferenced",
        "Result of '{0}' can be null or empty-default and is dereferenced immediately",
        "Nullability",
        DiagnosticSeverity.Warning,
        "Default-returning lookup and LINQ APIs require a guard, a non-null fallback, or a proof that a matching element exists before dereference.");

    public static readonly DiagnosticDescriptor PrematureQueryMaterializationRule = CreateCommonBugDescriptor(
        PrematureQueryMaterializationId,
        "Queryable is materialized before further filtering",
        "'{0}' materializes IQueryable before subsequent '{1}' processing",
        "Performance",
        DiagnosticSeverity.Info,
        "Compose supported filters and projections on IQueryable before materialization so the remote provider can execute them.");

    public static readonly DiagnosticDescriptor DeferredQuerySideEffectRule = CreateCommonBugDescriptor(
        DeferredQuerySideEffectId,
        "Deferred query lambda mutates state",
        "Deferred LINQ operator '{0}' contains state mutation '{1}'",
        "Correctness",
        DiagnosticSeverity.Warning,
        "Side effects in deferred LINQ lambdas run on every enumeration and can therefore execute zero, one, or multiple times unexpectedly.");

    public static readonly DiagnosticDescriptor QueryTranslationRiskRule = CreateCommonBugDescriptor(
        QueryTranslationRiskId,
        "Queryable expression calls source-only method",
        "Queryable operator '{0}' calls source method '{1}' that the remote provider may not translate",
        "Compatibility",
        DiagnosticSeverity.Info,
        "Remote IQueryable providers translate a bounded method set. Source-only helper calls in query predicates and selectors require provider-specific translation support.");

    public static readonly DiagnosticDescriptor SerializationCycleRiskRule = CreateCommonBugDescriptor(
        SerializationCycleRiskId,
        "Serialized source type has a reference cycle",
        "Type '{0}' contains a serializable reference cycle and is serialized without explicit cycle handling",
        "Serialization",
        DiagnosticSeverity.Info,
        "System.Text.Json rejects reachable object cycles by default. Project cyclic entities to DTOs, ignore a link, or configure deliberate reference handling.");

    public static readonly DiagnosticDescriptor SerializerAttributeMismatchRule = CreateCommonBugDescriptor(
        SerializerAttributeMismatchId,
        "Serializer ignores attribute from another JSON library",
        "Serializer '{0}' does not honor attribute '{1}' on member '{2}'",
        "Serialization",
        DiagnosticSeverity.Warning,
        "Newtonsoft.Json and System.Text.Json define similarly named attributes that are not interchangeable.");

    public static readonly DiagnosticDescriptor IneffectiveRequiredAttributeRule = CreateCommonBugDescriptor(
        IneffectiveRequiredAttributeId,
        "Required attribute cannot reject a non-nullable value type default",
        "[Required] on non-nullable value member '{0}' cannot distinguish omitted input from default({1})",
        "Usage",
        DiagnosticSeverity.Info,
        "Use a nullable value type when model binding must distinguish missing input, then validate the resulting value range separately.");

    public static readonly DiagnosticDescriptor UncheckedAllocationArithmeticRule = CreateCommonBugDescriptor(
        UncheckedAllocationArithmeticId,
        "Allocation length uses unchecked arithmetic",
        "Allocation length expression '{0}' can wrap before bounds validation",
        "Correctness",
        DiagnosticSeverity.Warning,
        "Compute allocation sizes in a checked context so overflow is reported instead of becoming a wrapped negative or undersized length.");

    public static readonly DiagnosticDescriptor SuppressionWithoutJustificationRule = CreateCommonBugDescriptor(
        SuppressionWithoutJustificationId,
        "Diagnostic suppression lacks justification",
        "Suppression '{0}' has no reviewable diagnostic scope or justification",
        "Review",
        DiagnosticSeverity.Info,
        "Broad pragma suppressions and SuppressMessage attributes without justification hide warning debt and should be narrowed or documented.");

    public static readonly DiagnosticDescriptor NullableAnalysisDisabledRule = CreateCommonBugDescriptor(
        NullableAnalysisDisabledId,
        "Nullable analysis explicitly disabled",
        "Nullable analysis is disabled for this source region",
        "Nullability",
        DiagnosticSeverity.Info,
        "Explicit nullable-disable directives create blind spots in compile-time null-state analysis. Prefer scoped annotations and resolved warnings.");

    public static readonly DiagnosticDescriptor IdenticalOperandsRule = CreateCommonBugDescriptor(
        IdenticalOperandsId,
        "Binary operation uses the same value on both sides",
        "Operation '{0}' uses '{1}' as both operands; verify that the second operand is correct",
        "Correctness",
        DiagnosticSeverity.Warning,
        "Identical stable operands in built-in comparisons, subtraction, division, and remainder usually indicate a copied or mistyped operand. Floating-point and user-defined operators are excluded because they need not be reflexive.");

    public static readonly DiagnosticDescriptor ContainerOwnedServiceDisposedRule = CreateCommonBugDescriptor(
        ContainerOwnedServiceDisposedId,
        "Container-owned service is disposed by its consumer",
        "Service resolved by '{0}' is disposed by consuming code; the dependency-injection container owns its lifetime",
        "ResourceLifetime",
        DiagnosticSeverity.Warning,
        "Services resolved from IServiceProvider are disposed by their owning service provider or scope. Consumers should not wrap the resolved service in using or call Dispose directly.");

    public static readonly DiagnosticDescriptor UnconsumedDeferredQueryRule = CreateCommonBugDescriptor(
        UnconsumedDeferredQueryId,
        "Deferred query is never consumed",
        "Deferred query produced by '{0}' is never enumerated or materialized",
        "Correctness",
        DiagnosticSeverity.Warning,
        "LINQ query operators are deferred. Constructing a query and discarding it performs no work and does not execute its predicate or selector.");

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

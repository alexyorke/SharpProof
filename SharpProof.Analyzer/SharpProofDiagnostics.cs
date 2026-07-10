using Microsoft.CodeAnalysis;

namespace SharpProof.Analyzer;

public static class SharpProofDiagnostics
{
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
    private static readonly LocalizableString PurityNotVerifiedTitle = "Purity Not Proven";

    private static readonly LocalizableString PurityNotVerifiedMessageFormat =
        "Method '{0}' is marked [EnforcePure]/[Pure], but its body contains operations the analyzer cannot prove pure";

    private static readonly LocalizableString PurityNotVerifiedDescription =
        "Methods marked with [EnforcePure] require analysis. This diagnostic indicates the analysis rules did not determine the method's purity status. Use `SharpProof.SymbolicCli explain --file <path> --line <number>` to inspect the symbolic proof evidence.";

    public static readonly DiagnosticDescriptor PurityNotVerifiedRule = CreateDescriptor(
        PurityNotVerifiedId,
        PurityNotVerifiedTitle,
        PurityNotVerifiedMessageFormat,
        "Purity",
        DiagnosticSeverity.Error,
        true,
        PurityNotVerifiedDescription);

    private static readonly LocalizableString BclFallbackGuessTitle = "BCL Purity Fallback Guess";

    private static readonly LocalizableString
        BclFallbackGuessMessageFormat = "BCL purity fallback for '{0}': {1} ({2})";

    private static readonly LocalizableString BclFallbackGuessDescription =
        "Reports a non-authoritative purity guess for a metadata BCL member when no stronger analyzer, attribute, generated summary, or user configuration evidence was available. Enable with sharpproof_emit_explanations or sharpproof_report_bcl_fallback_guesses.";

    public static readonly DiagnosticDescriptor BclFallbackGuessRule = CreateDescriptor(
        BclFallbackGuessId,
        BclFallbackGuessTitle,
        BclFallbackGuessMessageFormat,
        "Purity",
        DiagnosticSeverity.Info,
        true,
        BclFallbackGuessDescription);

    private static readonly LocalizableString AllocationInZeroAllocationMethodTitle =
        "Allocation In [ZeroAllocations] Method";

    private static readonly LocalizableString AllocationInZeroAllocationMethodMessageFormat =
        "Method '{1}' is marked [ZeroAllocations], but operation '{0}' allocates";

    private static readonly LocalizableString AllocationInZeroAllocationMethodDescription =
        "Reports direct source-visible allocation sites inside methods annotated with [ZeroAllocations].";

    public static readonly DiagnosticDescriptor AllocationInZeroAllocationMethodRule = CreateDescriptor(
        AllocationInZeroAllocationMethodId,
        AllocationInZeroAllocationMethodTitle,
        AllocationInZeroAllocationMethodMessageFormat,
        "Allocation",
        DiagnosticSeverity.Warning,
        true,
        AllocationInZeroAllocationMethodDescription);

    private static readonly LocalizableString PurityExplanationTitle = "Purity Diagnostic Explanation";
    private static readonly LocalizableString PurityExplanationMessageFormat = "Purity analysis for '{0}': {1}";

    private static readonly LocalizableString PurityExplanationDescription =
        "Provides structured explanation data for a purity diagnostic.";

    public static readonly DiagnosticDescriptor PurityExplanationRule = CreateDescriptor(
        PurityExplanationId,
        PurityExplanationTitle,
        PurityExplanationMessageFormat,
        "Purity",
        DiagnosticSeverity.Info,
        true,
        PurityExplanationDescription);

    private static readonly LocalizableString ExceptionSummaryTitle = "Method May Throw Exceptions";
    private static readonly LocalizableString ExceptionSummaryMessageFormat = "Method '{0}' can throw: {1}";

    private static readonly LocalizableString ExceptionSummaryDescription =
        "Reports exception types that can escape a method. Enable with sharpproof_report_exceptions = true or sharpproof_runtime_hazard_mode = summaries/all. Use `SharpProof.SymbolicCli explain --file <path> --line <number>` to inspect the exception proof evidence.";

    public static readonly DiagnosticDescriptor ExceptionSummaryRule = CreateDescriptor(
        ExceptionSummaryId,
        ExceptionSummaryTitle,
        ExceptionSummaryMessageFormat,
        "ExceptionFlow",
        DiagnosticSeverity.Info,
        true,
        ExceptionSummaryDescription);

    private static readonly LocalizableString UncaughtExceptionSiteTitle = "Operation May Throw Uncaught Exceptions";

    private static readonly LocalizableString UncaughtExceptionSiteMessageFormat =
        "Operation '{0}' may throw uncaught exceptions: {1}";

    private static readonly LocalizableString UncaughtExceptionSiteDescription =
        "Reports uncaught exceptions and proven runtime hazards at specific operations. Enable with sharpproof_checked_exceptions = true or sharpproof_runtime_hazard_mode = sites/all. Use `SharpProof.SymbolicCli explain --file <path> --line <number>` to inspect the runtime hazard evidence.";

    public static readonly DiagnosticDescriptor UncaughtExceptionSiteRule = CreateDescriptor(
        UncaughtExceptionSiteId,
        UncaughtExceptionSiteTitle,
        UncaughtExceptionSiteMessageFormat,
        "ExceptionFlow",
        DiagnosticSeverity.Warning,
        true,
        UncaughtExceptionSiteDescription);

    private static readonly LocalizableString UnknownRuntimeHazardTitle =
        "Runtime Hazard Candidate Could Not Be Proven";

    private static readonly LocalizableString UnknownRuntimeHazardMessageFormat =
        "Runtime hazard candidate '{0}' at operation '{1}' could not be proven: {2}";

    private static readonly LocalizableString UnknownRuntimeHazardDescription =
        "Reports bounded runtime-hazard candidates whose trigger could not be proven or rejected. " +
        "Enable with sharpproof_runtime_hazard_mode = unknowns, sites-and-unknowns, or all-and-unknowns. " +
        "The diagnostic is informational by default and carries stable proof, reason, trigger, and baseline metadata.";

    public static readonly DiagnosticDescriptor UnknownRuntimeHazardRule = CreateDescriptor(
        UnknownRuntimeHazardId,
        UnknownRuntimeHazardTitle,
        UnknownRuntimeHazardMessageFormat,
        "ExceptionFlow",
        DiagnosticSeverity.Info,
        true,
        UnknownRuntimeHazardDescription);

    private static readonly LocalizableString MisplacedZeroAllocationsAttributeTitle =
        "Misplaced [ZeroAllocations] Attribute";

    private static readonly LocalizableString MisplacedZeroAllocationsAttributeMessageFormat =
        "The [ZeroAllocations] attribute can only be applied to method declarations";

    private static readonly LocalizableString MisplacedZeroAllocationsAttributeDescription =
        "[ZeroAllocations] configures analyzer behavior for a method and should not be used on non-method declarations.";

    public static readonly DiagnosticDescriptor MisplacedZeroAllocationsAttributeRule = CreateDescriptor(
        MisplacedZeroAllocationsAttributeId,
        MisplacedZeroAllocationsAttributeTitle,
        MisplacedZeroAllocationsAttributeMessageFormat,
        "Usage",
        DiagnosticSeverity.Error,
        true,
        MisplacedZeroAllocationsAttributeDescription);

    private static readonly LocalizableString CapabilityViolationTitle = "Disallowed Capability Use";

    private static readonly LocalizableString CapabilityViolationMessageFormat =
        "Method '{1}' is marked [AllowedCapabilities], but operation '{0}' requires capabilities: {2}";

    private static readonly LocalizableString CapabilityViolationDescription =
        "Reports source-visible operations or proven transitive callees that exceed the method's declared allowed capability set.";

    public static readonly DiagnosticDescriptor CapabilityViolationRule = CreateDescriptor(
        CapabilityViolationId,
        CapabilityViolationTitle,
        CapabilityViolationMessageFormat,
        "Capabilities",
        DiagnosticSeverity.Warning,
        true,
        CapabilityViolationDescription);

    private static readonly LocalizableString CapabilityUnknownTitle = "Capability Contract Not Proven";

    private static readonly LocalizableString CapabilityUnknownMessageFormat =
        "Method '{1}' is marked [AllowedCapabilities], but operation '{0}' could not be capability-verified: {2}";

    private static readonly LocalizableString CapabilityUnknownDescription =
        "Reports operations whose capability set could not be conservatively proven under the current capability analysis. Use `SharpProof.SymbolicCli explain --file <path> --line <number>` to inspect the capability proof evidence.";

    public static readonly DiagnosticDescriptor CapabilityUnknownRule = CreateDescriptor(
        CapabilityUnknownId,
        CapabilityUnknownTitle,
        CapabilityUnknownMessageFormat,
        "Capabilities",
        DiagnosticSeverity.Warning,
        true,
        CapabilityUnknownDescription);

    private static readonly LocalizableString MisplacedAllowedCapabilitiesAttributeTitle =
        "Misplaced [AllowedCapabilities] Attribute";

    private static readonly LocalizableString MisplacedAllowedCapabilitiesAttributeMessageFormat =
        "The [AllowedCapabilities] attribute can only be applied to method declarations";

    private static readonly LocalizableString MisplacedAllowedCapabilitiesAttributeDescription =
        "[AllowedCapabilities] configures capability-contract analysis for a method and should not be used on non-method declarations.";

    public static readonly DiagnosticDescriptor MisplacedAllowedCapabilitiesAttributeRule = CreateDescriptor(
        MisplacedAllowedCapabilitiesAttributeId,
        MisplacedAllowedCapabilitiesAttributeTitle,
        MisplacedAllowedCapabilitiesAttributeMessageFormat,
        "Usage",
        DiagnosticSeverity.Error,
        true,
        MisplacedAllowedCapabilitiesAttributeDescription);

    private static readonly LocalizableString EnsuresNotProvenTitle = "Postcondition Not Proven";

    private static readonly LocalizableString EnsuresNotProvenMessageFormat =
        "Method '{1}' is marked [Ensures], but return site '{0}' does not prove postcondition '{2}'";

    private static readonly LocalizableString EnsuresNotProvenDescription =
        "Reports return sites that contradict a declared [Ensures] postcondition. Use `SharpProof.SymbolicCli explain --file <path> --line <number>` to inspect the proof evidence for each return site.";

    public static readonly DiagnosticDescriptor EnsuresNotProvenRule = CreateDescriptor(
        EnsuresNotProvenId,
        EnsuresNotProvenTitle,
        EnsuresNotProvenMessageFormat,
        "Contracts",
        DiagnosticSeverity.Warning,
        true,
        EnsuresNotProvenDescription);

    private static readonly LocalizableString EnsuresUnsupportedTitle = "Postcondition Could Not Be Verified";

    private static readonly LocalizableString EnsuresUnsupportedMessageFormat =
        "Method '{1}' is marked [Ensures], but postcondition '{0}' could not be verified: {2}";

    private static readonly LocalizableString EnsuresUnsupportedDescription =
        "Reports [Ensures] contracts that could not be parsed, lowered, or proven within the supported bounded proof surface. Use `SharpProof.SymbolicCli explain --file <path> --line <number>` to inspect the unprovable contract details.";

    public static readonly DiagnosticDescriptor EnsuresUnsupportedRule = CreateDescriptor(
        EnsuresUnsupportedId,
        EnsuresUnsupportedTitle,
        EnsuresUnsupportedMessageFormat,
        "Contracts",
        DiagnosticSeverity.Warning,
        true,
        EnsuresUnsupportedDescription);

    private static readonly LocalizableString MisplacedEnsuresAttributeTitle = "Misplaced [Ensures] Attribute";

    private static readonly LocalizableString MisplacedEnsuresAttributeMessageFormat =
        "The [Ensures] attribute can only be applied to method-like declarations";

    private static readonly LocalizableString MisplacedEnsuresAttributeDescription =
        "[Ensures] configures symbolic postcondition analysis for a method-like declaration and should not be used on non-method declarations.";

    public static readonly DiagnosticDescriptor MisplacedEnsuresAttributeRule = CreateDescriptor(
        MisplacedEnsuresAttributeId,
        MisplacedEnsuresAttributeTitle,
        MisplacedEnsuresAttributeMessageFormat,
        "Usage",
        DiagnosticSeverity.Error,
        true,
        MisplacedEnsuresAttributeDescription);

    private static readonly LocalizableString ComplexityExceededTitle = "Declared Complexity Exceeded";

    private static readonly LocalizableString ComplexityExceededMessageFormat =
        "Method '{0}' is marked [ExpectedComplexity({1})], but inferred complexity '{2}' exceeds the declared bound";

    private static readonly LocalizableString ComplexityExceededDescription =
        "Reports methods whose inferred bounded complexity is stronger than the declared [ExpectedComplexity] contract allows. Use `SharpProof.SymbolicCli explain --file <path> --line <number>` to inspect the complexity proof evidence.";

    public static readonly DiagnosticDescriptor ComplexityExceededRule = CreateDescriptor(
        ComplexityExceededId,
        ComplexityExceededTitle,
        ComplexityExceededMessageFormat,
        "Complexity",
        DiagnosticSeverity.Warning,
        true,
        ComplexityExceededDescription);

    private static readonly LocalizableString ComplexityCouldNotBeVerifiedTitle =
        "Declared Complexity Could Not Be Verified";

    private static readonly LocalizableString ComplexityCouldNotBeVerifiedMessageFormat =
        "Method '{0}' is marked [ExpectedComplexity({1})], but the declared bound could not be verified conservatively: {2}";

    private static readonly LocalizableString ComplexityCouldNotBeVerifiedDescription =
        "Reports [ExpectedComplexity] contracts that could not be verified because the inferred complexity is unknown, unsupported, or incomparable with the declared bound. Use `SharpProof.SymbolicCli explain --file <path> --line <number>` to inspect the complexity analysis details.";

    public static readonly DiagnosticDescriptor ComplexityCouldNotBeVerifiedRule = CreateDescriptor(
        ComplexityCouldNotBeVerifiedId,
        ComplexityCouldNotBeVerifiedTitle,
        ComplexityCouldNotBeVerifiedMessageFormat,
        "Complexity",
        DiagnosticSeverity.Warning,
        true,
        ComplexityCouldNotBeVerifiedDescription);

    private static readonly LocalizableString MisplacedExpectedComplexityAttributeTitle =
        "Misplaced [ExpectedComplexity] Attribute";

    private static readonly LocalizableString MisplacedExpectedComplexityAttributeMessageFormat =
        "The [ExpectedComplexity] attribute can only be applied to method-like declarations";

    private static readonly LocalizableString MisplacedExpectedComplexityAttributeDescription =
        "[ExpectedComplexity] configures complexity-contract analysis for a method-like declaration and should not be used on non-method declarations.";

    public static readonly DiagnosticDescriptor MisplacedExpectedComplexityAttributeRule = CreateDescriptor(
        MisplacedExpectedComplexityAttributeId,
        MisplacedExpectedComplexityAttributeTitle,
        MisplacedExpectedComplexityAttributeMessageFormat,
        "Usage",
        DiagnosticSeverity.Error,
        true,
        MisplacedExpectedComplexityAttributeDescription);

    private static readonly LocalizableString InvalidContractArgumentTitle = "Invalid SharpProof Contract Argument";

    private static readonly LocalizableString InvalidContractArgumentMessageFormat =
        "SharpProof contract '{0}' has invalid argument '{1}': {2}";

    private static readonly LocalizableString InvalidContractArgumentDescription =
        "Reports malformed SharpProof contract arguments, such as empty [Ensures] conditions, undefined [ExpectedComplexity] values, unknown [AllowedCapabilities] bits, and non-exception [AllowedExceptions] types.";

    public static readonly DiagnosticDescriptor InvalidContractArgumentRule = CreateDescriptor(
        InvalidContractArgumentId,
        InvalidContractArgumentTitle,
        InvalidContractArgumentMessageFormat,
        "Usage",
        DiagnosticSeverity.Error,
        true,
        InvalidContractArgumentDescription);

    private static readonly LocalizableString InvalidAnalyzerConfigurationTitle =
        "Invalid SharpProof Analyzer Configuration";

    private static readonly LocalizableString InvalidAnalyzerConfigurationMessageFormat =
        "SharpProof analyzer option '{0}' has invalid value '{1}': {2}";

    private static readonly LocalizableString InvalidAnalyzerConfigurationDescription =
        "Reports invalid sharpproof_* analyzer configuration values that would otherwise fall back to defaults silently.";

    public static readonly DiagnosticDescriptor InvalidAnalyzerConfigurationRule = CreateDescriptor(
        InvalidAnalyzerConfigurationId,
        InvalidAnalyzerConfigurationTitle,
        InvalidAnalyzerConfigurationMessageFormat,
        "Configuration",
        DiagnosticSeverity.Warning,
        true,
        InvalidAnalyzerConfigurationDescription);

    private static readonly LocalizableString
        InvalidAdditionalFileTitle = "Invalid SharpProof Analyzer Additional File";

    private static readonly LocalizableString InvalidAdditionalFileMessageFormat =
        "SharpProof analyzer input file '{0}' is invalid: {1}";

    private static readonly LocalizableString InvalidAdditionalFileDescription =
        "Reports malformed, empty, unsupported, or partially ignored SharpProof analyzer AdditionalFiles instead of silently dropping their data.";

    public static readonly DiagnosticDescriptor InvalidAdditionalFileRule = CreateDescriptor(
        InvalidAdditionalFileId,
        InvalidAdditionalFileTitle,
        InvalidAdditionalFileMessageFormat,
        "Configuration",
        DiagnosticSeverity.Warning,
        true,
        InvalidAdditionalFileDescription);

    private static readonly LocalizableString UnrecognizedAttributeIdentityTitle =
        "Unrecognized SharpProof Attribute Identity";

    private static readonly LocalizableString UnrecognizedAttributeIdentityMessageFormat =
        "Attribute '{0}' looks like a SharpProof contract, but type '{1}' is not in an accepted SharpProof attribute namespace";

    private static readonly LocalizableString UnrecognizedAttributeIdentityDescription =
        "Reports attributes whose simple name matches a SharpProof contract attribute but whose containing namespace is neither SharpProof.Attributes nor an opt-in source-stub namespace.";

    public static readonly DiagnosticDescriptor UnrecognizedAttributeIdentityRule = CreateDescriptor(
        UnrecognizedAttributeIdentityId,
        UnrecognizedAttributeIdentityTitle,
        UnrecognizedAttributeIdentityMessageFormat,
        "Usage",
        DiagnosticSeverity.Warning,
        true,
        UnrecognizedAttributeIdentityDescription);

    private static readonly LocalizableString RequiresNotProvenTitle = "Precondition Not Proven";

    private static readonly LocalizableString RequiresNotProvenMessageFormat =
        "Call to '{0}' does not prove precondition '{1}'";

    private static readonly LocalizableString RequiresNotProvenDescription =
        "Reports calls whose current path facts contradict a declared [Requires] precondition. Use `SharpProof.SymbolicCli explain --file <path> --line <number>` to inspect the proof evidence.";

    public static readonly DiagnosticDescriptor RequiresNotProvenRule = CreateDescriptor(
        RequiresNotProvenId,
        RequiresNotProvenTitle,
        RequiresNotProvenMessageFormat,
        "Contracts",
        DiagnosticSeverity.Warning,
        true,
        RequiresNotProvenDescription);

    private static readonly LocalizableString RequiresUnsupportedTitle = "Precondition Could Not Be Verified";

    private static readonly LocalizableString RequiresUnsupportedMessageFormat =
        "Precondition '{1}' for '{0}' could not be verified: {2}";

    private static readonly LocalizableString RequiresUnsupportedDescription =
        "Reports [Requires] contracts that could not be parsed, lowered, or proven within the supported bounded proof surface. Use `SharpProof.SymbolicCli explain --file <path> --line <number>` to inspect the unprovable contract details.";

    public static readonly DiagnosticDescriptor RequiresUnsupportedRule = CreateDescriptor(
        RequiresUnsupportedId,
        RequiresUnsupportedTitle,
        RequiresUnsupportedMessageFormat,
        "Contracts",
        DiagnosticSeverity.Warning,
        true,
        RequiresUnsupportedDescription);

    private static readonly LocalizableString MisplacedRequiresAttributeTitle = "Misplaced [Requires] Attribute";

    private static readonly LocalizableString MisplacedRequiresAttributeMessageFormat =
        "The [Requires] attribute can only be applied to method-like declarations";

    private static readonly LocalizableString MisplacedRequiresAttributeDescription =
        "[Requires] configures symbolic precondition analysis for a method-like declaration and should not be used on non-method declarations.";

    public static readonly DiagnosticDescriptor MisplacedRequiresAttributeRule = CreateDescriptor(
        MisplacedRequiresAttributeId,
        MisplacedRequiresAttributeTitle,
        MisplacedRequiresAttributeMessageFormat,
        "Usage",
        DiagnosticSeverity.Error,
        true,
        MisplacedRequiresAttributeDescription);

    private static readonly LocalizableString ExceptionContractViolationTitle = "Exception Contract Violated";

    private static readonly LocalizableString ExceptionContractViolationMessageFormat =
        "Method '{0}' is marked {1}, but operation '{2}' can throw disallowed exceptions: {3}";

    private static readonly LocalizableString ExceptionContractViolationDescription =
        "Reports operations whose escaping exceptions violate [DoesNotThrow] or [AllowedExceptions] contracts. Use `SharpProof.SymbolicCli explain --file <path> --line <number>` to inspect the exception proof evidence.";

    public static readonly DiagnosticDescriptor ExceptionContractViolationRule = CreateDescriptor(
        ExceptionContractViolationId,
        ExceptionContractViolationTitle,
        ExceptionContractViolationMessageFormat,
        "ExceptionFlow",
        DiagnosticSeverity.Warning,
        true,
        ExceptionContractViolationDescription);

    private static readonly LocalizableString MisplacedExceptionContractAttributeTitle =
        "Misplaced Exception Contract Attribute";

    private static readonly LocalizableString MisplacedExceptionContractAttributeMessageFormat =
        "The [DoesNotThrow] and [AllowedExceptions] attributes can only be applied to method-like declarations";

    private static readonly LocalizableString MisplacedExceptionContractAttributeDescription =
        "[DoesNotThrow] and [AllowedExceptions] configure symbolic exception-contract analysis for method-like declarations and should not be used on non-method declarations.";

    public static readonly DiagnosticDescriptor MisplacedExceptionContractAttributeRule = CreateDescriptor(
        MisplacedExceptionContractAttributeId,
        MisplacedExceptionContractAttributeTitle,
        MisplacedExceptionContractAttributeMessageFormat,
        "Usage",
        DiagnosticSeverity.Error,
        true,
        MisplacedExceptionContractAttributeDescription);

    private static readonly LocalizableString MisplacedAttributeTitle = "Misplaced [EnforcePure] Attribute";

    private static readonly LocalizableString MisplacedAttributeMessageFormat =
        "The [EnforcePure] attribute can only be applied to method declarations";

    private static readonly LocalizableString MisplacedAttributeDescription =
        "[EnforcePure] should only be used on methods to indicate they require purity analysis.";

    public static readonly DiagnosticDescriptor MisplacedAttributeRule = CreateDescriptor(
        MisplacedAttributeId,
        MisplacedAttributeTitle,
        MisplacedAttributeMessageFormat,
        "Usage",
        DiagnosticSeverity.Error,
        true,
        MisplacedAttributeDescription);

    private static readonly LocalizableString MissingEnforcePureAttributeTitle = "Missing [EnforcePure] Attribute";

    private static readonly LocalizableString MissingEnforcePureAttributeMessageFormat =
        "Method '{0}' appears to be pure but is not marked with [EnforcePure]. Consider adding the attribute to enforce and document its purity.";

    private static readonly LocalizableString MissingEnforcePureAttributeDescription =
        "This method seems to only contain operations considered pure, but it lacks the [EnforcePure] attribute. Adding the attribute helps ensure its purity is maintained and communicates intent.";

    public static readonly DiagnosticDescriptor MissingEnforcePureAttributeRule = CreateDescriptor(
        MissingEnforcePureAttributeId,
        MissingEnforcePureAttributeTitle,
        MissingEnforcePureAttributeMessageFormat,
        "Purity",
        DiagnosticSeverity.Warning,
        true,
        MissingEnforcePureAttributeDescription);

    private static readonly LocalizableString ConflictingPurityAttributesTitle = "Conflicting purity attributes";

    private static readonly LocalizableString ConflictingPurityAttributesMessageFormat =
        "Method '{0}' has conflicting purity attributes applied";

    private static readonly LocalizableString ConflictingPurityAttributesDescription =
        "Apply one purity contract to a method. Combining enforcing, trusted-external, and explicit-impure attributes is contradictory or confusing.";

    public static readonly DiagnosticDescriptor ConflictingPurityAttributesRule = CreateDescriptor(
        ConflictingPurityAttributesId,
        ConflictingPurityAttributesTitle,
        ConflictingPurityAttributesMessageFormat,
        "Usage",
        DiagnosticSeverity.Warning,
        true,
        ConflictingPurityAttributesDescription);

    private static readonly LocalizableString AllowSyncWithoutPurityTitle =
        "[AllowSynchronization] requires a purity attribute";

    private static readonly LocalizableString AllowSyncWithoutPurityMessageFormat =
        "Method '{0}' is marked with [AllowSynchronization] but is not marked with [EnforcePure] or [Pure]";

    private static readonly LocalizableString AllowSyncWithoutPurityDescription =
        "[AllowSynchronization] only affects methods participating in purity analysis. Apply [EnforcePure] or [Pure] for it to have effect.";

    public static readonly DiagnosticDescriptor AllowSynchronizationWithoutPurityAttributeRule = CreateDescriptor(
        AllowSynchronizationWithoutPurityAttributeId,
        AllowSyncWithoutPurityTitle,
        AllowSyncWithoutPurityMessageFormat,
        "Usage",
        DiagnosticSeverity.Warning,
        true,
        AllowSyncWithoutPurityDescription);

    private static readonly LocalizableString MisplacedAllowSynchronizationTitle =
        "Misplaced [AllowSynchronization] Attribute";

    private static readonly LocalizableString MisplacedAllowSynchronizationMessageFormat =
        "The [AllowSynchronization] attribute can only be applied to method declarations";

    private static readonly LocalizableString MisplacedAllowSynchronizationDescription =
        "[AllowSynchronization] configures analyzer behavior for a method and should not be used on non-method declarations.";

    public static readonly DiagnosticDescriptor MisplacedAllowSynchronizationAttributeRule = CreateDescriptor(
        MisplacedAllowSynchronizationAttributeId,
        MisplacedAllowSynchronizationTitle,
        MisplacedAllowSynchronizationMessageFormat,
        "Usage",
        DiagnosticSeverity.Error,
        true,
        MisplacedAllowSynchronizationDescription);

    private static readonly LocalizableString RedundantAllowSynchronizationTitle = "Redundant [AllowSynchronization]";

    private static readonly LocalizableString RedundantAllowSynchronizationMessageFormat =
        "Method '{0}' is marked with [AllowSynchronization] but contains no synchronization constructs";

    private static readonly LocalizableString RedundantAllowSynchronizationDescription =
        "Remove [AllowSynchronization] when the method does not use synchronization (e.g., lock).";

    public static readonly DiagnosticDescriptor RedundantAllowSynchronizationRule = CreateDescriptor(
        RedundantAllowSynchronizationId,
        RedundantAllowSynchronizationTitle,
        RedundantAllowSynchronizationMessageFormat,
        "Usage",
        DiagnosticSeverity.Info,
        true,
        RedundantAllowSynchronizationDescription);

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
}

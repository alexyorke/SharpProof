using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharpProof.Analyzer
{

    public static class SharpProofDiagnostics
    {

        public const string ImpurityDiagnosticId = "SP0001";
        private static readonly LocalizableString ImpurityTitle = "Impure Method Assumed";
        private static readonly LocalizableString ImpurityMessageFormat = "Method '{0}' marked with [EnforcePure] contains implementation and is assumed impure";
        private static readonly LocalizableString ImpurityDescription = "Methods marked with [EnforcePure] must have their purity explicitly verified or annotated.";



        public static readonly DiagnosticDescriptor ImpurityRule = new DiagnosticDescriptor(
            ImpurityDiagnosticId,
            ImpurityTitle,
            ImpurityMessageFormat,
            "Purity",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: ImpurityDescription);


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
        private static readonly LocalizableString PurityNotVerifiedTitle = "Purity Not Verified";
        private static readonly LocalizableString PurityNotVerifiedMessageFormat = "Method '{0}' is marked [EnforcePure]/[Pure], but its body contains operations the analyzer cannot prove pure";
        private static readonly LocalizableString PurityNotVerifiedDescription = "Methods marked with [EnforcePure] require analysis. This diagnostic indicates the analysis rules did not determine the method's purity status.";

        public static readonly DiagnosticDescriptor PurityNotVerifiedRule = new DiagnosticDescriptor(
            id: PurityNotVerifiedId,
            title: PurityNotVerifiedTitle,
            messageFormat: PurityNotVerifiedMessageFormat,
            category: "Purity",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: PurityNotVerifiedDescription);

        public const string BclFallbackGuessId = "SP0012";
        private static readonly LocalizableString BclFallbackGuessTitle = "BCL Purity Fallback Guess";
        private static readonly LocalizableString BclFallbackGuessMessageFormat = "BCL purity fallback for '{0}': {1} ({2})";
        private static readonly LocalizableString BclFallbackGuessDescription = "Reports a non-authoritative purity guess for a metadata BCL member when no stronger analyzer, attribute, generated summary, or user configuration evidence was available. Enable with sharpproof_emit_explanations or sharpproof_report_bcl_fallback_guesses.";

        public static readonly DiagnosticDescriptor BclFallbackGuessRule = new DiagnosticDescriptor(
            id: BclFallbackGuessId,
            title: BclFallbackGuessTitle,
            messageFormat: BclFallbackGuessMessageFormat,
            category: "Purity",
            defaultSeverity: DiagnosticSeverity.Info,
            isEnabledByDefault: true,
            description: BclFallbackGuessDescription);

        public const string AllocationInZeroAllocationMethodId = "SP0013";
        public const string AllocationKindProperty = "sharpproof.allocation.kind";
        public const string AllocationOperationKindProperty = "sharpproof.allocation.operation_kind";
        public const string AllocationSymbolProperty = "sharpproof.allocation.symbol";
        private static readonly LocalizableString AllocationInZeroAllocationMethodTitle = "Allocation In [ZeroAllocations] Method";
        private static readonly LocalizableString AllocationInZeroAllocationMethodMessageFormat = "Method '{1}' is marked [ZeroAllocations], but operation '{0}' allocates";
        private static readonly LocalizableString AllocationInZeroAllocationMethodDescription = "Reports direct source-visible allocation sites inside methods annotated with [ZeroAllocations].";

        public static readonly DiagnosticDescriptor AllocationInZeroAllocationMethodRule = new DiagnosticDescriptor(
            id: AllocationInZeroAllocationMethodId,
            title: AllocationInZeroAllocationMethodTitle,
            messageFormat: AllocationInZeroAllocationMethodMessageFormat,
            category: "Allocation",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: AllocationInZeroAllocationMethodDescription);

        public const string PurityExplanationId = "SP0009";
        private static readonly LocalizableString PurityExplanationTitle = "Purity Diagnostic Explanation";
        private static readonly LocalizableString PurityExplanationMessageFormat = "Purity analysis for '{0}': {1}";
        private static readonly LocalizableString PurityExplanationDescription = "Provides structured explanation data for a purity diagnostic.";

        public static readonly DiagnosticDescriptor PurityExplanationRule = new DiagnosticDescriptor(
            id: PurityExplanationId,
            title: PurityExplanationTitle,
            messageFormat: PurityExplanationMessageFormat,
            category: "Purity",
            defaultSeverity: DiagnosticSeverity.Info,
            isEnabledByDefault: true,
            description: PurityExplanationDescription);

        public const string ExceptionSummaryId = "SP0010";
        public const string ExceptionTypesProperty = "sharpproof.exceptions.types";
        public const string ExceptionCategoriesProperty = "sharpproof.exceptions.categories";
        public const string ExceptionSourcesProperty = "sharpproof.exceptions.sources";
        public const string ExceptionSymbolProperty = "sharpproof.exceptions.symbol";
        public const string ExceptionEdgesProperty = "sharpproof.exceptions.edges";
        private static readonly LocalizableString ExceptionSummaryTitle = "Method May Throw Exceptions";
        private static readonly LocalizableString ExceptionSummaryMessageFormat = "Method '{0}' can throw: {1}";
        private static readonly LocalizableString ExceptionSummaryDescription = "Reports exception types that can escape a method. Enable with sharpproof_report_exceptions = true or sharpproof_runtime_hazard_mode = summaries/all.";

        public static readonly DiagnosticDescriptor ExceptionSummaryRule = new DiagnosticDescriptor(
            id: ExceptionSummaryId,
            title: ExceptionSummaryTitle,
            messageFormat: ExceptionSummaryMessageFormat,
            category: "ExceptionFlow",
            defaultSeverity: DiagnosticSeverity.Info,
            isEnabledByDefault: true,
            description: ExceptionSummaryDescription);

        public const string UncaughtExceptionSiteId = "SP0011";
        private static readonly LocalizableString UncaughtExceptionSiteTitle = "Operation May Throw Uncaught Exceptions";
        private static readonly LocalizableString UncaughtExceptionSiteMessageFormat = "Operation '{0}' may throw uncaught exceptions: {1}";
        private static readonly LocalizableString UncaughtExceptionSiteDescription = "Reports uncaught exceptions and proven runtime hazards at specific operations. Enable with sharpproof_checked_exceptions = true or sharpproof_runtime_hazard_mode = sites/all.";

        public static readonly DiagnosticDescriptor UncaughtExceptionSiteRule = new DiagnosticDescriptor(
            id: UncaughtExceptionSiteId,
            title: UncaughtExceptionSiteTitle,
            messageFormat: UncaughtExceptionSiteMessageFormat,
            category: "ExceptionFlow",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: UncaughtExceptionSiteDescription);

        public const string MisplacedZeroAllocationsAttributeId = "SP0014";
        private static readonly LocalizableString MisplacedZeroAllocationsAttributeTitle = "Misplaced [ZeroAllocations] Attribute";
        private static readonly LocalizableString MisplacedZeroAllocationsAttributeMessageFormat = "The [ZeroAllocations] attribute can only be applied to method declarations";
        private static readonly LocalizableString MisplacedZeroAllocationsAttributeDescription = "[ZeroAllocations] configures analyzer behavior for a method and should not be used on non-method declarations.";

        public static readonly DiagnosticDescriptor MisplacedZeroAllocationsAttributeRule = new DiagnosticDescriptor(
            id: MisplacedZeroAllocationsAttributeId,
            title: MisplacedZeroAllocationsAttributeTitle,
            messageFormat: MisplacedZeroAllocationsAttributeMessageFormat,
            category: "Usage",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: MisplacedZeroAllocationsAttributeDescription);

        public const string CapabilityViolationId = "SP0015";
        public const string CapabilityProperty = "sharpproof.capability.flags";
        public const string CapabilityOperationKindProperty = "sharpproof.capability.operation_kind";
        public const string CapabilitySymbolProperty = "sharpproof.capability.symbol";
        public const string CapabilityUnknownReasonProperty = "sharpproof.capability.unknown_reason";
        private static readonly LocalizableString CapabilityViolationTitle = "Disallowed Capability Use";
        private static readonly LocalizableString CapabilityViolationMessageFormat = "Method '{1}' is marked [AllowedCapabilities], but operation '{0}' requires capabilities: {2}";
        private static readonly LocalizableString CapabilityViolationDescription = "Reports source-visible operations or proven transitive callees that exceed the method's declared allowed capability set.";

        public static readonly DiagnosticDescriptor CapabilityViolationRule = new DiagnosticDescriptor(
            id: CapabilityViolationId,
            title: CapabilityViolationTitle,
            messageFormat: CapabilityViolationMessageFormat,
            category: "Capabilities",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: CapabilityViolationDescription);

        public const string CapabilityUnknownId = "SP0016";
        private static readonly LocalizableString CapabilityUnknownTitle = "Capability Contract Not Fully Verified";
        private static readonly LocalizableString CapabilityUnknownMessageFormat = "Method '{1}' is marked [AllowedCapabilities], but operation '{0}' could not be capability-verified: {2}";
        private static readonly LocalizableString CapabilityUnknownDescription = "Reports operations whose capability set could not be conservatively proven under the current capability analysis.";

        public static readonly DiagnosticDescriptor CapabilityUnknownRule = new DiagnosticDescriptor(
            id: CapabilityUnknownId,
            title: CapabilityUnknownTitle,
            messageFormat: CapabilityUnknownMessageFormat,
            category: "Capabilities",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: CapabilityUnknownDescription);

        public const string MisplacedAllowedCapabilitiesAttributeId = "SP0017";
        private static readonly LocalizableString MisplacedAllowedCapabilitiesAttributeTitle = "Misplaced [AllowedCapabilities] Attribute";
        private static readonly LocalizableString MisplacedAllowedCapabilitiesAttributeMessageFormat = "The [AllowedCapabilities] attribute can only be applied to method declarations";
        private static readonly LocalizableString MisplacedAllowedCapabilitiesAttributeDescription = "[AllowedCapabilities] configures capability-contract analysis for a method and should not be used on non-method declarations.";

        public static readonly DiagnosticDescriptor MisplacedAllowedCapabilitiesAttributeRule = new DiagnosticDescriptor(
            id: MisplacedAllowedCapabilitiesAttributeId,
            title: MisplacedAllowedCapabilitiesAttributeTitle,
            messageFormat: MisplacedAllowedCapabilitiesAttributeMessageFormat,
            category: "Usage",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: MisplacedAllowedCapabilitiesAttributeDescription);


        public const string MisplacedAttributeId = "SP0003";
        private static readonly LocalizableString MisplacedAttributeTitle = "Misplaced [EnforcePure] Attribute";
        private static readonly LocalizableString MisplacedAttributeMessageFormat = "The [EnforcePure] attribute can only be applied to method declarations";
        private static readonly LocalizableString MisplacedAttributeDescription = "[EnforcePure] should only be used on methods to indicate they require purity analysis.";

        public static readonly DiagnosticDescriptor MisplacedAttributeRule = new DiagnosticDescriptor(
            id: MisplacedAttributeId,
            title: MisplacedAttributeTitle,
            messageFormat: MisplacedAttributeMessageFormat,
            category: "Usage",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: MisplacedAttributeDescription);


        public const string MissingEnforcePureAttributeId = "SP0004";
        private static readonly LocalizableString MissingEnforcePureAttributeTitle = "Missing [EnforcePure] Attribute";
        private static readonly LocalizableString MissingEnforcePureAttributeMessageFormat = "Method '{0}' appears to be pure but is not marked with [EnforcePure]. Consider adding the attribute to enforce and document its purity.";
        private static readonly LocalizableString MissingEnforcePureAttributeDescription = "This method seems to only contain operations considered pure, but it lacks the [EnforcePure] attribute. Adding the attribute helps ensure its purity is maintained and communicates intent.";

        public static readonly DiagnosticDescriptor MissingEnforcePureAttributeRule = new DiagnosticDescriptor(
            id: MissingEnforcePureAttributeId,
            title: MissingEnforcePureAttributeTitle,
            messageFormat: MissingEnforcePureAttributeMessageFormat,
            category: "Purity",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: MissingEnforcePureAttributeDescription);


        public const string ConflictingPurityAttributesId = "SP0005";
        private static readonly LocalizableString ConflictingPurityAttributesTitle = "Conflicting purity attributes";
        private static readonly LocalizableString ConflictingPurityAttributesMessageFormat = "Method '{0}' has conflicting purity attributes applied";
        private static readonly LocalizableString ConflictingPurityAttributesDescription = "Apply one purity contract to a method. Combining enforcing, trusted-external, and explicit-impure attributes is contradictory or confusing.";

        public static readonly DiagnosticDescriptor ConflictingPurityAttributesRule = new DiagnosticDescriptor(
            id: ConflictingPurityAttributesId,
            title: ConflictingPurityAttributesTitle,
            messageFormat: ConflictingPurityAttributesMessageFormat,
            category: "Usage",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: ConflictingPurityAttributesDescription);


        public const string AllowSynchronizationWithoutPurityAttributeId = "SP0006";
        private static readonly LocalizableString AllowSyncWithoutPurityTitle = "[AllowSynchronization] requires a purity attribute";
        private static readonly LocalizableString AllowSyncWithoutPurityMessageFormat = "Method '{0}' is marked with [AllowSynchronization] but is not marked with [EnforcePure] or [Pure]";
        private static readonly LocalizableString AllowSyncWithoutPurityDescription = "[AllowSynchronization] only affects methods participating in purity analysis. Apply [EnforcePure] or [Pure] for it to have effect.";

        public static readonly DiagnosticDescriptor AllowSynchronizationWithoutPurityAttributeRule = new DiagnosticDescriptor(
            id: AllowSynchronizationWithoutPurityAttributeId,
            title: AllowSyncWithoutPurityTitle,
            messageFormat: AllowSyncWithoutPurityMessageFormat,
            category: "Usage",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: AllowSyncWithoutPurityDescription);


        public const string MisplacedAllowSynchronizationAttributeId = "SP0007";
        private static readonly LocalizableString MisplacedAllowSynchronizationTitle = "Misplaced [AllowSynchronization] Attribute";
        private static readonly LocalizableString MisplacedAllowSynchronizationMessageFormat = "The [AllowSynchronization] attribute can only be applied to method declarations";
        private static readonly LocalizableString MisplacedAllowSynchronizationDescription = "[AllowSynchronization] configures analyzer behavior for a method and should not be used on non-method declarations.";

        public static readonly DiagnosticDescriptor MisplacedAllowSynchronizationAttributeRule = new DiagnosticDescriptor(
            id: MisplacedAllowSynchronizationAttributeId,
            title: MisplacedAllowSynchronizationTitle,
            messageFormat: MisplacedAllowSynchronizationMessageFormat,
            category: "Usage",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: MisplacedAllowSynchronizationDescription);


        public const string RedundantAllowSynchronizationId = "SP0008";
        private static readonly LocalizableString RedundantAllowSynchronizationTitle = "Redundant [AllowSynchronization]";
        private static readonly LocalizableString RedundantAllowSynchronizationMessageFormat = "Method '{0}' is marked with [AllowSynchronization] but contains no synchronization constructs";
        private static readonly LocalizableString RedundantAllowSynchronizationDescription = "Remove [AllowSynchronization] when the method does not use synchronization (e.g., lock).";

        public static readonly DiagnosticDescriptor RedundantAllowSynchronizationRule = new DiagnosticDescriptor(
            id: RedundantAllowSynchronizationId,
            title: RedundantAllowSynchronizationTitle,
            messageFormat: RedundantAllowSynchronizationMessageFormat,
            category: "Usage",
            defaultSeverity: DiagnosticSeverity.Info,
            isEnabledByDefault: true,
            description: RedundantAllowSynchronizationDescription);
    }
}

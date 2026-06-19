using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace PurelySharp.Analyzer
{

    public static class PurelySharpDiagnostics
    {

        public const string ImpurityDiagnosticId = "PS0001";
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


        public const string PurityNotVerifiedId = "PS0002";
        public const string ImpurityCategoryProperty = "purelysharp.impurity.category";
        public const string ImpurityRuleProperty = "purelysharp.impurity.rule";
        public const string ImpurityOperationKindProperty = "purelysharp.impurity.operation_kind";
        public const string ImpuritySymbolProperty = "purelysharp.impurity.symbol";
        public const string ImpurityCatalogSourceProperty = "purelysharp.impurity.catalog_source";
        public const string ImpurityCalleeChainProperty = "purelysharp.impurity.callee_chain";
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

        public const string PurityExplanationId = "PS0009";
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

        public const string ExceptionSummaryId = "PS0010";
        public const string ExceptionTypesProperty = "purelysharp.exceptions.types";
        public const string ExceptionCategoriesProperty = "purelysharp.exceptions.categories";
        public const string ExceptionSourcesProperty = "purelysharp.exceptions.sources";
        public const string ExceptionSymbolProperty = "purelysharp.exceptions.symbol";
        private static readonly LocalizableString ExceptionSummaryTitle = "Method May Throw Exceptions";
        private static readonly LocalizableString ExceptionSummaryMessageFormat = "Method '{0}' can throw: {1}";
        private static readonly LocalizableString ExceptionSummaryDescription = "Reports exception types that can escape a method. Enable with purelysharp_report_exceptions = true.";

        public static readonly DiagnosticDescriptor ExceptionSummaryRule = new DiagnosticDescriptor(
            id: ExceptionSummaryId,
            title: ExceptionSummaryTitle,
            messageFormat: ExceptionSummaryMessageFormat,
            category: "ExceptionFlow",
            defaultSeverity: DiagnosticSeverity.Info,
            isEnabledByDefault: true,
            description: ExceptionSummaryDescription);

        public const string UncaughtExceptionSiteId = "PS0011";
        private static readonly LocalizableString UncaughtExceptionSiteTitle = "Operation May Throw Uncaught Exceptions";
        private static readonly LocalizableString UncaughtExceptionSiteMessageFormat = "Operation '{0}' may throw uncaught exceptions: {1}";
        private static readonly LocalizableString UncaughtExceptionSiteDescription = "Reports uncaught exceptions at specific operations. Enable with purelysharp_report_exceptions = true.";

        public static readonly DiagnosticDescriptor UncaughtExceptionSiteRule = new DiagnosticDescriptor(
            id: UncaughtExceptionSiteId,
            title: UncaughtExceptionSiteTitle,
            messageFormat: UncaughtExceptionSiteMessageFormat,
            category: "ExceptionFlow",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: UncaughtExceptionSiteDescription);


        public const string MisplacedAttributeId = "PS0003";
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


        public const string MissingEnforcePureAttributeId = "PS0004";
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


        public const string ConflictingPurityAttributesId = "PS0005";
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


        public const string AllowSynchronizationWithoutPurityAttributeId = "PS0006";
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


        public const string MisplacedAllowSynchronizationAttributeId = "PS0007";
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


        public const string RedundantAllowSynchronizationId = "PS0008";
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

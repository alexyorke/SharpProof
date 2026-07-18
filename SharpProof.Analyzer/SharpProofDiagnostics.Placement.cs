using Microsoft.CodeAnalysis;

namespace SharpProof.Analyzer;

public static partial class SharpProofDiagnostics
{
    public static readonly DiagnosticDescriptor MisplacedZeroAllocationsAttributeRule =
        CreateMisplacedGetterAliasingAttributeDescriptor(
        MisplacedZeroAllocationsAttributeId,
        "ZeroAllocations",
        "allocation");

    public static readonly DiagnosticDescriptor MisplacedAllowedCapabilitiesAttributeRule =
        CreateMisplacedGetterAliasingAttributeDescriptor(
        MisplacedAllowedCapabilitiesAttributeId,
        "AllowedCapabilities",
        "capability");

    public static readonly DiagnosticDescriptor MisplacedEnsuresAttributeRule =
        CreateMisplacedGetterAliasingAttributeDescriptor(
        MisplacedEnsuresAttributeId,
        "Ensures",
        "symbolic postcondition");

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

    public static readonly DiagnosticDescriptor MisplacedRequiresAttributeRule = CreateDescriptor(
        MisplacedRequiresAttributeId,
        "Misplaced [Requires] Attribute",
        "The [Requires] attribute can only be applied to method-like declarations",
        "Usage",
        DiagnosticSeverity.Error,
        "[Requires] configures symbolic call-site precondition analysis for a method-like declaration. On a property or indexer, place it on the explicit get accessor.");

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

    public static readonly DiagnosticDescriptor MisplacedAllowSynchronizationAttributeRule = CreateDescriptor(
        MisplacedAllowSynchronizationAttributeId,
        "Misplaced [AllowSynchronization] Attribute",
        "The [AllowSynchronization] attribute can only be applied to method declarations",
        "Usage",
        DiagnosticSeverity.Error,
        "[AllowSynchronization] configures analyzer behavior for a method and should not be used on non-method declarations.");
}

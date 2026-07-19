using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;

#pragma warning disable RS2001 // Disabled-by-default rules are preserved exactly; release tracking reports them as severity changes.
#pragma warning disable RS1037 // Compilation-end reporting policy is separate from descriptor boundary metadata.

namespace SharpProof.Analyzer;

public static partial class SharpProofDiagnostics
{
    public static readonly DiagnosticDescriptor PurityNotVerifiedRule = new(
        "SP0002", "Purity Not Proven",
        "Method '{0}' is marked [EnforcePure]/[Pure], but its body contains operations the analyzer cannot prove pure", "Purity", DiagnosticSeverity.Error, true,
        "Methods marked with [EnforcePure] require analysis. This diagnostic indicates the analysis rules did not determine the method's purity status. Use `SharpProof.SymbolicCli explain --file <path> --line <number>` to inspect the symbolic proof evidence.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0002", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor MisplacedAttributeRule = new(
        "SP0003", "Misplaced [EnforcePure] Attribute",
        "The [EnforcePure]/[Pure] attributes can only be applied to method-like declarations or getter-bearing properties and indexers", "Usage", DiagnosticSeverity.Error, true,
        "[EnforcePure] and [Pure] configure purity analysis for a method-like declaration or alias the getter of a getter-bearing property or indexer.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0003", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor MissingEnforcePureAttributeRule = new(
        "SP0004", "Missing [EnforcePure] Attribute",
        "Method '{0}' appears to be pure but is not marked with [EnforcePure]. Consider adding the attribute to enforce and document its purity.", "Purity", DiagnosticSeverity.Warning, true,
        "This method seems to only contain operations considered pure, but it lacks the [EnforcePure] attribute. Adding the attribute helps ensure its purity is maintained and communicates intent.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0004", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor ConflictingPurityAttributesRule = new(
        "SP0005", "Conflicting purity attributes",
        "Method '{0}' has conflicting purity attributes applied", "Usage", DiagnosticSeverity.Warning, true,
        "Apply one purity contract to a method. Combining enforcing, trusted-external, and explicit-impure attributes is contradictory or confusing.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0005", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor AllowSynchronizationWithoutPurityAttributeRule = new(
        "SP0006", "[AllowSynchronization] requires a purity attribute",
        "Method '{0}' is marked with [AllowSynchronization] but is not marked with [EnforcePure] or [Pure]", "Usage", DiagnosticSeverity.Warning, true,
        "[AllowSynchronization] only affects methods participating in purity analysis. Apply [EnforcePure] or [Pure] for it to have effect.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0006", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor MisplacedAllowSynchronizationAttributeRule = new(
        "SP0007", "Misplaced [AllowSynchronization] Attribute",
        "The [AllowSynchronization] attribute can only be applied to method declarations", "Usage", DiagnosticSeverity.Error, true,
        "[AllowSynchronization] configures analyzer behavior for a method and should not be used on non-method declarations.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0007", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor RedundantAllowSynchronizationRule = new(
        "SP0008", "Redundant [AllowSynchronization]",
        "Method '{0}' is marked with [AllowSynchronization] but contains no synchronization constructs", "Usage", DiagnosticSeverity.Info, true,
        "Remove [AllowSynchronization] when the method does not use synchronization (e.g., lock).",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0008", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor PurityExplanationRule = new(
        "SP0009", "Purity Diagnostic Explanation",
        "Purity analysis for '{0}': {1}", "Purity", DiagnosticSeverity.Info, true,
        "Provides structured explanation data for a purity diagnostic.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0009", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor ExceptionSummaryRule = new(
        "SP0010", "Method May Throw Exceptions",
        "Method '{0}' can throw: {1}", "ExceptionFlow", DiagnosticSeverity.Info, true,
        "Reports exception types that can escape a method. Enable with sharpproof_report_exceptions = true or sharpproof_runtime_hazard_mode = summaries/all/all-and-unknowns. Use `SharpProof.SymbolicCli explain --file <path> --line <number>` to inspect the exception proof evidence.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0010", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor UncaughtExceptionSiteRule = new(
        "SP0011", "Operation May Throw Uncaught Exceptions",
        "Operation '{0}' may throw uncaught exceptions: {1}", "ExceptionFlow", DiagnosticSeverity.Warning, true,
        "Reports uncaught exceptions and proven runtime hazards at specific operations. Enable with sharpproof_checked_exceptions = true or sharpproof_runtime_hazard_mode = sites/all/sites-and-unknowns/all-and-unknowns. Use `SharpProof.SymbolicCli explain --file <path> --line <number>` to inspect the runtime hazard evidence.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0011", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor BclFallbackGuessRule = new(
        "SP0012", "BCL Purity Fallback Guess",
        "BCL purity fallback for '{0}': {1} ({2})", "Purity", DiagnosticSeverity.Info, true,
        "Reports a non-authoritative purity guess for a metadata BCL member when no stronger analyzer, attribute, generated summary, or user configuration evidence was available. Enable with sharpproof_emit_explanations or sharpproof_report_bcl_fallback_guesses.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0012", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor AllocationInZeroAllocationMethodRule = new(
        "SP0013", "Allocation In [ZeroAllocations] Method",
        "Method '{1}' is marked [ZeroAllocations], but operation '{0}' allocates", "Allocation", DiagnosticSeverity.Warning, true,
        "Reports direct source-visible allocation sites inside methods annotated with [ZeroAllocations].",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0013", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor MisplacedZeroAllocationsAttributeRule = new(
        "SP0014", "Misplaced [ZeroAllocations] Attribute",
        "The [ZeroAllocations] attribute can only be applied to method-like declarations or getter-bearing properties and indexers", "Usage", DiagnosticSeverity.Error, true,
        "[ZeroAllocations] configures allocation analysis for a method-like declaration or aliases the getter of a getter-bearing property or indexer.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0014", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor CapabilityViolationRule = new(
        "SP0015", "Disallowed Capability Use",
        "Method '{1}' is marked [AllowedCapabilities], but operation '{0}' requires capabilities: {2}", "Capabilities", DiagnosticSeverity.Warning, true,
        "Reports source-visible operations or proven transitive callees that exceed the method's declared allowed capability set.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0015", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor CapabilityUnknownRule = new(
        "SP0016", "Capability Contract Not Proven",
        "Method '{1}' is marked [AllowedCapabilities], but operation '{0}' could not be capability-verified: {2}", "Capabilities", DiagnosticSeverity.Warning, true,
        "Reports operations whose capability set could not be conservatively proven under the current capability analysis. Use `SharpProof.SymbolicCli explain --file <path> --line <number>` to inspect the capability proof evidence.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0016", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor MisplacedAllowedCapabilitiesAttributeRule = new(
        "SP0017", "Misplaced [AllowedCapabilities] Attribute",
        "The [AllowedCapabilities] attribute can only be applied to method-like declarations or getter-bearing properties and indexers", "Usage", DiagnosticSeverity.Error, true,
        "[AllowedCapabilities] configures capability analysis for a method-like declaration or aliases the getter of a getter-bearing property or indexer.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0017", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor EnsuresNotProvenRule = new(
        "SP0018", "Postcondition Not Proven",
        "Method '{1}' is marked [Ensures], but return site '{0}' does not prove postcondition '{2}'", "Contracts", DiagnosticSeverity.Warning, true,
        "Reports return sites that contradict a declared [Ensures] postcondition. Use `SharpProof.SymbolicCli explain --file <path> --line <number>` to inspect the proof evidence for each return site.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0018", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor EnsuresUnsupportedRule = new(
        "SP0019", "Postcondition Could Not Be Verified",
        "Method '{1}' is marked [Ensures], but postcondition '{0}' could not be verified: {2}", "Contracts", DiagnosticSeverity.Warning, true,
        "Reports [Ensures] contracts that could not be parsed, lowered, or proven within the supported bounded proof surface. Use `SharpProof.SymbolicCli explain --file <path> --line <number>` to inspect the unprovable contract details.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0019", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor MisplacedEnsuresAttributeRule = new(
        "SP0020", "Misplaced [Ensures] Attribute",
        "The [Ensures] attribute can only be applied to method-like declarations or getter-bearing properties and indexers", "Usage", DiagnosticSeverity.Error, true,
        "[Ensures] configures symbolic postcondition analysis for a method-like declaration or aliases the getter of a getter-bearing property or indexer.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0020", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor ComplexityExceededRule = new(
        "SP0021", "Declared Complexity Exceeded",
        "Method '{0}' is marked [ExpectedComplexity({1})], but inferred complexity '{2}' exceeds the declared bound", "Complexity", DiagnosticSeverity.Warning, true,
        "Reports methods whose inferred bounded complexity is stronger than the declared [ExpectedComplexity] contract allows. Use `SharpProof.SymbolicCli explain --file <path> --line <number>` to inspect the complexity proof evidence.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0021", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor ComplexityCouldNotBeVerifiedRule = new(
        "SP0022", "Declared Complexity Could Not Be Verified",
        "Method '{0}' is marked [ExpectedComplexity({1})], but the declared bound could not be verified conservatively: {2}", "Complexity", DiagnosticSeverity.Warning, true,
        "Reports [ExpectedComplexity] contracts that could not be verified because the inferred complexity is unknown, unsupported, or incomparable with the declared bound. Use `SharpProof.SymbolicCli explain --file <path> --line <number>` to inspect the complexity analysis details.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0022", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor MisplacedExpectedComplexityAttributeRule = new(
        "SP0023", "Misplaced [ExpectedComplexity] Attribute",
        "The [ExpectedComplexity] attribute can only be applied to method-like declarations or getter-bearing properties and indexers", "Usage", DiagnosticSeverity.Error, true,
        "[ExpectedComplexity] configures complexity analysis for a method-like declaration or aliases the getter of a getter-bearing property or indexer.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0023", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor InvalidContractArgumentRule = new(
        "SP0024", "Invalid SharpProof Contract Argument",
        "SharpProof contract '{0}' has invalid argument '{1}': {2}", "Usage", DiagnosticSeverity.Error, true,
        "Reports malformed SharpProof contract arguments, such as empty [Ensures] conditions, undefined [ExpectedComplexity] values, unknown [AllowedCapabilities] bits, and non-exception [AllowedExceptions] types.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0024", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor InvalidAnalyzerConfigurationRule = new(
        "SP0025", "Invalid SharpProof Analyzer Configuration",
        "SharpProof analyzer option '{0}' has invalid value '{1}': {2}", "Configuration", DiagnosticSeverity.Warning, true,
        "Reports invalid sharpproof_* analyzer configuration values that would otherwise fall back to defaults silently.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0025", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor UnrecognizedAttributeIdentityRule = new(
        "SP0026", "Unrecognized SharpProof Attribute Identity",
        "Attribute '{0}' looks like a SharpProof contract, but type '{1}' is not in an accepted SharpProof attribute namespace", "Usage", DiagnosticSeverity.Warning, true,
        "Reports attributes whose simple name matches a SharpProof contract attribute but whose containing namespace is neither SharpProof.Attributes nor an opt-in source-stub namespace.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0026", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor RequiresNotProvenRule = new(
        "SP0027", "Precondition Not Proven",
        "Call to '{0}' does not prove precondition '{1}'", "Contracts", DiagnosticSeverity.Warning, true,
        "Reports calls whose current path facts contradict a declared [Requires] precondition. Use `SharpProof.SymbolicCli explain --file <path> --line <number>` to inspect the proof evidence.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0027", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor RequiresUnsupportedRule = new(
        "SP0028", "Precondition Could Not Be Verified",
        "Precondition '{1}' for '{0}' could not be verified: {2}", "Contracts", DiagnosticSeverity.Warning, true,
        "Reports [Requires] contracts that could not be parsed, lowered, or proven within the supported bounded proof surface. Use `SharpProof.SymbolicCli explain --file <path> --line <number>` to inspect the unprovable contract details.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0028", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor MisplacedRequiresAttributeRule = new(
        "SP0029", "Misplaced [Requires] Attribute",
        "The [Requires] attribute can only be applied to method-like declarations", "Usage", DiagnosticSeverity.Error, true,
        "[Requires] configures symbolic call-site precondition analysis for a method-like declaration. On a property or indexer, place it on the explicit get accessor.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0029", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor ExceptionContractViolationRule = new(
        "SP0030", "Exception Contract Violated",
        "Method '{0}' is marked {1}, but operation '{2}' can throw disallowed exceptions: {3}", "ExceptionFlow", DiagnosticSeverity.Warning, true,
        "Reports operations whose escaping exceptions violate [DoesNotThrow] or [AllowedExceptions] contracts. Use `SharpProof.SymbolicCli explain --file <path> --line <number>` to inspect the exception proof evidence.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0030", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor MisplacedExceptionContractAttributeRule = new(
        "SP0031", "Misplaced Exception Contract Attribute",
        "The [DoesNotThrow] and [AllowedExceptions] attributes can only be applied to method-like declarations or getter-bearing properties and indexers", "Usage", DiagnosticSeverity.Error, true,
        "[DoesNotThrow] and [AllowedExceptions] configure exception analysis for method-like declarations or alias the getter of a getter-bearing property or indexer.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0031", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor InvalidAdditionalFileRule = new(
        "SP0032", "Invalid SharpProof Analyzer Additional File",
        "SharpProof analyzer input file '{0}' is invalid: {1}", "Configuration", DiagnosticSeverity.Warning, true,
        "Reports malformed, empty, unsupported, or partially ignored SharpProof analyzer AdditionalFiles instead of silently dropping their data.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0032", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor UnknownRuntimeHazardRule = new(
        "SP0033", "Runtime Hazard Candidate Could Not Be Proven",
        "Runtime hazard candidate '{0}' at operation '{1}' could not be proven: {2}", "ExceptionFlow", DiagnosticSeverity.Info, true,
        "Reports bounded runtime-hazard candidates whose trigger could not be proven or rejected. Enable with sharpproof_runtime_hazard_mode = unknowns, sites-and-unknowns, or all-and-unknowns. The diagnostic is informational by default and carries stable proof, reason, trigger, and baseline metadata.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0033", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor SuggestZeroAllocationsRule = new(
        "SP0034", "Inferred [ZeroAllocations] Contract",
        "Method '{0}' has {1}; consider adding {2} ({3} confidence)", "Suggestions", DiagnosticSeverity.Info, true,
        "Reports opt-in inferred contract candidates backed by bounded analyzer evidence. Enable with sharpproof_suggest_inferred_contracts and filter by family, visibility, and confidence.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0034", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor SuggestAllowedCapabilitiesRule = new(
        "SP0035", "Inferred [AllowedCapabilities] Contract",
        "Method '{0}' has {1}; consider adding {2} ({3} confidence)", "Suggestions", DiagnosticSeverity.Info, true,
        "Reports opt-in inferred contract candidates backed by bounded analyzer evidence. Enable with sharpproof_suggest_inferred_contracts and filter by family, visibility, and confidence.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0035", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor SuggestExpectedComplexityRule = new(
        "SP0036", "Inferred [ExpectedComplexity] Contract",
        "Method '{0}' has {1}; consider adding {2} ({3} confidence)", "Suggestions", DiagnosticSeverity.Info, true,
        "Reports opt-in inferred contract candidates backed by bounded analyzer evidence. Enable with sharpproof_suggest_inferred_contracts and filter by family, visibility, and confidence.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0036", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor SuggestExceptionContractRule = new(
        "SP0037", "Inferred Exception Contract",
        "Method '{0}' has {1}; consider adding {2} ({3} confidence)", "Suggestions", DiagnosticSeverity.Info, true,
        "Reports opt-in inferred contract candidates backed by bounded analyzer evidence. Enable with sharpproof_suggest_inferred_contracts and filter by family, visibility, and confidence.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0037", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor SuggestEnsuresRule = new(
        "SP0038", "Inferred [Ensures] Contract",
        "Method '{0}' has {1}; consider adding {2} ({3} confidence)", "Suggestions", DiagnosticSeverity.Info, true,
        "Reports opt-in inferred contract candidates backed by bounded analyzer evidence. Enable with sharpproof_suggest_inferred_contracts and filter by family, visibility, and confidence.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0038", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor SuggestRequiresRule = new(
        "SP0039", "Inferred [Requires] Contract",
        "Method '{0}' has {1}; consider adding {2} ({3} confidence)", "Suggestions", DiagnosticSeverity.Info, true,
        "Reports opt-in inferred contract candidates backed by bounded analyzer evidence. Enable with sharpproof_suggest_inferred_contracts and filter by family, visibility, and confidence.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0039", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor TrustedBoundaryReviewRule = new(
        "SP0040", "Trusted Purity Boundary Review",
        "Purity trust source '{0}' for '{1}' was {2}{3}", "Review", DiagnosticSeverity.Info, true,
        "Reports structured, opt-in audit evidence for applied and overridden purity trust shortcuts.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0040", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor NullableReturnContractViolationRule = new(
        "SP0041", "Nullable return contract violated",
        "Method '{0}' can return null despite contract '{1}'", "Nullability", DiagnosticSeverity.Warning, true,
        "Reports a reachable normal return that violates the declared nullable return contract.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0041", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor NullableParameterPostconditionViolationRule = new(
        "SP0042", "Nullable parameter postcondition violated",
        "Method '{0}' can complete with parameter '{1}' null despite contract '{2}'", "Nullability", DiagnosticSeverity.Warning, true,
        "Reports a reachable normal completion that violates a nullable parameter postcondition.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0042", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor NullableMemberContractViolationRule = new(
        "SP0043", "Nullable member contract violated",
        "Method '{0}' can complete with member '{1}' null despite contract '{2}'", "Nullability", DiagnosticSeverity.Warning, true,
        "Reports a reachable normal completion that violates a member-not-null contract.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0043", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor UnsafeNullForgivingOperatorRule = new(
        "SP0044", "Null-forgiving operator is unsafe",
        "Null-forgiving operator can suppress a feasible null value for '{0}'", "Nullability", DiagnosticSeverity.Warning, true,
        "Reports a null-forgiving operator reached by a proven null execution.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0044", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor UnnecessaryNullForgivingOperatorRule = new(
        "SP0045", "Null-forgiving operator is unnecessary",
        "Null-forgiving operator is unnecessary because '{0}' is proven non-null", "Nullability", DiagnosticSeverity.Info, true,
        "Reports a null-forgiving operator whose operand is already proven non-null.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0045", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor SuggestNullableContractRule = new(
        "SP0046", "Nullable contract can be declared",
        "Method '{0}' satisfies nullable contract '{1}'", "Nullability", DiagnosticSeverity.Info, true,
        "Suggests a nullable contract proved by every relevant completion path.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0046", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor NullableVerificationInconclusiveRule = new(
        "SP0047", "Nullable verification was inconclusive",
        "Nullable contract '{1}' on '{0}' could not be verified: {2}", "Nullability", DiagnosticSeverity.Info, true,
        "Reports bounded nullable proofs that ended as unsupported or unknown.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0047", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor AwaitNullConditionalRule = new(
        "SP0048", "Awaiting a null-conditional expression",
        "Awaiting null-conditional expression '{0}' can dereference a null awaitable", "AsyncCorrectness", DiagnosticSeverity.Warning, false,
        "A null-conditional invocation produces a null awaitable when its receiver is null. Coalesce to a non-null awaitable or guard the receiver before awaiting.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0048", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor TaskConvertedToStringRule = new(
        "SP0049", "Task converted to text without awaiting",
        "Task expression '{0}' is converted to text instead of awaiting its result", "AsyncCorrectness", DiagnosticSeverity.Warning, false,
        "String concatenation and interpolation call ToString on Task objects; they do not use the asynchronous result.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0049", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor TaskCompletionSourceContinuationsRule = new(
        "SP0050", "TaskCompletionSource may run continuations synchronously",
        "TaskCompletionSource construction '{0}' does not prove RunContinuationsAsynchronously", "AsyncCorrectness", DiagnosticSeverity.Warning, false,
        "TaskCompletionSource should normally include TaskCreationOptions.RunContinuationsAsynchronously to prevent completing threads from running arbitrary continuations inline.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0050", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor AsyncVoidRule = new(
        "SP0051", "Async void method is not an event handler",
        "Async void method '{0}' is not an event handler; return Task so callers can observe completion and exceptions", "AsyncCorrectness", DiagnosticSeverity.Warning, false,
        "Async void prevents callers from awaiting completion and routes exceptions through the current synchronization context. It is reserved for event-handler-shaped methods.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0051", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor BlockingAsyncRule = new(
        "SP0052", "Async method blocks on asynchronous work",
        "Async method '{0}' synchronously blocks on '{1}'", "AsyncCorrectness", DiagnosticSeverity.Warning, false,
        "Calling Task.Result, Task.Wait, or GetAwaiter().GetResult() inside async code can deadlock or starve worker threads. Await the operation instead.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0052", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor NullTaskReturnRule = new(
        "SP0053", "Task-returning method returns null",
        "Task-returning method '{0}' returns null; callers that await it will throw", "AsyncCorrectness", DiagnosticSeverity.Warning, false,
        "A non-async method whose declared return type is Task or Task<T> must return a task object, not null.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0053", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor TaskUsedAsDisposableRule = new(
        "SP0054", "Task used as a disposable resource",
        "Task expression '{0}' is disposed by using instead of awaiting its result", "AsyncCorrectness", DiagnosticSeverity.Warning, false,
        "Using a Task disposes the task object; it does not await the asynchronous operation or manage the resource produced by that operation.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0054", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor AsyncValidationDeferredRule = new(
        "SP0055", "Public async parameter validation is deferred",
        "Validation in async method '{0}' is captured by the returned task; use a synchronous wrapper when fail-fast argument validation is required", "AsyncCorrectness", DiagnosticSeverity.Info, false,
        "Exceptions thrown from an async Task method, including validation before its first await, are stored in the returned task. A synchronous wrapper is required when callers must observe argument errors at invocation time.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0055", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor CollectionMutationDuringEnumerationRule = new(
        "SP0056", "Collection mutated during enumeration",
        "Collection '{0}' is mutated by '{1}' while it is being enumerated", "CollectionSafety", DiagnosticSeverity.Warning, false,
        "Mutating the same ordinary mutable collection inside its foreach body invalidates the active enumerator and commonly throws InvalidOperationException.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0056", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor CapturedLoopVariableRule = new(
        "SP0057", "For-loop variable captured by an escaping closure",
        "For-loop variable '{0}' is captured by a closure that can observe a later iteration value", "Correctness", DiagnosticSeverity.Warning, false,
        "For-loop iteration variables are shared across iterations. Copy the value into a local inside the loop before capturing it in a lambda that can escape the iteration.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0057", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor MutableStructRule = new(
        "SP0058", "Struct exposes mutable instance state",
        "Struct '{0}' has mutable instance state; copies can be modified independently", "Design", DiagnosticSeverity.Info, false,
        "Mutable value types are frequently modified through accidental copies. Prefer readonly struct, record struct with immutable members, or a class when shared mutation is intended.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0058", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor OwnedDisposableFieldRule = new(
        "SP0059", "Owned disposable field has no owner disposal contract",
        "Type '{0}' creates disposable field '{1}' but does not implement '{2}'", "ResourceLifetime", DiagnosticSeverity.Warning, false,
        "A field initialized or assigned from a local allocation is owned by its containing type. The owner must expose the matching deterministic disposal lifecycle.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0059", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor HttpClientInLoopRule = new(
        "SP0060", "HttpClient created repeatedly inside a loop",
        "HttpClient is created inside loop '{0}'; reuse a client or use IHttpClientFactory", "ResourceLifetime", DiagnosticSeverity.Warning, false,
        "Repeated HttpClient construction creates avoidable connection pools and can exhaust sockets under sustained load.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0060", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor UnsynchronizedSharedMutationRule = new(
        "SP0061", "Shared state mutated by a parallel callback",
        "Shared state '{0}' is mutated in '{1}' without visible synchronization", "Concurrency", DiagnosticSeverity.Warning, false,
        "Captured locals and fields mutated by Task, Thread, timer, or Parallel callbacks require synchronization such as Interlocked or lock, or should be replaced with isolated state.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0061", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor ConcurrentCollectionEnumerationRule = new(
        "SP0062", "Concurrent collection enumerated through LINQ",
        "LINQ operator '{0}' enumerates concurrent collection '{1}' without snapshot guarantees", "Concurrency", DiagnosticSeverity.Info, false,
        "Concurrent collection members have documented concurrency behavior, but interface and LINQ extension methods do not necessarily provide an atomic snapshot.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0062", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor BoxingInLoopRule = new(
        "SP0063", "Value boxed inside a loop",
        "Value of type '{0}' is boxed inside loop '{1}'", "Performance", DiagnosticSeverity.Info, false,
        "Repeated boxing allocates objects and adds GC pressure. Prefer generic APIs or strongly typed interfaces in repeated paths.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0063", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor MaybeNullResultDereferenceRule = new(
        "SP0064", "Maybe-null query result is dereferenced",
        "Result of '{0}' can be null or empty-default and is dereferenced immediately", "Nullability", DiagnosticSeverity.Warning, false,
        "Default-returning lookup and LINQ APIs require a guard, a non-null fallback, or a proof that a matching element exists before dereference.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0064", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor PrematureQueryMaterializationRule = new(
        "SP0065", "Queryable is materialized before further filtering",
        "'{0}' materializes IQueryable before subsequent '{1}' processing", "Performance", DiagnosticSeverity.Info, false,
        "Compose supported filters and projections on IQueryable before materialization so the remote provider can execute them.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0065", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor DeferredQuerySideEffectRule = new(
        "SP0066", "Deferred query lambda mutates state",
        "Deferred LINQ operator '{0}' contains state mutation '{1}'", "Correctness", DiagnosticSeverity.Warning, false,
        "Side effects in deferred LINQ lambdas run on every enumeration and can therefore execute zero, one, or multiple times unexpectedly.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0066", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor QueryTranslationRiskRule = new(
        "SP0067", "Queryable expression calls source-only method",
        "Queryable operator '{0}' calls source method '{1}' that the remote provider may not translate", "Compatibility", DiagnosticSeverity.Info, false,
        "Remote IQueryable providers translate a bounded method set. Source-only helper calls in query predicates and selectors require provider-specific translation support.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0067", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor SerializationCycleRiskRule = new(
        "SP0068", "Serialized source type has a reference cycle",
        "Type '{0}' contains a serializable reference cycle and is serialized without explicit cycle handling", "Serialization", DiagnosticSeverity.Info, false,
        "System.Text.Json rejects reachable object cycles by default. Project cyclic entities to DTOs, ignore a link, or configure deliberate reference handling.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0068", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor SerializerAttributeMismatchRule = new(
        "SP0069", "Serializer ignores attribute from another JSON library",
        "Serializer '{0}' does not honor attribute '{1}' on member '{2}'", "Serialization", DiagnosticSeverity.Warning, false,
        "Newtonsoft.Json and System.Text.Json define similarly named attributes that are not interchangeable.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0069", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor IneffectiveRequiredAttributeRule = new(
        "SP0070", "Required attribute cannot reject a non-nullable value type default",
        "[Required] on non-nullable value member '{0}' cannot distinguish omitted input from default({1})", "Usage", DiagnosticSeverity.Info, false,
        "Use a nullable value type when model binding must distinguish missing input, then validate the resulting value range separately.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0070", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor UncheckedAllocationArithmeticRule = new(
        "SP0071", "Allocation length uses unchecked arithmetic",
        "Allocation length expression '{0}' can wrap before bounds validation", "Correctness", DiagnosticSeverity.Warning, false,
        "Compute allocation sizes in a checked context so overflow is reported instead of becoming a wrapped negative or undersized length.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0071", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor SuppressionWithoutJustificationRule = new(
        "SP0072", "Diagnostic suppression lacks justification",
        "Suppression '{0}' has no reviewable diagnostic scope or justification", "Review", DiagnosticSeverity.Info, false,
        "Broad pragma suppressions and SuppressMessage attributes without justification hide warning debt and should be narrowed or documented.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0072", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor NullableAnalysisDisabledRule = new(
        "SP0073", "Nullable analysis explicitly disabled",
        "Nullable analysis is disabled for this source region", "Nullability", DiagnosticSeverity.Info, false,
        "Explicit nullable-disable directives create blind spots in compile-time null-state analysis. Prefer scoped annotations and resolved warnings.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0073", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor IdenticalOperandsRule = new(
        "SP0074", "Binary operation uses the same value on both sides",
        "Operation '{0}' uses '{1}' as both operands; verify that the second operand is correct", "Correctness", DiagnosticSeverity.Warning, false,
        "Identical stable operands in built-in comparisons, subtraction, division, and remainder usually indicate a copied or mistyped operand. Floating-point and user-defined operators are excluded because they need not be reflexive.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0074", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor ContainerOwnedServiceDisposedRule = new(
        "SP0075", "Container-owned service is disposed by its consumer",
        "Service resolved by '{0}' is disposed by consuming code; the dependency-injection container owns its lifetime", "ResourceLifetime", DiagnosticSeverity.Warning, false,
        "Services resolved from IServiceProvider are disposed by their owning service provider or scope. Consumers should not wrap the resolved service in using or call Dispose directly.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0075", new[] { WellKnownDiagnosticTags.Telemetry });
    public static readonly DiagnosticDescriptor UnconsumedDeferredQueryRule = new(
        "SP0076", "Deferred query is never consumed",
        "Deferred query produced by '{0}' is never enumerated or materialized", "Correctness", DiagnosticSeverity.Warning, false,
        "LINQ query operators are deferred. Constructing a query and discarding it performs no work and does not execute its predicate or selector.",
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#sp0076", new[] { WellKnownDiagnosticTags.Telemetry });
}

internal static class AnalyzerDiagnosticCatalog
{
    internal static readonly ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =
        typeof(SharpProofDiagnostics)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(static field => field.FieldType == typeof(DiagnosticDescriptor))
            .Select(static field => (DiagnosticDescriptor)field.GetValue(null)!)
            .OrderBy(static descriptor => int.Parse(descriptor.Id.Substring(2)))
            .ToImmutableArray();
}

#pragma warning restore RS1037
#pragma warning restore RS2001

#pragma warning disable RS1037

namespace SharpProof.Analyzer;

internal static class AnalyzerDiagnosticCatalog {
    private const string HelpBase = "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#";

    private static readonly Definition[] Definitions = [
        new("PurityNotVerifiedRule", "SP0002", "Purity Not Proven",
            "Method '{0}' is marked [EnforcePure], but its effects do not prove observable purity", "Purity", DiagnosticSeverity.Error,
            "Methods marked [EnforcePure] require a proven observable-purity verdict derived from MethodEffects."),
        new("AllocationInZeroAllocationMethodRule", "SP0013", "Allocation In [ZeroAllocations] Method",
            "Method '{1}' is marked [ZeroAllocations], but operation '{0}' allocates", "Allocation", DiagnosticSeverity.Warning,
            "Reports allocation sites inside methods annotated with [ZeroAllocations]."),
        new("CapabilityViolationRule", "SP0015", "Disallowed Capability Use",
            "Method '{1}' is marked [AllowedCapabilities], but operation '{0}' requires capabilities: {2}", "Capabilities",
            DiagnosticSeverity.Warning, "Reports operations whose proven capabilities exceed the declared set."),
        new("CapabilityUnknownRule", "SP0016", "Capability Contract Not Proven",
            "Method '{1}' is marked [AllowedCapabilities], but operation '{0}' could not be capability-verified: {2}", "Capabilities",
            DiagnosticSeverity.Warning, "Reports capability contracts that cannot be proven conservatively."),
        new("EnsuresNotProvenRule", "SP0018", "Postcondition Not Proven",
            "Method '{1}' is marked [Ensures], but return site '{0}' does not prove postcondition '{2}'", "Contracts",
            DiagnosticSeverity.Warning, "Reports return sites that contradict a declared postcondition."),
        new("EnsuresUnsupportedRule", "SP0019", "Postcondition Could Not Be Verified",
            "Method '{1}' is marked [Ensures], but postcondition '{0}' could not be verified: {2}", "Contracts",
            DiagnosticSeverity.Warning, "Reports postconditions that cannot be translated or proven."),
        new("ComplexityExceededRule", "SP0021", "Declared Complexity Exceeded",
            "Method '{0}' is marked [ExpectedComplexity({1})], but inferred complexity '{2}' exceeds the declared bound", "Complexity",
            DiagnosticSeverity.Warning, "Reports methods whose inferred complexity exceeds their declared bound."),
        new("ComplexityCouldNotBeVerifiedRule", "SP0022", "Declared Complexity Could Not Be Verified",
            "Method '{0}' is marked [ExpectedComplexity({1})], but the declared bound could not be verified conservatively: {2}",
            "Complexity", DiagnosticSeverity.Warning, "Reports complexity contracts that cannot be verified conservatively."),
        new("InvalidContractArgumentRule", "SP0024", "Invalid SharpProof Contract Argument",
            "SharpProof contract '{0}' has invalid argument '{1}': {2}", "Usage", DiagnosticSeverity.Error,
            "Reports malformed SharpProof contract arguments."),
        new("InvalidAnalyzerConfigurationRule", "SP0025", "Invalid SharpProof Analyzer Configuration",
            "SharpProof analyzer option '{0}' has invalid value '{1}': {2}", "Configuration", DiagnosticSeverity.Warning,
            "Reports invalid analyzer configuration values."),
        new("RequiresNotProvenRule", "SP0027", "Precondition Not Proven", "Call to '{0}' does not prove precondition '{1}'", "Contracts",
            DiagnosticSeverity.Warning, "Reports calls that contradict a declared precondition."),
        new("RequiresUnsupportedRule", "SP0028", "Precondition Could Not Be Verified",
            "Precondition '{1}' for '{0}' could not be verified: {2}", "Contracts", DiagnosticSeverity.Warning,
            "Reports preconditions that cannot be translated or proven."),
        new("ExceptionContractViolationRule", "SP0030", "Exception Contract Violated",
            "Method '{0}' is marked {1}, but operation '{2}' can throw disallowed exceptions: {3}", "ExceptionFlow",
            DiagnosticSeverity.Warning, "Reports escaping exceptions that violate an exception contract."),
        new("NullableReturnContractViolationRule", "SP0041", "Nullable return contract violated",
            "Method '{0}' can return null despite contract '{1}'", "Nullability", DiagnosticSeverity.Warning,
            "Reports reachable returns that violate a nullable return contract."),
        new("NullableParameterPostconditionViolationRule", "SP0042", "Nullable parameter postcondition violated",
            "Method '{0}' can complete with parameter '{1}' null despite contract '{2}'", "Nullability", DiagnosticSeverity.Warning,
            "Reports completions that violate nullable parameter postconditions."),
        new("NullableMemberContractViolationRule", "SP0043", "Nullable member contract violated",
            "Method '{0}' can complete with member '{1}' null despite contract '{2}'", "Nullability", DiagnosticSeverity.Warning,
            "Reports completions that violate nullable member contracts."),
        new("UnsafeNullForgivingOperatorRule", "SP0044", "Null-forgiving operator is unsafe",
            "Null-forgiving operator can suppress a feasible null value for '{0}'", "Nullability", DiagnosticSeverity.Warning,
            "Reports null-forgiving operators reached by a proven feasible null value.")
    ];

    private static readonly ImmutableDictionary<string, DiagnosticDescriptor> DescriptorsByField = Definitions
        .ToImmutableDictionary(static value => value.Field, static value => value.Create(), StringComparer.Ordinal);

    internal static readonly ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics = [.. Definitions.Select(static value
        => value.Create())];

    internal static DiagnosticDescriptor Get(string fieldName) => DescriptorsByField[fieldName];

    private sealed record Definition(
        string Field, string Id, string Title, string Message, string Category,
        DiagnosticSeverity Severity, string Description) {
        internal DiagnosticDescriptor Create() => new(
            Id, Title, Message, Category, Severity, true, Description, HelpBase + Id.ToLowerInvariant());
    }
}
#pragma warning restore RS1037

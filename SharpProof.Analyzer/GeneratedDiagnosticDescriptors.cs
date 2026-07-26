#pragma warning disable RS1037, RS2001
namespace SharpProof.Analyzer;

internal static class GeneratedDiagnosticDescriptors {
    private const string HelpBase =
        "https://github.com/alexyorke/SharpProof/blob/master/docs/diagnostic-examples.md#";

    internal static readonly DiagnosticDescriptor PurityNotVerifiedRule = Create(
        "SP0002",
        "Purity Not Proven",
        "Method '{0}' is marked [EnforcePure], but its effects do not prove observable purity",
        "Purity",
        "Unknown or observable effects prevent an [EnforcePure] success verdict.");

    internal static readonly DiagnosticDescriptor AllocationInZeroAllocationMethodRule = Create(
        "SP0013",
        "Allocation In [ZeroAllocations] Method",
        "Method '{1}' is marked [ZeroAllocations], but operation '{0}' allocates",
        "Allocation",
        "Reports a known allocation in a [ZeroAllocations] method.");

    internal static readonly DiagnosticDescriptor CapabilityViolationRule = Create(
        "SP0015",
        "Disallowed Capability Use",
        "Method '{1}' is marked [AllowedCapabilities], but operation '{0}' requires capabilities: {2}",
        "Capabilities",
        "Reports known capabilities outside the declared set.");

    internal static readonly DiagnosticDescriptor CapabilityUnknownRule = Create(
        "SP0016",
        "Capability Contract Not Proven",
        "Method '{1}' is marked [AllowedCapabilities], but operation '{0}' could not be capability-verified: {2}",
        "Capabilities",
        "Reports capability contracts whose rich effect summary remains unknown.");

    internal static readonly DiagnosticDescriptor InvalidContractArgumentRule = Create(
        "SP0024",
        "Invalid SharpProof Contract Argument",
        "SharpProof contract '{0}' has invalid argument '{1}': {2}",
        "Usage",
        "Reports malformed SharpProof contract or control arguments.",
        isEnabledByDefault: true,
        severity: DiagnosticSeverity.Error);

    internal static readonly DiagnosticDescriptor InvalidAnalyzerConfigurationRule = Create(
        "SP0025",
        "Invalid SharpProof Analyzer Configuration",
        "SharpProof analyzer option '{0}' has invalid value '{1}': {2}",
        "Configuration",
        "Reports invalid compilation-global SharpProof analyzer options.",
        isEnabledByDefault: true,
        severity: DiagnosticSeverity.Warning);

    internal static readonly DiagnosticDescriptor RequiresNotProvenRule = Create(
        "SP0027",
        "Precondition Violated",
        "Call to '{0}' violates precondition '{1}'",
        "Contracts",
        "Reports only compiler-bound preconditions that concretely replay as false.");

    internal static readonly DiagnosticDescriptor ExceptionContractViolationRule = Create(
        "SP0030",
        "Exception Contract Violated",
        "Method '{0}' is marked {1}, but operation '{2}' can throw disallowed exceptions: {3}",
        "ExceptionFlow",
        "Reports known escaping exceptions outside the declared exception contract.");

    internal static readonly DiagnosticDescriptor ZeroAllocationsNotVerifiedRule = Create(
        "SP0045",
        "Zero-allocation Contract Not Proven",
        "Method '{0}' is marked [ZeroAllocations], but allocation freedom could not be verified: {1}",
        "Allocation",
        "Reports [ZeroAllocations] methods whose rich effect summary remains unknown.");

    internal static readonly DiagnosticDescriptor ExceptionContractNotVerifiedRule = Create(
        "SP0046",
        "Exception Contract Not Proven",
        "Method '{0}' is marked {1}, but its exception behavior could not be verified: {2}",
        "ExceptionFlow",
        "Reports exception contracts whose rich effect summary remains unknown.");

    internal static readonly ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics = [
        PurityNotVerifiedRule,
        AllocationInZeroAllocationMethodRule,
        CapabilityViolationRule,
        CapabilityUnknownRule,
        InvalidContractArgumentRule,
        InvalidAnalyzerConfigurationRule,
        RequiresNotProvenRule,
        ExceptionContractViolationRule,
        ZeroAllocationsNotVerifiedRule,
        ExceptionContractNotVerifiedRule
    ];

    private static DiagnosticDescriptor Create(
        string id,
        string title,
        string message,
        string category,
        string description,
        bool isEnabledByDefault = false,
        DiagnosticSeverity severity = DiagnosticSeverity.Info) =>
        new(
            id,
            title,
            message,
            category,
            severity,
            isEnabledByDefault,
            description,
            HelpBase + id.ToLowerInvariant());
}
#pragma warning restore RS1037, RS2001

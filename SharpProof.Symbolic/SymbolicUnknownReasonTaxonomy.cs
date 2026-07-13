namespace SharpProof.Symbolic;

public enum SymbolicUnknownReasonSource
{
    Proof,
    Capability,
    Complexity,
    RuntimeHazard,
    Purity,
    Ensures
}

public enum SymbolicUnknownReasonCategory
{
    None,
    UnsupportedSyntax,
    UnsupportedOperation,
    UnsupportedLibraryModel,
    DynamicDispatch,
    ExternalBoundary,
    RecursiveAnalysis,
    SolverDisabled,
    SolverBudget,
    SolverTimeout,
    NativeSolverFailure,
    SolverEncodingFailure,
    Cancellation,
    InvalidInput,
    AnalysisUnavailable,
    Unknown
}

public sealed class SymbolicUnknownReasonInfo
{
    internal SymbolicUnknownReasonInfo(
        SymbolicUnknownReasonSource source,
        SymbolicUnknownReasonCategory category,
        string code,
        string rawReason,
        bool isRetryable,
        bool isConfigurationRelated)
    {
        Source = source;
        Category = category;
        Code = code ?? string.Empty;
        RawReason = rawReason ?? string.Empty;
        IsRetryable = isRetryable;
        IsConfigurationRelated = isConfigurationRelated;
    }

    public SymbolicUnknownReasonSource Source { get; }

    public SymbolicUnknownReasonCategory Category { get; }

    public string Code { get; }

    public string RawReason { get; }

    public bool IsRetryable { get; }

    public bool IsConfigurationRelated { get; }

    public bool IsUnknown => Category != SymbolicUnknownReasonCategory.None;
}

internal static class SymbolicUnknownReasonTaxonomy
{
    private static readonly IReadOnlyDictionary<SymbolicCapabilityUnknownReason, DomainReasonDescriptor>
        CapabilityReasonDescriptors =
            new Dictionary<SymbolicCapabilityUnknownReason, DomainReasonDescriptor>
            {
                [SymbolicCapabilityUnknownReason.None] = new(SymbolicUnknownReasonCategory.None, "capability.none"),
                [SymbolicCapabilityUnknownReason.UnsupportedTarget] = new(SymbolicUnknownReasonCategory.UnsupportedSyntax, "capability.unsupported_target"),
                [SymbolicCapabilityUnknownReason.NoContainingMethodLikeBody] = new(SymbolicUnknownReasonCategory.UnsupportedSyntax, "capability.no_containing_method_body"),
                [SymbolicCapabilityUnknownReason.DynamicDispatch] = new(SymbolicUnknownReasonCategory.DynamicDispatch, "capability.dynamic_dispatch"),
                [SymbolicCapabilityUnknownReason.MetadataClassificationUnavailable] = new(SymbolicUnknownReasonCategory.UnsupportedLibraryModel, "capability.library_model_unavailable"),
                [SymbolicCapabilityUnknownReason.UnsupportedOperation] = new(SymbolicUnknownReasonCategory.UnsupportedOperation, "capability.unsupported_operation"),
                [SymbolicCapabilityUnknownReason.RecursiveSourceCycle] = new(SymbolicUnknownReasonCategory.RecursiveAnalysis, "capability.recursive_source_cycle"),
                [SymbolicCapabilityUnknownReason.ExternalSourceBoundary] = new(SymbolicUnknownReasonCategory.ExternalBoundary, "capability.external_source_boundary"),
                [SymbolicCapabilityUnknownReason.CancellationRequested] = new(SymbolicUnknownReasonCategory.Cancellation, "capability.canceled", IsRetryable: true),
                [SymbolicCapabilityUnknownReason.Unknown] = new(SymbolicUnknownReasonCategory.Unknown, "capability.unknown")
            };

    private static readonly IReadOnlyDictionary<SymbolicComplexityUnknownReason, DomainReasonDescriptor>
        ComplexityReasonDescriptors =
            new Dictionary<SymbolicComplexityUnknownReason, DomainReasonDescriptor>
            {
                [SymbolicComplexityUnknownReason.None] = new(SymbolicUnknownReasonCategory.None, "complexity.none"),
                [SymbolicComplexityUnknownReason.UnsupportedTarget] = new(SymbolicUnknownReasonCategory.UnsupportedSyntax, "complexity.unsupported_target"),
                [SymbolicComplexityUnknownReason.NoContainingMethodLikeBody] = new(SymbolicUnknownReasonCategory.UnsupportedSyntax, "complexity.no_containing_method_body"),
                [SymbolicComplexityUnknownReason.UnsupportedLoopShape] = new(SymbolicUnknownReasonCategory.UnsupportedSyntax, "complexity.unsupported_loop_shape"),
                [SymbolicComplexityUnknownReason.UnsupportedWhileLoop] = new(SymbolicUnknownReasonCategory.UnsupportedSyntax, "complexity.unsupported_while_loop"),
                [SymbolicComplexityUnknownReason.UnknownCallee] = new(SymbolicUnknownReasonCategory.UnsupportedLibraryModel, "complexity.unknown_callee"),
                [SymbolicComplexityUnknownReason.ExternalCallee] = new(SymbolicUnknownReasonCategory.ExternalBoundary, "complexity.external_callee"),
                [SymbolicComplexityUnknownReason.DynamicDispatch] = new(SymbolicUnknownReasonCategory.DynamicDispatch, "complexity.dynamic_dispatch"),
                [SymbolicComplexityUnknownReason.RecursiveCycle] = new(SymbolicUnknownReasonCategory.RecursiveAnalysis, "complexity.recursive_cycle"),
                [SymbolicComplexityUnknownReason.UnsupportedOperation] = new(SymbolicUnknownReasonCategory.UnsupportedOperation, "complexity.unsupported_operation"),
                [SymbolicComplexityUnknownReason.CancellationRequested] = new(SymbolicUnknownReasonCategory.Cancellation, "complexity.canceled", IsRetryable: true),
                [SymbolicComplexityUnknownReason.Unknown] = new(SymbolicUnknownReasonCategory.Unknown, "complexity.unknown")
            };

    private static readonly DomainReasonDescriptor CapabilityUnknownDescriptor =
        new(SymbolicUnknownReasonCategory.Unknown, "capability.unknown");

    private static readonly DomainReasonDescriptor ComplexityUnknownDescriptor =
        new(SymbolicUnknownReasonCategory.Unknown, "complexity.unknown");

    internal static SymbolicUnknownReasonInfo ForProof(SymbolicUnknownReason reason, string? rawReason = null)
    {
        return reason switch
        {
            SymbolicUnknownReason.None => Create(
                SymbolicUnknownReasonSource.Proof,
                SymbolicUnknownReasonCategory.None,
                "proof.none",
                rawReason),
            SymbolicUnknownReason.UnsupportedIrEncoding => Create(
                SymbolicUnknownReasonSource.Proof,
                SymbolicUnknownReasonCategory.UnsupportedSyntax,
                "proof.unsupported_ir_encoding",
                rawReason),
            SymbolicUnknownReason.SmtDisabled => Create(
                SymbolicUnknownReasonSource.Proof,
                SymbolicUnknownReasonCategory.SolverDisabled,
                "proof.solver_disabled",
                rawReason,
                isConfigurationRelated: true),
            SymbolicUnknownReason.SmtUnavailable => Create(
                SymbolicUnknownReasonSource.Proof,
                SymbolicUnknownReasonCategory.NativeSolverFailure,
                "proof.native_solver_failure",
                rawReason,
                isRetryable: true),
            SymbolicUnknownReason.Timeout => Create(
                SymbolicUnknownReasonSource.Proof,
                SymbolicUnknownReasonCategory.SolverTimeout,
                "proof.solver_timeout",
                rawReason,
                isRetryable: true,
                isConfigurationRelated: true),
            SymbolicUnknownReason.MethodBudgetExceeded => Create(
                SymbolicUnknownReasonSource.Proof,
                SymbolicUnknownReasonCategory.SolverBudget,
                "proof.solver_method_budget",
                rawReason,
                isRetryable: true,
                isConfigurationRelated: true),
            SymbolicUnknownReason.PathConditionBudgetExceeded => Create(
                SymbolicUnknownReasonSource.Proof,
                SymbolicUnknownReasonCategory.SolverBudget,
                "proof.solver_path_condition_budget",
                rawReason,
                isRetryable: true,
                isConfigurationRelated: true),
            SymbolicUnknownReason.ExpressionBudgetExceeded => Create(
                SymbolicUnknownReasonSource.Proof,
                SymbolicUnknownReasonCategory.SolverBudget,
                "proof.solver_expression_budget",
                rawReason,
                isRetryable: true,
                isConfigurationRelated: true),
            SymbolicUnknownReason.CancellationRequested => Create(
                SymbolicUnknownReasonSource.Proof,
                SymbolicUnknownReasonCategory.Cancellation,
                "proof.canceled",
                rawReason,
                isRetryable: true),
            SymbolicUnknownReason.EncodingFailure => Create(
                SymbolicUnknownReasonSource.Proof,
                SymbolicUnknownReasonCategory.SolverEncodingFailure,
                "proof.solver_encoding_failure",
                rawReason),
            _ => Create(
                SymbolicUnknownReasonSource.Proof,
                SymbolicUnknownReasonCategory.Unknown,
                "proof.unknown",
                rawReason)
        };
    }

    internal static SymbolicUnknownReasonInfo ForCapability(SymbolicCapabilityUnknownReason reason)
    {
        return ForDomain(
            reason,
            SymbolicUnknownReasonSource.Capability,
            CapabilityReasonDescriptors,
            CapabilityUnknownDescriptor);
    }

    internal static SymbolicUnknownReasonInfo ForCapabilityFailure(string? rawReason)
    {
        return Create(
            SymbolicUnknownReasonSource.Capability,
            SymbolicUnknownReasonCategory.AnalysisUnavailable,
            "capability.analysis_failure",
            rawReason,
            isRetryable: true);
    }

    internal static SymbolicUnknownReasonInfo ForComplexity(SymbolicComplexityUnknownReason reason)
    {
        return ForDomain(
            reason,
            SymbolicUnknownReasonSource.Complexity,
            ComplexityReasonDescriptors,
            ComplexityUnknownDescriptor);
    }

    internal static SymbolicUnknownReasonInfo ForComplexityFailure(string? rawReason)
    {
        return Create(
            SymbolicUnknownReasonSource.Complexity,
            SymbolicUnknownReasonCategory.AnalysisUnavailable,
            "complexity.analysis_failure",
            rawReason,
            isRetryable: true);
    }

    internal static SymbolicUnknownReasonInfo ForRuntimeHazard(
        SymbolicRuntimeHazardStatus status,
        string? rawReason,
        SymbolicUnknownReason proofReason)
    {
        if (status is SymbolicRuntimeHazardStatus.Proven or SymbolicRuntimeHazardStatus.Unreachable)
            return Create(
                SymbolicUnknownReasonSource.RuntimeHazard,
                SymbolicUnknownReasonCategory.None,
                "runtime_hazard.none",
                rawReason);

        if (Contains(rawReason, "unsupported_typed_projection"))
            return Create(
                SymbolicUnknownReasonSource.RuntimeHazard,
                SymbolicUnknownReasonCategory.UnsupportedOperation,
                "runtime_hazard.unsupported_typed_projection",
                rawReason);

        if (proofReason is SymbolicUnknownReason.None or SymbolicUnknownReason.Unknown)
            return Create(
                SymbolicUnknownReasonSource.RuntimeHazard,
                status == SymbolicRuntimeHazardStatus.Unsupported
                    ? SymbolicUnknownReasonCategory.UnsupportedOperation
                    : SymbolicUnknownReasonCategory.Unknown,
                status == SymbolicRuntimeHazardStatus.Unsupported
                    ? "runtime_hazard.unsupported"
                    : "runtime_hazard.unknown",
                rawReason);

        return ChangeSource(ForProof(proofReason, rawReason), SymbolicUnknownReasonSource.RuntimeHazard,
            "runtime_hazard");
    }

    internal static SymbolicUnknownReasonInfo ForEnsures(
        string? rawReason,
        SymbolicUnknownReason proofReason = SymbolicUnknownReason.Unknown)
    {
        if (proofReason != SymbolicUnknownReason.None && proofReason != SymbolicUnknownReason.Unknown)
            return ChangeSource(ForProof(proofReason, rawReason), SymbolicUnknownReasonSource.Ensures, "ensures");

        if (Contains(rawReason, "parse") || Contains(rawReason, "binding"))
            return Create(
                SymbolicUnknownReasonSource.Ensures,
                SymbolicUnknownReasonCategory.InvalidInput,
                "ensures.invalid_condition",
                rawReason);

        if (Contains(rawReason, "not supported") ||
            Contains(rawReason, "unsupported") ||
            Contains(rawReason, "not available"))
            return Create(
                SymbolicUnknownReasonSource.Ensures,
                SymbolicUnknownReasonCategory.UnsupportedSyntax,
                "ensures.unsupported_condition",
                rawReason);

        var classified = SymbolicUnknownReasonClassifier.Classify(rawReason ?? string.Empty);
        if (classified != SymbolicUnknownReason.Unknown)
            return ChangeSource(ForProof(classified, rawReason), SymbolicUnknownReasonSource.Ensures, "ensures");

        return Create(
            SymbolicUnknownReasonSource.Ensures,
            SymbolicUnknownReasonCategory.Unknown,
            "ensures.unknown",
            rawReason);
    }

    internal static SymbolicUnknownReasonInfo ForPurity(
        string? category,
        string? bclFallbackReason)
    {
        if (!string.IsNullOrWhiteSpace(bclFallbackReason))
            return Create(
                SymbolicUnknownReasonSource.Purity,
                SymbolicUnknownReasonCategory.UnsupportedLibraryModel,
                "purity.library_model_fallback",
                bclFallbackReason);

        if (Contains(category, "unsupported") || Contains(category, "unsafe_pointer"))
            return Create(
                SymbolicUnknownReasonSource.Purity,
                SymbolicUnknownReasonCategory.UnsupportedOperation,
                "purity.unsupported_operation",
                category);

        if (Contains(category, "dynamic"))
            return Create(
                SymbolicUnknownReasonSource.Purity,
                SymbolicUnknownReasonCategory.DynamicDispatch,
                "purity.dynamic_dispatch",
                category);

        if (Contains(category, "external") || Contains(category, "unknown_callee"))
            return Create(
                SymbolicUnknownReasonSource.Purity,
                SymbolicUnknownReasonCategory.ExternalBoundary,
                "purity.external_boundary",
                category);

        if (Contains(category, "recursive"))
            return Create(
                SymbolicUnknownReasonSource.Purity,
                SymbolicUnknownReasonCategory.RecursiveAnalysis,
                "purity.recursive_analysis",
                category);

        if (Contains(category, "cancel"))
            return Create(
                SymbolicUnknownReasonSource.Purity,
                SymbolicUnknownReasonCategory.Cancellation,
                "purity.canceled",
                category,
                isRetryable: true);

        if (Contains(category, "analysis_failure"))
            return Create(
                SymbolicUnknownReasonSource.Purity,
                SymbolicUnknownReasonCategory.AnalysisUnavailable,
                "purity.analysis_failure",
                category,
                isRetryable: true);

        if (string.Equals(category, "unknown", StringComparison.OrdinalIgnoreCase))
            return Create(
                SymbolicUnknownReasonSource.Purity,
                SymbolicUnknownReasonCategory.Unknown,
                "purity.unknown",
                category);

        return Create(
            SymbolicUnknownReasonSource.Purity,
            SymbolicUnknownReasonCategory.None,
            "purity.none",
            category);
    }

    private static SymbolicUnknownReasonInfo ChangeSource(
        SymbolicUnknownReasonInfo info,
        SymbolicUnknownReasonSource source,
        string prefix)
    {
        var suffixIndex = info.Code.IndexOf(".", StringComparison.Ordinal);
        var suffix = suffixIndex >= 0 ? info.Code.Substring(suffixIndex + 1) : info.Code;
        return new SymbolicUnknownReasonInfo(
            source,
            info.Category,
            prefix + "." + suffix,
            info.RawReason,
            info.IsRetryable,
            info.IsConfigurationRelated);
    }

    private static SymbolicUnknownReasonInfo ForDomain<TReason>(
        TReason reason,
        SymbolicUnknownReasonSource source,
        IReadOnlyDictionary<TReason, DomainReasonDescriptor> descriptors,
        DomainReasonDescriptor unknownDescriptor)
        where TReason : struct, Enum
    {
        var descriptor = descriptors.TryGetValue(reason, out var knownDescriptor)
            ? knownDescriptor
            : unknownDescriptor;
        return Create(
            source,
            descriptor.Category,
            descriptor.Code,
            reason.ToString(),
            descriptor.IsRetryable,
            descriptor.IsConfigurationRelated);
    }

    private static SymbolicUnknownReasonInfo Create(
        SymbolicUnknownReasonSource source,
        SymbolicUnknownReasonCategory category,
        string code,
        string? rawReason,
        bool isRetryable = false,
        bool isConfigurationRelated = false)
    {
        return new SymbolicUnknownReasonInfo(
            source,
            category,
            code,
            rawReason ?? string.Empty,
            isRetryable,
            isConfigurationRelated);
    }

    private static bool Contains(string? value, string fragment)
    {
        return value?.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private readonly record struct DomainReasonDescriptor(
        SymbolicUnknownReasonCategory Category,
        string Code,
        bool IsRetryable = false,
        bool IsConfigurationRelated = false);
}

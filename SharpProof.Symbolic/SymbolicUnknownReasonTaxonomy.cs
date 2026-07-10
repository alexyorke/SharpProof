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
        return reason switch
        {
            SymbolicCapabilityUnknownReason.None => Create(
                SymbolicUnknownReasonSource.Capability,
                SymbolicUnknownReasonCategory.None,
                "capability.none",
                reason.ToString()),
            SymbolicCapabilityUnknownReason.UnsupportedTarget => Create(
                SymbolicUnknownReasonSource.Capability,
                SymbolicUnknownReasonCategory.UnsupportedSyntax,
                "capability.unsupported_target",
                reason.ToString()),
            SymbolicCapabilityUnknownReason.NoContainingMethodLikeBody => Create(
                SymbolicUnknownReasonSource.Capability,
                SymbolicUnknownReasonCategory.UnsupportedSyntax,
                "capability.no_containing_method_body",
                reason.ToString()),
            SymbolicCapabilityUnknownReason.DynamicDispatch => Create(
                SymbolicUnknownReasonSource.Capability,
                SymbolicUnknownReasonCategory.DynamicDispatch,
                "capability.dynamic_dispatch",
                reason.ToString()),
            SymbolicCapabilityUnknownReason.MetadataClassificationUnavailable => Create(
                SymbolicUnknownReasonSource.Capability,
                SymbolicUnknownReasonCategory.UnsupportedLibraryModel,
                "capability.library_model_unavailable",
                reason.ToString()),
            SymbolicCapabilityUnknownReason.UnsupportedOperation => Create(
                SymbolicUnknownReasonSource.Capability,
                SymbolicUnknownReasonCategory.UnsupportedOperation,
                "capability.unsupported_operation",
                reason.ToString()),
            SymbolicCapabilityUnknownReason.RecursiveSourceCycle => Create(
                SymbolicUnknownReasonSource.Capability,
                SymbolicUnknownReasonCategory.RecursiveAnalysis,
                "capability.recursive_source_cycle",
                reason.ToString()),
            SymbolicCapabilityUnknownReason.ExternalSourceBoundary => Create(
                SymbolicUnknownReasonSource.Capability,
                SymbolicUnknownReasonCategory.ExternalBoundary,
                "capability.external_source_boundary",
                reason.ToString()),
            SymbolicCapabilityUnknownReason.CancellationRequested => Create(
                SymbolicUnknownReasonSource.Capability,
                SymbolicUnknownReasonCategory.Cancellation,
                "capability.canceled",
                reason.ToString(),
                isRetryable: true),
            _ => Create(
                SymbolicUnknownReasonSource.Capability,
                SymbolicUnknownReasonCategory.Unknown,
                "capability.unknown",
                reason.ToString())
        };
    }

    internal static SymbolicUnknownReasonInfo ForComplexity(SymbolicComplexityUnknownReason reason)
    {
        return reason switch
        {
            SymbolicComplexityUnknownReason.None => Create(
                SymbolicUnknownReasonSource.Complexity,
                SymbolicUnknownReasonCategory.None,
                "complexity.none",
                reason.ToString()),
            SymbolicComplexityUnknownReason.UnsupportedTarget => Create(
                SymbolicUnknownReasonSource.Complexity,
                SymbolicUnknownReasonCategory.UnsupportedSyntax,
                "complexity.unsupported_target",
                reason.ToString()),
            SymbolicComplexityUnknownReason.NoContainingMethodLikeBody => Create(
                SymbolicUnknownReasonSource.Complexity,
                SymbolicUnknownReasonCategory.UnsupportedSyntax,
                "complexity.no_containing_method_body",
                reason.ToString()),
            SymbolicComplexityUnknownReason.UnsupportedLoopShape => Create(
                SymbolicUnknownReasonSource.Complexity,
                SymbolicUnknownReasonCategory.UnsupportedSyntax,
                "complexity.unsupported_loop_shape",
                reason.ToString()),
            SymbolicComplexityUnknownReason.UnsupportedWhileLoop => Create(
                SymbolicUnknownReasonSource.Complexity,
                SymbolicUnknownReasonCategory.UnsupportedSyntax,
                "complexity.unsupported_while_loop",
                reason.ToString()),
            SymbolicComplexityUnknownReason.UnknownCallee => Create(
                SymbolicUnknownReasonSource.Complexity,
                SymbolicUnknownReasonCategory.UnsupportedLibraryModel,
                "complexity.unknown_callee",
                reason.ToString()),
            SymbolicComplexityUnknownReason.ExternalCallee => Create(
                SymbolicUnknownReasonSource.Complexity,
                SymbolicUnknownReasonCategory.ExternalBoundary,
                "complexity.external_callee",
                reason.ToString()),
            SymbolicComplexityUnknownReason.DynamicDispatch => Create(
                SymbolicUnknownReasonSource.Complexity,
                SymbolicUnknownReasonCategory.DynamicDispatch,
                "complexity.dynamic_dispatch",
                reason.ToString()),
            SymbolicComplexityUnknownReason.RecursiveCycle => Create(
                SymbolicUnknownReasonSource.Complexity,
                SymbolicUnknownReasonCategory.RecursiveAnalysis,
                "complexity.recursive_cycle",
                reason.ToString()),
            SymbolicComplexityUnknownReason.UnsupportedOperation => Create(
                SymbolicUnknownReasonSource.Complexity,
                SymbolicUnknownReasonCategory.UnsupportedOperation,
                "complexity.unsupported_operation",
                reason.ToString()),
            SymbolicComplexityUnknownReason.CancellationRequested => Create(
                SymbolicUnknownReasonSource.Complexity,
                SymbolicUnknownReasonCategory.Cancellation,
                "complexity.canceled",
                reason.ToString(),
                isRetryable: true),
            _ => Create(
                SymbolicUnknownReasonSource.Complexity,
                SymbolicUnknownReasonCategory.Unknown,
                "complexity.unknown",
                reason.ToString())
        };
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

        if (Contains(rawReason, "unsupported_formula_fallback"))
            return Create(
                SymbolicUnknownReasonSource.RuntimeHazard,
                SymbolicUnknownReasonCategory.UnsupportedSyntax,
                "runtime_hazard.unsupported_formula_fallback",
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
}

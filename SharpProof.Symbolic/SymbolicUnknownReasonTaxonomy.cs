namespace SharpProof.Symbolic;

internal enum SymbolicUnknownReasonSource
{
    Proof,
    Capability,
    Complexity,
    RuntimeHazard,
    Purity,
    Ensures
}

internal enum SymbolicUnknownReasonCategory
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

internal sealed class SymbolicUnknownReasonInfo(
    SymbolicUnknownReasonSource source,
    SymbolicUnknownReasonCategory category,
    string code,
    string rawReason,
    bool isRetryable,
    bool isConfigurationRelated)
{
    public SymbolicUnknownReasonSource Source { get; } = source;
    public SymbolicUnknownReasonCategory Category { get; } = category;
    public string Code { get; } = code ?? string.Empty;
    public string RawReason { get; } = rawReason ?? string.Empty;
    public bool IsRetryable { get; } = isRetryable;
    public bool IsConfigurationRelated { get; } = isConfigurationRelated;
    public bool IsUnknown => Category != SymbolicUnknownReasonCategory.None;
}

internal static class SymbolicUnknownReasonTaxonomy
{
    internal static SymbolicUnknownReasonInfo ForProof(SymbolicUnknownReason reason, string? rawReason = null) =>
        Create(SymbolicUnknownReasonSource.Proof, "proof", Describe(reason), rawReason);

    internal static SymbolicUnknownReasonInfo ForCapability(SymbolicCapabilityUnknownReason reason) =>
        Create(SymbolicUnknownReasonSource.Capability, "capability", Describe(reason), reason.ToString());

    internal static SymbolicUnknownReasonInfo ForCapabilityFailure(string? rawReason) =>
        Failure(SymbolicUnknownReasonSource.Capability, "capability", rawReason);

    internal static SymbolicUnknownReasonInfo ForComplexity(SymbolicComplexityUnknownReason reason) =>
        Create(SymbolicUnknownReasonSource.Complexity, "complexity", Describe(reason), reason.ToString());

    internal static SymbolicUnknownReasonInfo ForComplexityFailure(string? rawReason) =>
        Failure(SymbolicUnknownReasonSource.Complexity, "complexity", rawReason);

    internal static SymbolicUnknownReasonInfo ForRuntimeHazard(
        SymbolicRuntimeHazardStatus status,
        string? rawReason,
        SymbolicUnknownReason proofReason)
    {
        if (status is SymbolicRuntimeHazardStatus.Proven or SymbolicRuntimeHazardStatus.Unreachable)
            return Create(SymbolicUnknownReasonSource.RuntimeHazard, "runtime_hazard",
                new(SymbolicUnknownReasonCategory.None, "none"), rawReason);

        if (Contains(rawReason, "unsupported_typed_projection"))
            return Create(SymbolicUnknownReasonSource.RuntimeHazard, "runtime_hazard",
                new(SymbolicUnknownReasonCategory.UnsupportedOperation, "unsupported_typed_projection"), rawReason);

        if (proofReason is not (SymbolicUnknownReason.None or SymbolicUnknownReason.Unknown))
            return ChangeSource(ForProof(proofReason, rawReason), SymbolicUnknownReasonSource.RuntimeHazard,
                "runtime_hazard");

        var unsupported = status == SymbolicRuntimeHazardStatus.Unsupported;
        return Create(SymbolicUnknownReasonSource.RuntimeHazard, "runtime_hazard",
            new(unsupported ? SymbolicUnknownReasonCategory.UnsupportedOperation : SymbolicUnknownReasonCategory.Unknown,
                unsupported ? "unsupported" : "unknown"), rawReason);
    }

    internal static SymbolicUnknownReasonInfo ForEnsures(
        string? rawReason,
        SymbolicUnknownReason proofReason = SymbolicUnknownReason.Unknown)
    {
        if (proofReason is not (SymbolicUnknownReason.None or SymbolicUnknownReason.Unknown))
            return ChangeSource(ForProof(proofReason, rawReason), SymbolicUnknownReasonSource.Ensures, "ensures");

        if (Contains(rawReason, "parse") || Contains(rawReason, "binding"))
            return Create(SymbolicUnknownReasonSource.Ensures, "ensures",
                new(SymbolicUnknownReasonCategory.InvalidInput, "invalid_condition"), rawReason);

        if (Contains(rawReason, "not supported") || Contains(rawReason, "unsupported") ||
            Contains(rawReason, "not available"))
            return Create(SymbolicUnknownReasonSource.Ensures, "ensures",
                new(SymbolicUnknownReasonCategory.UnsupportedSyntax, "unsupported_condition"), rawReason);

        var classified = SymbolicUnknownReasonClassifier.Classify(rawReason ?? string.Empty);
        return classified == SymbolicUnknownReason.Unknown
            ? Create(SymbolicUnknownReasonSource.Ensures, "ensures",
                new(SymbolicUnknownReasonCategory.Unknown, "unknown"), rawReason)
            : ChangeSource(ForProof(classified, rawReason), SymbolicUnknownReasonSource.Ensures, "ensures");
    }

    internal static SymbolicUnknownReasonInfo ForPurity(string? category, string? bclFallbackReason)
    {
        if (!string.IsNullOrWhiteSpace(bclFallbackReason))
            return Create(SymbolicUnknownReasonSource.Purity, "purity",
                new(SymbolicUnknownReasonCategory.UnsupportedLibraryModel, "library_model_fallback"),
                bclFallbackReason);

        var descriptor = Contains(category, "unsupported") || Contains(category, "unsafe_pointer")
            ? new ReasonDescriptor(SymbolicUnknownReasonCategory.UnsupportedOperation, "unsupported_operation")
            : Contains(category, "dynamic")
                ? new(SymbolicUnknownReasonCategory.DynamicDispatch, "dynamic_dispatch")
                : Contains(category, "external") || Contains(category, "unknown_callee")
                    ? new(SymbolicUnknownReasonCategory.ExternalBoundary, "external_boundary")
                    : Contains(category, "recursive")
                        ? new(SymbolicUnknownReasonCategory.RecursiveAnalysis, "recursive_analysis")
                        : Contains(category, "cancel")
                            ? new(SymbolicUnknownReasonCategory.Cancellation, "canceled", true)
                            : Contains(category, "analysis_failure")
                                ? new(SymbolicUnknownReasonCategory.AnalysisUnavailable, "analysis_failure", true)
                                : string.Equals(category, "unknown", StringComparison.OrdinalIgnoreCase)
                                    ? new(SymbolicUnknownReasonCategory.Unknown, "unknown")
                                    : new(SymbolicUnknownReasonCategory.None, "none");
        return Create(SymbolicUnknownReasonSource.Purity, "purity", descriptor, category);
    }

    private static ReasonDescriptor Describe(SymbolicUnknownReason reason) => reason switch
    {
        SymbolicUnknownReason.None => new(SymbolicUnknownReasonCategory.None, "none"),
        SymbolicUnknownReason.UnsupportedIrEncoding => new(SymbolicUnknownReasonCategory.UnsupportedSyntax,
            "unsupported_ir_encoding"),
        SymbolicUnknownReason.SmtDisabled => new(SymbolicUnknownReasonCategory.SolverDisabled, "solver_disabled",
            IsConfigurationRelated: true),
        SymbolicUnknownReason.SmtUnavailable => new(SymbolicUnknownReasonCategory.NativeSolverFailure,
            "native_solver_failure", true),
        SymbolicUnknownReason.Timeout => new(SymbolicUnknownReasonCategory.SolverTimeout, "solver_timeout", true,
            true),
        SymbolicUnknownReason.MethodBudgetExceeded => Budget("solver_method_budget"),
        SymbolicUnknownReason.PathConditionBudgetExceeded => Budget("solver_path_condition_budget"),
        SymbolicUnknownReason.ExpressionBudgetExceeded => Budget("solver_expression_budget"),
        SymbolicUnknownReason.CancellationRequested => new(SymbolicUnknownReasonCategory.Cancellation, "canceled",
            true),
        SymbolicUnknownReason.EncodingFailure => new(SymbolicUnknownReasonCategory.SolverEncodingFailure,
            "solver_encoding_failure"),
        _ => new(SymbolicUnknownReasonCategory.Unknown, "unknown")
    };

    private static ReasonDescriptor Describe(SymbolicCapabilityUnknownReason reason) => reason switch
    {
        SymbolicCapabilityUnknownReason.None => new(SymbolicUnknownReasonCategory.None, "none"),
        SymbolicCapabilityUnknownReason.UnsupportedTarget => Syntax("unsupported_target"),
        SymbolicCapabilityUnknownReason.NoContainingMethodLikeBody => Syntax("no_containing_method_body"),
        SymbolicCapabilityUnknownReason.DynamicDispatch => new(SymbolicUnknownReasonCategory.DynamicDispatch,
            "dynamic_dispatch"),
        SymbolicCapabilityUnknownReason.MetadataClassificationUnavailable =>
            new(SymbolicUnknownReasonCategory.UnsupportedLibraryModel, "library_model_unavailable"),
        SymbolicCapabilityUnknownReason.UnsupportedOperation =>
            new(SymbolicUnknownReasonCategory.UnsupportedOperation, "unsupported_operation"),
        SymbolicCapabilityUnknownReason.RecursiveSourceCycle =>
            new(SymbolicUnknownReasonCategory.RecursiveAnalysis, "recursive_source_cycle"),
        SymbolicCapabilityUnknownReason.ExternalSourceBoundary =>
            new(SymbolicUnknownReasonCategory.ExternalBoundary, "external_source_boundary"),
        SymbolicCapabilityUnknownReason.CancellationRequested =>
            new(SymbolicUnknownReasonCategory.Cancellation, "canceled", true),
        _ => new(SymbolicUnknownReasonCategory.Unknown, "unknown")
    };

    private static ReasonDescriptor Describe(SymbolicComplexityUnknownReason reason) => reason switch
    {
        SymbolicComplexityUnknownReason.None => new(SymbolicUnknownReasonCategory.None, "none"),
        SymbolicComplexityUnknownReason.UnsupportedTarget => Syntax("unsupported_target"),
        SymbolicComplexityUnknownReason.NoContainingMethodLikeBody => Syntax("no_containing_method_body"),
        SymbolicComplexityUnknownReason.UnsupportedLoopShape => Syntax("unsupported_loop_shape"),
        SymbolicComplexityUnknownReason.UnsupportedWhileLoop => Syntax("unsupported_while_loop"),
        SymbolicComplexityUnknownReason.UnknownCallee =>
            new(SymbolicUnknownReasonCategory.UnsupportedLibraryModel, "unknown_callee"),
        SymbolicComplexityUnknownReason.ExternalCallee =>
            new(SymbolicUnknownReasonCategory.ExternalBoundary, "external_callee"),
        SymbolicComplexityUnknownReason.DynamicDispatch =>
            new(SymbolicUnknownReasonCategory.DynamicDispatch, "dynamic_dispatch"),
        SymbolicComplexityUnknownReason.RecursiveCycle =>
            new(SymbolicUnknownReasonCategory.RecursiveAnalysis, "recursive_cycle"),
        SymbolicComplexityUnknownReason.UnsupportedOperation =>
            new(SymbolicUnknownReasonCategory.UnsupportedOperation, "unsupported_operation"),
        SymbolicComplexityUnknownReason.CancellationRequested =>
            new(SymbolicUnknownReasonCategory.Cancellation, "canceled", true),
        _ => new(SymbolicUnknownReasonCategory.Unknown, "unknown")
    };

    private static ReasonDescriptor Syntax(string suffix) =>
        new(SymbolicUnknownReasonCategory.UnsupportedSyntax, suffix);

    private static ReasonDescriptor Budget(string suffix) =>
        new(SymbolicUnknownReasonCategory.SolverBudget, suffix, true, true);

    private static SymbolicUnknownReasonInfo Failure(
        SymbolicUnknownReasonSource source,
        string prefix,
        string? rawReason) => Create(source, prefix,
        new(SymbolicUnknownReasonCategory.AnalysisUnavailable, "analysis_failure", true), rawReason);

    private static SymbolicUnknownReasonInfo ChangeSource(
        SymbolicUnknownReasonInfo info,
        SymbolicUnknownReasonSource source,
        string prefix) => new(
        source,
        info.Category,
        prefix + info.Code.Substring(info.Code.IndexOf(".", StringComparison.Ordinal)),
        info.RawReason,
        info.IsRetryable,
        info.IsConfigurationRelated);

    private static SymbolicUnknownReasonInfo Create(
        SymbolicUnknownReasonSource source,
        string prefix,
        ReasonDescriptor descriptor,
        string? rawReason) => new(
        source,
        descriptor.Category,
        prefix + "." + descriptor.Suffix,
        rawReason ?? string.Empty,
        descriptor.IsRetryable,
        descriptor.IsConfigurationRelated);

    private static bool Contains(string? value, string fragment) =>
        value?.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0;

    private readonly record struct ReasonDescriptor(
        SymbolicUnknownReasonCategory Category,
        string Suffix,
        bool IsRetryable = false,
        bool IsConfigurationRelated = false);
}

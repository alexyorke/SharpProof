namespace SharpProof.Symbolic;
internal enum SymbolicUnknownReasonSource {
    Proof,
    Complexity,
    RuntimeHazard
}
internal enum SymbolicUnknownReasonCategory {
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
    Unknown
}
internal sealed record SymbolicUnknownReasonInfo(
    SymbolicUnknownReasonSource Source,
    SymbolicUnknownReasonCategory Category,
    string Code,
    string RawReason,
    bool IsRetryable,
    bool IsConfigurationRelated) {
    public bool IsUnknown => Category != SymbolicUnknownReasonCategory.None;
}
internal static class SymbolicUnknownReasonTaxonomy {
    internal static SymbolicUnknownReasonInfo ForProof(SymbolicUnknownReason reason, string? rawReason = null) =>
        Create(SymbolicUnknownReasonSource.Proof, "proof", Describe(reason), rawReason);
    internal static SymbolicUnknownReasonInfo ForComplexity(SymbolicComplexityUnknownReason reason) =>
        Create(SymbolicUnknownReasonSource.Complexity, "complexity", Describe(reason), reason.ToString());
    internal static SymbolicUnknownReasonInfo ForRuntimeHazard(
        SymbolicRuntimeHazardStatus status,
        string? rawReason,
        SymbolicUnknownReason proofReason) {
        if (status is SymbolicRuntimeHazardStatus.Proven or SymbolicRuntimeHazardStatus.Unreachable)
            return Create(SymbolicUnknownReasonSource.RuntimeHazard, "runtime_hazard",
                new(SymbolicUnknownReasonCategory.None, "none"), rawReason);
        if (Contains(rawReason, "unsupported_typed_projection"))
            return Create(SymbolicUnknownReasonSource.RuntimeHazard, "runtime_hazard",
                new(SymbolicUnknownReasonCategory.UnsupportedOperation, "unsupported_typed_projection"), rawReason);
        if (proofReason is not (SymbolicUnknownReason.None or SymbolicUnknownReason.Unknown))
            return ChangeSource(ForProof(proofReason, rawReason), SymbolicUnknownReasonSource.RuntimeHazard, "runtime_hazard");
        var unsupported = status == SymbolicRuntimeHazardStatus.Unsupported;
        return Create(SymbolicUnknownReasonSource.RuntimeHazard, "runtime_hazard",
            new(unsupported ? SymbolicUnknownReasonCategory.UnsupportedOperation : SymbolicUnknownReasonCategory.Unknown,
                unsupported ? "unsupported" : "unknown"), rawReason);
    }
    private static ReasonDescriptor Describe(SymbolicUnknownReason reason) => reason switch {
        SymbolicUnknownReason.None => new(SymbolicUnknownReasonCategory.None, "none"),
        SymbolicUnknownReason.UnsupportedIrEncoding => new(SymbolicUnknownReasonCategory.UnsupportedSyntax, "unsupported_ir_encoding"),
        SymbolicUnknownReason.SmtDisabled => new(SymbolicUnknownReasonCategory.SolverDisabled, "solver_disabled",
            IsConfigurationRelated: true),
        SymbolicUnknownReason.SmtUnavailable => new(SymbolicUnknownReasonCategory.NativeSolverFailure, "native_solver_failure", true),
        SymbolicUnknownReason.Timeout => new(SymbolicUnknownReasonCategory.SolverTimeout, "solver_timeout", true, true),
        SymbolicUnknownReason.MethodBudgetExceeded => Budget("solver_method_budget"),
        SymbolicUnknownReason.PathConditionBudgetExceeded => Budget("solver_path_condition_budget"),
        SymbolicUnknownReason.ExpressionBudgetExceeded => Budget("solver_expression_budget"),
        SymbolicUnknownReason.CancellationRequested => new(SymbolicUnknownReasonCategory.Cancellation, "canceled", true),
        SymbolicUnknownReason.EncodingFailure => new(SymbolicUnknownReasonCategory.SolverEncodingFailure, "solver_encoding_failure"),
        _ => new(SymbolicUnknownReasonCategory.Unknown, "unknown")
    };
    private static ReasonDescriptor Describe(SymbolicComplexityUnknownReason reason) => reason switch {
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
    private static SymbolicUnknownReasonInfo ChangeSource(SymbolicUnknownReasonInfo info, SymbolicUnknownReasonSource source,
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
    readonly record struct ReasonDescriptor(
        SymbolicUnknownReasonCategory Category,
        string Suffix,
        bool IsRetryable = false,
        bool IsConfigurationRelated = false);
}

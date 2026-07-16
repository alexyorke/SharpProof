using Microsoft.CodeAnalysis;

namespace SharpProof.Symbolic;

public sealed class SymbolicAnalysisLimits
{
    public static readonly SymbolicAnalysisLimits Default = new();

    public SymbolicAnalysisLimits(
        int maxMergedIfElseFacts = 16,
        int maxMergedSwitchFacts = 32,
        int maxMergedTryFacts = 16,
        int maxTryCompletionBranches = 8,
        int maxFiniteForeachElementFacts = 8,
        int maxScopedBlockCompletionStatements = 32,
        int maxStructuralNullStateDepth = 4,
        int maxMergedPathConditions = 32,
        int maxMergeableFactsPerTargetPerState = 4,
        int maxFactChoiceCombinationsPerTarget = 64,
        int maxGuardFactsPerTargetPerState = 6)
    {
        MaxMergedIfElseFacts = ValidatePositive(maxMergedIfElseFacts, nameof(maxMergedIfElseFacts));
        MaxMergedSwitchFacts = ValidatePositive(maxMergedSwitchFacts, nameof(maxMergedSwitchFacts));
        MaxMergedTryFacts = ValidatePositive(maxMergedTryFacts, nameof(maxMergedTryFacts));
        MaxTryCompletionBranches = ValidatePositive(maxTryCompletionBranches, nameof(maxTryCompletionBranches));
        MaxFiniteForeachElementFacts = ValidatePositive(
            maxFiniteForeachElementFacts,
            nameof(maxFiniteForeachElementFacts));
        MaxScopedBlockCompletionStatements = ValidatePositive(
            maxScopedBlockCompletionStatements,
            nameof(maxScopedBlockCompletionStatements));
        MaxStructuralNullStateDepth = ValidatePositive(
            maxStructuralNullStateDepth,
            nameof(maxStructuralNullStateDepth));
        MaxMergedPathConditions = ValidatePositive(maxMergedPathConditions, nameof(maxMergedPathConditions));
        MaxMergeableFactsPerTargetPerState = ValidatePositive(
            maxMergeableFactsPerTargetPerState,
            nameof(maxMergeableFactsPerTargetPerState));
        MaxFactChoiceCombinationsPerTarget = ValidatePositive(
            maxFactChoiceCombinationsPerTarget,
            nameof(maxFactChoiceCombinationsPerTarget));
        MaxGuardFactsPerTargetPerState = ValidatePositive(
            maxGuardFactsPerTargetPerState,
            nameof(maxGuardFactsPerTargetPerState));
    }

    public int MaxMergedIfElseFacts { get; }

    public int MaxMergedSwitchFacts { get; }

    public int MaxMergedTryFacts { get; }

    public int MaxTryCompletionBranches { get; }

    public int MaxFiniteForeachElementFacts { get; }

    public int MaxScopedBlockCompletionStatements { get; }

    public int MaxStructuralNullStateDepth { get; }

    public int MaxMergedPathConditions { get; }

    public int MaxMergeableFactsPerTargetPerState { get; }

    public int MaxFactChoiceCombinationsPerTarget { get; }

    public int MaxGuardFactsPerTargetPerState { get; }

    public SymbolicAnalysisLimits WithOverrides(
        int? maxMergedIfElseFacts = null,
        int? maxMergedSwitchFacts = null,
        int? maxMergedTryFacts = null,
        int? maxTryCompletionBranches = null,
        int? maxFiniteForeachElementFacts = null,
        int? maxScopedBlockCompletionStatements = null,
        int? maxStructuralNullStateDepth = null,
        int? maxMergedPathConditions = null,
        int? maxMergeableFactsPerTargetPerState = null,
        int? maxFactChoiceCombinationsPerTarget = null,
        int? maxGuardFactsPerTargetPerState = null)
    {
        return new SymbolicAnalysisLimits(
            maxMergedIfElseFacts ?? MaxMergedIfElseFacts,
            maxMergedSwitchFacts ?? MaxMergedSwitchFacts,
            maxMergedTryFacts ?? MaxMergedTryFacts,
            maxTryCompletionBranches ?? MaxTryCompletionBranches,
            maxFiniteForeachElementFacts ?? MaxFiniteForeachElementFacts,
            maxScopedBlockCompletionStatements ?? MaxScopedBlockCompletionStatements,
            maxStructuralNullStateDepth ?? MaxStructuralNullStateDepth,
            maxMergedPathConditions ?? MaxMergedPathConditions,
            maxMergeableFactsPerTargetPerState ?? MaxMergeableFactsPerTargetPerState,
            maxFactChoiceCombinationsPerTarget ?? MaxFactChoiceCombinationsPerTarget,
            maxGuardFactsPerTargetPerState ?? MaxGuardFactsPerTargetPerState);
    }

    private static int ValidatePositive(int value, string parameterName)
    {
        if (value <= 0) throw new ArgumentOutOfRangeException(parameterName, "Analysis limits must be positive.");

        return value;
    }
}

public enum SymbolicAnalysisLimitKind
{
    IfElseFactMerge,
    SwitchFactMerge,
    TryFactMerge,
    TryCompletionBranches,
    ForeachElementFacts,
    ScopedBlockCompletionStatements,
    StructuralNullStateDepth,
    MergedPathConditions,
    MergeableFactsPerTargetPerState,
    FactChoiceCombinationsPerTarget,
    GuardFactsPerTargetPerState
}

public sealed class SymbolicAnalysisTruncationEvent
{
    internal SymbolicAnalysisTruncationEvent(
        SymbolicAnalysisLimitKind kind,
        int limit,
        int observed,
        string provenance,
        int? sourceSpanStart)
    {
        Kind = kind;
        Code = GetCode(kind);
        Limit = limit;
        Observed = observed;
        Provenance = provenance ?? string.Empty;
        SourceSpanStart = sourceSpanStart;
    }

    public SymbolicAnalysisLimitKind Kind { get; }

    public string Code { get; }

    public int Limit { get; }

    public int Observed { get; }

    public string Provenance { get; }

    public int? SourceSpanStart { get; }

    private static string GetCode(SymbolicAnalysisLimitKind kind)
    {
        return kind switch
        {
            SymbolicAnalysisLimitKind.IfElseFactMerge => "analysis_limit.if_else_fact_merge",
            SymbolicAnalysisLimitKind.SwitchFactMerge => "analysis_limit.switch_fact_merge",
            SymbolicAnalysisLimitKind.TryFactMerge => "analysis_limit.try_fact_merge",
            SymbolicAnalysisLimitKind.TryCompletionBranches => "analysis_limit.try_completion_branches",
            SymbolicAnalysisLimitKind.ForeachElementFacts => "analysis_limit.foreach_element_facts",
            SymbolicAnalysisLimitKind.ScopedBlockCompletionStatements =>
                "analysis_limit.scoped_block_completion_statements",
            SymbolicAnalysisLimitKind.StructuralNullStateDepth =>
                "analysis_limit.structural_null_state_depth",
            SymbolicAnalysisLimitKind.MergedPathConditions => "analysis_limit.merged_path_conditions",
            SymbolicAnalysisLimitKind.MergeableFactsPerTargetPerState =>
                "analysis_limit.mergeable_facts_per_target_per_state",
            SymbolicAnalysisLimitKind.FactChoiceCombinationsPerTarget =>
                "analysis_limit.fact_choice_combinations_per_target",
            SymbolicAnalysisLimitKind.GuardFactsPerTargetPerState =>
                "analysis_limit.guard_facts_per_target_per_state",
            _ => "analysis_limit.unknown"
        };
    }
}

public sealed class SymbolicAnalysisTruncationInfo
{
    public static readonly SymbolicAnalysisTruncationInfo None = new(
        Array.Empty<SymbolicAnalysisTruncationEvent>());

    internal SymbolicAnalysisTruncationInfo(IReadOnlyList<SymbolicAnalysisTruncationEvent> events)
    {
        Events = events ?? Array.Empty<SymbolicAnalysisTruncationEvent>();
    }

    public bool IsTruncated => Events.Count != 0;

    public IReadOnlyList<SymbolicAnalysisTruncationEvent> Events { get; }

    internal static SymbolicAnalysisTruncationInfo Combine(
        IEnumerable<SymbolicAnalysisTruncationInfo> truncations)
    {
        if (truncations == null) throw new ArgumentNullException(nameof(truncations));

        var events = new SymbolicAnalysisTruncationEventAccumulator();
        foreach (var truncation in truncations)
        {
            if (truncation == null) continue;

            foreach (var item in truncation.Events) events.Add(item);
        }

        return events.ToInfo();
    }
}

internal static class SymbolicAnalysisLimitContext
{
    private static readonly AsyncLocal<Scope?> CurrentScope = new();

    internal static SymbolicAnalysisLimits Limits => CurrentScope.Value?.Limits ?? SymbolicAnalysisLimits.Default;

    internal static Scope Push(SymbolicAnalysisLimits? limits = null, SyntaxNode? sourceNode = null)
        => Push(limits, sourceNode, propagateEvents: true);

    internal static Scope PushIsolated(SymbolicAnalysisLimits? limits = null, SyntaxNode? sourceNode = null)
        => Push(limits, sourceNode, propagateEvents: false);

    private static Scope Push(
        SymbolicAnalysisLimits? limits,
        SyntaxNode? sourceNode,
        bool propagateEvents)
    {
        var parent = CurrentScope.Value;
        var scope = new Scope(
            parent,
            limits ?? parent?.Limits ?? SymbolicAnalysisLimits.Default,
            sourceNode?.SpanStart ?? parent?.DefaultSourceSpanStart,
            propagateEvents);
        CurrentScope.Value = scope;
        return scope;
    }

    internal static void Record(
        SymbolicAnalysisLimitKind kind,
        int limit,
        int observed,
        SyntaxNode? sourceNode,
        string provenance)
    {
        CurrentScope.Value?.Record(kind, limit, observed, sourceNode, provenance);
    }

    internal static bool CanAddMergedSwitchFact(
        int addedCount,
        SyntaxNode sourceNode,
        string provenance)
    {
        var limit = Limits.MaxMergedSwitchFacts;
        if (addedCount < limit) return true;

        Record(
            SymbolicAnalysisLimitKind.SwitchFactMerge,
            limit,
            addedCount + 1,
            sourceNode,
            provenance);
        return false;
    }

    internal sealed class Scope : IDisposable
    {
        private readonly SymbolicAnalysisTruncationEventAccumulator _events = new();
        private readonly Scope? _parent;
        private readonly bool _propagateEvents;
        private bool _disposed;

        internal Scope(
            Scope? parent,
            SymbolicAnalysisLimits limits,
            int? defaultSourceSpanStart,
            bool propagateEvents)
        {
            _parent = parent;
            _propagateEvents = propagateEvents;
            Limits = limits;
            DefaultSourceSpanStart = defaultSourceSpanStart;
        }

        internal SymbolicAnalysisLimits Limits { get; }

        internal int? DefaultSourceSpanStart { get; }

        internal SymbolicAnalysisTruncationInfo Snapshot()
        {
            return _events.ToInfo();
        }

        internal void Record(
            SymbolicAnalysisLimitKind kind,
            int limit,
            int observed,
            SyntaxNode? sourceNode,
            string provenance)
        {
            Record(kind, limit, observed, sourceNode?.SpanStart ?? DefaultSourceSpanStart, provenance);
        }

        private void Record(
            SymbolicAnalysisLimitKind kind,
            int limit,
            int observed,
            int? sourceSpanStart,
            string provenance)
        {
            _events.Add(new SymbolicAnalysisTruncationEvent(
                kind,
                limit,
                observed,
                provenance,
                sourceSpanStart));
        }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            CurrentScope.Value = _parent;
            if (_parent == null || !_propagateEvents) return;

            foreach (var item in _events.Events)
                _parent.Record(
                    item.Kind,
                    item.Limit,
                    item.Observed,
                    item.SourceSpanStart,
                    item.Provenance);
        }
    }
}

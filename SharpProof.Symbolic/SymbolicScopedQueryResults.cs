using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using SharpProof.ProofCore.Smt;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Symbolic;

internal sealed class SymbolicFileQuery
{
    public SymbolicFileQuery(
        string filePath,
        int line,
        int column = 1,
        IEnumerable<MetadataReference>? references = null,
        IEnumerable<string>? impliedConditions = null)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path is required.", nameof(filePath));

        if (line <= 0) throw new ArgumentOutOfRangeException(nameof(line), "Line must be positive.");

        if (column <= 0) throw new ArgumentOutOfRangeException(nameof(column), "Column must be positive.");

        FilePath = filePath;
        Line = line;
        Column = column;
        References = SymbolicQueryOptionHelpers.NormalizeReferences(references, nameof(references));
        ImpliedConditions = impliedConditions?
            .Where(static condition => !string.IsNullOrWhiteSpace(condition))
            .ToImmutableArray() ?? ImmutableArray<string>.Empty;
    }

    public string FilePath { get; }

    public int Line { get; }

    public int Column { get; }

    public ImmutableArray<MetadataReference> References { get; }

    public ImmutableArray<string> ImpliedConditions { get; }
}

internal abstract class SymbolicScopedQueryAggregate
{
    protected SymbolicScopedQueryAggregate(
        IReadOnlyList<SymbolicProgramPointResult> programPoints,
        SymbolicSmtDiagnostics? smtDiagnostics)
    {
        ProgramPoints = programPoints ?? throw new ArgumentNullException(nameof(programPoints));
        AnalysisTruncation = SymbolicAnalysisTruncationInfo.Combine(
            ProgramPoints.Select(static point => point.AnalysisTruncation));
        var factSummary = SymbolicInvariantService.MergeInvariantFacts(
            ProgramPoints.Select(static point => point.Facts));
        Facts = factSummary.Facts;
        ObservedFactCount = Facts.Count;
        ObservedInvariant = SymbolicInvariantResult.FromFacts(Facts, factSummary.MergedInvariantText);
        MergedPathFacts = SymbolicMergedPathFacts.FromProgramPoints(ProgramPoints);
        MergedInvariantText = MergedPathFacts.MergedInvariantText;
        MergedInvariant = SymbolicInvariantResult.FromMergedPathFacts(MergedPathFacts);
        ProgramPointSummary = SymbolicProgramPointSummary.FromProgramPoints(ProgramPoints);
        Reachability = ProgramPointSummary.Reachability;
        ConditionProofs = SymbolicConditionProofSummary.FromProgramPoints(ProgramPoints);
        SymbolicFacts = SymbolicFactInfo.Distinct(ProgramPoints.SelectMany(static point => point.SymbolicFacts));
        InvariantInfo = new SymbolicInvariantInfo(
            MergedInvariantText,
            SymbolicFacts,
            ConditionProofs.Select(static proof => proof.Proof).ToArray(),
            MergedInvariant.MergeKind,
            MergedInvariant.ConditionCount);
        SmtDiagnostics = smtDiagnostics ?? SymbolicSmtDiagnostics.NotConfigured;
        InvariantQuery = SymbolicInvariantQueryView.FromMergedPathFacts(
            MergedInvariant,
            MergedPathFacts,
            Reachability,
            ProgramPointSummary.ProofOutcomes,
            SmtDiagnostics,
            ProgramPoints);
        ReachabilityWitnesses = ProgramPoints.Select(static point => point.ReachabilityWitness).ToArray();
        InputDomainSummary = SymbolicInputWitnessFactory.MergeAlternatives(ReachabilityWitnesses);
    }

    public IReadOnlyList<SymbolicProgramPointResult> ProgramPoints { get; }
    public int ProgramPointCount => ProgramPoints.Count;
    public SymbolicAnalysisTruncationInfo AnalysisTruncation { get; }
    public IReadOnlyList<string> Facts { get; }
    public int ObservedFactCount { get; }
    public SymbolicInvariantResult ObservedInvariant { get; }
    public SymbolicMergedPathFacts MergedPathFacts { get; }
    public string MergedInvariantText { get; }
    internal SymbolicInvariantResult MergedInvariant { get; }
    public IReadOnlyList<SymbolicFactInfo> SymbolicFacts { get; }
    public SymbolicInvariantInfo InvariantInfo { get; }
    public SymbolicProgramPointSummary ProgramPointSummary { get; }
    public SymbolicReachabilitySummary Reachability { get; }
    public IReadOnlyList<SymbolicConditionProofSummary> ConditionProofs { get; }
    public SymbolicSmtDiagnostics SmtDiagnostics { get; }
    public SymbolicInvariantQueryView InvariantQuery { get; }
    public IReadOnlyList<SymbolicInputWitness> ReachabilityWitnesses { get; }
    public SymbolicInputDomainSummary InputDomainSummary { get; }
}

internal sealed class SymbolicLineQueryResult : SymbolicScopedQueryAggregate
{
    internal SymbolicLineQueryResult(
        string filePath,
        int line,
        IReadOnlyList<SymbolicProgramPointResult> programPoints,
        SymbolicSmtDiagnostics? smtDiagnostics = null)
        : base(programPoints, smtDiagnostics)
    {
        FilePath = filePath;
        Line = line;
    }

    public string FilePath { get; }

    public int Line { get; }

    public SymbolicLineQueryResult Filter(SymbolicSourceQueryFilter filter)
    {
        if (filter == null) throw new ArgumentNullException(nameof(filter));

        return new SymbolicLineQueryResult(
            FilePath,
            Line,
            ProgramPoints.Where(filter.Matches).ToArray(),
            SmtDiagnostics);
    }
}

internal sealed class SymbolicSpanQueryResult : SymbolicScopedQueryAggregate
{
    internal SymbolicSpanQueryResult(
        string filePath,
        int spanStart,
        int spanEnd,
        int startLine,
        int startColumn,
        int endLine,
        int endColumn,
        IReadOnlyList<SymbolicProgramPointResult> programPoints,
        SymbolicSmtDiagnostics? smtDiagnostics = null)
        : base(Validate(spanStart, spanEnd, programPoints), smtDiagnostics)
    {
        FilePath = filePath;
        SpanStart = spanStart;
        SpanEnd = spanEnd;
        SpanLength = spanEnd - spanStart;
        StartLine = startLine;
        StartColumn = startColumn;
        EndLine = endLine;
        EndColumn = endColumn;
        LinesWithProgramPoints = ProgramPoints
            .Select(static point => point.Line)
            .Distinct()
            .Count();
    }

    public string FilePath { get; }

    public int SpanStart { get; }

    public int SpanEnd { get; }

    public int SpanLength { get; }

    public int StartLine { get; }

    public int StartColumn { get; }

    public int EndLine { get; }

    public int EndColumn { get; }

    public int LinesWithProgramPoints { get; }

    public SymbolicSpanQueryResult Filter(SymbolicSourceQueryFilter filter)
    {
        if (filter == null) throw new ArgumentNullException(nameof(filter));

        return new SymbolicSpanQueryResult(
            FilePath,
            SpanStart,
            SpanEnd,
            StartLine,
            StartColumn,
            EndLine,
            EndColumn,
            ProgramPoints.Where(filter.Matches).ToArray(),
            SmtDiagnostics);
    }

    private static IReadOnlyList<SymbolicProgramPointResult> Validate(
        int spanStart,
        int spanEnd,
        IReadOnlyList<SymbolicProgramPointResult> programPoints)
    {
        if (spanStart < 0) throw new ArgumentOutOfRangeException(nameof(spanStart), "Span start cannot be negative.");
        if (spanEnd < spanStart)
            throw new ArgumentOutOfRangeException(nameof(spanEnd), "Span end cannot be less than span start.");
        return programPoints ?? throw new ArgumentNullException(nameof(programPoints));
    }
}

internal sealed class SymbolicFileQueryResult : SymbolicScopedQueryAggregate
{
    internal SymbolicFileQueryResult(
        string filePath,
        int lineCount,
        IReadOnlyList<SymbolicLineQueryResult> lines,
        SymbolicSmtDiagnostics? smtDiagnostics = null)
        : base(GetProgramPoints(lineCount, lines), smtDiagnostics)
    {
        FilePath = filePath;
        LineCount = lineCount;
        Lines = lines;
        LinesWithProgramPoints = Lines.Count;
    }

    public string FilePath { get; }

    public int LineCount { get; }

    public int LinesWithProgramPoints { get; }

    public IReadOnlyList<SymbolicLineQueryResult> Lines { get; }

    public IReadOnlyList<string> ObservedFacts => Facts;

    public SymbolicFileQueryResult Filter(SymbolicSourceQueryFilter filter)
    {
        if (filter == null) throw new ArgumentNullException(nameof(filter));

        var lines = Lines
            .Select(line => line.Filter(filter))
            .Where(static line => line.ProgramPoints.Count != 0)
            .ToArray();
        return new SymbolicFileQueryResult(
            FilePath,
            LineCount,
            lines,
            SmtDiagnostics);
    }

    private static IReadOnlyList<SymbolicProgramPointResult> GetProgramPoints(
        int lineCount,
        IReadOnlyList<SymbolicLineQueryResult> lines)
    {
        if (lineCount < 0) throw new ArgumentOutOfRangeException(nameof(lineCount), "Line count cannot be negative.");
        if (lines == null) throw new ArgumentNullException(nameof(lines));
        return lines.SelectMany(static line => line.ProgramPoints).ToArray();
    }
}

public sealed class SymbolicInvariantQueryView
{
    private SymbolicInvariantQueryView(
        string text,
        SymbolicInvariantMergeKind mergeKind,
        IReadOnlyList<string> mustFacts,
        IReadOnlyList<string> maybeFacts,
        IReadOnlyList<string> unknownFacts,
        IReadOnlyList<SymbolicConservativeUnknownDiagnostic> unknownDiagnostics,
        IReadOnlyList<SymbolicInvariantTargetSummary> targetSummaries,
        IReadOnlyList<SymbolicInvariantTargetPathSummary> targetPathSummaries,
        int candidateProgramPointCount,
        int unreachableProgramPointCount,
        bool isUnreachable,
        SymbolicReachabilitySummary reachability,
        SymbolicProofOutcomeSummary proofOutcomes,
        SymbolicSmtDiagnostics smtDiagnostics)
    {
        Text = text ?? string.Empty;
        MergeKind = mergeKind;
        MustFacts = mustFacts ?? throw new ArgumentNullException(nameof(mustFacts));
        MaybeFacts = maybeFacts ?? throw new ArgumentNullException(nameof(maybeFacts));
        UnknownFacts = unknownFacts ?? throw new ArgumentNullException(nameof(unknownFacts));
        UnknownDiagnostics = unknownDiagnostics ?? throw new ArgumentNullException(nameof(unknownDiagnostics));
        TargetSummaries = targetSummaries ?? throw new ArgumentNullException(nameof(targetSummaries));
        TargetPathSummaries = targetPathSummaries ?? throw new ArgumentNullException(nameof(targetPathSummaries));
        CandidateProgramPointCount = candidateProgramPointCount;
        UnreachableProgramPointCount = unreachableProgramPointCount;
        IsUnreachable = isUnreachable;
        Reachability = reachability ?? throw new ArgumentNullException(nameof(reachability));
        ProofOutcomes = proofOutcomes ?? throw new ArgumentNullException(nameof(proofOutcomes));
        SmtDiagnostics = smtDiagnostics ?? SymbolicSmtDiagnostics.NotConfigured;
        Status = ResolveStatus();
        StatusReason = ResolveStatusReason();
        Summary = CreateSummary();
        Diagnostics = CreateDiagnostics();
    }

    public string Text { get; }

    public SymbolicInvariantMergeKind MergeKind { get; }

    public IReadOnlyList<string> MustFacts { get; }

    public int MustFactCount => MustFacts.Count;

    public IReadOnlyList<string> MaybeFacts { get; }

    public int MaybeFactCount => MaybeFacts.Count;

    public IReadOnlyList<string> UnknownFacts { get; }

    public int UnknownFactCount => UnknownFacts.Count;

    public IReadOnlyList<SymbolicConservativeUnknownDiagnostic> UnknownDiagnostics { get; }

    public IReadOnlyList<SymbolicInvariantTargetSummary> TargetSummaries { get; }

    public int TargetSummaryCount => TargetSummaries.Count;

    public IReadOnlyList<SymbolicInvariantTargetPathSummary> TargetPathSummaries { get; }

    public int TargetPathSummaryCount => TargetPathSummaries.Count;

    public int CandidateProgramPointCount { get; }

    public int UnreachableProgramPointCount { get; }

    public bool IsUnreachable { get; }

    public SymbolicReachabilitySummary Reachability { get; }

    public SymbolicProofOutcomeSummary ProofOutcomes { get; }

    public SymbolicSmtDiagnostics SmtDiagnostics { get; }

    public SymbolicInvariantQueryStatus Status { get; }

    public string StatusReason { get; }

    public string Summary { get; }

    public IReadOnlyList<SymbolicInvariantQueryDiagnostic> Diagnostics { get; }

    public int DiagnosticCount => Diagnostics.Count;

    public bool HasUnknowns => UnknownFacts.Count != 0;

    public bool HasMaybeFacts => MaybeFacts.Count != 0;

    public bool HasUnresolvedAnalysis =>
        HasUnknowns ||
        Reachability.UnknownCount != 0 ||
        Reachability.NotCheckedCount != 0 ||
        ProofOutcomes.UnknownCount != 0;

    public static SymbolicInvariantQueryView FromPoint(SymbolicProgramPointResult result)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));

        var reachability = SymbolicReachabilitySummary.FromProgramPoints(new[] { result });
        return new SymbolicInvariantQueryView(
            result.MergedInvariantText,
            result.Invariant.MergeKind,
            result.Invariant.Conditions.Select(static condition => condition.Text).ToArray(),
            Array.Empty<string>(),
            result.Invariant.Conditions
                .Where(static condition => condition.IsConservativeUnknown)
                .Select(static condition => condition.Text)
                .ToArray(),
            Array.Empty<SymbolicConservativeUnknownDiagnostic>(),
            SymbolicInvariantTargetSummary.FromPoint(result),
            SymbolicInvariantTargetPathSummary.FromProgramPoints(new[] { result }),
            result.Reachability == SymbolicReachability.Unreachable ? 0 : 1,
            result.Reachability == SymbolicReachability.Unreachable ? 1 : 0,
            result.Reachability == SymbolicReachability.Unreachable,
            reachability,
            result.ProofOutcomes,
            result.SmtDiagnostics);
    }

    public static SymbolicInvariantQueryView FromMergedPathFacts(
        SymbolicInvariantResult invariant,
        SymbolicMergedPathFacts mergedPathFacts,
        SymbolicReachabilitySummary reachability,
        SymbolicProofOutcomeSummary proofOutcomes,
        SymbolicSmtDiagnostics smtDiagnostics,
        IEnumerable<SymbolicProgramPointResult>? programPoints = null)
    {
        if (invariant == null) throw new ArgumentNullException(nameof(invariant));

        if (mergedPathFacts == null) throw new ArgumentNullException(nameof(mergedPathFacts));

        return new SymbolicInvariantQueryView(
            mergedPathFacts.MergedInvariantText,
            invariant.MergeKind,
            mergedPathFacts.AlwaysFacts,
            mergedPathFacts.MaybeFacts,
            mergedPathFacts.ConservativeUnknowns,
            mergedPathFacts.ConservativeUnknownDiagnostics,
            SymbolicInvariantTargetSummary.FromMergedPathFacts(invariant, mergedPathFacts),
            SymbolicInvariantTargetPathSummary.FromProgramPoints(programPoints ??
                                                                 Array.Empty<SymbolicProgramPointResult>()),
            mergedPathFacts.CandidateProgramPointCount,
            mergedPathFacts.UnreachableProgramPointCount,
            mergedPathFacts.IsUnreachable,
            reachability,
            proofOutcomes,
            smtDiagnostics);
    }

    private SymbolicInvariantQueryStatus ResolveStatus()
    {
        if (IsUnreachable) return SymbolicInvariantQueryStatus.Unreachable;

        if (Reachability.UnknownCount != 0 ||
            Reachability.NotCheckedCount != 0 ||
            ProofOutcomes.UnknownCount != 0 ||
            (SmtDiagnostics.IsConfigured && !SmtDiagnostics.IsEnabled))
            return SymbolicInvariantQueryStatus.Unresolved;

        if (HasMaybeFacts || HasUnknowns) return SymbolicInvariantQueryStatus.Conservative;

        return SymbolicInvariantQueryStatus.Exact;
    }

    private string ResolveStatusReason()
    {
        if (IsUnreachable) return "all_candidate_program_points_unreachable";

        if (Reachability.UnknownCount != 0 || Reachability.NotCheckedCount != 0)
            return "reachability_not_fully_resolved";

        if (ProofOutcomes.UnknownCount != 0) return "proofs_not_fully_resolved";

        if (SmtDiagnostics.IsConfigured && !SmtDiagnostics.IsEnabled) return "smt_disabled";

        if (HasUnknowns) return "path_varying_targets";

        if (HasMaybeFacts) return "path_specific_facts";

        return "all_candidate_program_points_exact";
    }

    private string CreateSummary()
    {
        switch (Status)
        {
            case SymbolicInvariantQueryStatus.Unreachable:
                return "No reachable candidate program points were found for this query.";
            case SymbolicInvariantQueryStatus.Unresolved:
                return "Invariant query has unresolved reachability, proof, or SMT diagnostics.";
            case SymbolicInvariantQueryStatus.Conservative:
                return
                    "Invariant query merged multiple reachable paths and includes conservative unknowns or maybe facts.";
            default:
                return "Invariant query is exact for the selected reachable program points.";
        }
    }

    private IReadOnlyList<SymbolicInvariantQueryDiagnostic> CreateDiagnostics()
    {
        var diagnostics = new List<SymbolicInvariantQueryDiagnostic>();
        if (IsUnreachable)
            diagnostics.Add(SymbolicInvariantQueryDiagnostic.Create(
                "SP-SYM-UNREACHABLE",
                "Info",
                "No reachable candidate program points contributed invariant facts.",
                UnreachableProgramPointCount,
                new[]
                {
                    "UnreachableProgramPoints=" + UnreachableProgramPointCount.ToString(CultureInfo.InvariantCulture)
                }));

        if (MaybeFacts.Count != 0)
            diagnostics.Add(SymbolicInvariantQueryDiagnostic.Create(
                "SP-SYM-MAYBE-FACTS",
                "Info",
                "Some path facts are present on only a subset of candidate program points.",
                MaybeFacts.Count,
                MaybeFacts));

        if (UnknownFacts.Count != 0)
            diagnostics.Add(SymbolicInvariantQueryDiagnostic.Create(
                "SP-SYM-CONSERVATIVE-UNKNOWN",
                "Warning",
                "The merged invariant contains conservative unknown placeholders for path-varying targets.",
                UnknownFacts.Count,
                UnknownFacts));

        if (Reachability.UnknownCount != 0 || Reachability.NotCheckedCount != 0)
            diagnostics.Add(SymbolicInvariantQueryDiagnostic.Create(
                "SP-SYM-REACHABILITY",
                "Warning",
                "Some program point reachability checks are unknown or were not requested.",
                Reachability.UnknownCount + Reachability.NotCheckedCount,
                new[]
                {
                    "Unknown=" + Reachability.UnknownCount.ToString(CultureInfo.InvariantCulture),
                    "NotChecked=" + Reachability.NotCheckedCount.ToString(CultureInfo.InvariantCulture)
                }));

        if (ProofOutcomes.UnknownCount != 0)
            diagnostics.Add(SymbolicInvariantQueryDiagnostic.Create(
                "SP-SYM-PROOF-UNKNOWN",
                "Warning",
                "Some requested implication proofs were not resolved by bounded SMT.",
                ProofOutcomes.UnknownCount,
                new[] { "UnknownProofs=" + ProofOutcomes.UnknownCount.ToString(CultureInfo.InvariantCulture) }));

        if (SmtDiagnostics.IsConfigured && !SmtDiagnostics.IsEnabled)
            diagnostics.Add(SymbolicInvariantQueryDiagnostic.Create(
                "SP-SYM-SMT-DISABLED",
                "Warning",
                "SMT is configured but disabled, so solver-backed reachability and implication proofs are conservative.",
                1,
                new[] { "Mode=" + SmtDiagnostics.Mode }));

        return diagnostics;
    }
}

public sealed class SymbolicInvariantTargetSummary
{
    private SymbolicInvariantTargetSummary(
        string target,
        IReadOnlyList<string> mustFacts,
        IReadOnlyList<string> maybeFacts,
        IReadOnlyList<string> unknownFacts)
    {
        Target = string.IsNullOrWhiteSpace(target) ? "path" : target;
        MustFacts = mustFacts ?? throw new ArgumentNullException(nameof(mustFacts));
        MaybeFacts = maybeFacts ?? throw new ArgumentNullException(nameof(maybeFacts));
        UnknownFacts = unknownFacts ?? throw new ArgumentNullException(nameof(unknownFacts));
        Status = ResolveStatus();
        StatusReason = ResolveStatusReason();
        ReasonCode = ResolveReasonCode();
        Summary = CreateSummary();
    }

    public string Target { get; }

    public IReadOnlyList<string> MustFacts { get; }

    public int MustFactCount => MustFacts.Count;

    public IReadOnlyList<string> MaybeFacts { get; }

    public int MaybeFactCount => MaybeFacts.Count;

    public IReadOnlyList<string> UnknownFacts { get; }

    public int UnknownFactCount => UnknownFacts.Count;

    public bool HasMaybeFacts => MaybeFactCount != 0;

    public bool HasUnknowns => UnknownFactCount != 0;

    public SymbolicInvariantQueryStatus Status { get; }

    public string StatusReason { get; }

    public string ReasonCode { get; }

    public string Summary { get; }

    internal static IReadOnlyList<SymbolicInvariantTargetSummary> FromPoint(SymbolicProgramPointResult result)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));

        var builders = new Dictionary<string, TargetFactBuilder>(StringComparer.Ordinal);
        foreach (var condition in result.Invariant.Conditions) AddCondition(builders, condition, false);

        return BuildSummaries(builders);
    }

    internal static IReadOnlyList<SymbolicInvariantTargetSummary> FromMergedPathFacts(
        SymbolicInvariantResult invariant,
        SymbolicMergedPathFacts mergedPathFacts)
    {
        if (invariant == null) throw new ArgumentNullException(nameof(invariant));

        if (mergedPathFacts == null) throw new ArgumentNullException(nameof(mergedPathFacts));

        var builders = new Dictionary<string, TargetFactBuilder>(StringComparer.Ordinal);
        foreach (var condition in invariant.Conditions) AddCondition(builders, condition, false);

        foreach (var diagnostic in mergedPathFacts.ConservativeUnknownDiagnostics)
        {
            var builder = GetBuilder(builders, diagnostic.Target);
            builder.AddUnknown(diagnostic.UnknownText);
            foreach (var maybeFact in diagnostic.MaybeFacts) builder.AddMaybe(maybeFact);
        }

        return BuildSummaries(builders);
    }

    private SymbolicInvariantQueryStatus ResolveStatus()
    {
        return HasUnknowns || HasMaybeFacts
            ? SymbolicInvariantQueryStatus.Conservative
            : SymbolicInvariantQueryStatus.Exact;
    }

    private string ResolveStatusReason()
    {
        if (HasUnknowns) return "target_has_conservative_unknowns";

        if (HasMaybeFacts) return "target_has_path_specific_facts";

        return "target_exact";
    }

    private string ResolveReasonCode()
    {
        if (HasUnknowns) return "SP-SYM-TARGET-CONSERVATIVE-UNKNOWN";

        if (HasMaybeFacts) return "SP-SYM-TARGET-PATH-SPECIFIC";

        return "SP-SYM-TARGET-EXACT";
    }

    private string CreateSummary()
    {
        if (HasUnknowns)
            return
                "Facts for this target differ across selected paths; the merged invariant keeps a conservative unknown for the target.";

        if (HasMaybeFacts) return "Some facts for this target apply only to a subset of selected paths.";

        return "All selected reachable program points agree on the facts for this target.";
    }

    private static void AddCondition(
        Dictionary<string, TargetFactBuilder> builders,
        SymbolicInvariantCondition condition,
        bool isMaybe)
    {
        var builder = GetBuilder(builders, GetConditionTarget(condition));
        if (condition.IsConservativeUnknown)
            builder.AddUnknown(condition.Text);
        else if (isMaybe)
            builder.AddMaybe(condition.Text);
        else
            builder.AddMust(condition.Text);
    }

    private static string GetConditionTarget(SymbolicInvariantCondition condition)
    {
        if (string.Equals(condition.FormulaKind, "Text", StringComparison.Ordinal) &&
            string.Equals(condition.Target, condition.Text, StringComparison.Ordinal))
        {
            var extracted = TextFactTargetExtraction.TryExtract(condition.Text);
            if (!string.IsNullOrWhiteSpace(extracted)) return extracted!;
        }

        return condition.Target;
    }

    private static TargetFactBuilder GetBuilder(
        Dictionary<string, TargetFactBuilder> builders,
        string? target)
    {
        var normalizedTarget = string.IsNullOrWhiteSpace(target) ? "path" : target!.Trim();
        if (!builders.TryGetValue(normalizedTarget, out var builder))
        {
            builder = new TargetFactBuilder(normalizedTarget);
            builders.Add(normalizedTarget, builder);
        }

        return builder;
    }

    private static IReadOnlyList<SymbolicInvariantTargetSummary> BuildSummaries(
        Dictionary<string, TargetFactBuilder> builders)
    {
        return builders.Values
            .OrderBy(static builder => builder.Target, StringComparer.Ordinal)
            .Select(static builder => builder.ToSummary())
            .ToArray();
    }

    private sealed class TargetFactBuilder
    {
        private readonly List<string> _maybeFacts = new();
        private readonly List<string> _mustFacts = new();
        private readonly List<string> _unknownFacts = new();

        public TargetFactBuilder(string target)
        {
            Target = target;
        }

        public string Target { get; }

        public void AddMust(string? fact)
        {
            AddFact(_mustFacts, fact);
        }

        public void AddMaybe(string? fact)
        {
            AddFact(_maybeFacts, fact);
        }

        public void AddUnknown(string? fact)
        {
            AddFact(_unknownFacts, fact);
        }

        public SymbolicInvariantTargetSummary ToSummary()
        {
            return new SymbolicInvariantTargetSummary(
                Target,
                Distinct(_mustFacts),
                Distinct(_maybeFacts),
                Distinct(_unknownFacts));
        }

        private static void AddFact(List<string> facts, string? fact)
        {
            if (!string.IsNullOrWhiteSpace(fact)) facts.Add(fact!.Trim());
        }

        private static IReadOnlyList<string> Distinct(List<string> facts)
        {
            return facts
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }
    }
}

public sealed class SymbolicInvariantTargetPathSummary
{
    public const int DefaultMaxConditions = 8;

    private SymbolicInvariantTargetPathSummary(
        string target,
        int pathConditionCount,
        int smtConditionCount,
        int conservativeUnknownCount,
        int programPointCount,
        int reachableProgramPointCount,
        int proofTotalCount,
        int proofUnknownCount,
        int proofProvenTrueCount,
        int proofProvenFalseCount,
        int proofUnreachableCount,
        IReadOnlyList<string> conditions,
        bool conditionsTruncated)
    {
        Target = string.IsNullOrWhiteSpace(target) ? "path" : target;
        PathConditionCount = pathConditionCount;
        SmtConditionCount = smtConditionCount;
        ConservativeUnknownCount = conservativeUnknownCount;
        ProgramPointCount = programPointCount;
        ReachableProgramPointCount = reachableProgramPointCount;
        ProofTotalCount = proofTotalCount;
        ProofUnknownCount = proofUnknownCount;
        ProofProvenTrueCount = proofProvenTrueCount;
        ProofProvenFalseCount = proofProvenFalseCount;
        ProofUnreachableCount = proofUnreachableCount;
        Conditions = conditions ?? throw new ArgumentNullException(nameof(conditions));
        ConditionsTruncated = conditionsTruncated;
        StatusReason = ResolveStatusReason();
        ReasonCode = ResolveReasonCode();
        Summary = CreateSummary();
    }

    public string Target { get; }

    public int PathConditionCount { get; }

    public int SmtConditionCount { get; }

    public int ConservativeUnknownCount { get; }

    public int ProgramPointCount { get; }

    public int ReachableProgramPointCount { get; }

    public int ProofTotalCount { get; }

    public int ProofUnknownCount { get; }

    public int ProofProvenTrueCount { get; }

    public int ProofProvenFalseCount { get; }

    public int ProofUnreachableCount { get; }

    public IReadOnlyList<string> Conditions { get; }

    public bool ConditionsTruncated { get; }

    public bool HasPathConditions => PathConditionCount != 0;

    public bool HasProofs => ProofTotalCount != 0;

    public bool HasUnknownProofs => ProofUnknownCount != 0;

    public string StatusReason { get; }

    public string ReasonCode { get; }

    public string Summary { get; }

    internal static IReadOnlyList<SymbolicInvariantTargetPathSummary> FromProgramPoints(
        IEnumerable<SymbolicProgramPointResult> programPoints)
    {
        if (programPoints == null) throw new ArgumentNullException(nameof(programPoints));

        var builders = new Dictionary<string, TargetPathBuilder>(StringComparer.Ordinal);
        foreach (var point in programPoints)
        {
            if (point == null) continue;

            var pointTargets = new HashSet<string>(StringComparer.Ordinal);
            foreach (var condition in point.Invariant.Conditions)
            {
                var builder = GetBuilder(builders, condition.Target);
                builder.AddCondition(condition);
                pointTargets.Add(builder.Target);
            }

            foreach (var proof in point.ConditionProofs)
            {
                var builder = GetBuilder(builders, proof.Target);
                builder.AddProof(proof);
                pointTargets.Add(builder.Target);
            }

            foreach (var target in pointTargets) GetBuilder(builders, target).AddProgramPoint(point.Reachability);
        }

        return builders.Values
            .OrderBy(static builder => builder.Target, StringComparer.Ordinal)
            .Select(static builder => builder.ToSummary())
            .ToArray();
    }

    private string ResolveStatusReason()
    {
        if (ProofUnknownCount != 0) return "target_has_unknown_proofs";

        if (PathConditionCount != 0) return "target_has_path_conditions";

        if (ProofTotalCount != 0) return "target_has_proofs";

        return "target_has_no_path_conditions";
    }

    private string ResolveReasonCode()
    {
        if (ProofUnknownCount != 0) return "SP-SYM-TARGET-PROOF-UNKNOWN";

        if (PathConditionCount != 0) return "SP-SYM-TARGET-PATH-CONDITIONS";

        if (ProofTotalCount != 0) return "SP-SYM-TARGET-PROOFS";

        return "SP-SYM-TARGET-NO-PATH-CONDITIONS";
    }

    private string CreateSummary()
    {
        if (ProofUnknownCount != 0)
            return "This target has path facts or proof requests with unresolved bounded-SMT outcomes.";

        if (PathConditionCount != 0)
            return "This target has source-location path conditions available for invariant queries.";

        if (ProofTotalCount != 0)
            return "This target appears in proof requests, but no direct path conditions were recorded for it.";

        return "No path conditions or proof requests were recorded for this target.";
    }

    private static TargetPathBuilder GetBuilder(
        Dictionary<string, TargetPathBuilder> builders,
        string? target)
    {
        var normalizedTarget = string.IsNullOrWhiteSpace(target) ? "path" : target!.Trim();
        if (!builders.TryGetValue(normalizedTarget, out var builder))
        {
            builder = new TargetPathBuilder(normalizedTarget);
            builders.Add(normalizedTarget, builder);
        }

        return builder;
    }

    private sealed class TargetPathBuilder
    {
        private readonly List<string> _conditions = new();
        private int _conservativeUnknownCount;
        private int _pathConditionCount;
        private int _programPointCount;
        private int _proofProvenFalseCount;
        private int _proofProvenTrueCount;
        private int _proofTotalCount;
        private int _proofUnknownCount;
        private int _proofUnreachableCount;
        private int _reachableProgramPointCount;
        private int _smtConditionCount;

        public TargetPathBuilder(string target)
        {
            Target = target;
        }

        public string Target { get; }

        public void AddCondition(SymbolicInvariantCondition condition)
        {
            _pathConditionCount++;
            if (condition.IsSolverBacked) _smtConditionCount++;

            if (condition.IsConservativeUnknown) _conservativeUnknownCount++;

            if (_conditions.Count < DefaultMaxConditions &&
                !string.IsNullOrWhiteSpace(condition.Text) &&
                !_conditions.Contains(condition.Text, StringComparer.Ordinal))
                _conditions.Add(condition.Text);
        }

        public void AddProof(SymbolicConditionProofResult proof)
        {
            _proofTotalCount++;
            switch (proof.TruthValue)
            {
                case SymbolicTruthValue.Unknown:
                    _proofUnknownCount++;
                    break;
                case SymbolicTruthValue.ProvenTrue:
                    _proofProvenTrueCount++;
                    break;
                case SymbolicTruthValue.ProvenFalse:
                    _proofProvenFalseCount++;
                    break;
                case SymbolicTruthValue.Unreachable:
                    _proofUnreachableCount++;
                    break;
            }
        }

        public void AddProgramPoint(SymbolicReachability reachability)
        {
            _programPointCount++;
            if (reachability == SymbolicReachability.Reachable) _reachableProgramPointCount++;
        }

        public SymbolicInvariantTargetPathSummary ToSummary()
        {
            return new SymbolicInvariantTargetPathSummary(
                Target,
                _pathConditionCount,
                _smtConditionCount,
                _conservativeUnknownCount,
                _programPointCount,
                _reachableProgramPointCount,
                _proofTotalCount,
                _proofUnknownCount,
                _proofProvenTrueCount,
                _proofProvenFalseCount,
                _proofUnreachableCount,
                _conditions.ToArray(),
                _pathConditionCount > _conditions.Count);
        }
    }
}

public enum SymbolicInvariantQueryStatus
{
    Exact,
    Conservative,
    Unresolved,
    Unreachable
}

public sealed class SymbolicInvariantQueryDiagnostic
{
    public const int DefaultMaxEvidence = 8;

    private SymbolicInvariantQueryDiagnostic(
        string code,
        string severity,
        string message,
        int count,
        IReadOnlyList<string> evidence,
        int evidenceTotalCount,
        bool evidenceTruncated)
    {
        Code = code ?? string.Empty;
        Severity = string.IsNullOrWhiteSpace(severity) ? "Info" : severity;
        Message = message ?? string.Empty;
        Count = count;
        Evidence = evidence ?? throw new ArgumentNullException(nameof(evidence));
        EvidenceTotalCount = evidenceTotalCount;
        EvidenceTruncated = evidenceTruncated;
    }

    public string Code { get; }

    public string Severity { get; }

    public string Message { get; }

    public int Count { get; }

    public IReadOnlyList<string> Evidence { get; }

    public int EvidenceTotalCount { get; }

    public bool EvidenceTruncated { get; }

    internal static SymbolicInvariantQueryDiagnostic Create(
        string code,
        string severity,
        string message,
        int count,
        IEnumerable<string> evidence)
    {
        var evidenceArray = (evidence ?? Array.Empty<string>())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var evidenceProjection = SymbolicCompactProjection.Project(
            evidenceArray,
            DefaultMaxEvidence);
        return new SymbolicInvariantQueryDiagnostic(
            code,
            severity,
            message,
            count,
            evidenceProjection.Items,
            evidenceProjection.TotalCount,
            evidenceProjection.IsTruncated);
    }
}

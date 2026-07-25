namespace SharpProof.Symbolic;
internal sealed class SymbolicInvariantService {
    internal SymbolicProgramPointQueryContext Analyze(
        SemanticModel semanticModel,
        int position,
        SyntaxNode node,
        SmtAnalysisService? smtAnalysis,
        CancellationToken cancellationToken,
        bool includeCurrentStatementCompletionFacts = false,
        SymbolicState? initialState = null) =>
        new(
            semanticModel,
            position,
            node,
            node is ForStatementSyntax forStatement
            ? AnalyzeForInitialEntry(forStatement, semanticModel, smtAnalysis, cancellationToken)
            : AnalyzeAt(node, semanticModel, smtAnalysis, cancellationToken, includeCurrentStatementCompletionFacts, initialState));
    public SymbolicProgramPointAnalysis AnalyzeAt(
        SyntaxNode site,
        SemanticModel semanticModel,
        SmtAnalysisService? smtAnalysis = null,
        CancellationToken cancellationToken = default,
        bool includeCurrentStatementCompletionFacts = false,
        SymbolicState? initialState = null) {
        var point = CollectProgramPoint(
            site,
            semanticModel,
            smtAnalysis,
            cancellationToken,
            includeCurrentStatementCompletionFacts,
            initialState);
        return CreateAnalysis(point.Formulas, point.PathState, smtAnalysis, point.Truncation);
    }
    public SymbolicProgramPointAnalysis AnalyzeForInitialEntry(
        ForStatementSyntax forStatement,
        SemanticModel semanticModel,
        SmtAnalysisService? smtAnalysis = null,
        CancellationToken cancellationToken = default) {
        using var limitScope = SymbolicAnalysisLimitContext.Push(SymbolicAnalysisLimitContext.Limits);
        var pathState = SymbolicReachabilityService.CollectForInitialEntryState(
            forStatement,
            semanticModel,
            cancellationToken,
            smtAnalysis);
        var formulas = EncodePathState(pathState);
        return CreateAnalysis(formulas, pathState, smtAnalysis, limitScope.Snapshot());
    }
    private static IReadOnlyList<SmtFormula> EncodePathState(SymbolicState pathState) {
        pathState = SymbolicProofStateFacts.NormalizeState(pathState);
        return SymbolicProofEncoder.EncodeState(pathState) is { Success: true } encoded
            ? encoded.PathConditions
            : [];
    }
    private static CollectedProgramPoint CollectProgramPoint(
        SyntaxNode site,
        SemanticModel semanticModel,
        SmtAnalysisService? smtAnalysis,
        CancellationToken cancellationToken,
        bool includeCurrentStatementCompletionFacts,
        SymbolicState? initialState) {
        using var limitScope = SymbolicAnalysisLimitContext.Push(SymbolicAnalysisLimitContext.Limits);
        var pathState = SymbolicReachabilityService.CollectPathStateAt(
            site,
            semanticModel,
            cancellationToken,
            smtAnalysis,
            initialState,
            includeCurrentStatementCompletionFacts);
        return new CollectedProgramPoint(pathState, EncodePathState(pathState), limitScope.Snapshot());
    }
    private static SymbolicProgramPointAnalysis CreateAnalysis(
        IReadOnlyList<SmtFormula> formulas,
        SymbolicState pathState,
        SmtAnalysisService? smtAnalysis,
        SymbolicAnalysisTruncationInfo truncation) {
        formulas = FlattenProjectedConjunctions(formulas);
        if (formulas.Count == 0 &&
            pathState.IsContradictory)
            formulas = new[] { new SmtBooleanConstant(false) };
        var shouldCheckState = (pathState.Facts.Length != 0 || pathState.PathConditions.Length != 0) || formulas.Count != 0;
        var stateProof = smtAnalysis == null || !shouldCheckState
            ? null
            : new SymbolicProofService(smtAnalysis).ClassifyReachability(pathState);
        if (stateProof?.Status == SymbolicProofStatus.Unreachable)
            return new SymbolicProgramPointAnalysis(
                formulas,
                pathState,
                SymbolicReachability.Unreachable,
                stateProof.Reason,
                truncation,
                stateProof.RawResult);
        if (formulas.Count == 0) {
            if (stateProof != null)
                return new SymbolicProgramPointAnalysis(
                    formulas,
                    pathState,
                    MapReachability(stateProof.Status),
                    stateProof.Reason,
                    truncation,
                    stateProof.RawResult);
            return new SymbolicProgramPointAnalysis(formulas, pathState, SymbolicReachability.Reachable, "no_path_conditions", truncation);
        }
        return new SymbolicProgramPointAnalysis(
            formulas,
            pathState,
            stateProof == null ? SymbolicReachability.NotChecked : MapReachability(stateProof.Status),
            stateProof?.Reason ?? "reachability_not_checked",
            truncation,
            stateProof?.RawResult);
    }
    private static IReadOnlyList<SmtFormula> FlattenProjectedConjunctions(IEnumerable<SmtFormula> formulas) {
        var projected = new List<SmtFormula>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Add(SmtFormula formula) {
            if (formula is SmtBinaryFormula { Operator: SmtBinaryOperator.And } conjunction) {
                Add(conjunction.Left);
                Add(conjunction.Right);
                return;
            }
            if (seen.Add(SmtFormulaStructuralKey.Create(formula))) projected.Add(formula);
        }
        foreach (var formula in formulas)
            if (formula != null)
                Add(formula);
        return projected;
    }
    private static SymbolicReachability MapReachability(SymbolicProofStatus status) => status switch {
        SymbolicProofStatus.Reachable => SymbolicReachability.Reachable,
        SymbolicProofStatus.Unreachable => SymbolicReachability.Unreachable,
        SymbolicProofStatus.Unknown => SymbolicReachability.Unknown,
        _ => SymbolicReachability.NotChecked
    };
    readonly record struct CollectedProgramPoint(
        SymbolicState PathState,
        IReadOnlyList<SmtFormula> Formulas,
        SymbolicAnalysisTruncationInfo Truncation);
}
internal sealed record SymbolicProgramPointQueryContext(
    SemanticModel SemanticModel,
    int Position,
    SyntaxNode Node,
    SymbolicProgramPointAnalysis Analysis);
internal sealed record SymbolicProgramPointAnalysis(
    IReadOnlyList<SmtFormula> PathConditions,
    SymbolicState PathState,
    SymbolicReachability Reachability,
    string ReachabilityReason,
    SymbolicAnalysisTruncationInfo AnalysisTruncation,
    AnalysisProofResult? ReachabilityProof = null);
internal enum SymbolicReachability {
    NotChecked,
    Unknown,
    Reachable,
    Unreachable
}

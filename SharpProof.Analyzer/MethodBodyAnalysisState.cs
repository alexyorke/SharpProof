namespace SharpProof.Analyzer;

internal sealed record AnalyzerQueryOutcome<T>(T? Value, SharpProofError? Error) where T : class {
    internal bool IsSuccess => Error == null;
}

internal sealed class MethodBodyAnalysisState {
    private readonly SymbolicConditionProofEngine _conditionProofEngine =
        new(new SymbolicInvariantService());
    private readonly MethodEffectAnalysisSession _effectAnalysis;
    private readonly object _gate = new();
    private AnalyzerQueryOutcome<SymbolicComplexityResult>? _complexity;
    private MethodEffects? _effects;

    internal MethodBodyAnalysisState(
        MethodAnalysisSnapshot snapshot,
        MethodEffectAnalysisSession? effectAnalysis = null) {
        Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        _effectAnalysis = effectAnalysis ?? new MethodEffectAnalysisSession(
            snapshot.SemanticModel.Compilation,
            CancellationToken.None);
    }

    internal MethodAnalysisSnapshot Snapshot { get; }

    internal MethodEffects GetMethodEffects(CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return _effects ??= _effectAnalysis.Analyze(
                Snapshot.MethodSymbol,
                Snapshot.Declaration,
                Snapshot.SemanticModel);
    }

    internal AnalyzerQueryOutcome<SymbolicComplexityResult> GetComplexityOutcome(
        CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return _complexity ??= AnalyzerSymbolicQueryBoundary.TryExecute(
                () => AnalyzeComplexity(cancellationToken));
    }

    private SymbolicComplexityResult AnalyzeComplexity(CancellationToken cancellationToken) {
        var target = ResolvedMethodLikeTarget.Create(
            Snapshot.Declaration, Snapshot.SemanticModel, cancellationToken);
        var summary = new SymbolicComplexityAnalysisSession(
            Snapshot.SemanticModel.Compilation, cancellationToken).Analyze(target);
        return SymbolicComplexityResultProjector.Project(target, summary, cancellationToken);
    }

    internal SymbolicConditionProofResult ProveAtNode(
        SyntaxNode node,
        string condition,
        SmtAnalysisService smtAnalysis,
        bool includeCurrentStatementCompletionFacts,
        CancellationToken cancellationToken) {
        return ProveAtNode(node, condition, cancellationToken, () => _conditionProofEngine.ProveAtSyntaxNode(
            Snapshot.SemanticModel,
            node,
            condition,
            smtAnalysis,
            includeCurrentStatementCompletionFacts,
            cancellationToken));
    }

    internal SymbolicConditionProofResult ProveAtNode(
        SyntaxNode node,
        string condition,
        SymbolicCondition symbolicCondition,
        SymbolicState initialState,
        SmtAnalysisService smtAnalysis,
        bool includeCurrentStatementCompletionFacts,
        CancellationToken cancellationToken) {
        return ProveAtNode(node, condition, cancellationToken, () => _conditionProofEngine.ProveAtSyntaxNode(
            Snapshot.SemanticModel,
            node,
            condition,
            symbolicCondition,
            initialState,
            smtAnalysis,
            includeCurrentStatementCompletionFacts,
            cancellationToken));
    }

    private SymbolicConditionProofResult ProveAtNode(
        SyntaxNode node,
        string condition,
        CancellationToken cancellationToken,
        Func<SymbolicConditionProofResult> prove) {
        ValidateNode(node);
        return AnalyzerSymbolicQueryBoundary.ResolveProof(
            AnalyzerSymbolicQueryBoundary.TryExecute(prove),
            condition,
            cancellationToken);
    }

    private void ValidateNode(SyntaxNode node) {
        if (node == null) throw new ArgumentNullException(nameof(node));
        if (node.SyntaxTree != Snapshot.Declaration.SyntaxTree)
            throw new ArgumentException(
                "The proof node must belong to the analyzed method syntax tree.",
                nameof(node));
    }

}

internal static class AnalyzerSymbolicQueryBoundary {
    internal static AnalyzerQueryOutcome<T> TryExecute<T>(Func<T> operation) where T : class {
        try {
            return new AnalyzerQueryOutcome<T>(operation(), null);
        }
        catch (Exception exception) when (!SymbolicErrorClassifier.IsFatal(exception)) {
            return new AnalyzerQueryOutcome<T>(null, SymbolicErrorClassifier.FromException(exception));
        }
    }

    internal static SymbolicConditionProofResult ResolveProof(
        AnalyzerQueryOutcome<SymbolicConditionProofResult> outcome,
        string condition,
        CancellationToken cancellationToken) {
        if (outcome.IsSuccess && outcome.Value != null) return outcome.Value;

        cancellationToken.ThrowIfCancellationRequested();
        if (outcome.Error?.Category == SharpProofErrorCategory.Cancellation)
            throw new OperationCanceledException(outcome.Error.Message);

        var reason = outcome.Error == null
            ? "symbolic proof failed without error details"
            : outcome.Error.Code + ": " + outcome.Error.Message;
        return new SymbolicConditionProofResult(
            condition,
            SymbolicTruthValue.Unknown,
            reason);
    }
}

internal sealed class MethodBodyAnalysisContext(
    MethodBodyAnalysisState state,
    CancellationToken cancellationToken,
    Action<Diagnostic> reportDiagnostic) {
    private readonly Action<Diagnostic> _reportDiagnostic =
        reportDiagnostic ?? throw new ArgumentNullException(nameof(reportDiagnostic));
    internal MethodBodyAnalysisState State { get; } = state ?? throw new ArgumentNullException(nameof(state));

    internal MethodAnalysisSnapshot Snapshot => State.Snapshot;

    internal IMethodSymbol MethodSymbol => Snapshot.MethodSymbol;

    internal SyntaxNode Node => Snapshot.Declaration;

    internal SemanticModel SemanticModel => Snapshot.SemanticModel;

    internal CancellationToken CancellationToken { get; } = cancellationToken;

    internal void ReportDiagnostic(Diagnostic diagnostic) {
        _reportDiagnostic(diagnostic);
    }
}

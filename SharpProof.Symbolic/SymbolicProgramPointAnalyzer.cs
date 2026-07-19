namespace SharpProof.Symbolic;

internal sealed class SymbolicProgramPointAnalyzer
{
    private readonly SymbolicInvariantService _invariantService;

    internal SymbolicProgramPointAnalyzer(SymbolicInvariantService invariantService)
    {
        _invariantService = invariantService ?? throw new ArgumentNullException(nameof(invariantService));
    }

    internal SymbolicProgramPointQueryContext Analyze(
        SemanticModel semanticModel,
        int position,
        SyntaxNode node,
        SmtAnalysisService? smtAnalysis,
        CancellationToken cancellationToken,
        bool includeCurrentStatementCompletionFacts = false,
        SymbolicState? initialState = null)
    {
        var analysis = node is ForStatementSyntax forStatement
            ? _invariantService.AnalyzeForInitialEntry(forStatement, semanticModel, smtAnalysis, cancellationToken)
            : _invariantService.AnalyzeAt(
                node,
                semanticModel,
                smtAnalysis,
                cancellationToken,
                includeCurrentStatementCompletionFacts,
                initialState);

        return new SymbolicProgramPointQueryContext(semanticModel, position, node, analysis);
    }
}

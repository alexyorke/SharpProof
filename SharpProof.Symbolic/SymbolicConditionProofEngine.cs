namespace SharpProof.Symbolic;

internal sealed class SymbolicConditionProofEngine
{
    private readonly SymbolicProgramPointAnalyzer _programPointAnalyzer;

    internal SymbolicConditionProofEngine(SymbolicProgramPointAnalyzer programPointAnalyzer)
    {
        _programPointAnalyzer = programPointAnalyzer ?? throw new ArgumentNullException(nameof(programPointAnalyzer));
    }

    internal SymbolicConditionProofResult ProveAtSyntaxTree(
        SyntaxTree syntaxTree,
        Compilation compilation,
        int line,
        int column,
        string conditionText,
        SmtAnalysisService smtAnalysis,
        CancellationToken cancellationToken = default)
    {
        if (syntaxTree == null) throw new ArgumentNullException(nameof(syntaxTree));
        if (compilation == null) throw new ArgumentNullException(nameof(compilation));
        ValidateCondition(conditionText, smtAnalysis);

        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var root = syntaxTree.GetRoot(cancellationToken);
        var position = SymbolicSourceLocation.GetPosition(syntaxTree, line, column, cancellationToken);
        var node = SymbolicSourceTargetSelector.FindAtPosition(root, position);
        var query = _programPointAnalyzer.Analyze(
            semanticModel, position, node, null, cancellationToken);
        return ProveAtQuery(query, conditionText, smtAnalysis, cancellationToken);
    }

    internal SymbolicConditionProofResult ProveAtSyntaxNode(
        SemanticModel semanticModel,
        SyntaxNode node,
        string conditionText,
        SmtAnalysisService smtAnalysis,
        bool includeCurrentStatementCompletionFacts,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(semanticModel, node, conditionText, smtAnalysis);
        var query = _programPointAnalyzer.Analyze(
            semanticModel,
            node.SpanStart,
            node,
            null,
            cancellationToken,
            includeCurrentStatementCompletionFacts);
        return ProveAtQuery(query, conditionText, smtAnalysis, cancellationToken);
    }

    internal SymbolicConditionProofResult ProveAtAnalysis(
        SemanticModel semanticModel,
        SyntaxNode node,
        SymbolicProgramPointAnalysis analysis,
        string conditionText,
        SmtAnalysisService smtAnalysis,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(semanticModel, node, conditionText, smtAnalysis);
        if (analysis == null) throw new ArgumentNullException(nameof(analysis));

        return Prove(
            semanticModel,
            node.SpanStart,
            node,
            analysis,
            conditionText,
            smtAnalysis,
            cancellationToken).WithAnalysisTruncation(analysis.Truncation);
    }

    internal SymbolicConditionProofResult ProveAtSyntaxNode(
        SemanticModel semanticModel,
        SyntaxNode node,
        string conditionText,
        SymbolicCondition symbolicCondition,
        SymbolicState initialState,
        SmtAnalysisService smtAnalysis,
        bool includeCurrentStatementCompletionFacts,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(semanticModel, node, conditionText, smtAnalysis);
        if (symbolicCondition == null) throw new ArgumentNullException(nameof(symbolicCondition));
        if (initialState == null) throw new ArgumentNullException(nameof(initialState));

        var query = _programPointAnalyzer.Analyze(
            semanticModel,
            node.SpanStart,
            node,
            null,
            cancellationToken,
            includeCurrentStatementCompletionFacts,
            initialState);
        return Prove(
            query.SemanticModel,
            query.Position,
            query.Node,
            query.Analysis,
            conditionText,
            symbolicCondition,
            smtAnalysis).WithAnalysisTruncation(query.Analysis.Truncation);
    }

    internal IReadOnlyList<SymbolicConditionProofResult> ProveAll(
        SemanticModel semanticModel,
        int position,
        SyntaxNode sourceNode,
        SymbolicProgramPointAnalysis analysis,
        IEnumerable<string>? conditionTexts,
        SmtAnalysisService? smtAnalysis,
        CancellationToken cancellationToken)
    {
        if (conditionTexts == null) return Array.Empty<SymbolicConditionProofResult>();

        return conditionTexts
            .Where(static condition => !string.IsNullOrWhiteSpace(condition))
            .Select(condition => Prove(
                semanticModel,
                position,
                sourceNode,
                analysis,
                condition,
                smtAnalysis,
                cancellationToken).WithAnalysisTruncation(analysis.Truncation))
            .ToArray();
    }

    private static SymbolicConditionProofResult ProveAtQuery(
        SymbolicProgramPointQueryContext query,
        string conditionText,
        SmtAnalysisService smtAnalysis,
        CancellationToken cancellationToken) =>
        Prove(
            query.SemanticModel,
            query.Position,
            query.Node,
            query.Analysis,
            conditionText,
            smtAnalysis,
            cancellationToken).WithAnalysisTruncation(query.Analysis.Truncation);

    private static SymbolicConditionProofResult Prove(
        SemanticModel semanticModel,
        int position,
        SyntaxNode sourceNode,
        SymbolicProgramPointAnalysis analysis,
        string conditionText,
        SmtAnalysisService? smtAnalysis,
        CancellationToken cancellationToken)
    {
        if (smtAnalysis == null)
            return new SymbolicConditionProofResult(conditionText, SymbolicTruthValue.Unknown, "smt_required");

        if (analysis.Reachability == SymbolicReachability.Unreachable)
            return new SymbolicConditionProofResult(
                conditionText, SymbolicTruthValue.Unreachable, analysis.ReachabilityReason);

        if (!TryCreateSpeculativeCondition(
                semanticModel,
                position,
                conditionText,
                out var condition,
                out var conditionSemanticModel,
                out var failureReason))
            return new SymbolicConditionProofResult(
                conditionText, SymbolicTruthValue.Unknown, failureReason);

        var lowering = SymbolicSemanticPipeline.LowerCondition(
            condition,
            new SymbolicLoweringContext(conditionSemanticModel, cancellationToken));
        if (lowering is not { IsExact: true, Value: { } symbolicCondition })
            return new SymbolicConditionProofResult(
                conditionText, SymbolicTruthValue.Unknown, "condition_not_supported");

        return Prove(
            semanticModel, position, sourceNode, analysis, conditionText, symbolicCondition, smtAnalysis);
    }

    private static SymbolicConditionProofResult Prove(
        SemanticModel semanticModel,
        int position,
        SyntaxNode sourceNode,
        SymbolicProgramPointAnalysis analysis,
        string conditionText,
        SymbolicCondition symbolicCondition,
        SmtAnalysisService smtAnalysis)
    {
        if (analysis.Reachability == SymbolicReachability.Unreachable)
            return new SymbolicConditionProofResult(
                conditionText, SymbolicTruthValue.Unreachable, analysis.ReachabilityReason);

        if (!SymbolicProofEncoder.TryEncodeConditionWithPathState(
                symbolicCondition, analysis.PathState, sourceNode, out var conditionFormula))
            return new SymbolicConditionProofResult(
                conditionText, SymbolicTruthValue.Unknown, "condition_not_supported");

        if (analysis.Reachability == SymbolicReachability.NotChecked)
        {
            var reachabilityProof = SymbolicReachabilityService.ClassifyStateFeasibility(
                analysis.PathState, smtAnalysis);
            if (reachabilityProof.Info.Status == SymbolicProofStatus.Unreachable)
                return new SymbolicConditionProofResult(
                    conditionText,
                    SymbolicTruthValue.Unreachable,
                    reachabilityProof.Info.Reason,
                    conditionFormula);
        }

        var truthProof = SymbolicReachabilityService.ClassifyStateConditionTruth(
            analysis.PathState, symbolicCondition, smtAnalysis);
        var truthValue = truthProof.Info.Status switch
        {
            SymbolicProofStatus.ProvenTrue => SymbolicTruthValue.ProvenTrue,
            SymbolicProofStatus.ProvenFalse => SymbolicTruthValue.ProvenFalse,
            SymbolicProofStatus.Unreachable => SymbolicTruthValue.Unreachable,
            _ => SymbolicTruthValue.Unknown
        };
        return CreateResult(
            conditionText,
            truthValue,
            truthProof,
            conditionFormula,
            analysis,
            semanticModel,
            position);
    }

    private static SymbolicConditionProofResult CreateResult(
        string conditionText,
        SymbolicTruthValue truthValue,
        SymbolicIrProofResult proof,
        SmtFormula conditionFormula,
        SymbolicProgramPointAnalysis analysis,
        SemanticModel? semanticModel,
        int position)
    {
        var reason = proof.RawResult?.Reason ?? proof.Info.Reason;
        if (truthValue == SymbolicTruthValue.Unreachable)
            return new SymbolicConditionProofResult(
                conditionText,
                truthValue,
                reason,
                conditionFormula,
                witness: SymbolicInputWitnessFactory.None(reason));

        var rawResult = proof.RawResult;
        var outcomeCondition = truthValue == SymbolicTruthValue.ProvenFalse
            ? new SmtUnaryFormula(SmtUnaryOperator.Not, conditionFormula)
            : conditionFormula;
        var unknownTrueBranch = truthValue == SymbolicTruthValue.Unknown &&
                                string.Equals(
                                    proof.Info.Reason,
                                    "ir_condition_true_branch_feasibility_unknown",
                                    StringComparison.Ordinal);
        var unknownFalseBranch = truthValue == SymbolicTruthValue.Unknown &&
                                 proof.Info.Reason is
                                     "ir_condition_false_branch_feasibility_unknown" or
                                     "ir_condition_both_branches_feasible";
        var selectedModel = unknownTrueBranch
            ? rawResult?.ImpurityCheck.Witness ?? rawResult?.PathCheck.Witness
            : truthValue == SymbolicTruthValue.Unknown
                ? null
                : rawResult?.PathCheck.Witness;
        var witness = SymbolicInputWitnessFactory.Create(
            selectedModel,
            analysis.PathConditions.Concat(new[] { outcomeCondition }),
            semanticModel,
            position,
            SymbolicWitnessStatus.Unsupported,
            "condition_witness_unavailable");
        var counterexampleModel = unknownFalseBranch
            ? rawResult?.ImpurityCheck.Witness ?? rawResult?.PathCheck.Witness
            : null;
        var counterexample = counterexampleModel != null
            ? SymbolicInputWitnessFactory.Create(
                counterexampleModel,
                analysis.PathConditions.Concat(new[]
                {
                    new SmtUnaryFormula(SmtUnaryOperator.Not, conditionFormula)
                }),
                semanticModel,
                position,
                SymbolicWitnessStatus.Unsupported,
                "condition_counterexample_unavailable")
            : SymbolicInputWitnessFactory.None("counterexample_not_available");
        return new SymbolicConditionProofResult(
            conditionText,
            truthValue,
            reason,
            conditionFormula,
            witness: witness,
            counterexampleWitness: counterexample);
    }

    private static bool TryCreateSpeculativeCondition(
        SemanticModel semanticModel,
        int position,
        string conditionText,
        out ExpressionSyntax condition,
        out SemanticModel conditionSemanticModel,
        out string failureReason)
    {
        var statement = SyntaxFactory.ParseStatement(
            "if (" + conditionText + ") { }",
            options: semanticModel.SyntaxTree.Options as CSharpParseOptions);
        if (statement.ContainsDiagnostics || statement is not IfStatementSyntax ifStatement)
        {
            condition = SyntaxFactory.LiteralExpression(SyntaxKind.FalseLiteralExpression);
            conditionSemanticModel = semanticModel;
            failureReason = "condition_parse_failure";
            return false;
        }

        if (!semanticModel.TryGetSpeculativeSemanticModel(position, ifStatement, out var speculativeModel) ||
            speculativeModel == null)
        {
            condition = ifStatement.Condition;
            conditionSemanticModel = semanticModel;
            failureReason = "condition_binding_failure";
            return false;
        }

        conditionSemanticModel = speculativeModel;
        condition = ifStatement.Condition;
        failureReason = string.Empty;
        return true;
    }

    private static void ValidateRequest(
        SemanticModel semanticModel,
        SyntaxNode node,
        string conditionText,
        SmtAnalysisService smtAnalysis)
    {
        if (semanticModel == null) throw new ArgumentNullException(nameof(semanticModel));
        if (node == null) throw new ArgumentNullException(nameof(node));
        ValidateCondition(conditionText, smtAnalysis);
    }

    private static void ValidateCondition(string conditionText, SmtAnalysisService smtAnalysis)
    {
        if (string.IsNullOrWhiteSpace(conditionText))
            throw new ArgumentException("Condition text is required.", nameof(conditionText));
        if (smtAnalysis == null) throw new ArgumentNullException(nameof(smtAnalysis));
    }
}

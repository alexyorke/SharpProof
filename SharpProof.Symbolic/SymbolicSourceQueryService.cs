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

internal sealed class SymbolicSourceQueryService
{
    private static readonly ConditionalWeakTable<SyntaxTree, QueryNodeIndex> QueryNodeIndexes = new();
    private readonly SymbolicInvariantService _invariantService;

    public SymbolicSourceQueryService()
        : this(new SymbolicInvariantService())
    {
    }

    public SymbolicSourceQueryService(SymbolicInvariantService invariantService)
    {
        _invariantService = invariantService ?? throw new ArgumentNullException(nameof(invariantService));
    }

    public SymbolicProgramPointResult QuerySyntaxTree(
        SyntaxTree syntaxTree,
        Compilation compilation,
        int line,
        int column = 1,
        CancellationToken cancellationToken = default,
        SmtAnalysisService? smtAnalysis = null,
        IEnumerable<string>? impliedConditions = null)
    {
        ValidateSyntaxTreeQuery(syntaxTree, compilation);

        var query = AnalyzeProgramPoint(
            syntaxTree,
            compilation,
            line,
            column,
            smtAnalysis,
            cancellationToken);
        return ProjectSourceQueryResult(
            syntaxTree,
            query,
            line,
            column,
            impliedConditions,
            smtAnalysis,
            cancellationToken);
    }

    public SymbolicQueryResult QuerySyntaxTreeLine(
        SyntaxTree syntaxTree,
        Compilation compilation,
        int line,
        CancellationToken cancellationToken = default,
        SmtAnalysisService? smtAnalysis = null,
        IEnumerable<string>? impliedConditions = null,
        bool includeExpressionProgramPoints = false,
        bool includeCurrentStatementCompletionFacts = false)
    {
        ValidateSyntaxTreeQuery(syntaxTree, compilation);

        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var nodes = FindQueryNodesOnLine(
            syntaxTree,
            line,
            cancellationToken,
            includeExpressionProgramPoints);
        var results = nodes
            .Select(node => AnalyzeAndProjectNode(
                    syntaxTree,
                    semanticModel,
                    node,
                    impliedConditions,
                    smtAnalysis,
                    cancellationToken,
                    includeCurrentStatementCompletionFacts))
            .ToArray();

        return SymbolicQueryResult.FromLine(
            syntaxTree.FilePath,
            line,
            results,
            SymbolicSmtDiagnostics.FromService(smtAnalysis));
    }

    public SymbolicProgramPointResult QuerySyntaxTreeLinePoint(
        SyntaxTree syntaxTree,
        Compilation compilation,
        int line,
        int column,
        CancellationToken cancellationToken = default,
        SmtAnalysisService? smtAnalysis = null,
        IEnumerable<string>? impliedConditions = null,
        bool includeExpressionProgramPoints = false,
        bool includeCurrentStatementCompletionFacts = false)
    {
        ValidateSyntaxTreeQuery(syntaxTree, compilation);

        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var position = SymbolicSourceLocation.GetPosition(syntaxTree, line, column, cancellationToken);
        var nodes = FindQueryNodesOnLine(
            syntaxTree,
            line,
            cancellationToken,
            includeExpressionProgramPoints);

        if (nodes.Count == 0) throw new ArgumentException("No program points found on --line.", nameof(line));

        var node = nodes
            .OrderBy(candidate => GetProgramPointDistance(candidate, position))
            .ThenBy(candidate => candidate.Span.Length)
            .ThenBy(candidate => Math.Abs(position - candidate.SpanStart))
            .ThenBy(candidate => candidate.SpanStart)
            .First();
        var requestedPositionDistance = GetProgramPointDistance(node, position);
        return AnalyzeAndProjectNode(
            syntaxTree,
            semanticModel,
            node,
            impliedConditions,
            smtAnalysis,
            cancellationToken,
            includeCurrentStatementCompletionFacts,
            line,
            column,
            position,
            requestedPositionDistance,
            ContainsProgramPointPosition(node, position));
    }

    public SymbolicQueryResult QuerySyntaxTreeSpan(
        SyntaxTree syntaxTree,
        Compilation compilation,
        int spanStart,
        int spanEnd,
        CancellationToken cancellationToken = default,
        SmtAnalysisService? smtAnalysis = null,
        IEnumerable<string>? impliedConditions = null,
        bool includeExpressionProgramPoints = false,
        bool includeCurrentStatementCompletionFacts = false)
    {
        ValidateSyntaxTreeQuery(syntaxTree, compilation);

        var sourceSpan = SymbolicSourceLocation.GetSourceSpan(syntaxTree, spanStart, spanEnd, cancellationToken);
        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var nodes = FindQueryNodesInSpan(
            syntaxTree,
            sourceSpan,
            includeExpressionProgramPoints,
            cancellationToken);
        var results = nodes
            .Select(node => AnalyzeAndProjectNode(
                    syntaxTree,
                    semanticModel,
                    node,
                    impliedConditions,
                    smtAnalysis,
                    cancellationToken,
                    includeCurrentStatementCompletionFacts))
            .ToArray();
        var startLineColumn = SymbolicSourceLocation.GetLineAndColumn(
            syntaxTree,
            sourceSpan.Start,
            cancellationToken,
            true);
        var endLineColumn = SymbolicSourceLocation.GetLineAndColumn(
            syntaxTree,
            sourceSpan.End,
            cancellationToken,
            true);

        return SymbolicQueryResult.FromSpan(
            syntaxTree.FilePath,
            sourceSpan.Start,
            sourceSpan.End,
            startLineColumn.Line,
            startLineColumn.Column,
            endLineColumn.Line,
            endLineColumn.Column,
            results,
            SymbolicSmtDiagnostics.FromService(smtAnalysis));
    }

    public SymbolicQueryResult QuerySyntaxTreeLineSpan(
        SyntaxTree syntaxTree,
        Compilation compilation,
        int startLine,
        int startColumn,
        int endLine,
        int endColumn,
        CancellationToken cancellationToken = default,
        SmtAnalysisService? smtAnalysis = null,
        IEnumerable<string>? impliedConditions = null,
        bool includeExpressionProgramPoints = false,
        bool includeCurrentStatementCompletionFacts = false)
    {
        if (syntaxTree == null) throw new ArgumentNullException(nameof(syntaxTree));

        var spanStart = SymbolicSourceLocation.GetPosition(syntaxTree, startLine, startColumn, cancellationToken);
        var spanEnd = SymbolicSourceLocation.GetPosition(syntaxTree, endLine, endColumn, cancellationToken);
        return QuerySyntaxTreeSpan(
            syntaxTree,
            compilation,
            spanStart,
            spanEnd,
            cancellationToken,
            smtAnalysis,
            impliedConditions,
            includeExpressionProgramPoints,
            includeCurrentStatementCompletionFacts);
    }

    public SymbolicQueryResult QuerySyntaxTreeAllLines(
        SyntaxTree syntaxTree,
        Compilation compilation,
        CancellationToken cancellationToken = default,
        SmtAnalysisService? smtAnalysis = null,
        IEnumerable<string>? impliedConditions = null,
        bool includeExpressionProgramPoints = false,
        bool includeCurrentStatementCompletionFacts = false)
    {
        ValidateSyntaxTreeQuery(syntaxTree, compilation);

        var lineCount = syntaxTree.GetText(cancellationToken).Lines.Count;
        var lineResults = new List<SymbolicQueryLineGroup>();
        for (var line = 1; line <= lineCount; line++)
        {
            var lineResult = QuerySyntaxTreeLine(
                syntaxTree,
                compilation,
                line,
                cancellationToken,
                smtAnalysis,
                impliedConditions,
                includeExpressionProgramPoints,
                includeCurrentStatementCompletionFacts);
            if (lineResult.ProgramPoints.Count != 0)
                lineResults.Add(new SymbolicQueryLineGroup(line, lineResult.ProgramPoints));
        }

        return SymbolicQueryResult.FromFile(
            syntaxTree.FilePath,
            lineCount,
            lineResults,
            SymbolicSmtDiagnostics.FromService(smtAnalysis));
    }

    public SymbolicProgramPointResult QuerySyntaxTreeAtPosition(
        SyntaxTree syntaxTree,
        Compilation compilation,
        int position,
        CancellationToken cancellationToken = default,
        SmtAnalysisService? smtAnalysis = null,
        IEnumerable<string>? impliedConditions = null)
    {
        ValidateSyntaxTreeQuery(syntaxTree, compilation);

        var query = AnalyzeProgramPointAtPosition(
            syntaxTree,
            compilation,
            position,
            smtAnalysis,
            cancellationToken);
        var lineColumn = SymbolicSourceLocation.GetLineAndColumn(
            syntaxTree,
            position,
            cancellationToken,
            true);
        return ProjectSourceQueryResult(
            syntaxTree,
            query,
            lineColumn.Line,
            lineColumn.Column,
            impliedConditions,
            smtAnalysis,
            cancellationToken);
    }

    public SymbolicProgramPointQueryResult AnalyzeSyntaxTree(
        SyntaxTree syntaxTree,
        Compilation compilation,
        int line,
        int column = 1,
        CancellationToken cancellationToken = default,
        SmtAnalysisService? smtAnalysis = null)
    {
        ValidateSyntaxTreeQuery(syntaxTree, compilation);

        var query = AnalyzeProgramPoint(
            syntaxTree,
            compilation,
            line,
            column,
            smtAnalysis,
            cancellationToken);
        return CreateProgramPointQueryResult(syntaxTree, query, line, column);
    }

    public SymbolicProgramPointQueryResult AnalyzeSyntaxTreeAtPosition(
        SyntaxTree syntaxTree,
        Compilation compilation,
        int position,
        CancellationToken cancellationToken = default,
        SmtAnalysisService? smtAnalysis = null)
    {
        ValidateSyntaxTreeQuery(syntaxTree, compilation);

        var query = AnalyzeProgramPointAtPosition(
            syntaxTree,
            compilation,
            position,
            smtAnalysis,
            cancellationToken);
        var lineColumn = SymbolicSourceLocation.GetLineAndColumn(
            syntaxTree,
            position,
            cancellationToken,
            true);
        return CreateProgramPointQueryResult(syntaxTree, query, lineColumn.Line, lineColumn.Column);
    }

    public SymbolicConditionProofResult ProveConditionAtSource(
        string sourceText,
        string filePath,
        int line,
        int column,
        string conditionText,
        SmtAnalysisService smtAnalysis,
        IEnumerable<MetadataReference>? references = null,
        CancellationToken cancellationToken = default,
        SymbolicSourceCompilationProfile? compilationProfile = null)
    {
        var (syntaxTree, compilation) = CompileQuerySource(
            sourceText, filePath, references, cancellationToken, compilationProfile);
        return ProveConditionAtSyntaxTree(
            syntaxTree,
            compilation,
            line,
            column,
            conditionText,
            smtAnalysis,
            cancellationToken);
    }

    public SymbolicConditionProofResult ProveConditionAtSyntaxTree(
        SyntaxTree syntaxTree,
        Compilation compilation,
        int line,
        int column,
        string conditionText,
        SmtAnalysisService smtAnalysis,
        CancellationToken cancellationToken = default)
    {
        ValidateSyntaxTreeQuery(syntaxTree, compilation);

        if (string.IsNullOrWhiteSpace(conditionText))
            throw new ArgumentException("Condition text is required.", nameof(conditionText));

        if (smtAnalysis == null) throw new ArgumentNullException(nameof(smtAnalysis));

        var query = AnalyzeProgramPoint(
            syntaxTree,
            compilation,
            line,
            column,
            null,
            cancellationToken);
        return ProveConditionAtQuery(query, conditionText, smtAnalysis, cancellationToken);
    }

    internal SymbolicConditionProofResult ProveConditionAtSyntaxNode(
        SemanticModel semanticModel,
        SyntaxNode node,
        string conditionText,
        SmtAnalysisService smtAnalysis,
        bool includeCurrentStatementCompletionFacts,
        CancellationToken cancellationToken = default)
    {
        ValidateProofRequest(semanticModel, node, conditionText, smtAnalysis);

        var query = AnalyzeProgramPointNode(
            semanticModel,
            node.SpanStart,
            node,
            null,
            cancellationToken,
            includeCurrentStatementCompletionFacts);
        return ProveConditionAtQuery(query, conditionText, smtAnalysis, cancellationToken);
    }

    internal SymbolicConditionProofResult ProveConditionAtAnalysis(
        SemanticModel semanticModel,
        SyntaxNode node,
        SymbolicProgramPointAnalysis analysis,
        string conditionText,
        SmtAnalysisService smtAnalysis,
        CancellationToken cancellationToken = default)
    {
        ValidateProofRequest(semanticModel, node, conditionText, smtAnalysis);

        if (analysis == null) throw new ArgumentNullException(nameof(analysis));

        return ProveCondition(
            semanticModel,
            node.SpanStart,
            node,
            analysis,
            conditionText,
            smtAnalysis,
            cancellationToken).WithAnalysisTruncation(analysis.Truncation);
    }

    internal SymbolicConditionProofResult ProveConditionAtSyntaxNode(
        SemanticModel semanticModel,
        SyntaxNode node,
        string conditionText,
        SymbolicCondition symbolicCondition,
        SymbolicState initialState,
        SmtAnalysisService smtAnalysis,
        bool includeCurrentStatementCompletionFacts,
        CancellationToken cancellationToken = default)
    {
        ValidateProofRequest(semanticModel, node, conditionText, smtAnalysis);

        if (symbolicCondition == null) throw new ArgumentNullException(nameof(symbolicCondition));

        if (initialState == null) throw new ArgumentNullException(nameof(initialState));

        var query = AnalyzeProgramPointNode(
            semanticModel,
            node.SpanStart,
            node,
            null,
            cancellationToken,
            includeCurrentStatementCompletionFacts,
            initialState);
        return ProveCondition(
            query.SemanticModel,
            query.Position,
            query.Node,
            query.Analysis,
            conditionText,
            symbolicCondition,
            smtAnalysis).WithAnalysisTruncation(query.Analysis.Truncation);
    }

    private static void ValidateProofRequest(
        SemanticModel semanticModel,
        SyntaxNode node,
        string conditionText,
        SmtAnalysisService smtAnalysis)
    {
        if (semanticModel == null) throw new ArgumentNullException(nameof(semanticModel));
        if (node == null) throw new ArgumentNullException(nameof(node));
        if (string.IsNullOrWhiteSpace(conditionText))
            throw new ArgumentException("Condition text is required.", nameof(conditionText));
        if (smtAnalysis == null) throw new ArgumentNullException(nameof(smtAnalysis));
    }

    private static (SyntaxTree SyntaxTree, Compilation Compilation) CompileQuerySource(
        string sourceText, string filePath, IEnumerable<MetadataReference>? references,
        CancellationToken cancellationToken, SymbolicSourceCompilationProfile? compilationProfile) =>
        SymbolicSourceCompilation.Create(
            sourceText, filePath, SymbolicSourceCompilationKind.Query, references, cancellationToken,
            compilationProfile);

    private static void ValidateSyntaxTreeQuery(SyntaxTree syntaxTree, Compilation compilation)
    {
        if (syntaxTree == null) throw new ArgumentNullException(nameof(syntaxTree));
        if (compilation == null) throw new ArgumentNullException(nameof(compilation));
    }

    private static SymbolicProgramPointQueryResult CreateProgramPointQueryResult(
        SyntaxTree syntaxTree,
        ProgramPointQueryContext query,
        int line,
        int column) =>
        new(
            syntaxTree.FilePath,
            line,
            column,
            query.Position,
            query.Node.SpanStart,
            query.Node.Kind().ToString(),
            query.Analysis,
            SymbolicProgramPointMetadata.GetProgramPointKind(query.Node));

    private static SymbolicConditionProofResult ProveConditionAtQuery(
        ProgramPointQueryContext query,
        string conditionText,
        SmtAnalysisService smtAnalysis,
        CancellationToken cancellationToken) =>
        ProveCondition(
            query.SemanticModel,
            query.Position,
            query.Node,
            query.Analysis,
            conditionText,
            smtAnalysis,
            cancellationToken).WithAnalysisTruncation(query.Analysis.Truncation);

    private ProgramPointQueryContext AnalyzeProgramPoint(
        SyntaxTree syntaxTree,
        Compilation compilation,
        int line,
        int column,
        SmtAnalysisService? smtAnalysis,
        CancellationToken cancellationToken)
    {
        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var root = syntaxTree.GetRoot(cancellationToken);
        var position = SymbolicSourceLocation.GetPosition(syntaxTree, line, column, cancellationToken);
        var node = FindQueryNode(root, position);
        return AnalyzeProgramPointNode(semanticModel, position, node, smtAnalysis, cancellationToken);
    }

    private ProgramPointQueryContext AnalyzeProgramPointAtPosition(
        SyntaxTree syntaxTree,
        Compilation compilation,
        int position,
        SmtAnalysisService? smtAnalysis,
        CancellationToken cancellationToken)
    {
        var text = syntaxTree.GetText(cancellationToken);
        if (position < 0 || position > text.Length)
            throw new ArgumentOutOfRangeException(nameof(position), "--position must be within the source text span.");

        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var root = syntaxTree.GetRoot(cancellationToken);
        var node = FindQueryNode(root, position);
        return AnalyzeProgramPointNode(semanticModel, position, node, smtAnalysis, cancellationToken);
    }

    private ProgramPointQueryContext AnalyzeProgramPointNode(
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

        return new ProgramPointQueryContext(semanticModel, position, node, analysis);
    }

    private static IReadOnlyList<SymbolicConditionProofResult> ProveConditions(
        SemanticModel semanticModel,
        int position,
        SyntaxNode sourceNode,
        SymbolicProgramPointAnalysis analysis,
        IEnumerable<string>? conditionTexts,
        SmtAnalysisService? smtAnalysis,
        CancellationToken cancellationToken)
    {
        if (conditionTexts == null) return Array.Empty<SymbolicConditionProofResult>();

        var proofs = conditionTexts
            .Where(static condition => !string.IsNullOrWhiteSpace(condition))
            .Select(condition => ProveCondition(
                semanticModel,
                position,
                sourceNode,
                analysis,
                condition,
                smtAnalysis,
                cancellationToken).WithAnalysisTruncation(analysis.Truncation))
            .ToArray();
        return proofs;
    }

    private static SymbolicConditionProofResult ProveCondition(
        SemanticModel semanticModel,
        int position,
        SyntaxNode sourceNode,
        SymbolicProgramPointAnalysis analysis,
        string conditionText,
        SmtAnalysisService? smtAnalysis,
        CancellationToken cancellationToken)
    {
        if (smtAnalysis == null)
            return new SymbolicConditionProofResult(
                conditionText,
                SymbolicTruthValue.Unknown,
                "smt_required");

        if (analysis.Reachability == SymbolicReachability.Unreachable)
            return new SymbolicConditionProofResult(
                conditionText,
                SymbolicTruthValue.Unreachable,
                analysis.ReachabilityReason);

        if (!TryCreateSpeculativeCondition(
                semanticModel,
                position,
                conditionText,
                out var condition,
                out var conditionSemanticModel,
                out var failureReason))
            return new SymbolicConditionProofResult(
                conditionText,
                SymbolicTruthValue.Unknown,
                failureReason);

        var lowering = SymbolicSemanticPipeline.LowerCondition(
            condition,
            new SymbolicLoweringContext(conditionSemanticModel, cancellationToken));
        if (lowering is not { IsExact: true, Value: { } symbolicCondition })
            return new SymbolicConditionProofResult(
                conditionText,
                SymbolicTruthValue.Unknown,
                "condition_not_supported");

        return ProveCondition(
            semanticModel,
            position,
            sourceNode,
            analysis,
            conditionText,
            symbolicCondition,
            smtAnalysis);
    }

    private static SymbolicConditionProofResult ProveCondition(
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
                conditionText,
                SymbolicTruthValue.Unreachable,
                analysis.ReachabilityReason);

        if (!SymbolicProofService.TryEncodeConditionWithPathState(
                symbolicCondition,
                analysis.PathState,
                sourceNode,
                out var conditionFormula))
            return new SymbolicConditionProofResult(
                conditionText,
                SymbolicTruthValue.Unknown,
                "condition_not_supported");

        if (analysis.Reachability == SymbolicReachability.NotChecked)
        {
            var reachabilityProof = SymbolicReachabilityService.ClassifyStateFeasibility(
                analysis.PathState,
                smtAnalysis);
            if (reachabilityProof.Info.Status == SymbolicProofStatus.Unreachable)
                return new SymbolicConditionProofResult(
                    conditionText,
                    SymbolicTruthValue.Unreachable,
                    reachabilityProof.Info.Reason,
                    conditionFormula);
        }

        var truthProof = SymbolicReachabilityService.ClassifyStateConditionTruth(
            analysis.PathState,
            symbolicCondition,
            smtAnalysis);
        var truthValue = truthProof.Info.Status switch
        {
            SymbolicProofStatus.ProvenTrue => SymbolicTruthValue.ProvenTrue,
            SymbolicProofStatus.ProvenFalse => SymbolicTruthValue.ProvenFalse,
            SymbolicProofStatus.Unreachable => SymbolicTruthValue.Unreachable,
            _ => SymbolicTruthValue.Unknown
        };
        return CreateConditionProofResult(
            conditionText,
            truthValue,
            truthProof,
            conditionFormula,
            analysis,
            semanticModel,
            position);
    }

    private static SymbolicConditionProofResult CreateConditionProofResult(
        string conditionText,
        SymbolicTruthValue truthValue,
        SymbolicIrProofResult proof,
        SmtFormula conditionFormula,
        SymbolicProgramPointAnalysis analysis,
        SemanticModel? semanticModel,
        int position)
    {
        var reason = proof.RawResult?.Reason ?? proof.Info.Reason;
        var effectiveTruth = truthValue;
        if (effectiveTruth == SymbolicTruthValue.Unreachable)
            return new SymbolicConditionProofResult(
                conditionText,
                effectiveTruth,
                reason,
                conditionFormula,
                witness: SymbolicInputWitnessFactory.None(reason));

        var rawResult = proof.RawResult;
        var outcomeCondition = effectiveTruth == SymbolicTruthValue.ProvenFalse
            ? new SmtUnaryFormula(SmtUnaryOperator.Not, conditionFormula)
            : conditionFormula;
        var unknownTrueBranch = effectiveTruth == SymbolicTruthValue.Unknown &&
                                string.Equals(
                                    proof.Info.Reason,
                                    "ir_condition_true_branch_feasibility_unknown",
                                    StringComparison.Ordinal);
        var unknownFalseBranch = effectiveTruth == SymbolicTruthValue.Unknown &&
                                 proof.Info.Reason is
                                     "ir_condition_false_branch_feasibility_unknown" or
                                     "ir_condition_both_branches_feasible";
        var selectedModel = unknownTrueBranch
            ? rawResult?.ImpurityCheck.Witness ?? rawResult?.PathCheck.Witness
            : effectiveTruth == SymbolicTruthValue.Unknown
                ? null
                : rawResult?.PathCheck.Witness;
        var selectedConditions = analysis.PathConditions.Concat(new[] { outcomeCondition });
        var witness = SymbolicInputWitnessFactory.Create(
            selectedModel,
            selectedConditions,
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
            effectiveTruth,
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
        if (statement.ContainsDiagnostics ||
            statement is not IfStatementSyntax ifStatement)
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

    private static SyntaxNode FindQueryNode(SyntaxNode root, int position)
    {
        var token = root.FindToken(position);
        var expressionContextNode = FindExpressionContextNode(token, position);
        if (expressionContextNode != null) return expressionContextNode;

        return token.Parent?
                   .AncestorsAndSelf()
                   .OfType<StatementSyntax>()
                   .FirstOrDefault(statement => statement.Span.Contains(position))
               ?? token.Parent
               ?? root;
    }

    private static IReadOnlyList<SyntaxNode> FindQueryNodesOnLine(
        SyntaxTree syntaxTree,
        int line,
        CancellationToken cancellationToken,
        bool includeExpressionProgramPoints)
    {
        var lineSpan = SymbolicSourceLocation.GetLineSpan(syntaxTree, line, cancellationToken);
        return FindQueryNodesInSpan(
            syntaxTree,
            lineSpan,
            includeExpressionProgramPoints,
            cancellationToken);
    }

    private static IReadOnlyList<SyntaxNode> FindQueryNodesInSpan(
        SyntaxTree syntaxTree,
        TextSpan lineSpan,
        bool includeExpressionProgramPoints,
        CancellationToken cancellationToken)
    {
        if (lineSpan.Length == 0) return Array.Empty<SyntaxNode>();

        cancellationToken.ThrowIfCancellationRequested();
        var index = QueryNodeIndexes.GetValue(
            syntaxTree,
            tree => new QueryNodeIndex(tree, cancellationToken));
        return index.FindIntersecting(lineSpan, includeExpressionProgramPoints, cancellationToken);
    }

    private static int GetProgramPointDistance(SyntaxNode candidate, int targetPosition)
    {
        if (ContainsProgramPointPosition(candidate, targetPosition)) return 0;

        var span = candidate.Span;
        return targetPosition < span.Start
            ? span.Start - targetPosition
            : targetPosition - span.End;
    }

    private static bool ContainsProgramPointPosition(SyntaxNode candidate, int targetPosition)
    {
        return candidate.Span.Contains(targetPosition);
    }

    private static bool IsUsefulLineExpressionProgramPoint(ExpressionSyntax expression)
    {
        return expression is AssignmentExpressionSyntax or AwaitExpressionSyntax or BinaryExpressionSyntax
            or CastExpressionSyntax or
            ConditionalAccessExpressionSyntax or ConditionalExpressionSyntax or ElementAccessExpressionSyntax
            or InvocationExpressionSyntax or
            IsPatternExpressionSyntax or MemberAccessExpressionSyntax or ObjectCreationExpressionSyntax
            or PrefixUnaryExpressionSyntax or
            PostfixUnaryExpressionSyntax or RangeExpressionSyntax or SwitchExpressionSyntax or ThrowExpressionSyntax;
    }

    private static SyntaxNode? FindExpressionContextNode(SyntaxToken token, int position)
    {
        foreach (var node in token.Parent?.AncestorsAndSelf() ?? Enumerable.Empty<SyntaxNode>())
            switch (node)
            {
                case SwitchExpressionArmSyntax switchArm when switchArm.Expression.Span.Contains(position):
                    return FindInnermostExpression(switchArm.Expression, position);
                case ConditionalExpressionSyntax conditionalExpression
                    when conditionalExpression.WhenTrue.Span.Contains(position):
                    return FindInnermostExpression(conditionalExpression.WhenTrue, position);
                case ConditionalExpressionSyntax conditionalExpression
                    when conditionalExpression.WhenFalse.Span.Contains(position):
                    return FindInnermostExpression(conditionalExpression.WhenFalse, position);
                case BinaryExpressionSyntax binaryExpression
                    when binaryExpression.IsKind(SyntaxKind.CoalesceExpression) &&
                         binaryExpression.Right.Span.Contains(position):
                    return FindInnermostExpression(binaryExpression.Right, position);
                case ConditionalAccessExpressionSyntax conditionalAccess
                    when conditionalAccess.WhenNotNull.Span.Contains(position):
                    return FindInnermostExpression(conditionalAccess.WhenNotNull, position);
            }

        return null;
    }

    private static ExpressionSyntax FindInnermostExpression(ExpressionSyntax expression, int position)
    {
        return expression
                   .DescendantNodesAndSelf()
                   .Where(node => node.Span.Contains(position))
                   .OfType<ExpressionSyntax>()
                   .OrderBy(node => node.Span.Length)
                   .FirstOrDefault()
               ?? expression;
    }

    private SymbolicProgramPointResult AnalyzeAndProjectNode(
        SyntaxTree syntaxTree,
        SemanticModel semanticModel,
        SyntaxNode node,
        IEnumerable<string>? impliedConditions,
        SmtAnalysisService? smtAnalysis,
        CancellationToken cancellationToken,
        bool includeCurrentStatementCompletionFacts,
        int? requestedLine = null,
        int? requestedColumn = null,
        int? requestedPosition = null,
        int? requestedPositionDistance = null,
        bool? containsRequestedPosition = null)
    {
        var query = AnalyzeProgramPointNode(
            semanticModel,
            node.SpanStart,
            node,
            smtAnalysis,
            cancellationToken,
            includeCurrentStatementCompletionFacts);
        var lineColumn = SymbolicSourceLocation.GetLineAndColumn(
            syntaxTree,
            query.Position,
            cancellationToken,
            true);
        return ProjectSourceQueryResult(
            syntaxTree,
            query,
            lineColumn.Line,
            lineColumn.Column,
            impliedConditions,
            smtAnalysis,
            cancellationToken,
            requestedLine,
            requestedColumn,
            requestedPosition,
            requestedPositionDistance,
            containsRequestedPosition);
    }

    private static SymbolicProgramPointResult ProjectSourceQueryResult(
        SyntaxTree syntaxTree,
        ProgramPointQueryContext query,
        int line,
        int column,
        IEnumerable<string>? impliedConditions,
        SmtAnalysisService? smtAnalysis,
        CancellationToken cancellationToken,
        int? requestedLine = null,
        int? requestedColumn = null,
        int? requestedPosition = null,
        int? requestedPositionDistance = null,
        bool? containsRequestedPosition = null)
    {
        var conditionProofs = ProveConditions(
            query.SemanticModel,
            query.Position,
            query.Node,
            query.Analysis,
            impliedConditions,
            smtAnalysis,
            cancellationToken);
        return CreateSourceQueryResult(
            syntaxTree,
            query,
            line,
            column,
            conditionProofs,
            SymbolicSmtDiagnostics.FromService(smtAnalysis),
            cancellationToken,
            requestedLine,
            requestedColumn,
            requestedPosition,
            requestedPositionDistance,
            containsRequestedPosition);
    }

    private static SymbolicProgramPointResult CreateSourceQueryResult(
        SyntaxTree syntaxTree,
        ProgramPointQueryContext query,
        int line,
        int column,
        IReadOnlyList<SymbolicConditionProofResult> conditionProofs,
        SymbolicSmtDiagnostics smtDiagnostics,
        CancellationToken cancellationToken,
        int? requestedLine = null,
        int? requestedColumn = null,
        int? requestedPosition = null,
        int? requestedPositionDistance = null,
        bool? containsRequestedPosition = null)
    {
        var nodeSourceSpan = SymbolicSourceLocation.GetNodeSourceSpan(
            syntaxTree,
            query.Node.Span,
            cancellationToken);
        var mergedInvariantText = SymbolicFormulaDisplay.FormatMergedInvariant(query.Analysis.PathConditions);
        var invariant = SymbolicInvariantResult.FromFormulas(
            query.Analysis.PathConditions,
            mergedInvariantText);
        return new SymbolicProgramPointResult(
            syntaxTree.FilePath,
            line,
            column,
            query.Position,
            query.Node.SpanStart,
            query.Node.Kind().ToString(),
            query.Analysis.Facts,
            query.Analysis.Reachability,
            query.Analysis.ReachabilityReason,
            conditionProofs,
            smtDiagnostics,
            mergedInvariantText,
            invariant,
            query.Node.Span.End,
            nodeSourceSpan.StartLine,
            nodeSourceSpan.StartColumn,
            nodeSourceSpan.EndLine,
            nodeSourceSpan.EndColumn,
            SymbolicProgramPointMetadata.GetContainingMethodName(query.Node),
            SymbolicProgramPointMetadata.GetProgramPointKind(query.Node),
            requestedLine,
            requestedColumn,
            requestedPosition,
            requestedPositionDistance,
            containsRequestedPosition,
            SymbolicFactInfo.FromState(query.Analysis.PathState),
            SymbolicInputWitnessFactory.CreateReachability(
                query.Analysis.ReachabilityProof?.PathCheck.Witness,
                query.Analysis.PathConditions,
                query.SemanticModel,
                query.Position,
                query.Analysis.Reachability,
                query.Analysis.ReachabilityReason),
            query.Analysis.Truncation);
    }

    private sealed class ProgramPointQueryContext(
        SemanticModel semanticModel,
        int position,
        SyntaxNode node,
        SymbolicProgramPointAnalysis analysis)
    {
        public SemanticModel SemanticModel { get; } = semanticModel;

        public int Position { get; } = position;

        public SyntaxNode Node { get; } = node;

        public SymbolicProgramPointAnalysis Analysis { get; } = analysis;
    }

    private sealed class QueryNodeIndex
    {
        private readonly IReadOnlyDictionary<int, ImmutableArray<SyntaxNode>> _baseNodesByLine;
        private readonly IReadOnlyDictionary<int, ImmutableArray<SyntaxNode>> _expressionNodesByLine;
        private readonly SourceText _text;

        public QueryNodeIndex(SyntaxTree syntaxTree, CancellationToken cancellationToken)
        {
            _text = syntaxTree.GetText(cancellationToken);
            var root = syntaxTree.GetRoot(cancellationToken);
            var baseNodesByLine = new Dictionary<int,
                Dictionary<(int RawKind, int Start, int End), SyntaxNode>>();
            var tokenIndex = 0;
            foreach (var token in root.DescendantTokens(descendIntoTrivia: false))
            {
                if ((tokenIndex++ & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                if (token.Span.Length == 0) continue;

                var node = FindQueryNode(root, token.SpanStart);
                if (node is not StatementSyntax and not ExpressionSyntax || node.Span.Length == 0) continue;

                var key = (node.RawKind, node.SpanStart, node.Span.End);
                var tokenStartLine = _text.Lines.GetLineFromPosition(token.SpanStart).LineNumber;
                var tokenEndLine = _text.Lines.GetLineFromPosition(token.Span.End - 1).LineNumber;
                for (var line = tokenStartLine; line <= tokenEndLine; line++)
                {
                    if (!baseNodesByLine.TryGetValue(line, out var lineNodes))
                    {
                        lineNodes = new Dictionary<(int RawKind, int Start, int End), SyntaxNode>();
                        baseNodesByLine.Add(line, lineNodes);
                    }

                    if (!lineNodes.ContainsKey(key)) lineNodes.Add(key, node);
                }
            }

            var expressionNodes = new Dictionary<(int RawKind, int Start, int End), SyntaxNode>();
            foreach (var expression in root.DescendantNodes(descendIntoTrivia: false)
                         .OfType<ExpressionSyntax>()
                         .Where(static expression =>
                             expression.Span.Length > 0 && IsUsefulLineExpressionProgramPoint(expression)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var key = (expression.RawKind, expression.SpanStart, expression.Span.End);
                if (!expressionNodes.ContainsKey(key)) expressionNodes.Add(key, expression);
            }

            _baseNodesByLine = baseNodesByLine.ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value.Values.ToImmutableArray());
            _expressionNodesByLine = BuildLineIndex(expressionNodes.Values);
        }

        public IReadOnlyList<SyntaxNode> FindIntersecting(
            TextSpan span,
            bool includeExpressionProgramPoints,
            CancellationToken cancellationToken)
        {
            if (span.Length == 0) return Array.Empty<SyntaxNode>();

            var startLine = _text.Lines.GetLineFromPosition(span.Start).LineNumber;
            var endLine = _text.Lines.GetLineFromPosition(span.End - 1).LineNumber;
            var seen = new HashSet<(int RawKind, int Start, int End)>();
            var nodes = new List<SyntaxNode>();
            for (var line = startLine; line <= endLine; line++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddNodes(_baseNodesByLine, line, span, seen, nodes);
                if (includeExpressionProgramPoints)
                    AddNodes(_expressionNodesByLine, line, span, seen, nodes);
            }

            return nodes
                .OrderBy(static node => node.SpanStart)
                .ThenBy(static node => node.Span.Length)
                .ToArray();
        }

        private IReadOnlyDictionary<int, ImmutableArray<SyntaxNode>> BuildLineIndex(
            IEnumerable<SyntaxNode> nodes)
        {
            var lineNodes = new Dictionary<int, List<SyntaxNode>>();
            foreach (var node in nodes)
            {
                var startLine = _text.Lines.GetLineFromPosition(node.SpanStart).LineNumber;
                var endLine = _text.Lines.GetLineFromPosition(node.Span.End - 1).LineNumber;
                for (var line = startLine; line <= endLine; line++)
                {
                    if (!lineNodes.TryGetValue(line, out var values))
                    {
                        values = new List<SyntaxNode>();
                        lineNodes.Add(line, values);
                    }

                    values.Add(node);
                }
            }

            return lineNodes.ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value.ToImmutableArray());
        }

        private static void AddNodes(
            IReadOnlyDictionary<int, ImmutableArray<SyntaxNode>> index,
            int line,
            TextSpan span,
            ISet<(int RawKind, int Start, int End)> seen,
            ICollection<SyntaxNode> nodes)
        {
            if (!index.TryGetValue(line, out var candidates)) return;

            foreach (var candidate in candidates)
            {
                if (!candidate.Span.IntersectsWith(span)) continue;

                var key = (candidate.RawKind, candidate.SpanStart, candidate.Span.End);
                if (seen.Add(key)) nodes.Add(candidate);
            }
        }
    }
}

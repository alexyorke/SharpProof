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
    private readonly SymbolicProgramPointAnalyzer _programPointAnalyzer;
    private readonly SymbolicConditionProofEngine _conditionProofEngine;

    public SymbolicSourceQueryService()
        : this(new SymbolicInvariantService())
    {
    }

    public SymbolicSourceQueryService(SymbolicInvariantService invariantService)
    {
        _programPointAnalyzer = new SymbolicProgramPointAnalyzer(
            invariantService ?? throw new ArgumentNullException(nameof(invariantService)));
        _conditionProofEngine = new SymbolicConditionProofEngine(_programPointAnalyzer);
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
        => _conditionProofEngine.ProveAtSource(
            sourceText,
            filePath,
            line,
            column,
            conditionText,
            smtAnalysis,
            references,
            cancellationToken,
            compilationProfile);

    public SymbolicConditionProofResult ProveConditionAtSyntaxTree(
        SyntaxTree syntaxTree,
        Compilation compilation,
        int line,
        int column,
        string conditionText,
        SmtAnalysisService smtAnalysis,
        CancellationToken cancellationToken = default)
        => _conditionProofEngine.ProveAtSyntaxTree(
            syntaxTree, compilation, line, column, conditionText, smtAnalysis, cancellationToken);

    internal SymbolicConditionProofResult ProveConditionAtSyntaxNode(
        SemanticModel semanticModel,
        SyntaxNode node,
        string conditionText,
        SmtAnalysisService smtAnalysis,
        bool includeCurrentStatementCompletionFacts,
        CancellationToken cancellationToken = default)
        => _conditionProofEngine.ProveAtSyntaxNode(
            semanticModel,
            node,
            conditionText,
            smtAnalysis,
            includeCurrentStatementCompletionFacts,
            cancellationToken);

    internal SymbolicConditionProofResult ProveConditionAtAnalysis(
        SemanticModel semanticModel,
        SyntaxNode node,
        SymbolicProgramPointAnalysis analysis,
        string conditionText,
        SmtAnalysisService smtAnalysis,
        CancellationToken cancellationToken = default)
        => _conditionProofEngine.ProveAtAnalysis(
            semanticModel, node, analysis, conditionText, smtAnalysis, cancellationToken);

    internal SymbolicConditionProofResult ProveConditionAtSyntaxNode(
        SemanticModel semanticModel,
        SyntaxNode node,
        string conditionText,
        SymbolicCondition symbolicCondition,
        SymbolicState initialState,
        SmtAnalysisService smtAnalysis,
        bool includeCurrentStatementCompletionFacts,
        CancellationToken cancellationToken = default)
        => _conditionProofEngine.ProveAtSyntaxNode(
            semanticModel,
            node,
            conditionText,
            symbolicCondition,
            initialState,
            smtAnalysis,
            includeCurrentStatementCompletionFacts,
            cancellationToken);

    private static void ValidateSyntaxTreeQuery(SyntaxTree syntaxTree, Compilation compilation)
    {
        if (syntaxTree == null) throw new ArgumentNullException(nameof(syntaxTree));
        if (compilation == null) throw new ArgumentNullException(nameof(compilation));
    }

    private SymbolicProgramPointQueryContext AnalyzeProgramPoint(
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
        return _programPointAnalyzer.Analyze(semanticModel, position, node, smtAnalysis, cancellationToken);
    }

    private SymbolicProgramPointQueryContext AnalyzeProgramPointAtPosition(
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
        return _programPointAnalyzer.Analyze(semanticModel, position, node, smtAnalysis, cancellationToken);
    }

    internal static SyntaxNode FindQueryNode(SyntaxNode root, int position)
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
        var query = _programPointAnalyzer.Analyze(
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

    private SymbolicProgramPointResult ProjectSourceQueryResult(
        SyntaxTree syntaxTree,
        SymbolicProgramPointQueryContext query,
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
        var conditionProofs = _conditionProofEngine.ProveAll(
            query.SemanticModel,
            query.Position,
            query.Node,
            query.Analysis,
            impliedConditions,
            smtAnalysis,
            cancellationToken);
        return SymbolicProgramPointProjector.Project(
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

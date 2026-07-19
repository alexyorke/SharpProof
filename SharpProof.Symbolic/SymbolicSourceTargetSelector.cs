namespace SharpProof.Symbolic;

internal static class SymbolicSourceTargetSelector
{
    private static readonly ConditionalWeakTable<SyntaxTree, QueryNodeIndex> QueryNodeIndexes = new();

    internal static SyntaxNode FindAtPosition(SyntaxNode root, int position)
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

    internal static IReadOnlyList<SyntaxNode> FindOnLine(
        SyntaxTree syntaxTree,
        int line,
        CancellationToken cancellationToken,
        bool includeExpressionProgramPoints)
    {
        var lineSpan = SymbolicSourceLocation.GetLineSpan(syntaxTree, line, cancellationToken);
        return FindInSpan(syntaxTree, lineSpan, includeExpressionProgramPoints, cancellationToken);
    }

    internal static IReadOnlyList<SyntaxNode> FindInSpan(
        SyntaxTree syntaxTree,
        TextSpan span,
        bool includeExpressionProgramPoints,
        CancellationToken cancellationToken)
    {
        if (span.Length == 0) return Array.Empty<SyntaxNode>();

        cancellationToken.ThrowIfCancellationRequested();
        var index = QueryNodeIndexes.GetValue(
            syntaxTree,
            tree => new QueryNodeIndex(tree, cancellationToken));
        return index.FindIntersecting(span, includeExpressionProgramPoints, cancellationToken);
    }

    internal static SyntaxNode SelectNearest(IReadOnlyList<SyntaxNode> nodes, int position)
    {
        return nodes
            .OrderBy(candidate => GetDistance(candidate, position))
            .ThenBy(candidate => candidate.Span.Length)
            .ThenBy(candidate => Math.Abs(position - candidate.SpanStart))
            .ThenBy(candidate => candidate.SpanStart)
            .First();
    }

    internal static int GetDistance(SyntaxNode candidate, int targetPosition)
    {
        if (ContainsPosition(candidate, targetPosition)) return 0;

        var span = candidate.Span;
        return targetPosition < span.Start
            ? span.Start - targetPosition
            : targetPosition - span.End;
    }

    internal static bool ContainsPosition(SyntaxNode candidate, int targetPosition) =>
        candidate.Span.Contains(targetPosition);

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

    private sealed class QueryNodeIndex
    {
        private readonly IReadOnlyDictionary<int, ImmutableArray<SyntaxNode>> _baseNodesByLine;
        private readonly IReadOnlyDictionary<int, ImmutableArray<SyntaxNode>> _expressionNodesByLine;
        private readonly SourceText _text;

        internal QueryNodeIndex(SyntaxTree syntaxTree, CancellationToken cancellationToken)
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

                var node = FindAtPosition(root, token.SpanStart);
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

        internal IReadOnlyList<SyntaxNode> FindIntersecting(
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

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PurelySharp.Symbolic
{
    public sealed class SymbolicSourceQueryService
    {
        private readonly SymbolicInvariantService _invariantService;

        public SymbolicSourceQueryService()
            : this(new SymbolicInvariantService())
        {
        }

        public SymbolicSourceQueryService(SymbolicInvariantService invariantService)
        {
            _invariantService = invariantService ?? throw new ArgumentNullException(nameof(invariantService));
        }

        public SymbolicSourceQueryResult QueryFile(
            string filePath,
            int line,
            int column = 1,
            IEnumerable<MetadataReference>? references = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("File path is required.", nameof(filePath));
            }

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("Source file does not exist.", filePath);
            }

            return QuerySource(
                File.ReadAllText(filePath),
                Path.GetFullPath(filePath),
                line,
                column,
                references,
                cancellationToken);
        }

        public SymbolicSourceQueryResult QuerySource(
            string sourceText,
            string filePath,
            int line,
            int column = 1,
            IEnumerable<MetadataReference>? references = null,
            CancellationToken cancellationToken = default)
        {
            if (sourceText == null)
            {
                throw new ArgumentNullException(nameof(sourceText));
            }

            if (string.IsNullOrWhiteSpace(filePath))
            {
                filePath = "PurelySharp.Symbolic.Query.cs";
            }

            var syntaxTree = CSharpSyntaxTree.ParseText(
                sourceText,
                new CSharpParseOptions(LanguageVersion.Preview),
                filePath,
                cancellationToken: cancellationToken);
            var referenceArray = references?.ToImmutableArray() ?? GetTrustedPlatformReferences();
            var compilation = CSharpCompilation.Create(
                "PurelySharp.Symbolic.Query",
                new[] { syntaxTree },
                referenceArray,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            return QuerySyntaxTree(syntaxTree, compilation, line, column, cancellationToken);
        }

        public SymbolicSourceQueryResult QuerySyntaxTree(
            SyntaxTree syntaxTree,
            Compilation compilation,
            int line,
            int column = 1,
            CancellationToken cancellationToken = default)
        {
            if (syntaxTree == null)
            {
                throw new ArgumentNullException(nameof(syntaxTree));
            }

            if (compilation == null)
            {
                throw new ArgumentNullException(nameof(compilation));
            }

            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var root = syntaxTree.GetRoot(cancellationToken);
            var position = GetPosition(syntaxTree, line, column, cancellationToken);
            var node = FindQueryNode(root, position);
            var snapshot = node is ForStatementSyntax forStatement
                ? _invariantService.GetForInitialEntryInvariants(forStatement, semanticModel, cancellationToken)
                : _invariantService.GetInvariantsAt(node, semanticModel, cancellationToken);

            return new SymbolicSourceQueryResult(
                syntaxTree.FilePath,
                line,
                column,
                position,
                node.SpanStart,
                node.Kind().ToString(),
                snapshot.Facts);
        }

        private static ImmutableArray<MetadataReference> GetTrustedPlatformReferences()
        {
            var trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
            if (string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
            {
                return ImmutableArray.Create<MetadataReference>(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
            }

            return trustedPlatformAssemblies!
                .Split(Path.PathSeparator)
                .Where(static path => !string.IsNullOrWhiteSpace(path))
                .Select(static path => MetadataReference.CreateFromFile(path))
                .ToImmutableArray<MetadataReference>();
        }

        private static SyntaxNode FindQueryNode(SyntaxNode root, int position)
        {
            var token = root.FindToken(position);
            var expressionContextNode = FindExpressionContextNode(token, position);
            if (expressionContextNode != null)
            {
                return expressionContextNode;
            }

            return root
                .DescendantNodesAndSelf()
                .Where(node => node.Span.Contains(position))
                .OfType<StatementSyntax>()
                .OrderBy(node => node.Span.Length)
                .FirstOrDefault()
                ?? token.Parent
                ?? root;
        }

        private static SyntaxNode? FindExpressionContextNode(SyntaxToken token, int position)
        {
            foreach (var node in token.Parent?.AncestorsAndSelf() ?? Enumerable.Empty<SyntaxNode>())
            {
                switch (node)
                {
                    case SwitchExpressionArmSyntax switchArm when switchArm.Expression.Span.Contains(position):
                        return FindInnermostExpression(switchArm.Expression, position);
                    case ConditionalExpressionSyntax conditionalExpression when conditionalExpression.WhenTrue.Span.Contains(position):
                        return FindInnermostExpression(conditionalExpression.WhenTrue, position);
                    case ConditionalExpressionSyntax conditionalExpression when conditionalExpression.WhenFalse.Span.Contains(position):
                        return FindInnermostExpression(conditionalExpression.WhenFalse, position);
                    case BinaryExpressionSyntax binaryExpression
                        when binaryExpression.IsKind(SyntaxKind.CoalesceExpression) &&
                             binaryExpression.Right.Span.Contains(position):
                        return FindInnermostExpression(binaryExpression.Right, position);
                    case ConditionalAccessExpressionSyntax conditionalAccess
                        when conditionalAccess.WhenNotNull.Span.Contains(position):
                        return FindInnermostExpression(conditionalAccess.WhenNotNull, position);
                }
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

        private static int GetPosition(
            SyntaxTree syntaxTree,
            int line,
            int column,
            CancellationToken cancellationToken)
        {
            if (line < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(line), "--line must be 1 or greater.");
            }

            if (column < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(column), "--column must be 1 or greater.");
            }

            var text = syntaxTree.GetText(cancellationToken);
            if (line > text.Lines.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(line), "--line exceeds the file line count.");
            }

            var textLine = text.Lines[line - 1];
            var zeroBasedColumn = column - 1;
            if (zeroBasedColumn > textLine.Span.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(column), "--column exceeds the line length.");
            }

            return textLine.Start + zeroBasedColumn;
        }
    }

    public sealed class SymbolicSourceQueryResult
    {
        public SymbolicSourceQueryResult(
            string filePath,
            int line,
            int column,
            int position,
            int nodeSpanStart,
            string nodeKind,
            IReadOnlyList<string> facts)
        {
            FilePath = filePath;
            Line = line;
            Column = column;
            Position = position;
            NodeSpanStart = nodeSpanStart;
            NodeKind = nodeKind;
            Facts = facts;
        }

        public string FilePath { get; }

        public int Line { get; }

        public int Column { get; }

        public int Position { get; }

        public int NodeSpanStart { get; }

        public string NodeKind { get; }

        public IReadOnlyList<string> Facts { get; }
    }
}

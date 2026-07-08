using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;
using SearchLib.Smt;

namespace SharpProof.Symbolic
{
    internal sealed class SymbolicSourceQueryService
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
            SymbolicFileQuery query,
            CancellationToken cancellationToken = default,
            SmtAnalysisService? smtAnalysis = null)
        {
            if (query == null)
            {
                throw new ArgumentNullException(nameof(query));
            }

            return QueryFile(
                query.FilePath,
                query.Line,
                query.Column,
                query.References.IsDefaultOrEmpty ? null : query.References,
                cancellationToken,
                smtAnalysis,
                query.ImpliedConditions);
        }

        public SymbolicSourceQueryResult QueryFile(
            string filePath,
            int line,
            int column = 1,
            IEnumerable<MetadataReference>? references = null,
            CancellationToken cancellationToken = default,
            SmtAnalysisService? smtAnalysis = null,
            IEnumerable<string>? impliedConditions = null)
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
                cancellationToken,
                smtAnalysis,
                impliedConditions);
        }

        public SymbolicSourceQueryResult QueryFileAtPosition(
            string filePath,
            int position,
            IEnumerable<MetadataReference>? references = null,
            CancellationToken cancellationToken = default,
            SmtAnalysisService? smtAnalysis = null,
            IEnumerable<string>? impliedConditions = null)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("File path is required.", nameof(filePath));
            }

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("Source file does not exist.", filePath);
            }

            return QuerySourceAtPosition(
                File.ReadAllText(filePath),
                Path.GetFullPath(filePath),
                position,
                references,
                cancellationToken,
                smtAnalysis,
                impliedConditions);
        }

        public SymbolicLineQueryResult QueryFileLine(
            string filePath,
            int line,
            IEnumerable<MetadataReference>? references = null,
            CancellationToken cancellationToken = default,
            SmtAnalysisService? smtAnalysis = null,
            IEnumerable<string>? impliedConditions = null,
            bool includeExpressionProgramPoints = false,
            bool includeCurrentStatementCompletionFacts = false)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("File path is required.", nameof(filePath));
            }

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("Source file does not exist.", filePath);
            }

            return QuerySourceLine(
                File.ReadAllText(filePath),
                Path.GetFullPath(filePath),
                line,
                references,
                cancellationToken,
                smtAnalysis,
                impliedConditions,
                includeExpressionProgramPoints,
                includeCurrentStatementCompletionFacts);
        }

        public SymbolicSourceQueryResult QueryFileLinePoint(
            string filePath,
            int line,
            int column,
            IEnumerable<MetadataReference>? references = null,
            CancellationToken cancellationToken = default,
            SmtAnalysisService? smtAnalysis = null,
            IEnumerable<string>? impliedConditions = null,
            bool includeExpressionProgramPoints = false,
            bool includeCurrentStatementCompletionFacts = false)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("File path is required.", nameof(filePath));
            }

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("Source file does not exist.", filePath);
            }

            return QuerySourceLinePoint(
                File.ReadAllText(filePath),
                Path.GetFullPath(filePath),
                line,
                column,
                references,
                cancellationToken,
                smtAnalysis,
                impliedConditions,
                includeExpressionProgramPoints,
                includeCurrentStatementCompletionFacts);
        }

        public SymbolicSpanQueryResult QueryFileSpan(
            string filePath,
            int spanStart,
            int spanEnd,
            IEnumerable<MetadataReference>? references = null,
            CancellationToken cancellationToken = default,
            SmtAnalysisService? smtAnalysis = null,
            IEnumerable<string>? impliedConditions = null,
            bool includeExpressionProgramPoints = false,
            bool includeCurrentStatementCompletionFacts = false)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("File path is required.", nameof(filePath));
            }

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("Source file does not exist.", filePath);
            }

            return QuerySourceSpan(
                File.ReadAllText(filePath),
                Path.GetFullPath(filePath),
                spanStart,
                spanEnd,
                references,
                cancellationToken,
                smtAnalysis,
                impliedConditions,
                includeExpressionProgramPoints,
                includeCurrentStatementCompletionFacts);
        }

        public SymbolicSpanQueryResult QueryFileLineSpan(
            string filePath,
            int startLine,
            int startColumn,
            int endLine,
            int endColumn,
            IEnumerable<MetadataReference>? references = null,
            CancellationToken cancellationToken = default,
            SmtAnalysisService? smtAnalysis = null,
            IEnumerable<string>? impliedConditions = null,
            bool includeExpressionProgramPoints = false,
            bool includeCurrentStatementCompletionFacts = false)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("File path is required.", nameof(filePath));
            }

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("Source file does not exist.", filePath);
            }

            return QuerySourceLineSpan(
                File.ReadAllText(filePath),
                Path.GetFullPath(filePath),
                startLine,
                startColumn,
                endLine,
                endColumn,
                references,
                cancellationToken,
                smtAnalysis,
                impliedConditions,
                includeExpressionProgramPoints,
                includeCurrentStatementCompletionFacts);
        }

        public SymbolicFileQueryResult QueryFileAllLines(
            string filePath,
            IEnumerable<MetadataReference>? references = null,
            CancellationToken cancellationToken = default,
            SmtAnalysisService? smtAnalysis = null,
            IEnumerable<string>? impliedConditions = null,
            bool includeExpressionProgramPoints = false,
            bool includeCurrentStatementCompletionFacts = false)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("File path is required.", nameof(filePath));
            }

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("Source file does not exist.", filePath);
            }

            return QuerySourceAllLines(
                File.ReadAllText(filePath),
                Path.GetFullPath(filePath),
                references,
                cancellationToken,
                smtAnalysis,
                impliedConditions,
                includeExpressionProgramPoints,
                includeCurrentStatementCompletionFacts);
        }

        public SymbolicProgramPointQueryResult AnalyzeFile(
            SymbolicFileQuery query,
            CancellationToken cancellationToken = default,
            SmtAnalysisService? smtAnalysis = null)
        {
            if (query == null)
            {
                throw new ArgumentNullException(nameof(query));
            }

            return AnalyzeFile(
                query.FilePath,
                query.Line,
                query.Column,
                query.References.IsDefaultOrEmpty ? null : query.References,
                cancellationToken,
                smtAnalysis);
        }

        public SymbolicProgramPointQueryResult AnalyzeFile(
            string filePath,
            int line,
            int column = 1,
            IEnumerable<MetadataReference>? references = null,
            CancellationToken cancellationToken = default,
            SmtAnalysisService? smtAnalysis = null)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("File path is required.", nameof(filePath));
            }

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("Source file does not exist.", filePath);
            }

            return AnalyzeSource(
                File.ReadAllText(filePath),
                Path.GetFullPath(filePath),
                line,
                column,
                references,
                cancellationToken,
                smtAnalysis);
        }

        public SymbolicSourceQueryResult QuerySource(
            string sourceText,
            string filePath,
            int line,
            int column = 1,
            IEnumerable<MetadataReference>? references = null,
            CancellationToken cancellationToken = default,
            SmtAnalysisService? smtAnalysis = null,
            IEnumerable<string>? impliedConditions = null)
        {
            var (syntaxTree, compilation) = SymbolicSourceCompilation.Create(
                sourceText,
                filePath,
                "SharpProof.Symbolic.Query.cs",
                "SharpProof.Symbolic.Query",
                references,
                cancellationToken);
            return QuerySyntaxTree(
                syntaxTree,
                compilation,
                line,
                column,
                cancellationToken,
                smtAnalysis,
                impliedConditions);
        }

        public SymbolicSourceQueryResult QuerySourceAtPosition(
            string sourceText,
            string filePath,
            int position,
            IEnumerable<MetadataReference>? references = null,
            CancellationToken cancellationToken = default,
            SmtAnalysisService? smtAnalysis = null,
            IEnumerable<string>? impliedConditions = null)
        {
            var (syntaxTree, compilation) = SymbolicSourceCompilation.Create(
                sourceText,
                filePath,
                "SharpProof.Symbolic.Query.cs",
                "SharpProof.Symbolic.Query",
                references,
                cancellationToken);
            return QuerySyntaxTreeAtPosition(
                syntaxTree,
                compilation,
                position,
                cancellationToken,
                smtAnalysis,
                impliedConditions);
        }

        public SymbolicLineQueryResult QuerySourceLine(
            string sourceText,
            string filePath,
            int line,
            IEnumerable<MetadataReference>? references = null,
            CancellationToken cancellationToken = default,
            SmtAnalysisService? smtAnalysis = null,
            IEnumerable<string>? impliedConditions = null,
            bool includeExpressionProgramPoints = false,
            bool includeCurrentStatementCompletionFacts = false)
        {
            var (syntaxTree, compilation) = SymbolicSourceCompilation.Create(
                sourceText,
                filePath,
                "SharpProof.Symbolic.Query.cs",
                "SharpProof.Symbolic.Query",
                references,
                cancellationToken);
            return QuerySyntaxTreeLine(
                syntaxTree,
                compilation,
                line,
                cancellationToken,
                smtAnalysis,
                impliedConditions,
                includeExpressionProgramPoints,
                includeCurrentStatementCompletionFacts);
        }

        public SymbolicSourceQueryResult QuerySourceLinePoint(
            string sourceText,
            string filePath,
            int line,
            int column,
            IEnumerable<MetadataReference>? references = null,
            CancellationToken cancellationToken = default,
            SmtAnalysisService? smtAnalysis = null,
            IEnumerable<string>? impliedConditions = null,
            bool includeExpressionProgramPoints = false,
            bool includeCurrentStatementCompletionFacts = false)
        {
            var (syntaxTree, compilation) = SymbolicSourceCompilation.Create(
                sourceText,
                filePath,
                "SharpProof.Symbolic.Query.cs",
                "SharpProof.Symbolic.Query",
                references,
                cancellationToken);
            return QuerySyntaxTreeLinePoint(
                syntaxTree,
                compilation,
                line,
                column,
                cancellationToken,
                smtAnalysis,
                impliedConditions,
                includeExpressionProgramPoints,
                includeCurrentStatementCompletionFacts);
        }

        public SymbolicSpanQueryResult QuerySourceSpan(
            string sourceText,
            string filePath,
            int spanStart,
            int spanEnd,
            IEnumerable<MetadataReference>? references = null,
            CancellationToken cancellationToken = default,
            SmtAnalysisService? smtAnalysis = null,
            IEnumerable<string>? impliedConditions = null,
            bool includeExpressionProgramPoints = false,
            bool includeCurrentStatementCompletionFacts = false)
        {
            var (syntaxTree, compilation) = SymbolicSourceCompilation.Create(
                sourceText,
                filePath,
                "SharpProof.Symbolic.Query.cs",
                "SharpProof.Symbolic.Query",
                references,
                cancellationToken);
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

        public SymbolicSpanQueryResult QuerySourceLineSpan(
            string sourceText,
            string filePath,
            int startLine,
            int startColumn,
            int endLine,
            int endColumn,
            IEnumerable<MetadataReference>? references = null,
            CancellationToken cancellationToken = default,
            SmtAnalysisService? smtAnalysis = null,
            IEnumerable<string>? impliedConditions = null,
            bool includeExpressionProgramPoints = false,
            bool includeCurrentStatementCompletionFacts = false)
        {
            var (syntaxTree, compilation) = SymbolicSourceCompilation.Create(
                sourceText,
                filePath,
                "SharpProof.Symbolic.Query.cs",
                "SharpProof.Symbolic.Query",
                references,
                cancellationToken);
            return QuerySyntaxTreeLineSpan(
                syntaxTree,
                compilation,
                startLine,
                startColumn,
                endLine,
                endColumn,
                cancellationToken,
                smtAnalysis,
                impliedConditions,
                includeExpressionProgramPoints,
                includeCurrentStatementCompletionFacts);
        }

        public SymbolicFileQueryResult QuerySourceAllLines(
            string sourceText,
            string filePath,
            IEnumerable<MetadataReference>? references = null,
            CancellationToken cancellationToken = default,
            SmtAnalysisService? smtAnalysis = null,
            IEnumerable<string>? impliedConditions = null,
            bool includeExpressionProgramPoints = false,
            bool includeCurrentStatementCompletionFacts = false)
        {
            var (syntaxTree, compilation) = SymbolicSourceCompilation.Create(
                sourceText,
                filePath,
                "SharpProof.Symbolic.Query.cs",
                "SharpProof.Symbolic.Query",
                references,
                cancellationToken);
            return QuerySyntaxTreeAllLines(
                syntaxTree,
                compilation,
                cancellationToken,
                smtAnalysis,
                impliedConditions,
                includeExpressionProgramPoints,
                includeCurrentStatementCompletionFacts);
        }

        public SymbolicProgramPointQueryResult AnalyzeSource(
            string sourceText,
            string filePath,
            int line,
            int column = 1,
            IEnumerable<MetadataReference>? references = null,
            CancellationToken cancellationToken = default,
            SmtAnalysisService? smtAnalysis = null)
        {
            var (syntaxTree, compilation) = SymbolicSourceCompilation.Create(
                sourceText,
                filePath,
                "SharpProof.Symbolic.Query.cs",
                "SharpProof.Symbolic.Query",
                references,
                cancellationToken);
            return AnalyzeSyntaxTree(
                syntaxTree,
                compilation,
                line,
                column,
                cancellationToken,
                smtAnalysis);
        }

        public SymbolicProgramPointQueryResult AnalyzeSourceAtPosition(
            string sourceText,
            string filePath,
            int position,
            IEnumerable<MetadataReference>? references = null,
            CancellationToken cancellationToken = default,
            SmtAnalysisService? smtAnalysis = null)
        {
            var (syntaxTree, compilation) = SymbolicSourceCompilation.Create(
                sourceText,
                filePath,
                "SharpProof.Symbolic.Query.cs",
                "SharpProof.Symbolic.Query",
                references,
                cancellationToken);
            return AnalyzeSyntaxTreeAtPosition(
                syntaxTree,
                compilation,
                position,
                cancellationToken,
                smtAnalysis);
        }

        public SymbolicSourceQueryResult QuerySyntaxTree(
            SyntaxTree syntaxTree,
            Compilation compilation,
            int line,
            int column = 1,
            CancellationToken cancellationToken = default,
            SmtAnalysisService? smtAnalysis = null,
            IEnumerable<string>? impliedConditions = null)
        {
            if (syntaxTree == null)
            {
                throw new ArgumentNullException(nameof(syntaxTree));
            }

            if (compilation == null)
            {
                throw new ArgumentNullException(nameof(compilation));
            }

            var query = AnalyzeProgramPoint(
                syntaxTree,
                compilation,
                line,
                column,
                smtAnalysis,
                cancellationToken);
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
                cancellationToken);
        }

        public SymbolicLineQueryResult QuerySyntaxTreeLine(
            SyntaxTree syntaxTree,
            Compilation compilation,
            int line,
            CancellationToken cancellationToken = default,
            SmtAnalysisService? smtAnalysis = null,
            IEnumerable<string>? impliedConditions = null,
            bool includeExpressionProgramPoints = false,
            bool includeCurrentStatementCompletionFacts = false)
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
            var nodes = FindQueryNodesOnLine(
                root,
                syntaxTree,
                line,
                cancellationToken,
                includeExpressionProgramPoints);
            var results = nodes
                .Select(node =>
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
                        validatePosition: true);
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
                        lineColumn.Line,
                        lineColumn.Column,
                        conditionProofs,
                        SymbolicSmtDiagnostics.FromService(smtAnalysis),
                        cancellationToken);
                })
                .ToArray();

            return new SymbolicLineQueryResult(
                syntaxTree.FilePath,
                line,
                results,
                SymbolicSmtDiagnostics.FromService(smtAnalysis));
        }

        public SymbolicSourceQueryResult QuerySyntaxTreeLinePoint(
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
            var position = SymbolicSourceLocation.GetPosition(syntaxTree, line, column, cancellationToken);
            var nodes = FindQueryNodesOnLine(
                root,
                syntaxTree,
                line,
                cancellationToken,
                includeExpressionProgramPoints);

            if (nodes.Count == 0)
            {
                throw new ArgumentException("No program points found on --line.", nameof(line));
            }

            var node = nodes
                .OrderBy(candidate => GetProgramPointDistance(candidate, position))
                .ThenBy(candidate => candidate.Span.Length)
                .ThenBy(candidate => Math.Abs(position - candidate.SpanStart))
                .ThenBy(candidate => candidate.SpanStart)
                .First();
            var requestedPositionDistance = GetProgramPointDistance(node, position);
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
                validatePosition: true);
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
                lineColumn.Line,
                lineColumn.Column,
                conditionProofs,
                SymbolicSmtDiagnostics.FromService(smtAnalysis),
                cancellationToken,
                line,
                column,
                position,
                requestedPositionDistance,
                ContainsProgramPointPosition(node, position));
        }

        public SymbolicSpanQueryResult QuerySyntaxTreeSpan(
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
            if (syntaxTree == null)
            {
                throw new ArgumentNullException(nameof(syntaxTree));
            }

            if (compilation == null)
            {
                throw new ArgumentNullException(nameof(compilation));
            }

            var sourceSpan = SymbolicSourceLocation.GetSourceSpan(syntaxTree, spanStart, spanEnd, cancellationToken);
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var root = syntaxTree.GetRoot(cancellationToken);
            var nodes = FindQueryNodesInSpan(
                root,
                sourceSpan,
                includeExpressionProgramPoints);
            var results = nodes
                .Select(node =>
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
                        validatePosition: true);
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
                        lineColumn.Line,
                        lineColumn.Column,
                        conditionProofs,
                        SymbolicSmtDiagnostics.FromService(smtAnalysis),
                        cancellationToken);
                })
                .ToArray();
            var startLineColumn = SymbolicSourceLocation.GetLineAndColumn(
                syntaxTree,
                sourceSpan.Start,
                cancellationToken,
                validatePosition: true);
            var endLineColumn = SymbolicSourceLocation.GetLineAndColumn(
                syntaxTree,
                sourceSpan.End,
                cancellationToken,
                validatePosition: true);

            return new SymbolicSpanQueryResult(
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

        public SymbolicSpanQueryResult QuerySyntaxTreeLineSpan(
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
            if (syntaxTree == null)
            {
                throw new ArgumentNullException(nameof(syntaxTree));
            }

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

        public SymbolicFileQueryResult QuerySyntaxTreeAllLines(
            SyntaxTree syntaxTree,
            Compilation compilation,
            CancellationToken cancellationToken = default,
            SmtAnalysisService? smtAnalysis = null,
            IEnumerable<string>? impliedConditions = null,
            bool includeExpressionProgramPoints = false,
            bool includeCurrentStatementCompletionFacts = false)
        {
            if (syntaxTree == null)
            {
                throw new ArgumentNullException(nameof(syntaxTree));
            }

            if (compilation == null)
            {
                throw new ArgumentNullException(nameof(compilation));
            }

            var lineCount = syntaxTree.GetText(cancellationToken).Lines.Count;
            var lineResults = new List<SymbolicLineQueryResult>();
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
                {
                    lineResults.Add(lineResult);
                }
            }

            return new SymbolicFileQueryResult(
                syntaxTree.FilePath,
                lineCount,
                lineResults,
                SymbolicSmtDiagnostics.FromService(smtAnalysis));
        }

        public SymbolicSourceQueryResult QuerySyntaxTreeAtPosition(
            SyntaxTree syntaxTree,
            Compilation compilation,
            int position,
            CancellationToken cancellationToken = default,
            SmtAnalysisService? smtAnalysis = null,
            IEnumerable<string>? impliedConditions = null)
        {
            if (syntaxTree == null)
            {
                throw new ArgumentNullException(nameof(syntaxTree));
            }

            if (compilation == null)
            {
                throw new ArgumentNullException(nameof(compilation));
            }

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
                validatePosition: true);
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
                lineColumn.Line,
                lineColumn.Column,
                conditionProofs,
                SymbolicSmtDiagnostics.FromService(smtAnalysis),
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
            if (syntaxTree == null)
            {
                throw new ArgumentNullException(nameof(syntaxTree));
            }

            if (compilation == null)
            {
                throw new ArgumentNullException(nameof(compilation));
            }

            var query = AnalyzeProgramPoint(
                syntaxTree,
                compilation,
                line,
                column,
                smtAnalysis,
                cancellationToken);
            return new SymbolicProgramPointQueryResult(
                syntaxTree.FilePath,
                line,
                column,
                query.Position,
                query.Node.SpanStart,
                query.Node.Kind().ToString(),
                query.Analysis,
                SymbolicProgramPointMetadata.GetProgramPointKind(query.Node));
        }

        public SymbolicProgramPointQueryResult AnalyzeSyntaxTreeAtPosition(
            SyntaxTree syntaxTree,
            Compilation compilation,
            int position,
            CancellationToken cancellationToken = default,
            SmtAnalysisService? smtAnalysis = null)
        {
            if (syntaxTree == null)
            {
                throw new ArgumentNullException(nameof(syntaxTree));
            }

            if (compilation == null)
            {
                throw new ArgumentNullException(nameof(compilation));
            }

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
                validatePosition: true);
            return new SymbolicProgramPointQueryResult(
                syntaxTree.FilePath,
                lineColumn.Line,
                lineColumn.Column,
                query.Position,
                query.Node.SpanStart,
                query.Node.Kind().ToString(),
                query.Analysis,
                SymbolicProgramPointMetadata.GetProgramPointKind(query.Node));
        }

        public SymbolicConditionProofResult ProveConditionAtFile(
            string filePath,
            int line,
            int column,
            string conditionText,
            SmtAnalysisService smtAnalysis,
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

            return ProveConditionAtSource(
                File.ReadAllText(filePath),
                Path.GetFullPath(filePath),
                line,
                column,
                conditionText,
                smtAnalysis,
                references,
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
            CancellationToken cancellationToken = default)
        {
            var (syntaxTree, compilation) = SymbolicSourceCompilation.Create(
                sourceText,
                filePath,
                "SharpProof.Symbolic.Query.cs",
                "SharpProof.Symbolic.Query",
                references,
                cancellationToken);
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
            if (syntaxTree == null)
            {
                throw new ArgumentNullException(nameof(syntaxTree));
            }

            if (compilation == null)
            {
                throw new ArgumentNullException(nameof(compilation));
            }

            if (string.IsNullOrWhiteSpace(conditionText))
            {
                throw new ArgumentException("Condition text is required.", nameof(conditionText));
            }

            if (smtAnalysis == null)
            {
                throw new ArgumentNullException(nameof(smtAnalysis));
            }

            var query = AnalyzeProgramPoint(
                syntaxTree,
                compilation,
                line,
                column,
                smtAnalysis: null,
                cancellationToken);
            return ProveCondition(
                query.SemanticModel,
                query.Position,
                query.Node,
                query.Analysis,
                conditionText,
                smtAnalysis,
                cancellationToken);
        }

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
            {
                throw new ArgumentOutOfRangeException(nameof(position), "--position must be within the source text span.");
            }

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
            bool includeCurrentStatementCompletionFacts = false)
        {
            var analysis = node is ForStatementSyntax forStatement
                ? _invariantService.AnalyzeForInitialEntry(forStatement, semanticModel, smtAnalysis, cancellationToken)
                : _invariantService.AnalyzeAt(
                    node,
                    semanticModel,
                    smtAnalysis,
                    cancellationToken,
                    includeCurrentStatementCompletionFacts);

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
            if (conditionTexts == null)
            {
                return Array.Empty<SymbolicConditionProofResult>();
            }

            var proofs = conditionTexts
                .Where(static condition => !string.IsNullOrWhiteSpace(condition))
                .Select(condition => ProveCondition(
                    semanticModel,
                    position,
                    sourceNode,
                    analysis,
                    condition,
                    smtAnalysis,
                    cancellationToken))
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
            {
                return new SymbolicConditionProofResult(
                    conditionText,
                    SymbolicTruthValue.Unknown,
                    "smt_required");
            }

            if (analysis.Reachability == SymbolicReachability.Unreachable)
            {
                return new SymbolicConditionProofResult(
                    conditionText,
                    SymbolicTruthValue.Unreachable,
                    analysis.ReachabilityReason);
            }

            if (!TryCreateSpeculativeCondition(
                    semanticModel,
                    position,
                    conditionText,
                    out var condition,
                    out var conditionSemanticModel,
                    out var failureReason))
            {
                return new SymbolicConditionProofResult(
                    conditionText,
                    SymbolicTruthValue.Unknown,
                    failureReason);
            }

            if (!TryTranslateProofCondition(
                    condition,
                    conditionSemanticModel,
                    cancellationToken,
                    out var symbolicCondition,
                    out var conditionFormula) ||
                conditionFormula == null)
            {
                return new SymbolicConditionProofResult(
                    conditionText,
                    SymbolicTruthValue.Unknown,
                    "condition_not_supported");
            }

            var formulaTruth = SymbolicReachabilityService.ClassifyFormulaConditionTruthWithIrFallback(
                analysis.PathConditions,
                conditionFormula,
                sourceNode,
                smtAnalysis,
                "source.query.condition",
                "source-query-condition");
            if (formulaTruth.Info.Status == SymbolicProofStatus.Unreachable)
            {
                return new SymbolicConditionProofResult(
                    conditionText,
                    SymbolicTruthValue.Unreachable,
                    formulaTruth.Info.Reason,
                    conditionFormula);
            }

            if (formulaTruth.Info.Status == SymbolicProofStatus.ProvenTrue)
            {
                return new SymbolicConditionProofResult(
                    conditionText,
                    SymbolicTruthValue.ProvenTrue,
                    formulaTruth.Info.Reason,
                    conditionFormula);
            }

            if (formulaTruth.Info.Status == SymbolicProofStatus.ProvenFalse)
            {
                return new SymbolicConditionProofResult(
                    conditionText,
                    SymbolicTruthValue.ProvenFalse,
                    formulaTruth.Info.Reason,
                    conditionFormula);
            }

            if (analysis.Reachability == SymbolicReachability.NotChecked)
            {
                var reachabilityProof = SymbolicReachabilityService.ClassifyStateFeasibilityWithFormulaFallback(
                    analysis.PathState,
                    analysis.PathConditions,
                    smtAnalysis);
                if (reachabilityProof.Info.Status == SymbolicProofStatus.Unreachable)
                {
                    return new SymbolicConditionProofResult(
                        conditionText,
                        SymbolicTruthValue.Unreachable,
                        reachabilityProof.Info.Reason,
                        conditionFormula);
                }
            }

            if (symbolicCondition != null &&
                TryProveConditionWithIrState(
                    analysis.PathState,
                    symbolicCondition,
                    conditionText,
                    conditionFormula,
                    smtAnalysis,
                    out var irProofResult))
            {
                return irProofResult;
            }

            return new SymbolicConditionProofResult(
                conditionText,
                SymbolicTruthValue.Unknown,
                formulaTruth.Info.Reason,
                conditionFormula);
        }

        private static bool TryTranslateProofCondition(
            ExpressionSyntax condition,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SymbolicCondition? symbolicCondition,
            out SmtFormula? formula)
        {
            symbolicCondition = null;
            SmtFormula? irFormula = null;
            var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
            if (SymbolicIrLowerer.TryLowerCondition(condition, context, out var loweredCondition))
            {
                symbolicCondition = loweredCondition;
                if (SymbolicIrFormulaEncoder.TryEncode(loweredCondition, out var encoded))
                {
                    irFormula = encoded;
                }
            }

            if (SymbolicReachabilityService.TryTranslateConditionFormula(
                condition,
                semanticModel,
                cancellationToken,
                out formula))
            {
                return true;
            }

            formula = irFormula;
            return formula != null;
        }

        private static bool TryProveConditionWithIrState(
            SymbolicState pathState,
            SymbolicCondition symbolicCondition,
            string conditionText,
            SmtFormula conditionFormula,
            SmtAnalysisService smtAnalysis,
            out SymbolicConditionProofResult proofResult)
        {
            var truthProof = SymbolicReachabilityService.ClassifyStateConditionTruth(
                pathState,
                symbolicCondition,
                smtAnalysis);
            if (truthProof.Info.Status == SymbolicProofStatus.ProvenTrue)
            {
                proofResult = CreateConditionProofResult(
                    conditionText,
                    SymbolicTruthValue.ProvenTrue,
                    truthProof,
                    conditionFormula);
                return true;
            }

            if (truthProof.Info.Status == SymbolicProofStatus.ProvenFalse)
            {
                proofResult = CreateConditionProofResult(
                    conditionText,
                    SymbolicTruthValue.ProvenFalse,
                    truthProof,
                    conditionFormula);
                return true;
            }

            if (truthProof.Info.Status == SymbolicProofStatus.Unreachable)
            {
                proofResult = CreateConditionProofResult(
                    conditionText,
                    SymbolicTruthValue.Unreachable,
                    truthProof,
                    conditionFormula);
                return true;
            }

            proofResult = null!;
            return false;
        }

        private static SymbolicConditionProofResult CreateConditionProofResult(
            string conditionText,
            SymbolicTruthValue truthValue,
            SymbolicIrProofResult proof,
            SmtFormula conditionFormula)
        {
            var reason = proof.RawResult?.Reason ?? proof.Info.Reason;
            return new SymbolicConditionProofResult(
                conditionText,
                string.Equals(reason, "path_unsatisfiable", StringComparison.Ordinal)
                    ? SymbolicTruthValue.Unreachable
                    : truthValue,
                reason,
                conditionFormula);
        }

        private static bool TryCreateSpeculativeCondition(
            SemanticModel semanticModel,
            int position,
            string conditionText,
            out ExpressionSyntax condition,
            out SemanticModel conditionSemanticModel,
            out string failureReason)
        {
            var statement = SyntaxFactory.ParseStatement("if (" + conditionText + ") { }");
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

        private static IReadOnlyList<SyntaxNode> FindQueryNodesOnLine(
            SyntaxNode root,
            SyntaxTree syntaxTree,
            int line,
            CancellationToken cancellationToken,
            bool includeExpressionProgramPoints)
        {
            var lineSpan = SymbolicSourceLocation.GetLineSpan(syntaxTree, line, cancellationToken);
            return FindQueryNodesInSpan(root, lineSpan, includeExpressionProgramPoints);
        }

        private static IReadOnlyList<SyntaxNode> FindQueryNodesInSpan(
            SyntaxNode root,
            TextSpan lineSpan,
            bool includeExpressionProgramPoints)
        {
            if (lineSpan.Length == 0)
            {
                return Array.Empty<SyntaxNode>();
            }

            var seen = new HashSet<string>();
            var nodes = root
                .DescendantTokens(descendIntoTrivia: false)
                .Where(token => token.Span.Length > 0 && token.Span.IntersectsWith(lineSpan))
                .Select(token => FindQueryNode(root, token.SpanStart))
                .Where(static node => node is StatementSyntax or ExpressionSyntax)
                .Where(node => node.Span.IntersectsWith(lineSpan));

            if (includeExpressionProgramPoints)
            {
                nodes = nodes.Concat(root
                    .DescendantNodes(descendIntoTrivia: false)
                    .OfType<ExpressionSyntax>()
                    .Where(expression => expression.Span.Length > 0 && expression.Span.IntersectsWith(lineSpan))
                    .Where(IsUsefulLineExpressionProgramPoint));
            }

            return nodes
                .Where(node => seen.Add(node.RawKind.ToString() + ":" + node.SpanStart.ToString() + ":" + node.Span.End.ToString()))
                .OrderBy(static node => node.SpanStart)
                .ThenBy(static node => node.Span.Length)
                .ToArray();
        }

        private static int GetProgramPointDistance(SyntaxNode candidate, int targetPosition)
        {
            if (ContainsProgramPointPosition(candidate, targetPosition))
            {
                return 0;
            }

            var span = candidate.Span;
            return targetPosition < span.Start
                ? span.Start - targetPosition
                : targetPosition - span.End;
        }

        private static bool ContainsProgramPointPosition(SyntaxNode candidate, int targetPosition)
        {
            var span = candidate.Span;
            return targetPosition >= span.Start && targetPosition <= span.End;
        }

        private static bool IsUsefulLineExpressionProgramPoint(ExpressionSyntax expression)
        {
            return expression is AssignmentExpressionSyntax or AwaitExpressionSyntax or BinaryExpressionSyntax or CastExpressionSyntax or
                ConditionalAccessExpressionSyntax or ConditionalExpressionSyntax or ElementAccessExpressionSyntax or InvocationExpressionSyntax or
                IsPatternExpressionSyntax or MemberAccessExpressionSyntax or ObjectCreationExpressionSyntax or PrefixUnaryExpressionSyntax or
                PostfixUnaryExpressionSyntax or RangeExpressionSyntax or SwitchExpressionSyntax or ThrowExpressionSyntax;
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

        private static SymbolicSourceQueryResult CreateSourceQueryResult(
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
                mergedInvariantText,
                SymbolicInvariantMergeKind.Conjunction);
            return new SymbolicSourceQueryResult(
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
                SymbolicFactInfo.FromState(query.Analysis.PathState));
        }

        private sealed class ProgramPointQueryContext
        {
            public ProgramPointQueryContext(
                SemanticModel semanticModel,
                int position,
                SyntaxNode node,
                SymbolicProgramPointAnalysis analysis)
            {
                SemanticModel = semanticModel;
                Position = position;
                Node = node;
                Analysis = analysis;
            }

            public SemanticModel SemanticModel { get; }

            public int Position { get; }

            public SyntaxNode Node { get; }

            public SymbolicProgramPointAnalysis Analysis { get; }
        }
    }

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
            {
                throw new ArgumentException("File path is required.", nameof(filePath));
            }

            if (line <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(line), "Line must be positive.");
            }

            if (column <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(column), "Column must be positive.");
            }

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

    public sealed class SymbolicLineQueryResult
    {
        internal SymbolicLineQueryResult(
            string filePath,
            int line,
            IReadOnlyList<SymbolicSourceQueryResult> programPoints,
            SymbolicSmtDiagnostics? smtDiagnostics = null)
        {
            FilePath = filePath;
            Line = line;
            ProgramPoints = programPoints ?? throw new ArgumentNullException(nameof(programPoints));
            var factSummary = SymbolicInvariantService.MergeInvariantFacts(ProgramPoints.Select(static point => point.Facts));
            Facts = factSummary.Facts;
            ObservedFactCount = Facts.Count;
            ObservedInvariant = SymbolicInvariantResult.FromFacts(
                Facts,
                factSummary.MergedInvariantText,
                SymbolicInvariantMergeKind.DistinctFactUnion);
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
        }

        public string FilePath { get; }

        public int Line { get; }

        public IReadOnlyList<SymbolicSourceQueryResult> ProgramPoints { get; }

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

        public SymbolicCompactQueryResult ToCompactResult(SymbolicCompactQueryOptions? options = null)
        {
            return SymbolicCompactQueryResult.FromLine(this, options);
        }

        public SymbolicInvariantQueryResult ToInvariantQueryResult(SymbolicCompactQueryOptions? options = null)
        {
            return SymbolicInvariantQueryResult.FromLine(this, options);
        }

        public SymbolicLineQueryResult Filter(SymbolicSourceQueryFilter filter)
        {
            if (filter == null)
            {
                throw new ArgumentNullException(nameof(filter));
            }

            return new SymbolicLineQueryResult(
                FilePath,
                Line,
                ProgramPoints.Where(filter.Matches).ToArray(),
                SmtDiagnostics);
        }
    }

    public sealed class SymbolicSpanQueryResult
    {
        internal SymbolicSpanQueryResult(
            string filePath,
            int spanStart,
            int spanEnd,
            int startLine,
            int startColumn,
            int endLine,
            int endColumn,
            IReadOnlyList<SymbolicSourceQueryResult> programPoints,
            SymbolicSmtDiagnostics? smtDiagnostics = null)
        {
            if (spanStart < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(spanStart), "Span start cannot be negative.");
            }

            if (spanEnd < spanStart)
            {
                throw new ArgumentOutOfRangeException(nameof(spanEnd), "Span end cannot be less than span start.");
            }

            FilePath = filePath;
            SpanStart = spanStart;
            SpanEnd = spanEnd;
            SpanLength = spanEnd - spanStart;
            StartLine = startLine;
            StartColumn = startColumn;
            EndLine = endLine;
            EndColumn = endColumn;
            ProgramPoints = programPoints ?? throw new ArgumentNullException(nameof(programPoints));
            ProgramPointCount = ProgramPoints.Count;
            LinesWithProgramPoints = ProgramPoints
                .Select(static point => point.Line)
                .Distinct()
                .Count();
            var factSummary = SymbolicInvariantService.MergeInvariantFacts(ProgramPoints.Select(static point => point.Facts));
            Facts = factSummary.Facts;
            ObservedFactCount = Facts.Count;
            ObservedInvariant = SymbolicInvariantResult.FromFacts(
                Facts,
                factSummary.MergedInvariantText,
                SymbolicInvariantMergeKind.DistinctFactUnion);
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

        public int ProgramPointCount { get; }

        public IReadOnlyList<SymbolicSourceQueryResult> ProgramPoints { get; }

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

        public SymbolicCompactQueryResult ToCompactResult(SymbolicCompactQueryOptions? options = null)
        {
            return SymbolicCompactQueryResult.FromSpan(this, options);
        }

        public SymbolicInvariantQueryResult ToInvariantQueryResult(SymbolicCompactQueryOptions? options = null)
        {
            return SymbolicInvariantQueryResult.FromSpan(this, options);
        }

        public SymbolicSpanQueryResult Filter(SymbolicSourceQueryFilter filter)
        {
            if (filter == null)
            {
                throw new ArgumentNullException(nameof(filter));
            }

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
    }

    public sealed class SymbolicFileQueryResult
    {
        internal SymbolicFileQueryResult(
            string filePath,
            int lineCount,
            IReadOnlyList<SymbolicLineQueryResult> lines,
            SymbolicSmtDiagnostics? smtDiagnostics = null)
        {
            if (lineCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(lineCount), "Line count cannot be negative.");
            }

            FilePath = filePath;
            LineCount = lineCount;
            Lines = lines ?? throw new ArgumentNullException(nameof(lines));
            LinesWithProgramPoints = Lines.Count;
            ProgramPointCount = Lines.Sum(static line => line.ProgramPoints.Count);
            var programPoints = Lines.SelectMany(static line => line.ProgramPoints).ToArray();
            var factSummary = SymbolicInvariantService.MergeInvariantFacts(programPoints.Select(static point => point.Facts));
            ObservedFacts = factSummary.Facts;
            ObservedFactCount = ObservedFacts.Count;
            ObservedInvariant = SymbolicInvariantResult.FromFacts(
                ObservedFacts,
                factSummary.MergedInvariantText,
                SymbolicInvariantMergeKind.DistinctFactUnion);
            MergedPathFacts = SymbolicMergedPathFacts.FromProgramPoints(programPoints);
            MergedInvariantText = MergedPathFacts.MergedInvariantText;
            MergedInvariant = SymbolicInvariantResult.FromMergedPathFacts(MergedPathFacts);
            ProgramPointSummary = SymbolicProgramPointSummary.FromProgramPoints(programPoints);
            Reachability = ProgramPointSummary.Reachability;
            ConditionProofs = SymbolicConditionProofSummary.FromProgramPoints(programPoints);
            SymbolicFacts = SymbolicFactInfo.Distinct(programPoints.SelectMany(static point => point.SymbolicFacts));
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
                programPoints);
        }

        public string FilePath { get; }

        public int LineCount { get; }

        public int LinesWithProgramPoints { get; }

        public int ProgramPointCount { get; }

        public IReadOnlyList<SymbolicLineQueryResult> Lines { get; }

        public SymbolicProgramPointSummary ProgramPointSummary { get; }

        public IReadOnlyList<string> ObservedFacts { get; }

        public int ObservedFactCount { get; }

        public SymbolicInvariantResult ObservedInvariant { get; }

        public SymbolicMergedPathFacts MergedPathFacts { get; }

        public string MergedInvariantText { get; }

        internal SymbolicInvariantResult MergedInvariant { get; }

        public IReadOnlyList<SymbolicFactInfo> SymbolicFacts { get; }

        public SymbolicInvariantInfo InvariantInfo { get; }

        public SymbolicReachabilitySummary Reachability { get; }

        public IReadOnlyList<SymbolicConditionProofSummary> ConditionProofs { get; }

        public SymbolicSmtDiagnostics SmtDiagnostics { get; }

        public SymbolicInvariantQueryView InvariantQuery { get; }

        public SymbolicCompactQueryResult ToCompactResult(SymbolicCompactQueryOptions? options = null)
        {
            return SymbolicCompactQueryResult.FromFile(this, options);
        }

        public SymbolicInvariantQueryResult ToInvariantQueryResult(SymbolicCompactQueryOptions? options = null)
        {
            return SymbolicInvariantQueryResult.FromFile(this, options);
        }

        public SymbolicFileQueryResult Filter(SymbolicSourceQueryFilter filter)
        {
            if (filter == null)
            {
                throw new ArgumentNullException(nameof(filter));
            }

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

        public static SymbolicInvariantQueryView FromPoint(SymbolicSourceQueryResult result)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

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
            IEnumerable<SymbolicSourceQueryResult>? programPoints = null)
        {
            if (invariant == null)
            {
                throw new ArgumentNullException(nameof(invariant));
            }

            if (mergedPathFacts == null)
            {
                throw new ArgumentNullException(nameof(mergedPathFacts));
            }

            return new SymbolicInvariantQueryView(
                mergedPathFacts.MergedInvariantText,
                invariant.MergeKind,
                mergedPathFacts.AlwaysFacts,
                mergedPathFacts.MaybeFacts,
                mergedPathFacts.ConservativeUnknowns,
                mergedPathFacts.ConservativeUnknownDiagnostics,
                SymbolicInvariantTargetSummary.FromMergedPathFacts(invariant, mergedPathFacts),
                SymbolicInvariantTargetPathSummary.FromProgramPoints(programPoints ?? Array.Empty<SymbolicSourceQueryResult>()),
                mergedPathFacts.CandidateProgramPointCount,
                mergedPathFacts.UnreachableProgramPointCount,
                mergedPathFacts.IsUnreachable,
                reachability,
                proofOutcomes,
                smtDiagnostics);
        }

        private SymbolicInvariantQueryStatus ResolveStatus()
        {
            if (IsUnreachable)
            {
                return SymbolicInvariantQueryStatus.Unreachable;
            }

            if (Reachability.UnknownCount != 0 ||
                Reachability.NotCheckedCount != 0 ||
                ProofOutcomes.UnknownCount != 0 ||
                (SmtDiagnostics.IsConfigured && !SmtDiagnostics.IsEnabled))
            {
                return SymbolicInvariantQueryStatus.Unresolved;
            }

            if (HasMaybeFacts || HasUnknowns)
            {
                return SymbolicInvariantQueryStatus.Conservative;
            }

            return SymbolicInvariantQueryStatus.Exact;
        }

        private string ResolveStatusReason()
        {
            if (IsUnreachable)
            {
                return "all_candidate_program_points_unreachable";
            }

            if (Reachability.UnknownCount != 0 || Reachability.NotCheckedCount != 0)
            {
                return "reachability_not_fully_resolved";
            }

            if (ProofOutcomes.UnknownCount != 0)
            {
                return "proofs_not_fully_resolved";
            }

            if (SmtDiagnostics.IsConfigured && !SmtDiagnostics.IsEnabled)
            {
                return "smt_disabled";
            }

            if (HasUnknowns)
            {
                return "path_varying_targets";
            }

            if (HasMaybeFacts)
            {
                return "path_specific_facts";
            }

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
                    return "Invariant query merged multiple reachable paths and includes conservative unknowns or maybe facts.";
                default:
                    return "Invariant query is exact for the selected reachable program points.";
            }
        }

        private IReadOnlyList<SymbolicInvariantQueryDiagnostic> CreateDiagnostics()
        {
            var diagnostics = new List<SymbolicInvariantQueryDiagnostic>();
            if (IsUnreachable)
            {
                diagnostics.Add(SymbolicInvariantQueryDiagnostic.Create(
                    "SP-SYM-UNREACHABLE",
                    "Info",
                    "No reachable candidate program points contributed invariant facts.",
                    UnreachableProgramPointCount,
                    new[] { "UnreachableProgramPoints=" + UnreachableProgramPointCount.ToString(CultureInfo.InvariantCulture) }));
            }

            if (MaybeFacts.Count != 0)
            {
                diagnostics.Add(SymbolicInvariantQueryDiagnostic.Create(
                    "SP-SYM-MAYBE-FACTS",
                    "Info",
                    "Some path facts are present on only a subset of candidate program points.",
                    MaybeFacts.Count,
                    MaybeFacts));
            }

            if (UnknownFacts.Count != 0)
            {
                diagnostics.Add(SymbolicInvariantQueryDiagnostic.Create(
                    "SP-SYM-CONSERVATIVE-UNKNOWN",
                    "Warning",
                    "The merged invariant contains conservative unknown placeholders for path-varying targets.",
                    UnknownFacts.Count,
                    UnknownFacts));
            }

            if (Reachability.UnknownCount != 0 || Reachability.NotCheckedCount != 0)
            {
                diagnostics.Add(SymbolicInvariantQueryDiagnostic.Create(
                    "SP-SYM-REACHABILITY",
                    "Warning",
                    "Some program point reachability checks are unknown or were not requested.",
                    Reachability.UnknownCount + Reachability.NotCheckedCount,
                    new[]
                    {
                        "Unknown=" + Reachability.UnknownCount.ToString(CultureInfo.InvariantCulture),
                        "NotChecked=" + Reachability.NotCheckedCount.ToString(CultureInfo.InvariantCulture),
                    }));
            }

            if (ProofOutcomes.UnknownCount != 0)
            {
                diagnostics.Add(SymbolicInvariantQueryDiagnostic.Create(
                    "SP-SYM-PROOF-UNKNOWN",
                    "Warning",
                    "Some requested implication proofs were not resolved by bounded SMT.",
                    ProofOutcomes.UnknownCount,
                    new[] { "UnknownProofs=" + ProofOutcomes.UnknownCount.ToString(CultureInfo.InvariantCulture) }));
            }

            if (SmtDiagnostics.IsConfigured && !SmtDiagnostics.IsEnabled)
            {
                diagnostics.Add(SymbolicInvariantQueryDiagnostic.Create(
                    "SP-SYM-SMT-DISABLED",
                    "Warning",
                    "SMT is configured but disabled, so solver-backed reachability and implication proofs are conservative.",
                    1,
                    new[] { "Mode=" + SmtDiagnostics.Mode.ToString() }));
            }

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

        internal static IReadOnlyList<SymbolicInvariantTargetSummary> FromPoint(SymbolicSourceQueryResult result)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            var builders = new Dictionary<string, TargetFactBuilder>(StringComparer.Ordinal);
            foreach (var condition in result.Invariant.Conditions)
            {
                AddCondition(builders, condition, isMaybe: false);
            }

            return BuildSummaries(builders);
        }

        internal static IReadOnlyList<SymbolicInvariantTargetSummary> FromMergedPathFacts(
            SymbolicInvariantResult invariant,
            SymbolicMergedPathFacts mergedPathFacts)
        {
            if (invariant == null)
            {
                throw new ArgumentNullException(nameof(invariant));
            }

            if (mergedPathFacts == null)
            {
                throw new ArgumentNullException(nameof(mergedPathFacts));
            }

            var builders = new Dictionary<string, TargetFactBuilder>(StringComparer.Ordinal);
            foreach (var condition in invariant.Conditions)
            {
                AddCondition(builders, condition, isMaybe: false);
            }

            foreach (var diagnostic in mergedPathFacts.ConservativeUnknownDiagnostics)
            {
                var builder = GetBuilder(builders, diagnostic.Target);
                builder.AddUnknown(diagnostic.UnknownText);
                foreach (var maybeFact in diagnostic.MaybeFacts)
                {
                    builder.AddMaybe(maybeFact);
                }
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
            if (HasUnknowns)
            {
                return "target_has_conservative_unknowns";
            }

            if (HasMaybeFacts)
            {
                return "target_has_path_specific_facts";
            }

            return "target_exact";
        }

        private string ResolveReasonCode()
        {
            if (HasUnknowns)
            {
                return "SP-SYM-TARGET-CONSERVATIVE-UNKNOWN";
            }

            if (HasMaybeFacts)
            {
                return "SP-SYM-TARGET-PATH-SPECIFIC";
            }

            return "SP-SYM-TARGET-EXACT";
        }

        private string CreateSummary()
        {
            if (HasUnknowns)
            {
                return "Facts for this target differ across selected paths; the merged invariant keeps a conservative unknown for the target.";
            }

            if (HasMaybeFacts)
            {
                return "Some facts for this target apply only to a subset of selected paths.";
            }

            return "All selected reachable program points agree on the facts for this target.";
        }

        private static void AddCondition(
            Dictionary<string, TargetFactBuilder> builders,
            SymbolicInvariantCondition condition,
            bool isMaybe)
        {
            var builder = GetBuilder(builders, GetConditionTarget(condition));
            if (condition.IsConservativeUnknown)
            {
                builder.AddUnknown(condition.Text);
            }
            else if (isMaybe)
            {
                builder.AddMaybe(condition.Text);
            }
            else
            {
                builder.AddMust(condition.Text);
            }
        }

        private static string GetConditionTarget(SymbolicInvariantCondition condition)
        {
            if (string.Equals(condition.FormulaKind, "Text", StringComparison.Ordinal) &&
                string.Equals(condition.Target, condition.Text, StringComparison.Ordinal))
            {
                var extracted = TextFactTargetExtraction.TryExtract(condition.Text);
                if (!string.IsNullOrWhiteSpace(extracted))
                {
                    return extracted!;
                }
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
            private readonly List<string> _mustFacts = new();
            private readonly List<string> _maybeFacts = new();
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
                if (!string.IsNullOrWhiteSpace(fact))
                {
                    facts.Add(fact!.Trim());
                }
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
            IEnumerable<SymbolicSourceQueryResult> programPoints)
        {
            if (programPoints == null)
            {
                throw new ArgumentNullException(nameof(programPoints));
            }

            var builders = new Dictionary<string, TargetPathBuilder>(StringComparer.Ordinal);
            foreach (var point in programPoints)
            {
                if (point == null)
                {
                    continue;
                }

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

                foreach (var target in pointTargets)
                {
                    GetBuilder(builders, target).AddProgramPoint(point.Reachability);
                }
            }

            return builders.Values
                .OrderBy(static builder => builder.Target, StringComparer.Ordinal)
                .Select(static builder => builder.ToSummary())
                .ToArray();
        }

        private string ResolveStatusReason()
        {
            if (ProofUnknownCount != 0)
            {
                return "target_has_unknown_proofs";
            }

            if (PathConditionCount != 0)
            {
                return "target_has_path_conditions";
            }

            if (ProofTotalCount != 0)
            {
                return "target_has_proofs";
            }

            return "target_has_no_path_conditions";
        }

        private string ResolveReasonCode()
        {
            if (ProofUnknownCount != 0)
            {
                return "SP-SYM-TARGET-PROOF-UNKNOWN";
            }

            if (PathConditionCount != 0)
            {
                return "SP-SYM-TARGET-PATH-CONDITIONS";
            }

            if (ProofTotalCount != 0)
            {
                return "SP-SYM-TARGET-PROOFS";
            }

            return "SP-SYM-TARGET-NO-PATH-CONDITIONS";
        }

        private string CreateSummary()
        {
            if (ProofUnknownCount != 0)
            {
                return "This target has path facts or proof requests with unresolved bounded-SMT outcomes.";
            }

            if (PathConditionCount != 0)
            {
                return "This target has source-location path conditions available for invariant queries.";
            }

            if (ProofTotalCount != 0)
            {
                return "This target appears in proof requests, but no direct path conditions were recorded for it.";
            }

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
            private int _pathConditionCount;
            private int _smtConditionCount;
            private int _conservativeUnknownCount;
            private int _programPointCount;
            private int _reachableProgramPointCount;
            private int _proofTotalCount;
            private int _proofUnknownCount;
            private int _proofProvenTrueCount;
            private int _proofProvenFalseCount;
            private int _proofUnreachableCount;

            public TargetPathBuilder(string target)
            {
                Target = target;
            }

            public string Target { get; }

            public void AddCondition(SymbolicInvariantCondition condition)
            {
                _pathConditionCount++;
                if (condition.IsSolverBacked)
                {
                    _smtConditionCount++;
                }

                if (condition.IsConservativeUnknown)
                {
                    _conservativeUnknownCount++;
                }

                if (_conditions.Count < DefaultMaxConditions &&
                    !string.IsNullOrWhiteSpace(condition.Text) &&
                    !_conditions.Contains(condition.Text, StringComparer.Ordinal))
                {
                    _conditions.Add(condition.Text);
                }
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
                if (reachability == SymbolicReachability.Reachable)
                {
                    _reachableProgramPointCount++;
                }
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
        Unreachable,
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
            return new SymbolicInvariantQueryDiagnostic(
                code,
                severity,
                message,
                count,
                evidenceArray.Take(DefaultMaxEvidence).ToArray(),
                evidenceArray.Length,
                evidenceArray.Length > DefaultMaxEvidence);
        }
    }

    public sealed class SymbolicCompactQueryOptions
    {
        public const int DefaultMaxLines = 100;
        public const int DefaultMaxProgramPoints = 250;
        public const int DefaultMaxFacts = 50;
        public const int DefaultMaxConditions = 50;
        public const int DefaultMaxProofs = 50;

        public static readonly SymbolicCompactQueryOptions Default = new SymbolicCompactQueryOptions();

        public static readonly SymbolicCompactQueryOptions SummaryOnly = new SymbolicCompactQueryOptions(
            maxLines: 0,
            maxProgramPoints: 0);

        public SymbolicCompactQueryOptions(
            int maxLines = DefaultMaxLines,
            int maxProgramPoints = DefaultMaxProgramPoints,
            int maxFacts = DefaultMaxFacts,
            int maxConditions = DefaultMaxConditions,
            int maxProofs = DefaultMaxProofs,
            IEnumerable<string>? invariantTargets = null)
        {
            MaxLines = ValidateNonNegative(maxLines, nameof(maxLines));
            MaxProgramPoints = ValidateNonNegative(maxProgramPoints, nameof(maxProgramPoints));
            MaxFacts = ValidateNonNegative(maxFacts, nameof(maxFacts));
            MaxConditions = ValidateNonNegative(maxConditions, nameof(maxConditions));
            MaxProofs = ValidateNonNegative(maxProofs, nameof(maxProofs));
            InvariantTargets = NormalizeInvariantTargets(invariantTargets);
        }

        public int MaxLines { get; }

        public int MaxProgramPoints { get; }

        public int MaxFacts { get; }

        public int MaxConditions { get; }

        public int MaxProofs { get; }

        public IReadOnlyList<string> InvariantTargets { get; }

        public bool HasInvariantTargetFilter => InvariantTargets.Count != 0;

        private static int ValidateNonNegative(int value, string paramName)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(paramName, "Compact output limits cannot be negative.");
            }

            return value;
        }

        private static IReadOnlyList<string> NormalizeInvariantTargets(IEnumerable<string>? targets)
        {
            if (targets == null)
            {
                return Array.Empty<string>();
            }

            return targets
                .Where(static target => !string.IsNullOrWhiteSpace(target))
                .Select(static target => target!.Trim())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static target => target, StringComparer.Ordinal)
                .ToArray();
        }
    }

    public sealed class SymbolicCompactSourceQueryDescriptor
    {
        private SymbolicCompactSourceQueryDescriptor(
            string kind,
            string filePath,
            int? line,
            int? column,
            int? position,
            int? spanStart,
            int? spanEnd,
            int? spanLength,
            int? startLine,
            int? startColumn,
            int? endLine,
            int? endColumn,
            string? nodeKind,
            string? methodName,
            string? programPointKind)
        {
            Kind = kind ?? string.Empty;
            FilePath = filePath ?? string.Empty;
            Line = line;
            Column = column;
            Position = position;
            SpanStart = spanStart;
            SpanEnd = spanEnd;
            SpanLength = spanLength;
            StartLine = startLine;
            StartColumn = startColumn;
            EndLine = endLine;
            EndColumn = endColumn;
            NodeKind = nodeKind;
            MethodName = string.IsNullOrWhiteSpace(methodName) ? null : methodName;
            ProgramPointKind = programPointKind;
        }

        public string Kind { get; }

        public string FilePath { get; }

        public int? Line { get; }

        public int? Column { get; }

        public int? Position { get; }

        public int? SpanStart { get; }

        public int? SpanEnd { get; }

        public int? SpanLength { get; }

        public int? StartLine { get; }

        public int? StartColumn { get; }

        public int? EndLine { get; }

        public int? EndColumn { get; }

        public string? NodeKind { get; }

        public string? MethodName { get; }

        public string? ProgramPointKind { get; }

        internal static SymbolicCompactSourceQueryDescriptor FromCompactResult(SymbolicCompactQueryResult result)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            return new SymbolicCompactSourceQueryDescriptor(
                result.Kind,
                result.FilePath,
                result.Line,
                result.Column,
                result.Position,
                result.QuerySpanStart,
                result.QuerySpanEnd,
                result.QuerySpanLength,
                result.QueryStartLine,
                result.QueryStartColumn,
                result.QueryEndLine,
                result.QueryEndColumn,
                result.NodeKind,
                result.MethodName,
                result.ProgramPointKind);
        }
    }

    public sealed class SymbolicInvariantQueryFocus
    {
        private SymbolicInvariantQueryFocus(
            string scopeKind,
            string filePath,
            bool hasSourceLocation,
            int? line,
            int? column,
            int? position,
            int? requestedLine,
            int? requestedColumn,
            int? requestedPosition,
            int? requestedPositionDistance,
            bool? containsRequestedPosition,
            int? spanStart,
            int? spanEnd,
            int? spanLength,
            int? startLine,
            int? startColumn,
            int? endLine,
            int? endColumn,
            string? nodeKind,
            string? methodName,
            string? programPointKind,
            string reachabilityStatus,
            string reachabilityReason,
            int programPointCount,
            int reachabilityKnownCount)
        {
            ScopeKind = scopeKind ?? string.Empty;
            FilePath = filePath ?? string.Empty;
            HasSourceLocation = hasSourceLocation;
            Line = line;
            Column = column;
            Position = position;
            RequestedLine = requestedLine;
            RequestedColumn = requestedColumn;
            RequestedPosition = requestedPosition;
            RequestedPositionDistance = requestedPositionDistance;
            ContainsRequestedPosition = containsRequestedPosition;
            SpanStart = spanStart;
            SpanEnd = spanEnd;
            SpanLength = spanLength;
            StartLine = startLine;
            StartColumn = startColumn;
            EndLine = endLine;
            EndColumn = endColumn;
            NodeKind = nodeKind;
            MethodName = string.IsNullOrWhiteSpace(methodName) ? null : methodName;
            ProgramPointKind = programPointKind;
            ReachabilityStatus = reachabilityStatus ?? string.Empty;
            ReachabilityReason = reachabilityReason ?? string.Empty;
            ProgramPointCount = programPointCount;
            ReachabilityKnownCount = reachabilityKnownCount;
        }

        public string ScopeKind { get; }

        public string FilePath { get; }

        public bool HasSourceLocation { get; }

        public int? Line { get; }

        public int? Column { get; }

        public int? Position { get; }

        public int? RequestedLine { get; }

        public int? RequestedColumn { get; }

        public int? RequestedPosition { get; }

        public int? RequestedPositionDistance { get; }

        public bool? ContainsRequestedPosition { get; }

        public int? SpanStart { get; }

        public int? SpanEnd { get; }

        public int? SpanLength { get; }

        public int? StartLine { get; }

        public int? StartColumn { get; }

        public int? EndLine { get; }

        public int? EndColumn { get; }

        public string? NodeKind { get; }

        public string? MethodName { get; }

        public string? ProgramPointKind { get; }

        public string ReachabilityStatus { get; }

        public string ReachabilityReason { get; }

        public int ProgramPointCount { get; }

        public int ReachabilityKnownCount { get; }

        public bool HasKnownReachability => ReachabilityKnownCount != 0;

        internal static SymbolicInvariantQueryFocus FromCompactResult(SymbolicCompactQueryResult result)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            var reachabilityStatus = ResolveReachabilityStatus(result);
            return new SymbolicInvariantQueryFocus(
                result.Kind,
                result.FilePath,
                result.Line.HasValue ||
                    result.Position.HasValue ||
                    result.QuerySpanStart.HasValue ||
                    result.QueryStartLine.HasValue,
                result.Line,
                result.Column,
                result.Position,
                result.RequestedLine,
                result.RequestedColumn,
                result.RequestedPosition,
                result.RequestedPositionDistance,
                result.ContainsRequestedPosition,
                result.QuerySpanStart,
                result.QuerySpanEnd,
                result.QuerySpanLength,
                result.QueryStartLine,
                result.QueryStartColumn,
                result.QueryEndLine,
                result.QueryEndColumn,
                result.NodeKind,
                result.MethodName,
                result.ProgramPointKind,
                reachabilityStatus,
                ResolveReachabilityReason(result, reachabilityStatus),
                result.ProgramPointCount,
                result.Reachability.ReachableCount + result.Reachability.UnreachableCount);
        }

        private static string ResolveReachabilityStatus(SymbolicCompactQueryResult result)
        {
            if (!string.IsNullOrWhiteSpace(result.PointReachability))
            {
                return result.PointReachability!;
            }

            if (result.ProgramPointCount == 0)
            {
                return "NoProgramPoints";
            }

            var reachability = result.Reachability;
            if (reachability.ReachableCount == result.ProgramPointCount)
            {
                return SymbolicReachability.Reachable.ToString();
            }

            if (reachability.UnreachableCount == result.ProgramPointCount)
            {
                return SymbolicReachability.Unreachable.ToString();
            }

            if (reachability.UnknownCount == result.ProgramPointCount)
            {
                return SymbolicReachability.Unknown.ToString();
            }

            if (reachability.NotCheckedCount == result.ProgramPointCount)
            {
                return SymbolicReachability.NotChecked.ToString();
            }

            return "Mixed";
        }

        private static string ResolveReachabilityReason(
            SymbolicCompactQueryResult result,
            string reachabilityStatus)
        {
            if (!string.IsNullOrWhiteSpace(result.ReachabilityReason))
            {
                return result.ReachabilityReason!;
            }

            if (result.ProgramPointCount == 0)
            {
                return "no_program_points";
            }

            if (string.Equals(reachabilityStatus, "Mixed", StringComparison.Ordinal))
            {
                return "mixed_program_point_reachability";
            }

            return "uniform_program_point_reachability";
        }
    }

    public sealed class SymbolicCompactQueryResult
    {
        private SymbolicCompactQueryResult(
            string kind,
            string filePath,
            int? line,
            int? column,
            int? position,
            string? nodeKind,
            string? methodName,
            string? programPointKind,
            int? nodeSpanStart,
            int? nodeSpanEnd,
            int? nodeSpanLength,
            int? nodeStartLine,
            int? nodeStartColumn,
            int? nodeEndLine,
            int? nodeEndColumn,
            string? pointReachability,
            string? reachabilityReason,
            int? lineCount,
            int linesWithProgramPoints,
            int programPointCount,
            SymbolicCompactInvariantSummary observedInvariant,
            SymbolicCompactInvariantSummary conservativeInvariant,
            SymbolicCompactInvariantQueryView invariantQuery,
            SymbolicReachabilitySummary reachability,
            SymbolicProgramPointSummary programPointSummary,
            IReadOnlyList<SymbolicConditionProofSummary> conditionProofs,
            IReadOnlyList<SymbolicCompactLineResult> lines,
            IReadOnlyList<SymbolicCompactProgramPointResult> programPoints,
            SymbolicCompactSmtDiagnostics smtDiagnostics,
            SymbolicCompactOutputTruncation truncation,
            int? requestedLine = null,
            int? requestedColumn = null,
            int? requestedPosition = null,
            int? requestedPositionDistance = null,
            bool? containsRequestedPosition = null)
        {
            Kind = kind ?? throw new ArgumentNullException(nameof(kind));
            FilePath = filePath ?? string.Empty;
            Line = line;
            Column = column;
            Position = position;
            RequestedLine = requestedLine;
            RequestedColumn = requestedColumn;
            RequestedPosition = requestedPosition;
            RequestedPositionDistance = requestedPositionDistance;
            ContainsRequestedPosition = containsRequestedPosition;
            NodeKind = nodeKind;
            MethodName = methodName;
            ProgramPointKind = programPointKind;
            NodeSpanStart = nodeSpanStart;
            NodeSpanEnd = nodeSpanEnd;
            NodeSpanLength = nodeSpanLength;
            NodeStartLine = nodeStartLine;
            NodeStartColumn = nodeStartColumn;
            NodeEndLine = nodeEndLine;
            NodeEndColumn = nodeEndColumn;
            PointReachability = pointReachability;
            ReachabilityReason = reachabilityReason;
            LineCount = lineCount;
            LinesWithProgramPoints = linesWithProgramPoints;
            ProgramPointCount = programPointCount;
            ObservedInvariant = observedInvariant ?? throw new ArgumentNullException(nameof(observedInvariant));
            ConservativeInvariant = conservativeInvariant ?? throw new ArgumentNullException(nameof(conservativeInvariant));
            InvariantQuery = invariantQuery ?? throw new ArgumentNullException(nameof(invariantQuery));
            MergedInvariantText = ConservativeInvariant.Text;
            Reachability = reachability ?? throw new ArgumentNullException(nameof(reachability));
            ProgramPointSummary = programPointSummary ?? throw new ArgumentNullException(nameof(programPointSummary));
            ProofOutcomes = ProgramPointSummary.ProofOutcomes;
            ConditionProofs = conditionProofs ?? throw new ArgumentNullException(nameof(conditionProofs));
            Lines = lines ?? throw new ArgumentNullException(nameof(lines));
            ProgramPoints = programPoints ?? throw new ArgumentNullException(nameof(programPoints));
            SmtDiagnostics = smtDiagnostics ?? throw new ArgumentNullException(nameof(smtDiagnostics));
            AnalysisSummary = SymbolicCompactAnalysisSummary.From(
                InvariantQuery,
                ProgramPointSummary,
                SmtDiagnostics);
            QueryDescriptor = SymbolicCompactSourceQueryDescriptor.FromCompactResult(this);
            Truncation = truncation ?? throw new ArgumentNullException(nameof(truncation));
        }

        public string Kind { get; }

        public int SchemaVersion => 1;

        public string FilePath { get; }

        public SymbolicCompactSourceQueryDescriptor QueryDescriptor { get; }

        public int? Line { get; }

        public int? Column { get; }

        public int? Position { get; }

        public int? RequestedLine { get; }

        public int? RequestedColumn { get; }

        public int? RequestedPosition { get; }

        public int? RequestedPositionDistance { get; }

        public bool? ContainsRequestedPosition { get; }

        public string? NodeKind { get; }

        public string? MethodName { get; }

        public string? ProgramPointKind { get; }

        public int? NodeSpanStart { get; }

        public int? NodeSpanEnd { get; }

        public int? NodeSpanLength { get; }

        public int? NodeStartLine { get; }

        public int? NodeStartColumn { get; }

        public int? NodeEndLine { get; }

        public int? NodeEndColumn { get; }

        public int? QuerySpanStart => string.Equals(Kind, "span", StringComparison.Ordinal) ? NodeSpanStart : null;

        public int? QuerySpanEnd => string.Equals(Kind, "span", StringComparison.Ordinal) ? NodeSpanEnd : null;

        public int? QuerySpanLength => string.Equals(Kind, "span", StringComparison.Ordinal) ? NodeSpanLength : null;

        public int? QueryStartLine => string.Equals(Kind, "span", StringComparison.Ordinal) ? NodeStartLine : null;

        public int? QueryStartColumn => string.Equals(Kind, "span", StringComparison.Ordinal) ? NodeStartColumn : null;

        public int? QueryEndLine => string.Equals(Kind, "span", StringComparison.Ordinal) ? NodeEndLine : null;

        public int? QueryEndColumn => string.Equals(Kind, "span", StringComparison.Ordinal) ? NodeEndColumn : null;

        public string? PointReachability { get; }

        public string? ReachabilityReason { get; }

        public int? LineCount { get; }

        public int LinesWithProgramPoints { get; }

        public int ProgramPointCount { get; }

        public SymbolicCompactInvariantSummary ObservedInvariant { get; }

        public SymbolicCompactInvariantSummary ConservativeInvariant { get; }

        public SymbolicCompactInvariantQueryView InvariantQuery { get; }

        public string MergedInvariantText { get; }

        public SymbolicReachabilitySummary Reachability { get; }

        public SymbolicProgramPointSummary ProgramPointSummary { get; }

        public SymbolicProofOutcomeSummary ProofOutcomes { get; }

        public IReadOnlyList<SymbolicConditionProofSummary> ConditionProofs { get; }

        public IReadOnlyList<SymbolicCompactLineResult> Lines { get; }

        public IReadOnlyList<SymbolicCompactProgramPointResult> ProgramPoints { get; }

        public SymbolicCompactSmtDiagnostics SmtDiagnostics { get; }

        public SymbolicCompactAnalysisSummary AnalysisSummary { get; }

        public SymbolicCompactOutputTruncation Truncation { get; }

        public static SymbolicCompactQueryResult FromPoint(
            SymbolicSourceQueryResult result,
            SymbolicCompactQueryOptions? options = null)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            var normalizedOptions = options ?? SymbolicCompactQueryOptions.Default;
            var sourcePoints = new[] { result };
            var programPoint = normalizedOptions.MaxProgramPoints == 0
                ? null
                : SymbolicCompactProgramPointResult.FromResult(result, normalizedOptions);
            var programPoints = programPoint == null
                ? Array.Empty<SymbolicCompactProgramPointResult>()
                : new[] { programPoint };
            var conditionProofSummaries = SymbolicInvariantTargetFilter.ApplyToProofSummaries(
                SymbolicConditionProofSummary.FromProgramPoints(sourcePoints),
                normalizedOptions);
            var observedInvariant = SymbolicCompactInvariantSummary.FromObservedFacts(
                SymbolicInvariantResult.FromFacts(result.Facts),
                result.Facts,
                normalizedOptions);
            var conservativeInvariant = SymbolicCompactInvariantSummary.FromInvariant(
                result.Invariant,
                null,
                normalizedOptions);
            var invariantQuery = SymbolicCompactInvariantQueryView.FromQueryView(
                result.InvariantQuery,
                normalizedOptions);

            return new SymbolicCompactQueryResult(
                "point",
                result.FilePath,
                result.Line,
                result.Column,
                result.Position,
                result.NodeKind,
                result.MethodName,
                result.ProgramPointKind,
                result.NodeSpanStart,
                result.NodeSpanEnd,
                result.NodeSpanLength,
                result.NodeStartLine,
                result.NodeStartColumn,
                result.NodeEndLine,
                result.NodeEndColumn,
                result.Reachability.ToString(),
                result.ReachabilityReason,
                null,
                1,
                1,
                observedInvariant,
                conservativeInvariant,
                invariantQuery,
                SymbolicReachabilitySummary.FromProgramPoints(sourcePoints),
                SymbolicProgramPointSummary.FromProgramPoints(sourcePoints),
                SymbolicCompactProjection.Take(
                    conditionProofSummaries,
                    normalizedOptions.MaxProofs),
                Array.Empty<SymbolicCompactLineResult>(),
                programPoints,
                SymbolicCompactSmtDiagnostics.FromDiagnostics(result.SmtDiagnostics),
                SymbolicCompactOutputTruncation.Combine(
                    new SymbolicCompactOutputTruncation(
                        false,
                        programPoints.Length == 0,
                        false,
                        false,
                        conditionProofSummaries.Count > normalizedOptions.MaxProofs),
                    programPoint == null
                        ? new SymbolicCompactOutputTruncation(false, false, false, false, false)
                        : programPoint.Truncation,
                    SymbolicCompactOutputTruncation.FromInvariant(observedInvariant),
                    SymbolicCompactOutputTruncation.FromInvariant(conservativeInvariant)),
                result.RequestedLine,
                result.RequestedColumn,
                result.RequestedPosition,
                result.RequestedPositionDistance,
                result.ContainsRequestedPosition);
        }

        public static SymbolicCompactQueryResult FromLine(
            SymbolicLineQueryResult result,
            SymbolicCompactQueryOptions? options = null)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            var normalizedOptions = options ?? SymbolicCompactQueryOptions.Default;
            var lineResult = SymbolicCompactLineResult.FromResult(
                result,
                normalizedOptions,
                normalizedOptions.MaxProgramPoints);

            return new SymbolicCompactQueryResult(
                "line",
                result.FilePath,
                result.Line,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                result.ProgramPoints.Count == 0 ? 0 : 1,
                result.ProgramPoints.Count,
                lineResult.ObservedInvariant,
                lineResult.ConservativeInvariant,
                lineResult.InvariantQuery,
                lineResult.Reachability,
                result.ProgramPointSummary,
                lineResult.ConditionProofs,
                Array.Empty<SymbolicCompactLineResult>(),
                lineResult.ProgramPoints,
                lineResult.SmtDiagnostics,
                lineResult.Truncation);
        }

        public static SymbolicCompactQueryResult FromSpan(
            SymbolicSpanQueryResult result,
            SymbolicCompactQueryOptions? options = null)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            var normalizedOptions = options ?? SymbolicCompactQueryOptions.Default;
            var programPoints = SymbolicCompactProjection
                .Take(result.ProgramPoints, normalizedOptions.MaxProgramPoints)
                .Select(point => SymbolicCompactProgramPointResult.FromResult(point, normalizedOptions))
                .ToArray();
            var observedInvariant = SymbolicCompactInvariantSummary.FromObservedFacts(
                result.ObservedInvariant,
                result.Facts,
                normalizedOptions);
            var conservativeInvariant = SymbolicCompactInvariantSummary.FromInvariant(
                result.MergedInvariant,
                result.MergedPathFacts,
                normalizedOptions);
            var conditionProofSummaries = SymbolicInvariantTargetFilter.ApplyToProofSummaries(
                result.ConditionProofs,
                normalizedOptions);
            var conditionProofs = SymbolicCompactProjection.Take(
                conditionProofSummaries,
                normalizedOptions.MaxProofs);
            var truncation = SymbolicCompactOutputTruncation.Combine(
                new SymbolicCompactOutputTruncation(
                    false,
                    result.ProgramPoints.Count > programPoints.Length,
                    false,
                    false,
                    conditionProofSummaries.Count > normalizedOptions.MaxProofs),
                SymbolicCompactOutputTruncation.FromInvariant(observedInvariant),
                SymbolicCompactOutputTruncation.FromInvariant(conservativeInvariant),
                SymbolicCompactOutputTruncation.Combine(programPoints.Select(static point => point.Truncation)));

            return new SymbolicCompactQueryResult(
                "span",
                result.FilePath,
                null,
                null,
                null,
                null,
                null,
                null,
                result.SpanStart,
                result.SpanEnd,
                result.SpanLength,
                result.StartLine,
                result.StartColumn,
                result.EndLine,
                result.EndColumn,
                null,
                null,
                null,
                result.LinesWithProgramPoints,
                result.ProgramPointCount,
                observedInvariant,
                conservativeInvariant,
                SymbolicCompactInvariantQueryView.FromQueryView(result.InvariantQuery, normalizedOptions),
                result.Reachability,
                result.ProgramPointSummary,
                conditionProofs,
                Array.Empty<SymbolicCompactLineResult>(),
                programPoints,
                SymbolicCompactSmtDiagnostics.FromDiagnostics(result.SmtDiagnostics),
                truncation);
        }

        public static SymbolicCompactQueryResult FromFile(
            SymbolicFileQueryResult result,
            SymbolicCompactQueryOptions? options = null)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            var normalizedOptions = options ?? SymbolicCompactQueryOptions.Default;
            var lineResults = new List<SymbolicCompactLineResult>();
            var remainingProgramPoints = normalizedOptions.MaxProgramPoints;
            foreach (var line in result.Lines)
            {
                if (lineResults.Count >= normalizedOptions.MaxLines)
                {
                    break;
                }

                var pointLimit = remainingProgramPoints;
                lineResults.Add(SymbolicCompactLineResult.FromResult(line, normalizedOptions, pointLimit));
                if (remainingProgramPoints > 0)
                {
                    remainingProgramPoints -= Math.Min(line.ProgramPoints.Count, pointLimit);
                }
            }

            var observedInvariant = SymbolicCompactInvariantSummary.FromObservedFacts(
                result.ObservedInvariant,
                result.ObservedFacts,
                normalizedOptions);
            var conservativeInvariant = SymbolicCompactInvariantSummary.FromInvariant(
                result.MergedInvariant,
                result.MergedPathFacts,
                normalizedOptions);
            var conditionProofSummaries = SymbolicInvariantTargetFilter.ApplyToProofSummaries(
                result.ConditionProofs,
                normalizedOptions);
            var selectedProgramPointCount = lineResults.Sum(static line => line.ProgramPoints.Count);
            var truncation = SymbolicCompactOutputTruncation.Combine(
                new SymbolicCompactOutputTruncation(
                    result.Lines.Count > lineResults.Count,
                    result.ProgramPointCount > selectedProgramPointCount,
                    false,
                    false,
                    conditionProofSummaries.Count > normalizedOptions.MaxProofs),
                SymbolicCompactOutputTruncation.FromInvariant(observedInvariant),
                SymbolicCompactOutputTruncation.FromInvariant(conservativeInvariant),
                SymbolicCompactOutputTruncation.Combine(lineResults.Select(static line => line.Truncation)));

            return new SymbolicCompactQueryResult(
                "file",
                result.FilePath,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                result.LineCount,
                result.LinesWithProgramPoints,
                result.ProgramPointCount,
                observedInvariant,
                conservativeInvariant,
                SymbolicCompactInvariantQueryView.FromQueryView(result.InvariantQuery, normalizedOptions),
                result.Reachability,
                result.ProgramPointSummary,
                SymbolicCompactProjection.Take(conditionProofSummaries, normalizedOptions.MaxProofs),
                lineResults,
                Array.Empty<SymbolicCompactProgramPointResult>(),
                SymbolicCompactSmtDiagnostics.FromDiagnostics(result.SmtDiagnostics),
                truncation);
        }
    }

    public sealed class SymbolicInvariantQueryResult
    {
        private SymbolicInvariantQueryResult(
            string scopeKind,
            string filePath,
            SymbolicCompactSourceQueryDescriptor queryDescriptor,
            SymbolicInvariantQuerySummary querySummary,
            SymbolicInvariantQueryFocus focus,
            string mergedInvariantText,
            SymbolicCompactInvariantQueryView invariantQuery,
            SymbolicCompactAnalysisSummary analysisSummary,
            SymbolicReachabilitySummary reachability,
            SymbolicProgramPointSummary programPointSummary,
            IReadOnlyList<SymbolicConditionProofSummary> conditionProofs,
            bool conditionProofsTruncated,
            SymbolicCompactSmtDiagnostics smtDiagnostics,
            int? lineCount,
            int linesWithProgramPoints,
            int programPointCount)
        {
            ScopeKind = scopeKind ?? string.Empty;
            FilePath = filePath ?? string.Empty;
            QueryDescriptor = queryDescriptor ?? throw new ArgumentNullException(nameof(queryDescriptor));
            QuerySummary = querySummary ?? throw new ArgumentNullException(nameof(querySummary));
            Focus = focus ?? throw new ArgumentNullException(nameof(focus));
            MergedInvariantText = mergedInvariantText ?? string.Empty;
            InvariantQuery = invariantQuery ?? throw new ArgumentNullException(nameof(invariantQuery));
            AnalysisSummary = analysisSummary ?? throw new ArgumentNullException(nameof(analysisSummary));
            Reachability = reachability ?? throw new ArgumentNullException(nameof(reachability));
            ProgramPointSummary = programPointSummary ?? throw new ArgumentNullException(nameof(programPointSummary));
            ProofOutcomes = ProgramPointSummary.ProofOutcomes;
            ConditionProofs = conditionProofs ?? throw new ArgumentNullException(nameof(conditionProofs));
            ConditionProofsTruncated = conditionProofsTruncated;
            SmtDiagnostics = smtDiagnostics ?? throw new ArgumentNullException(nameof(smtDiagnostics));
            LineCount = lineCount;
            LinesWithProgramPoints = linesWithProgramPoints;
            ProgramPointCount = programPointCount;
        }

        public string Kind => "invariantQuery";

        public int SchemaVersion => 1;

        public string ScopeKind { get; }

        public string FilePath { get; }

        public SymbolicCompactSourceQueryDescriptor QueryDescriptor { get; }

        public SymbolicInvariantQuerySummary QuerySummary { get; }

        public SymbolicInvariantQueryFocus Focus { get; }

        public string MergedInvariantText { get; }

        public SymbolicCompactInvariantQueryView InvariantQuery { get; }

        public SymbolicCompactAnalysisSummary AnalysisSummary { get; }

        public SymbolicReachabilitySummary Reachability { get; }

        public SymbolicProgramPointSummary ProgramPointSummary { get; }

        public SymbolicProofOutcomeSummary ProofOutcomes { get; }

        public int ConditionProofCount => ConditionProofs.Count;

        public IReadOnlyList<SymbolicConditionProofSummary> ConditionProofs { get; }

        public bool ConditionProofsTruncated { get; }

        public SymbolicCompactSmtDiagnostics SmtDiagnostics { get; }

        public int? LineCount { get; }

        public int LinesWithProgramPoints { get; }

        public int ProgramPointCount { get; }

        public static SymbolicInvariantQueryResult FromPoint(
            SymbolicSourceQueryResult result,
            SymbolicCompactQueryOptions? options = null)
        {
            var normalizedOptions = NormalizeOptions(options);
            return FromCompactResult(SymbolicCompactQueryResult.FromPoint(result, normalizedOptions), normalizedOptions);
        }

        public static SymbolicInvariantQueryResult FromLine(
            SymbolicLineQueryResult result,
            SymbolicCompactQueryOptions? options = null)
        {
            var normalizedOptions = NormalizeOptions(options);
            return FromCompactResult(SymbolicCompactQueryResult.FromLine(result, normalizedOptions), normalizedOptions);
        }

        public static SymbolicInvariantQueryResult FromSpan(
            SymbolicSpanQueryResult result,
            SymbolicCompactQueryOptions? options = null)
        {
            var normalizedOptions = NormalizeOptions(options);
            return FromCompactResult(SymbolicCompactQueryResult.FromSpan(result, normalizedOptions), normalizedOptions);
        }

        public static SymbolicInvariantQueryResult FromFile(
            SymbolicFileQueryResult result,
            SymbolicCompactQueryOptions? options = null)
        {
            var normalizedOptions = NormalizeOptions(options);
            return FromCompactResult(SymbolicCompactQueryResult.FromFile(result, normalizedOptions), normalizedOptions);
        }

        private static SymbolicInvariantQueryResult FromCompactResult(
            SymbolicCompactQueryResult result,
            SymbolicCompactQueryOptions options)
        {
            return new SymbolicInvariantQueryResult(
                result.Kind,
                result.FilePath,
                result.QueryDescriptor,
                SymbolicInvariantQuerySummary.FromCompactResult(result, options),
                SymbolicInvariantQueryFocus.FromCompactResult(result),
                result.InvariantQuery.Text,
                result.InvariantQuery,
                result.AnalysisSummary,
                result.Reachability,
                result.ProgramPointSummary,
                result.ConditionProofs,
                result.Truncation.Proofs,
                result.SmtDiagnostics,
                result.LineCount,
                result.LinesWithProgramPoints,
                result.ProgramPointCount);
        }

        private static SymbolicCompactQueryOptions NormalizeOptions(SymbolicCompactQueryOptions? options)
        {
            var normalizedOptions = options ?? SymbolicCompactQueryOptions.Default;
            return new SymbolicCompactQueryOptions(
                maxLines: 0,
                maxProgramPoints: 0,
                maxFacts: normalizedOptions.MaxFacts,
                maxConditions: normalizedOptions.MaxConditions,
                maxProofs: normalizedOptions.MaxProofs,
                invariantTargets: normalizedOptions.InvariantTargets);
        }
    }

    public sealed class SymbolicInvariantQuerySummary
    {
        private const int MaxSummaryReasons = 16;
        private const int MaxSummaryTargets = 32;

        private SymbolicInvariantQuerySummary(
            int outputMaxFacts,
            int outputMaxConditions,
            int outputMaxProofs,
            bool hasTruncatedOutput,
            bool factsTruncated,
            bool conditionsTruncated,
            bool proofsTruncated,
            bool hasUnresolvedAnalysis,
            int programPointCount,
            int totalPathConditionCount,
            int maxPathConditionCount,
            int proofTotalCount,
            int proofUnknownCount,
            int conservativeUnknownCount,
            int targetCount,
            IReadOnlyList<string> targets,
            bool targetsTruncated,
            int reasonCount,
            IReadOnlyList<string> reasons,
            bool reasonsTruncated,
            bool smtConfigured,
            bool smtEnabled,
            int smtExecutedQueryCount,
            int smtCacheEntryCount,
            int smtQueryTimeoutMs,
            int smtMethodBudgetMs,
            int smtMaxPathConditions,
            int smtMaxExpressionNodes,
            bool pathConditionBudgetExceeded)
        {
            OutputMaxFacts = outputMaxFacts;
            OutputMaxConditions = outputMaxConditions;
            OutputMaxProofs = outputMaxProofs;
            HasTruncatedOutput = hasTruncatedOutput;
            FactsTruncated = factsTruncated;
            ConditionsTruncated = conditionsTruncated;
            ProofsTruncated = proofsTruncated;
            HasUnresolvedAnalysis = hasUnresolvedAnalysis;
            ProgramPointCount = programPointCount;
            TotalPathConditionCount = totalPathConditionCount;
            MaxPathConditionCount = maxPathConditionCount;
            ProofTotalCount = proofTotalCount;
            ProofUnknownCount = proofUnknownCount;
            ConservativeUnknownCount = conservativeUnknownCount;
            TargetCount = targetCount;
            Targets = targets ?? throw new ArgumentNullException(nameof(targets));
            TargetsTruncated = targetsTruncated;
            ReasonCount = reasonCount;
            Reasons = reasons ?? throw new ArgumentNullException(nameof(reasons));
            ReasonsTruncated = reasonsTruncated;
            SmtConfigured = smtConfigured;
            SmtEnabled = smtEnabled;
            SmtExecutedQueryCount = smtExecutedQueryCount;
            SmtCacheEntryCount = smtCacheEntryCount;
            SmtQueryTimeoutMs = smtQueryTimeoutMs;
            SmtMethodBudgetMs = smtMethodBudgetMs;
            SmtMaxPathConditions = smtMaxPathConditions;
            SmtMaxExpressionNodes = smtMaxExpressionNodes;
            PathConditionBudgetExceeded = pathConditionBudgetExceeded;
        }

        public int OutputMaxFacts { get; }

        public int OutputMaxConditions { get; }

        public int OutputMaxProofs { get; }

        public bool HasTruncatedOutput { get; }

        public bool FactsTruncated { get; }

        public bool ConditionsTruncated { get; }

        public bool ProofsTruncated { get; }

        public bool HasUnresolvedAnalysis { get; }

        public int ProgramPointCount { get; }

        public int TotalPathConditionCount { get; }

        public int MaxPathConditionCount { get; }

        public int ProofTotalCount { get; }

        public int ProofUnknownCount { get; }

        public int ConservativeUnknownCount { get; }

        public int TargetCount { get; }

        public IReadOnlyList<string> Targets { get; }

        public bool TargetsTruncated { get; }

        public int ReasonCount { get; }

        public IReadOnlyList<string> Reasons { get; }

        public bool ReasonsTruncated { get; }

        public bool SmtConfigured { get; }

        public bool SmtEnabled { get; }

        public int SmtExecutedQueryCount { get; }

        public int SmtCacheEntryCount { get; }

        public int SmtQueryTimeoutMs { get; }

        public int SmtMethodBudgetMs { get; }

        public int SmtMaxPathConditions { get; }

        public int SmtMaxExpressionNodes { get; }

        public bool PathConditionBudgetExceeded { get; }

        internal static SymbolicInvariantQuerySummary FromCompactResult(
            SymbolicCompactQueryResult result,
            SymbolicCompactQueryOptions options)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            var targetLimit = Math.Min(options.MaxConditions, MaxSummaryTargets);
            var reasonLimit = Math.Min(options.MaxConditions, MaxSummaryReasons);
            var targets = GetTargets(result).ToArray();
            var targetCount = result.InvariantQuery.HasTargetFilter
                ? targets.Length
                : Math.Max(
                    targets.Length,
                    Math.Max(result.ConservativeInvariant.TargetCount, result.ObservedInvariant.TargetCount));
            var targetView = SymbolicCompactProjection.Take(targets, targetLimit);
            var targetTruncated =
                targetCount > targetView.Count ||
                (!result.InvariantQuery.HasTargetFilter &&
                    (result.ConservativeInvariant.TargetsTruncated ||
                     result.ObservedInvariant.TargetsTruncated));

            var reasons = GetReasons(result).ToArray();
            var reasonView = SymbolicCompactProjection.Take(reasons, reasonLimit);
            var truncation = result.Truncation;
            var analysisSummary = result.AnalysisSummary;
            var smtDiagnostics = result.SmtDiagnostics;
            var hasTruncatedOutput =
                truncation.Lines ||
                truncation.ProgramPoints ||
                truncation.Facts ||
                truncation.Conditions ||
                truncation.Proofs ||
                result.InvariantQuery.IsTruncated;

            return new SymbolicInvariantQuerySummary(
                options.MaxFacts,
                options.MaxConditions,
                options.MaxProofs,
                hasTruncatedOutput,
                truncation.Facts,
                truncation.Conditions || result.InvariantQuery.IsTruncated,
                truncation.Proofs,
                analysisSummary.HasUnresolvedAnalysis || result.InvariantQuery.HasUnresolvedAnalysis,
                result.ProgramPointCount,
                analysisSummary.TotalPathConditionCount,
                analysisSummary.MaxPathConditionCount,
                analysisSummary.ProofTotalCount,
                analysisSummary.ProofUnknownCount,
                analysisSummary.ConservativeUnknownCount,
                targetCount,
                targetView,
                targetTruncated,
                reasons.Length,
                reasonView,
                reasons.Length > reasonView.Count,
                smtDiagnostics.IsConfigured,
                smtDiagnostics.IsEnabled,
                smtDiagnostics.ExecutedQueryCount,
                smtDiagnostics.CacheEntryCount,
                smtDiagnostics.QueryTimeoutMs,
                smtDiagnostics.MethodBudgetMs,
                smtDiagnostics.MaxPathConditions,
                smtDiagnostics.MaxExpressionNodes,
                smtDiagnostics.MaxPathConditions > 0 &&
                    analysisSummary.MaxPathConditionCount > smtDiagnostics.MaxPathConditions);
        }

        private static IEnumerable<string> GetTargets(SymbolicCompactQueryResult result)
        {
            var targets = result.ConservativeInvariant.Targets
                .Concat(result.ObservedInvariant.Targets)
                .Concat(result.ConditionProofs.Select(static proof => proof.Target))
                .Concat(result.InvariantQuery.TargetPathSummaries.Select(static summary => summary.Target))
                .Where(static target => IsSummaryTarget(target))
                .Where(static target => !string.IsNullOrWhiteSpace(target));

            if (result.InvariantQuery.HasTargetFilter)
            {
                targets = targets.Where(target => result.InvariantQuery.TargetFilters.Contains(
                    NormalizeTarget(target),
                    StringComparer.Ordinal));
            }

            return targets
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static target => target, StringComparer.Ordinal);
        }

        private static bool IsSummaryTarget(string? target)
        {
            if (string.IsNullOrWhiteSpace(target))
            {
                return false;
            }

            var trimmed = target!.Trim();
            return SyntaxFacts.IsValidIdentifier(trimmed) ||
                trimmed.EndsWith(".Length", StringComparison.Ordinal);
        }

        private static string NormalizeTarget(string? target)
        {
            return string.IsNullOrWhiteSpace(target)
                ? "path"
                : target!.Trim();
        }

        private static IEnumerable<string> GetReasons(SymbolicCompactQueryResult result)
        {
            var reasons = new List<string>();
            AddReason(reasons, result.InvariantQuery.StatusReason);

            foreach (var diagnostic in result.InvariantQuery.Diagnostics)
            {
                AddReason(reasons, diagnostic.Code + ": " + diagnostic.Message);
            }

            foreach (var diagnostic in result.InvariantQuery.UnknownDiagnostics)
            {
                AddReason(reasons, diagnostic.UnknownText + ": " + diagnostic.Reason);
            }

            if (result.AnalysisSummary.ReachabilityUnknownCount != 0)
            {
                AddReason(reasons, "reachability_unknown=" + result.AnalysisSummary.ReachabilityUnknownCount.ToString(CultureInfo.InvariantCulture));
            }

            if (result.AnalysisSummary.ReachabilityNotCheckedCount != 0)
            {
                AddReason(reasons, "reachability_not_checked=" + result.AnalysisSummary.ReachabilityNotCheckedCount.ToString(CultureInfo.InvariantCulture));
            }

            if (result.AnalysisSummary.ProofUnknownCount != 0)
            {
                AddReason(reasons, "proof_unknown=" + result.AnalysisSummary.ProofUnknownCount.ToString(CultureInfo.InvariantCulture));
            }

            if (!result.SmtDiagnostics.IsConfigured)
            {
                AddReason(reasons, "smt_not_configured");
            }
            else if (!result.SmtDiagnostics.IsEnabled)
            {
                AddReason(reasons, "smt_disabled");
            }

            if (result.Truncation.Facts)
            {
                AddReason(reasons, "fact_output_truncated");
            }

            if (result.Truncation.Conditions || result.InvariantQuery.IsTruncated)
            {
                AddReason(reasons, "condition_output_truncated");
            }

            if (result.Truncation.Proofs)
            {
                AddReason(reasons, "proof_output_truncated");
            }

            return reasons
                .Where(static reason => !string.IsNullOrWhiteSpace(reason))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static reason => reason, StringComparer.Ordinal);
        }

        private static void AddReason(List<string> reasons, string? reason)
        {
            if (!string.IsNullOrWhiteSpace(reason))
            {
                reasons.Add(reason!.Trim());
            }
        }
    }

    public sealed class SymbolicCompactLineResult
    {
        private SymbolicCompactLineResult(
            string filePath,
            int line,
            int programPointCount,
            SymbolicCompactInvariantSummary observedInvariant,
            SymbolicCompactInvariantSummary conservativeInvariant,
            SymbolicCompactInvariantQueryView invariantQuery,
            SymbolicReachabilitySummary reachability,
            SymbolicProgramPointSummary programPointSummary,
            IReadOnlyList<SymbolicConditionProofSummary> conditionProofs,
            IReadOnlyList<SymbolicCompactProgramPointResult> programPoints,
            SymbolicCompactSmtDiagnostics smtDiagnostics,
            SymbolicCompactOutputTruncation truncation)
        {
            FilePath = filePath ?? string.Empty;
            Line = line;
            ProgramPointCount = programPointCount;
            ObservedInvariant = observedInvariant ?? throw new ArgumentNullException(nameof(observedInvariant));
            ConservativeInvariant = conservativeInvariant ?? throw new ArgumentNullException(nameof(conservativeInvariant));
            InvariantQuery = invariantQuery ?? throw new ArgumentNullException(nameof(invariantQuery));
            MergedInvariantText = ConservativeInvariant.Text;
            Reachability = reachability ?? throw new ArgumentNullException(nameof(reachability));
            ProgramPointSummary = programPointSummary ?? throw new ArgumentNullException(nameof(programPointSummary));
            ProofOutcomes = ProgramPointSummary.ProofOutcomes;
            ConditionProofs = conditionProofs ?? throw new ArgumentNullException(nameof(conditionProofs));
            ProgramPoints = programPoints ?? throw new ArgumentNullException(nameof(programPoints));
            SmtDiagnostics = smtDiagnostics ?? throw new ArgumentNullException(nameof(smtDiagnostics));
            Truncation = truncation ?? throw new ArgumentNullException(nameof(truncation));
        }

        public string FilePath { get; }

        public int Line { get; }

        public int ProgramPointCount { get; }

        public SymbolicCompactInvariantSummary ObservedInvariant { get; }

        public SymbolicCompactInvariantSummary ConservativeInvariant { get; }

        public SymbolicCompactInvariantQueryView InvariantQuery { get; }

        public string MergedInvariantText { get; }

        public SymbolicReachabilitySummary Reachability { get; }

        public SymbolicProgramPointSummary ProgramPointSummary { get; }

        public SymbolicProofOutcomeSummary ProofOutcomes { get; }

        public IReadOnlyList<SymbolicConditionProofSummary> ConditionProofs { get; }

        public IReadOnlyList<SymbolicCompactProgramPointResult> ProgramPoints { get; }

        public SymbolicCompactSmtDiagnostics SmtDiagnostics { get; }

        public SymbolicCompactOutputTruncation Truncation { get; }

        internal static SymbolicCompactLineResult FromResult(
            SymbolicLineQueryResult result,
            SymbolicCompactQueryOptions options,
            int maxProgramPoints)
        {
            var observedInvariant = SymbolicCompactInvariantSummary.FromObservedFacts(
                result.ObservedInvariant,
                result.Facts,
                options);
            var conservativeInvariant = SymbolicCompactInvariantSummary.FromInvariant(
                result.MergedInvariant,
                result.MergedPathFacts,
                options);
            var programPoints = SymbolicCompactProjection
                .Take(result.ProgramPoints, maxProgramPoints)
                .Select(point => SymbolicCompactProgramPointResult.FromResult(point, options))
                .ToArray();
            var proofSummaries = SymbolicInvariantTargetFilter.ApplyToProofSummaries(
                SymbolicConditionProofSummary.FromProgramPoints(result.ProgramPoints),
                options);
            var conditionProofs = SymbolicCompactProjection.Take(
                proofSummaries,
                options.MaxProofs);
            var truncation = SymbolicCompactOutputTruncation.Combine(
                new SymbolicCompactOutputTruncation(
                    false,
                    result.ProgramPoints.Count > programPoints.Length,
                    false,
                    false,
                    proofSummaries.Count > options.MaxProofs),
                SymbolicCompactOutputTruncation.FromInvariant(observedInvariant),
                SymbolicCompactOutputTruncation.FromInvariant(conservativeInvariant),
                SymbolicCompactOutputTruncation.Combine(programPoints.Select(static point => point.Truncation)));

            return new SymbolicCompactLineResult(
                result.FilePath,
                result.Line,
                result.ProgramPoints.Count,
                observedInvariant,
                conservativeInvariant,
                SymbolicCompactInvariantQueryView.FromQueryView(result.InvariantQuery, options),
                result.ProgramPointSummary.Reachability,
                result.ProgramPointSummary,
                conditionProofs,
                programPoints,
                SymbolicCompactSmtDiagnostics.FromDiagnostics(result.SmtDiagnostics),
                truncation);
        }
    }

    public sealed class SymbolicCompactProgramPointResult
    {
        private SymbolicCompactProgramPointResult(
            string filePath,
            int line,
            int column,
            int position,
            int nodeSpanStart,
            int nodeSpanEnd,
            int nodeSpanLength,
            int nodeStartLine,
            int nodeStartColumn,
            int nodeEndLine,
            int nodeEndColumn,
            string nodeKind,
            string? methodName,
            string programPointKind,
            int factCount,
            IReadOnlyList<string> facts,
            IReadOnlyList<SymbolicFactInfo> symbolicFacts,
            SymbolicCompactInvariantSummary observedInvariant,
            SymbolicCompactInvariantSummary conservativeInvariant,
            SymbolicCompactInvariantQueryView invariantQuery,
            int pathConditionCount,
            IReadOnlyList<SymbolicInvariantCondition> pathConditions,
            string reachability,
            string reachabilityReason,
            IReadOnlyList<SymbolicConditionProofResult> conditionProofs,
            SymbolicProofOutcomeSummary proofOutcomes,
            SymbolicCompactSmtDiagnostics smtDiagnostics,
            SymbolicCompactOutputTruncation truncation,
            int? requestedLine = null,
            int? requestedColumn = null,
            int? requestedPosition = null,
            int? requestedPositionDistance = null,
            bool? containsRequestedPosition = null)
        {
            FilePath = filePath ?? string.Empty;
            Line = line;
            Column = column;
            Position = position;
            RequestedLine = requestedLine;
            RequestedColumn = requestedColumn;
            RequestedPosition = requestedPosition;
            RequestedPositionDistance = requestedPositionDistance;
            ContainsRequestedPosition = containsRequestedPosition;
            NodeSpanStart = nodeSpanStart;
            NodeSpanEnd = nodeSpanEnd;
            NodeSpanLength = nodeSpanLength;
            NodeStartLine = nodeStartLine;
            NodeStartColumn = nodeStartColumn;
            NodeEndLine = nodeEndLine;
            NodeEndColumn = nodeEndColumn;
            NodeKind = nodeKind ?? string.Empty;
            MethodName = string.IsNullOrWhiteSpace(methodName) ? null : methodName;
            ProgramPointKind = SymbolicProgramPointKinds.Normalize(programPointKind, nodeKind);
            FactCount = factCount;
            Facts = facts ?? throw new ArgumentNullException(nameof(facts));
            SymbolicFacts = symbolicFacts ?? throw new ArgumentNullException(nameof(symbolicFacts));
            ObservedInvariant = observedInvariant ?? throw new ArgumentNullException(nameof(observedInvariant));
            ConservativeInvariant = conservativeInvariant ?? throw new ArgumentNullException(nameof(conservativeInvariant));
            InvariantQuery = invariantQuery ?? throw new ArgumentNullException(nameof(invariantQuery));
            MergedInvariantText = ConservativeInvariant.Text;
            PathConditionCount = pathConditionCount;
            InvariantConditions = pathConditions ?? throw new ArgumentNullException(nameof(pathConditions));
            Reachability = reachability ?? string.Empty;
            ReachabilityReason = reachabilityReason ?? string.Empty;
            ConditionProofs = conditionProofs ?? throw new ArgumentNullException(nameof(conditionProofs));
            ProofOutcomes = proofOutcomes ?? throw new ArgumentNullException(nameof(proofOutcomes));
            SmtDiagnostics = smtDiagnostics ?? throw new ArgumentNullException(nameof(smtDiagnostics));
            Truncation = truncation ?? throw new ArgumentNullException(nameof(truncation));
        }

        public string FilePath { get; }

        public int Line { get; }

        public int Column { get; }

        public int Position { get; }

        public int? RequestedLine { get; }

        public int? RequestedColumn { get; }

        public int? RequestedPosition { get; }

        public int? RequestedPositionDistance { get; }

        public bool? ContainsRequestedPosition { get; }

        public int NodeSpanStart { get; }

        public int NodeSpanEnd { get; }

        public int NodeSpanLength { get; }

        public int NodeStartLine { get; }

        public int NodeStartColumn { get; }

        public int NodeEndLine { get; }

        public int NodeEndColumn { get; }

        public string NodeKind { get; }

        public string? MethodName { get; }

        public string ProgramPointKind { get; }

        public int FactCount { get; }

        public IReadOnlyList<string> Facts { get; }

        public IReadOnlyList<SymbolicFactInfo> SymbolicFacts { get; }

        public SymbolicCompactInvariantSummary ObservedInvariant { get; }

        public SymbolicCompactInvariantSummary ConservativeInvariant { get; }

        public SymbolicCompactInvariantQueryView InvariantQuery { get; }

        public string MergedInvariantText { get; }

        public int PathConditionCount { get; }

        public IReadOnlyList<SymbolicInvariantCondition> InvariantConditions { get; }

        internal IReadOnlyList<SymbolicInvariantCondition> PathConditions => InvariantConditions;

        public string Reachability { get; }

        public string ReachabilityReason { get; }

        public IReadOnlyList<SymbolicConditionProofResult> ConditionProofs { get; }

        public SymbolicProofOutcomeSummary ProofOutcomes { get; }

        public SymbolicCompactSmtDiagnostics SmtDiagnostics { get; }

        public SymbolicCompactOutputTruncation Truncation { get; }

        internal static SymbolicCompactProgramPointResult FromResult(
            SymbolicSourceQueryResult result,
            SymbolicCompactQueryOptions options)
        {
            var observedInvariant = SymbolicCompactInvariantSummary.FromObservedFacts(
                SymbolicInvariantResult.FromFacts(result.Facts),
                result.Facts,
                options);
            var conservativeInvariant = SymbolicCompactInvariantSummary.FromInvariant(
                result.Invariant,
                null,
                options);
            var focusedPathConditions = SymbolicInvariantTargetFilter.ApplyToConditions(
                result.Invariant.Conditions,
                options);
            var focusedFacts = options.HasInvariantTargetFilter
                ? focusedPathConditions
                    .Select(static condition => condition.Text)
                    .Where(static fact => !string.IsNullOrWhiteSpace(fact))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray()
                : result.Facts;
            var focusedConditionProofs = SymbolicInvariantTargetFilter.ApplyToProofResults(
                result.ConditionProofs,
                options);
            var facts = SymbolicCompactProjection.Take(focusedFacts, options.MaxFacts);
            var symbolicFacts = SymbolicCompactProjection.Take(result.SymbolicFacts, options.MaxFacts);
            var pathConditions = SymbolicCompactProjection.Take(focusedPathConditions, options.MaxConditions);
            var conditionProofs = SymbolicCompactProjection.Take(focusedConditionProofs, options.MaxProofs);
            var truncation = SymbolicCompactOutputTruncation.Combine(
                new SymbolicCompactOutputTruncation(
                    false,
                    false,
                    focusedFacts.Count > facts.Count ||
                    result.SymbolicFacts.Count > symbolicFacts.Count,
                    focusedPathConditions.Count > pathConditions.Count,
                    focusedConditionProofs.Count > conditionProofs.Count),
                SymbolicCompactOutputTruncation.FromInvariant(observedInvariant),
                SymbolicCompactOutputTruncation.FromInvariant(conservativeInvariant));

            return new SymbolicCompactProgramPointResult(
                result.FilePath,
                result.Line,
                result.Column,
                result.Position,
                result.NodeSpanStart,
                result.NodeSpanEnd,
                result.NodeSpanLength,
                result.NodeStartLine,
                result.NodeStartColumn,
                result.NodeEndLine,
                result.NodeEndColumn,
                result.NodeKind,
                result.MethodName,
                result.ProgramPointKind,
                focusedFacts.Count,
                facts,
                symbolicFacts,
                observedInvariant,
                conservativeInvariant,
                SymbolicCompactInvariantQueryView.FromQueryView(result.InvariantQuery, options),
                focusedPathConditions.Count,
                pathConditions,
                result.Reachability.ToString(),
                result.ReachabilityReason,
                conditionProofs,
                result.ProofOutcomes,
                SymbolicCompactSmtDiagnostics.FromDiagnostics(result.SmtDiagnostics),
                truncation,
                result.RequestedLine,
                result.RequestedColumn,
                result.RequestedPosition,
                result.RequestedPositionDistance,
                result.ContainsRequestedPosition);
        }
    }

    public sealed class SymbolicCompactInvariantSummary
    {
        private SymbolicCompactInvariantSummary(
            string mergeKind,
            string text,
            int conditionCount,
            IReadOnlyList<string> conditions,
            int targetCount,
            IReadOnlyList<string> targets,
            int rawFactCount,
            IReadOnlyList<string> rawFacts,
            int conservativeUnknownCount,
            SymbolicCompactMergedPathFacts? mergedPathFacts,
            bool conditionsTruncated,
            bool targetsTruncated,
            bool rawFactsTruncated)
        {
            MergeKind = mergeKind ?? string.Empty;
            Text = text ?? string.Empty;
            ConditionCount = conditionCount;
            Conditions = conditions ?? throw new ArgumentNullException(nameof(conditions));
            TargetCount = targetCount;
            Targets = targets ?? throw new ArgumentNullException(nameof(targets));
            RawFactCount = rawFactCount;
            RawFacts = rawFacts ?? throw new ArgumentNullException(nameof(rawFacts));
            ConservativeUnknownCount = conservativeUnknownCount;
            HasConservativeUnknowns = conservativeUnknownCount != 0;
            MergedPathFacts = mergedPathFacts;
            ConditionsTruncated = conditionsTruncated;
            TargetsTruncated = targetsTruncated;
            RawFactsTruncated = rawFactsTruncated;
        }

        public string MergeKind { get; }

        public string Text { get; }

        public int ConditionCount { get; }

        public IReadOnlyList<string> Conditions { get; }

        public int TargetCount { get; }

        public IReadOnlyList<string> Targets { get; }

        public int RawFactCount { get; }

        public IReadOnlyList<string> RawFacts { get; }

        public int ConservativeUnknownCount { get; }

        public bool HasConservativeUnknowns { get; }

        public SymbolicCompactMergedPathFacts? MergedPathFacts { get; }

        public bool ConditionsTruncated { get; }

        public bool TargetsTruncated { get; }

        public bool RawFactsTruncated { get; }

        internal static SymbolicCompactInvariantSummary FromObservedFacts(
            SymbolicInvariantResult invariant,
            IReadOnlyList<string> rawFacts,
            SymbolicCompactQueryOptions options)
        {
            var conditions = invariant.Conditions
                .Select(static condition => condition.Text)
                .ToArray();
            var targets = GetDistinctTargets(invariant);
            return new SymbolicCompactInvariantSummary(
                invariant.MergeKind.ToString(),
                invariant.MergedInvariantText,
                invariant.ConditionCount,
                SymbolicCompactProjection.Take(conditions, options.MaxConditions),
                targets.Length,
                SymbolicCompactProjection.Take(targets, options.MaxConditions),
                rawFacts.Count,
                SymbolicCompactProjection.Take(rawFacts, options.MaxFacts),
                invariant.ConservativeUnknownCount,
                null,
                conditions.Length > options.MaxConditions,
                targets.Length > options.MaxConditions,
                rawFacts.Count > options.MaxFacts);
        }

        internal static SymbolicCompactInvariantSummary FromInvariant(
            SymbolicInvariantResult invariant,
            SymbolicMergedPathFacts? mergedPathFacts,
            SymbolicCompactQueryOptions options)
        {
            var conditions = invariant.Conditions
                .Select(static condition => condition.Text)
                .ToArray();
            var targets = GetDistinctTargets(invariant);
            return new SymbolicCompactInvariantSummary(
                invariant.MergeKind.ToString(),
                invariant.MergedInvariantText,
                invariant.ConditionCount,
                SymbolicCompactProjection.Take(conditions, options.MaxConditions),
                targets.Length,
                SymbolicCompactProjection.Take(targets, options.MaxConditions),
                0,
                Array.Empty<string>(),
                invariant.ConservativeUnknownCount,
                mergedPathFacts == null
                    ? null
                    : SymbolicCompactMergedPathFacts.FromMergedPathFacts(mergedPathFacts, options),
                conditions.Length > options.MaxConditions,
                targets.Length > options.MaxConditions,
                false);
        }

        private static string[] GetDistinctTargets(SymbolicInvariantResult invariant)
        {
            return invariant.Conditions
                .Select(static condition => condition.Target)
                .Where(static target => !string.IsNullOrWhiteSpace(target))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }
    }

    public sealed class SymbolicCompactMergedPathFacts
    {
        private SymbolicCompactMergedPathFacts(
            int alwaysFactCount,
            IReadOnlyList<string> alwaysFacts,
            int maybeFactCount,
            IReadOnlyList<string> maybeFacts,
            int conservativeUnknownCount,
            IReadOnlyList<string> conservativeUnknowns,
            IReadOnlyList<SymbolicCompactConservativeUnknownDiagnostic> conservativeUnknownDiagnostics,
            int candidateProgramPointCount,
            int unreachableProgramPointCount,
            bool isUnreachable,
            bool alwaysFactsTruncated,
            bool maybeFactsTruncated,
            bool conservativeUnknownsTruncated,
            bool conservativeUnknownDiagnosticsTruncated)
        {
            AlwaysFactCount = alwaysFactCount;
            AlwaysFacts = alwaysFacts ?? throw new ArgumentNullException(nameof(alwaysFacts));
            MaybeFactCount = maybeFactCount;
            MaybeFacts = maybeFacts ?? throw new ArgumentNullException(nameof(maybeFacts));
            ConservativeUnknownCount = conservativeUnknownCount;
            ConservativeUnknowns = conservativeUnknowns ?? throw new ArgumentNullException(nameof(conservativeUnknowns));
            ConservativeUnknownDiagnostics = conservativeUnknownDiagnostics ?? throw new ArgumentNullException(nameof(conservativeUnknownDiagnostics));
            CandidateProgramPointCount = candidateProgramPointCount;
            UnreachableProgramPointCount = unreachableProgramPointCount;
            IsUnreachable = isUnreachable;
            AlwaysFactsTruncated = alwaysFactsTruncated;
            MaybeFactsTruncated = maybeFactsTruncated;
            ConservativeUnknownsTruncated = conservativeUnknownsTruncated;
            ConservativeUnknownDiagnosticsTruncated = conservativeUnknownDiagnosticsTruncated;
        }

        public int AlwaysFactCount { get; }

        public IReadOnlyList<string> AlwaysFacts { get; }

        public int MaybeFactCount { get; }

        public IReadOnlyList<string> MaybeFacts { get; }

        public int ConservativeUnknownCount { get; }

        public IReadOnlyList<string> ConservativeUnknowns { get; }

        public IReadOnlyList<SymbolicCompactConservativeUnknownDiagnostic> ConservativeUnknownDiagnostics { get; }

        public int CandidateProgramPointCount { get; }

        public int UnreachableProgramPointCount { get; }

        public bool IsUnreachable { get; }

        public bool AlwaysFactsTruncated { get; }

        public bool MaybeFactsTruncated { get; }

        public bool ConservativeUnknownsTruncated { get; }

        public bool ConservativeUnknownDiagnosticsTruncated { get; }

        internal bool IsTruncated =>
            AlwaysFactsTruncated ||
            MaybeFactsTruncated ||
            ConservativeUnknownsTruncated ||
            ConservativeUnknownDiagnosticsTruncated ||
            ConservativeUnknownDiagnostics.Any(static diagnostic => diagnostic.MaybeFactsTruncated);

        internal static SymbolicCompactMergedPathFacts FromMergedPathFacts(
            SymbolicMergedPathFacts facts,
            SymbolicCompactQueryOptions options)
        {
            var conservativeUnknownDiagnostics = SymbolicCompactProjection
                .Take(facts.ConservativeUnknownDiagnostics, options.MaxConditions)
                .Select(diagnostic => SymbolicCompactConservativeUnknownDiagnostic.FromDiagnostic(diagnostic, options))
                .ToArray();
            return new SymbolicCompactMergedPathFacts(
                facts.AlwaysFacts.Count,
                SymbolicCompactProjection.Take(facts.AlwaysFacts, options.MaxConditions),
                facts.MaybeFacts.Count,
                SymbolicCompactProjection.Take(facts.MaybeFacts, options.MaxConditions),
                facts.ConservativeUnknowns.Count,
                SymbolicCompactProjection.Take(facts.ConservativeUnknowns, options.MaxConditions),
                conservativeUnknownDiagnostics,
                facts.CandidateProgramPointCount,
                facts.UnreachableProgramPointCount,
                facts.IsUnreachable,
                facts.AlwaysFacts.Count > options.MaxConditions,
                facts.MaybeFacts.Count > options.MaxConditions,
                facts.ConservativeUnknowns.Count > options.MaxConditions,
                facts.ConservativeUnknownDiagnostics.Count > options.MaxConditions);
        }
    }

    public sealed class SymbolicCompactConservativeUnknownDiagnostic
    {
        private SymbolicCompactConservativeUnknownDiagnostic(
            string target,
            string unknownText,
            string reason,
            int maybeFactCount,
            IReadOnlyList<string> maybeFacts,
            int candidateProgramPointCount,
            int unreachableProgramPointCount,
            bool maybeFactsTruncated)
        {
            Target = target ?? string.Empty;
            UnknownText = unknownText ?? string.Empty;
            Reason = reason ?? string.Empty;
            MaybeFactCount = maybeFactCount;
            MaybeFacts = maybeFacts ?? throw new ArgumentNullException(nameof(maybeFacts));
            CandidateProgramPointCount = candidateProgramPointCount;
            UnreachableProgramPointCount = unreachableProgramPointCount;
            MaybeFactsTruncated = maybeFactsTruncated;
        }

        public string Target { get; }

        public string UnknownText { get; }

        public string Reason { get; }

        public int MaybeFactCount { get; }

        public IReadOnlyList<string> MaybeFacts { get; }

        public int CandidateProgramPointCount { get; }

        public int UnreachableProgramPointCount { get; }

        public bool MaybeFactsTruncated { get; }

        internal static SymbolicCompactConservativeUnknownDiagnostic FromDiagnostic(
            SymbolicConservativeUnknownDiagnostic diagnostic,
            SymbolicCompactQueryOptions options)
        {
            return new SymbolicCompactConservativeUnknownDiagnostic(
                diagnostic.Target,
                diagnostic.UnknownText,
                diagnostic.Reason,
                diagnostic.MaybeFacts.Count,
                SymbolicCompactProjection.Take(diagnostic.MaybeFacts, options.MaxConditions),
                diagnostic.CandidateProgramPointCount,
                diagnostic.UnreachableProgramPointCount,
                diagnostic.MaybeFacts.Count > options.MaxConditions);
        }
    }

    public sealed class SymbolicCompactInvariantQueryView
    {
        private SymbolicCompactInvariantQueryView(
            string text,
            string mergeKind,
            int mustFactCount,
            IReadOnlyList<string> mustFacts,
            int maybeFactCount,
            IReadOnlyList<string> maybeFacts,
            int unknownFactCount,
            IReadOnlyList<string> unknownFacts,
            IReadOnlyList<SymbolicCompactConservativeUnknownDiagnostic> unknownDiagnostics,
            int targetSummaryCount,
            IReadOnlyList<SymbolicCompactInvariantTargetSummary> targetSummaries,
            int targetPathSummaryCount,
            IReadOnlyList<SymbolicCompactInvariantTargetPathSummary> targetPathSummaries,
            IReadOnlyList<string> targetFilters,
            int targetFilterCount,
            bool hasTargetFilter,
            bool targetFilterMatched,
            int matchedTargetFilterCount,
            IReadOnlyList<string> matchedTargetFilters,
            int unmatchedTargetFilterCount,
            IReadOnlyList<string> unmatchedTargetFilters,
            int unfilteredTargetSummaryCount,
            int unfilteredTargetPathSummaryCount,
            int diagnosticCount,
            IReadOnlyList<SymbolicCompactInvariantQueryDiagnostic> diagnostics,
            int candidateProgramPointCount,
            int unreachableProgramPointCount,
            bool isUnreachable,
            string status,
            string statusReason,
            string summary,
            bool hasMaybeFacts,
            bool hasUnknowns,
            bool hasUnresolvedAnalysis,
            bool mustFactsTruncated,
            bool maybeFactsTruncated,
            bool unknownFactsTruncated,
            bool unknownDiagnosticsTruncated,
            bool targetSummariesTruncated,
            bool targetPathSummariesTruncated,
            bool matchedTargetFiltersTruncated,
            bool unmatchedTargetFiltersTruncated,
            bool diagnosticsTruncated)
        {
            Text = text ?? string.Empty;
            MergeKind = mergeKind ?? string.Empty;
            MustFactCount = mustFactCount;
            MustFacts = mustFacts ?? throw new ArgumentNullException(nameof(mustFacts));
            MaybeFactCount = maybeFactCount;
            MaybeFacts = maybeFacts ?? throw new ArgumentNullException(nameof(maybeFacts));
            UnknownFactCount = unknownFactCount;
            UnknownFacts = unknownFacts ?? throw new ArgumentNullException(nameof(unknownFacts));
            UnknownDiagnostics = unknownDiagnostics ?? throw new ArgumentNullException(nameof(unknownDiagnostics));
            TargetSummaryCount = targetSummaryCount;
            TargetSummaries = targetSummaries ?? throw new ArgumentNullException(nameof(targetSummaries));
            TargetPathSummaryCount = targetPathSummaryCount;
            TargetPathSummaries = targetPathSummaries ?? throw new ArgumentNullException(nameof(targetPathSummaries));
            TargetFilters = targetFilters ?? throw new ArgumentNullException(nameof(targetFilters));
            TargetFilterCount = targetFilterCount;
            HasTargetFilter = hasTargetFilter;
            TargetFilterMatched = targetFilterMatched;
            MatchedTargetFilterCount = matchedTargetFilterCount;
            MatchedTargetFilters = matchedTargetFilters ?? throw new ArgumentNullException(nameof(matchedTargetFilters));
            UnmatchedTargetFilterCount = unmatchedTargetFilterCount;
            UnmatchedTargetFilters = unmatchedTargetFilters ?? throw new ArgumentNullException(nameof(unmatchedTargetFilters));
            UnfilteredTargetSummaryCount = unfilteredTargetSummaryCount;
            UnfilteredTargetPathSummaryCount = unfilteredTargetPathSummaryCount;
            DiagnosticCount = diagnosticCount;
            Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            CandidateProgramPointCount = candidateProgramPointCount;
            UnreachableProgramPointCount = unreachableProgramPointCount;
            IsUnreachable = isUnreachable;
            Status = status ?? string.Empty;
            StatusReason = statusReason ?? string.Empty;
            Summary = summary ?? string.Empty;
            HasMaybeFacts = hasMaybeFacts;
            HasUnknowns = hasUnknowns;
            HasUnresolvedAnalysis = hasUnresolvedAnalysis;
            MustFactsTruncated = mustFactsTruncated;
            MaybeFactsTruncated = maybeFactsTruncated;
            UnknownFactsTruncated = unknownFactsTruncated;
            UnknownDiagnosticsTruncated = unknownDiagnosticsTruncated;
            TargetSummariesTruncated = targetSummariesTruncated;
            TargetPathSummariesTruncated = targetPathSummariesTruncated;
            MatchedTargetFiltersTruncated = matchedTargetFiltersTruncated;
            UnmatchedTargetFiltersTruncated = unmatchedTargetFiltersTruncated;
            DiagnosticsTruncated = diagnosticsTruncated;
        }

        public string Text { get; }

        public string MergeKind { get; }

        public int MustFactCount { get; }

        public IReadOnlyList<string> MustFacts { get; }

        public int MaybeFactCount { get; }

        public IReadOnlyList<string> MaybeFacts { get; }

        public int UnknownFactCount { get; }

        public IReadOnlyList<string> UnknownFacts { get; }

        public IReadOnlyList<SymbolicCompactConservativeUnknownDiagnostic> UnknownDiagnostics { get; }

        public int TargetSummaryCount { get; }

        public IReadOnlyList<SymbolicCompactInvariantTargetSummary> TargetSummaries { get; }

        public int TargetPathSummaryCount { get; }

        public IReadOnlyList<SymbolicCompactInvariantTargetPathSummary> TargetPathSummaries { get; }

        public IReadOnlyList<string> TargetFilters { get; }

        public int TargetFilterCount { get; }

        public bool HasTargetFilter { get; }

        public bool TargetFilterMatched { get; }

        public int MatchedTargetFilterCount { get; }

        public IReadOnlyList<string> MatchedTargetFilters { get; }

        public int UnmatchedTargetFilterCount { get; }

        public IReadOnlyList<string> UnmatchedTargetFilters { get; }

        public int UnfilteredTargetSummaryCount { get; }

        public int UnfilteredTargetPathSummaryCount { get; }

        public int DiagnosticCount { get; }

        public IReadOnlyList<SymbolicCompactInvariantQueryDiagnostic> Diagnostics { get; }

        public int CandidateProgramPointCount { get; }

        public int UnreachableProgramPointCount { get; }

        public bool IsUnreachable { get; }

        public string Status { get; }

        public string StatusReason { get; }

        public string Summary { get; }

        public bool HasMaybeFacts { get; }

        public bool HasUnknowns { get; }

        public bool HasUnresolvedAnalysis { get; }

        public bool MustFactsTruncated { get; }

        public bool MaybeFactsTruncated { get; }

        public bool UnknownFactsTruncated { get; }

        public bool UnknownDiagnosticsTruncated { get; }

        public bool TargetSummariesTruncated { get; }

        public bool TargetPathSummariesTruncated { get; }

        public bool MatchedTargetFiltersTruncated { get; }

        public bool UnmatchedTargetFiltersTruncated { get; }

        public bool DiagnosticsTruncated { get; }

        public bool IsTruncated =>
            MustFactsTruncated ||
            MaybeFactsTruncated ||
            UnknownFactsTruncated ||
            UnknownDiagnosticsTruncated ||
            TargetSummariesTruncated ||
            TargetPathSummariesTruncated ||
            MatchedTargetFiltersTruncated ||
            UnmatchedTargetFiltersTruncated ||
            DiagnosticsTruncated ||
            Diagnostics.Any(static diagnostic => diagnostic.EvidenceTruncated) ||
            UnknownDiagnostics.Any(static diagnostic => diagnostic.MaybeFactsTruncated) ||
            TargetSummaries.Any(static target => target.IsTruncated) ||
            TargetPathSummaries.Any(static target => target.ConditionsTruncated);

        internal static SymbolicCompactInvariantQueryView FromQueryView(
            SymbolicInvariantQueryView query,
            SymbolicCompactQueryOptions options)
        {
            if (query == null)
            {
                throw new ArgumentNullException(nameof(query));
            }

            var filteredTargetSummaries = SymbolicInvariantTargetFilter.ApplyToTargets(
                query.TargetSummaries,
                options,
                static summary => summary.Target);
            var focusedMustFacts = SymbolicInvariantTargetFilter.SelectFacts(
                query.MustFacts,
                filteredTargetSummaries,
                options,
                static summary => summary.MustFacts);
            var focusedMaybeFacts = SymbolicInvariantTargetFilter.SelectFacts(
                query.MaybeFacts,
                filteredTargetSummaries,
                options,
                static summary => summary.MaybeFacts);
            var focusedUnknownFacts = SymbolicInvariantTargetFilter.SelectFacts(
                query.UnknownFacts,
                filteredTargetSummaries,
                options,
                static summary => summary.UnknownFacts);
            var focusedMergedFacts = options.HasInvariantTargetFilter
                ? focusedMustFacts.Concat(focusedUnknownFacts).ToArray()
                : Array.Empty<string>();
            var focusedText = options.HasInvariantTargetFilter
                ? SymbolicInvariantService.FormatMergedInvariantFacts(focusedMergedFacts)
                : query.Text;
            var filteredUnknownDiagnostics = SymbolicInvariantTargetFilter.ApplyToTargets(
                query.UnknownDiagnostics,
                options,
                static diagnostic => diagnostic.Target);
            var unknownDiagnostics = SymbolicCompactProjection
                .Take(filteredUnknownDiagnostics, options.MaxConditions)
                .Select(diagnostic => SymbolicCompactConservativeUnknownDiagnostic.FromDiagnostic(diagnostic, options))
                .ToArray();
            var targetSummaries = SymbolicCompactProjection
                .Take(filteredTargetSummaries, options.MaxConditions)
                .Select(target => SymbolicCompactInvariantTargetSummary.FromSummary(target, options))
                .ToArray();
            var filteredTargetPathSummaries = SymbolicInvariantTargetFilter.ApplyToTargets(
                query.TargetPathSummaries,
                options,
                static summary => summary.Target);
            var targetPathSummaries = SymbolicCompactProjection
                .Take(filteredTargetPathSummaries, options.MaxConditions)
                .Select(target => SymbolicCompactInvariantTargetPathSummary.FromSummary(target, options))
                .ToArray();
            var diagnostics = SymbolicCompactProjection
                .Take(query.Diagnostics, options.MaxConditions)
                .Select(diagnostic => SymbolicCompactInvariantQueryDiagnostic.FromDiagnostic(diagnostic, options))
                .ToArray();
            var matchedTargetFilters = SymbolicInvariantTargetFilter.GetMatchedTargetFilters(query, options);
            var unmatchedTargetFilters = SymbolicInvariantTargetFilter.GetUnmatchedTargetFilters(options, matchedTargetFilters);
            var visibleMatchedTargetFilters = SymbolicCompactProjection.Take(matchedTargetFilters, options.MaxConditions);
            var visibleUnmatchedTargetFilters = SymbolicCompactProjection.Take(unmatchedTargetFilters, options.MaxConditions);
            var targetFilterMatched = !options.HasInvariantTargetFilter || matchedTargetFilters.Count != 0;
            return new SymbolicCompactInvariantQueryView(
                focusedText,
                query.MergeKind.ToString(),
                focusedMustFacts.Count,
                SymbolicCompactProjection.Take(focusedMustFacts, options.MaxConditions),
                focusedMaybeFacts.Count,
                SymbolicCompactProjection.Take(focusedMaybeFacts, options.MaxConditions),
                focusedUnknownFacts.Count,
                SymbolicCompactProjection.Take(focusedUnknownFacts, options.MaxConditions),
                unknownDiagnostics,
                filteredTargetSummaries.Count,
                targetSummaries,
                filteredTargetPathSummaries.Count,
                targetPathSummaries,
                options.InvariantTargets,
                options.InvariantTargets.Count,
                options.HasInvariantTargetFilter,
                targetFilterMatched,
                matchedTargetFilters.Count,
                visibleMatchedTargetFilters,
                unmatchedTargetFilters.Count,
                visibleUnmatchedTargetFilters,
                query.TargetSummaryCount,
                query.TargetPathSummaryCount,
                query.DiagnosticCount,
                diagnostics,
                query.CandidateProgramPointCount,
                query.UnreachableProgramPointCount,
                query.IsUnreachable,
                query.Status.ToString(),
                query.StatusReason,
                query.Summary,
                focusedMaybeFacts.Count != 0,
                focusedUnknownFacts.Count != 0,
                query.HasUnresolvedAnalysis,
                focusedMustFacts.Count > options.MaxConditions,
                focusedMaybeFacts.Count > options.MaxConditions,
                focusedUnknownFacts.Count > options.MaxConditions,
                filteredUnknownDiagnostics.Count > options.MaxConditions,
                filteredTargetSummaries.Count > targetSummaries.Length,
                filteredTargetPathSummaries.Count > targetPathSummaries.Length,
                matchedTargetFilters.Count > visibleMatchedTargetFilters.Count,
                unmatchedTargetFilters.Count > visibleUnmatchedTargetFilters.Count,
                query.Diagnostics.Count > options.MaxConditions);
        }

    }

    public sealed class SymbolicCompactInvariantTargetSummary
    {
        private SymbolicCompactInvariantTargetSummary(
            string target,
            string status,
            string statusReason,
            string reasonCode,
            string summary,
            int mustFactCount,
            IReadOnlyList<string> mustFacts,
            int maybeFactCount,
            IReadOnlyList<string> maybeFacts,
            int unknownFactCount,
            IReadOnlyList<string> unknownFacts,
            bool mustFactsTruncated,
            bool maybeFactsTruncated,
            bool unknownFactsTruncated)
        {
            Target = target ?? string.Empty;
            Status = status ?? string.Empty;
            StatusReason = statusReason ?? string.Empty;
            ReasonCode = reasonCode ?? string.Empty;
            Summary = summary ?? string.Empty;
            MustFactCount = mustFactCount;
            MustFacts = mustFacts ?? throw new ArgumentNullException(nameof(mustFacts));
            MaybeFactCount = maybeFactCount;
            MaybeFacts = maybeFacts ?? throw new ArgumentNullException(nameof(maybeFacts));
            UnknownFactCount = unknownFactCount;
            UnknownFacts = unknownFacts ?? throw new ArgumentNullException(nameof(unknownFacts));
            MustFactsTruncated = mustFactsTruncated;
            MaybeFactsTruncated = maybeFactsTruncated;
            UnknownFactsTruncated = unknownFactsTruncated;
        }

        public string Target { get; }

        public string Status { get; }

        public string StatusReason { get; }

        public string ReasonCode { get; }

        public string Summary { get; }

        public int MustFactCount { get; }

        public IReadOnlyList<string> MustFacts { get; }

        public int MaybeFactCount { get; }

        public IReadOnlyList<string> MaybeFacts { get; }

        public int UnknownFactCount { get; }

        public IReadOnlyList<string> UnknownFacts { get; }

        public bool MustFactsTruncated { get; }

        public bool MaybeFactsTruncated { get; }

        public bool UnknownFactsTruncated { get; }

        internal bool IsTruncated => MustFactsTruncated || MaybeFactsTruncated || UnknownFactsTruncated;

        internal static SymbolicCompactInvariantTargetSummary FromSummary(
            SymbolicInvariantTargetSummary summary,
            SymbolicCompactQueryOptions options)
        {
            return new SymbolicCompactInvariantTargetSummary(
                summary.Target,
                summary.Status.ToString(),
                summary.StatusReason,
                summary.ReasonCode,
                summary.Summary,
                summary.MustFactCount,
                SymbolicCompactProjection.Take(summary.MustFacts, options.MaxConditions),
                summary.MaybeFactCount,
                SymbolicCompactProjection.Take(summary.MaybeFacts, options.MaxConditions),
                summary.UnknownFactCount,
                SymbolicCompactProjection.Take(summary.UnknownFacts, options.MaxConditions),
                summary.MustFactCount > options.MaxConditions,
                summary.MaybeFactCount > options.MaxConditions,
                summary.UnknownFactCount > options.MaxConditions);
        }
    }

    public sealed class SymbolicCompactInvariantTargetPathSummary
    {
        private SymbolicCompactInvariantTargetPathSummary(
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
            bool conditionsTruncated,
            string statusReason,
            string reasonCode,
            string summary)
        {
            Target = target ?? string.Empty;
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
            StatusReason = statusReason ?? string.Empty;
            ReasonCode = reasonCode ?? string.Empty;
            Summary = summary ?? string.Empty;
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

        public string StatusReason { get; }

        public string ReasonCode { get; }

        public string Summary { get; }

        internal static SymbolicCompactInvariantTargetPathSummary FromSummary(
            SymbolicInvariantTargetPathSummary summary,
            SymbolicCompactQueryOptions options)
        {
            var conditions = SymbolicCompactProjection.Take(summary.Conditions, options.MaxConditions);
            return new SymbolicCompactInvariantTargetPathSummary(
                summary.Target,
                summary.PathConditionCount,
                summary.SmtConditionCount,
                summary.ConservativeUnknownCount,
                summary.ProgramPointCount,
                summary.ReachableProgramPointCount,
                summary.ProofTotalCount,
                summary.ProofUnknownCount,
                summary.ProofProvenTrueCount,
                summary.ProofProvenFalseCount,
                summary.ProofUnreachableCount,
                conditions,
                summary.ConditionsTruncated || summary.Conditions.Count > conditions.Count,
                summary.StatusReason,
                summary.ReasonCode,
                summary.Summary);
        }
    }

    public sealed class SymbolicCompactInvariantQueryDiagnostic
    {
        private SymbolicCompactInvariantQueryDiagnostic(
            string code,
            string severity,
            string message,
            int count,
            int evidenceTotalCount,
            IReadOnlyList<string> evidence,
            bool evidenceTruncated)
        {
            Code = code ?? string.Empty;
            Severity = severity ?? string.Empty;
            Message = message ?? string.Empty;
            Count = count;
            EvidenceTotalCount = evidenceTotalCount;
            Evidence = evidence ?? throw new ArgumentNullException(nameof(evidence));
            EvidenceTruncated = evidenceTruncated;
        }

        public string Code { get; }

        public string Severity { get; }

        public string Message { get; }

        public int Count { get; }

        public int EvidenceTotalCount { get; }

        public IReadOnlyList<string> Evidence { get; }

        public bool EvidenceTruncated { get; }

        internal static SymbolicCompactInvariantQueryDiagnostic FromDiagnostic(
            SymbolicInvariantQueryDiagnostic diagnostic,
            SymbolicCompactQueryOptions options)
        {
            return new SymbolicCompactInvariantQueryDiagnostic(
                diagnostic.Code,
                diagnostic.Severity,
                diagnostic.Message,
                diagnostic.Count,
                diagnostic.EvidenceTotalCount,
                SymbolicCompactProjection.Take(diagnostic.Evidence, options.MaxConditions),
                diagnostic.EvidenceTruncated || diagnostic.Evidence.Count > options.MaxConditions);
        }
    }

    public sealed class SymbolicCompactSmtDiagnostics
    {
        private SymbolicCompactSmtDiagnostics(
            bool isConfigured,
            string mode,
            bool isEnabled,
            int queryTimeoutMs,
            int methodBudgetMs,
            int maxPathConditions,
            int maxExpressionNodes,
            int executedQueryCount,
            int cacheEntryCount)
        {
            IsConfigured = isConfigured;
            Mode = mode ?? string.Empty;
            IsEnabled = isEnabled;
            QueryTimeoutMs = queryTimeoutMs;
            MethodBudgetMs = methodBudgetMs;
            MaxPathConditions = maxPathConditions;
            MaxExpressionNodes = maxExpressionNodes;
            ExecutedQueryCount = executedQueryCount;
            CacheEntryCount = cacheEntryCount;
        }

        public bool IsConfigured { get; }

        public string Mode { get; }

        public bool IsEnabled { get; }

        public int QueryTimeoutMs { get; }

        public int MethodBudgetMs { get; }

        public int MaxPathConditions { get; }

        public int MaxExpressionNodes { get; }

        public int ExecutedQueryCount { get; }

        public int CacheEntryCount { get; }

        internal static SymbolicCompactSmtDiagnostics FromDiagnostics(SymbolicSmtDiagnostics diagnostics)
        {
            return new SymbolicCompactSmtDiagnostics(
                diagnostics.IsConfigured,
                diagnostics.Mode.ToString(),
                diagnostics.IsEnabled,
                diagnostics.QueryTimeoutMs,
                diagnostics.MethodBudgetMs,
                diagnostics.MaxPathConditions,
                diagnostics.MaxExpressionNodes,
                diagnostics.ExecutedQueryCount,
                diagnostics.CacheEntryCount);
        }
    }

    public sealed class SymbolicCompactAnalysisSummary
    {
        private SymbolicCompactAnalysisSummary(
            int programPointCount,
            int invariantConditionCount,
            int conservativeUnknownCount,
            int mustFactCount,
            int maybeFactCount,
            int unknownFactCount,
            string invariantStatus,
            string invariantStatusReason,
            string invariantSummary,
            int invariantDiagnosticCount,
            int totalPathConditionCount,
            int maxPathConditionCount,
            int reachabilityCheckedCount,
            int reachabilityKnownCount,
            int reachabilityUnknownCount,
            int reachabilityNotCheckedCount,
            int proofTotalCount,
            int proofResolvedCount,
            int proofUnknownCount,
            bool smtConfigured,
            bool smtEnabled,
            int smtExecutedQueryCount,
            int smtCacheEntryCount,
            int smtQueryTimeoutMs,
            int smtMethodBudgetMs,
            int smtMaxPathConditions,
            int smtMaxExpressionNodes)
        {
            ProgramPointCount = programPointCount;
            InvariantConditionCount = invariantConditionCount;
            ConservativeUnknownCount = conservativeUnknownCount;
            MustFactCount = mustFactCount;
            MaybeFactCount = maybeFactCount;
            UnknownFactCount = unknownFactCount;
            InvariantStatus = invariantStatus ?? string.Empty;
            InvariantStatusReason = invariantStatusReason ?? string.Empty;
            InvariantSummary = invariantSummary ?? string.Empty;
            InvariantDiagnosticCount = invariantDiagnosticCount;
            TotalPathConditionCount = totalPathConditionCount;
            MaxPathConditionCount = maxPathConditionCount;
            ReachabilityCheckedCount = reachabilityCheckedCount;
            ReachabilityKnownCount = reachabilityKnownCount;
            ReachabilityUnknownCount = reachabilityUnknownCount;
            ReachabilityNotCheckedCount = reachabilityNotCheckedCount;
            ProofTotalCount = proofTotalCount;
            ProofResolvedCount = proofResolvedCount;
            ProofUnknownCount = proofUnknownCount;
            SmtConfigured = smtConfigured;
            SmtEnabled = smtEnabled;
            SmtExecutedQueryCount = smtExecutedQueryCount;
            SmtCacheEntryCount = smtCacheEntryCount;
            SmtQueryTimeoutMs = smtQueryTimeoutMs;
            SmtMethodBudgetMs = smtMethodBudgetMs;
            SmtMaxPathConditions = smtMaxPathConditions;
            SmtMaxExpressionNodes = smtMaxExpressionNodes;
        }

        public int ProgramPointCount { get; }

        public int InvariantConditionCount { get; }

        public int ConservativeUnknownCount { get; }

        public int MustFactCount { get; }

        public int MaybeFactCount { get; }

        public int UnknownFactCount { get; }

        public string InvariantStatus { get; }

        public string InvariantStatusReason { get; }

        public string InvariantSummary { get; }

        public int InvariantDiagnosticCount { get; }

        public int TotalPathConditionCount { get; }

        public int MaxPathConditionCount { get; }

        public int ReachabilityCheckedCount { get; }

        public int ReachabilityKnownCount { get; }

        public int ReachabilityUnknownCount { get; }

        public int ReachabilityNotCheckedCount { get; }

        public int ProofTotalCount { get; }

        public int ProofResolvedCount { get; }

        public int ProofUnknownCount { get; }

        public bool SmtConfigured { get; }

        public bool SmtEnabled { get; }

        public int SmtExecutedQueryCount { get; }

        public int SmtCacheEntryCount { get; }

        public int SmtQueryTimeoutMs { get; }

        public int SmtMethodBudgetMs { get; }

        public int SmtMaxPathConditions { get; }

        public int SmtMaxExpressionNodes { get; }

        public bool HasUnresolvedAnalysis =>
            ConservativeUnknownCount != 0 ||
            ReachabilityUnknownCount != 0 ||
            ReachabilityNotCheckedCount != 0 ||
            ProofUnknownCount != 0;

        internal static SymbolicCompactAnalysisSummary From(
            SymbolicCompactInvariantQueryView invariantQuery,
            SymbolicProgramPointSummary programPointSummary,
            SymbolicCompactSmtDiagnostics smtDiagnostics)
        {
            if (invariantQuery == null)
            {
                throw new ArgumentNullException(nameof(invariantQuery));
            }

            if (programPointSummary == null)
            {
                throw new ArgumentNullException(nameof(programPointSummary));
            }

            if (smtDiagnostics == null)
            {
                throw new ArgumentNullException(nameof(smtDiagnostics));
            }

            var reachability = programPointSummary.Reachability;
            var proofOutcomes = programPointSummary.ProofOutcomes;
            var reachabilityCheckedCount =
                reachability.ReachableCount +
                reachability.UnreachableCount +
                reachability.UnknownCount;
            var reachabilityKnownCount =
                reachability.ReachableCount +
                reachability.UnreachableCount;
            var proofResolvedCount =
                proofOutcomes.ProvenTrueCount +
                proofOutcomes.ProvenFalseCount +
                proofOutcomes.UnreachableCount;

            return new SymbolicCompactAnalysisSummary(
                programPointSummary.ProgramPointCount,
                invariantQuery.MustFactCount + invariantQuery.UnknownFactCount,
                invariantQuery.UnknownFactCount,
                invariantQuery.MustFactCount,
                invariantQuery.MaybeFactCount,
                invariantQuery.UnknownFactCount,
                invariantQuery.Status,
                invariantQuery.StatusReason,
                invariantQuery.Summary,
                invariantQuery.DiagnosticCount,
                programPointSummary.TotalPathConditionCount,
                programPointSummary.MaxPathConditionCount,
                reachabilityCheckedCount,
                reachabilityKnownCount,
                reachability.UnknownCount,
                reachability.NotCheckedCount,
                proofOutcomes.TotalCount,
                proofResolvedCount,
                proofOutcomes.UnknownCount,
                smtDiagnostics.IsConfigured,
                smtDiagnostics.IsEnabled,
                smtDiagnostics.ExecutedQueryCount,
                smtDiagnostics.CacheEntryCount,
                smtDiagnostics.QueryTimeoutMs,
                smtDiagnostics.MethodBudgetMs,
                smtDiagnostics.MaxPathConditions,
                smtDiagnostics.MaxExpressionNodes);
        }
    }

    public sealed class SymbolicCompactOutputTruncation
    {
        public SymbolicCompactOutputTruncation(
            bool lines,
            bool programPoints,
            bool facts,
            bool conditions,
            bool proofs)
        {
            Lines = lines;
            ProgramPoints = programPoints;
            Facts = facts;
            Conditions = conditions;
            Proofs = proofs;
        }

        public bool Lines { get; }

        public bool ProgramPoints { get; }

        public bool Facts { get; }

        public bool Conditions { get; }

        public bool Proofs { get; }

        public bool IsTruncated =>
            Lines ||
            ProgramPoints ||
            Facts ||
            Conditions ||
            Proofs;

        internal static SymbolicCompactOutputTruncation FromInvariant(SymbolicCompactInvariantSummary invariant)
        {
            return new SymbolicCompactOutputTruncation(
                false,
                false,
                invariant.RawFactsTruncated,
                invariant.ConditionsTruncated ||
                    invariant.TargetsTruncated ||
                    (invariant.MergedPathFacts != null && invariant.MergedPathFacts.IsTruncated),
                false);
        }

        internal static SymbolicCompactOutputTruncation Combine(
            IEnumerable<SymbolicCompactOutputTruncation> truncations)
        {
            if (truncations == null)
            {
                throw new ArgumentNullException(nameof(truncations));
            }

            var lines = false;
            var programPoints = false;
            var facts = false;
            var conditions = false;
            var proofs = false;
            foreach (var truncation in truncations)
            {
                if (truncation == null)
                {
                    continue;
                }

                lines |= truncation.Lines;
                programPoints |= truncation.ProgramPoints;
                facts |= truncation.Facts;
                conditions |= truncation.Conditions;
                proofs |= truncation.Proofs;
            }

            return new SymbolicCompactOutputTruncation(lines, programPoints, facts, conditions, proofs);
        }

        internal static SymbolicCompactOutputTruncation Combine(
            params SymbolicCompactOutputTruncation[] truncations)
        {
            return Combine((IEnumerable<SymbolicCompactOutputTruncation>)truncations);
        }
    }

    internal static class SymbolicCompactProjection
    {
        public static IReadOnlyList<T> Take<T>(IEnumerable<T> values, int maxCount)
        {
            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            if (maxCount == 0)
            {
                return Array.Empty<T>();
            }

            if (maxCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxCount), "Compact output limits cannot be negative.");
            }

            return values.Take(maxCount).ToArray();
        }
    }

    public sealed class SymbolicConservativeUnknownDiagnostic
    {
        public SymbolicConservativeUnknownDiagnostic(
            string target,
            string unknownText,
            string reason,
            IReadOnlyList<string> maybeFacts,
            int candidateProgramPointCount,
            int unreachableProgramPointCount)
        {
            Target = string.IsNullOrWhiteSpace(target) ? "path" : target;
            UnknownText = string.IsNullOrWhiteSpace(unknownText)
                ? SymbolicMergedPathFacts.FormatConservativeUnknown(Target)
                : unknownText;
            Reason = string.IsNullOrWhiteSpace(reason)
                ? "not_common_to_all_candidate_program_points"
                : reason;
            MaybeFacts = maybeFacts ?? throw new ArgumentNullException(nameof(maybeFacts));
            CandidateProgramPointCount = candidateProgramPointCount;
            UnreachableProgramPointCount = unreachableProgramPointCount;
        }

        public string Target { get; }

        public string UnknownText { get; }

        public string Reason { get; }

        public IReadOnlyList<string> MaybeFacts { get; }

        public int MaybeFactCount => MaybeFacts.Count;

        public int CandidateProgramPointCount { get; }

        public int UnreachableProgramPointCount { get; }

        public string GetDisplayReason()
        {
            if (string.IsNullOrWhiteSpace(Reason))
            {
                return Reason ?? string.Empty;
            }

            return Reason switch
            {
                "not_common_to_all_candidate_program_points" => "not common to all candidate program points",
                "smt_disabled" => "SMT disabled",
                "smt_disposed" => "SMT solver disposed",
                "smt_timeout" => "SMT solver timed out",
                "smt_unavailable" => "SMT solver unavailable",
                "smt_encoding_failure" => "SMT formula encoding failed",
                "smt_expression_budget_exceeded" => "SMT expression node budget exceeded",
                "smt_path_condition_budget_exceeded" => "SMT path condition budget exceeded",
                "smt_method_budget_exceeded" => "SMT method-level budget exceeded",
                "unsupported_formula_fallback" => "unsupported formula fallback; legacy translated trigger was not trusted as proof",
                _ => Reason,
            };
        }
    }

    public sealed class SymbolicMergedPathFacts
    {
        private SymbolicMergedPathFacts(
            IReadOnlyList<string> alwaysFacts,
            IReadOnlyList<string> maybeFacts,
            IReadOnlyList<string> conservativeUnknowns,
            IReadOnlyList<SymbolicConservativeUnknownDiagnostic> conservativeUnknownDiagnostics,
            IReadOnlyList<string> mergedFacts,
            string mergedInvariantText,
            int candidateProgramPointCount,
            int unreachableProgramPointCount,
            bool isUnreachable)
        {
            AlwaysFacts = alwaysFacts ?? throw new ArgumentNullException(nameof(alwaysFacts));
            MaybeFacts = maybeFacts ?? throw new ArgumentNullException(nameof(maybeFacts));
            ConservativeUnknowns = conservativeUnknowns ?? throw new ArgumentNullException(nameof(conservativeUnknowns));
            ConservativeUnknownDiagnostics = conservativeUnknownDiagnostics ?? throw new ArgumentNullException(nameof(conservativeUnknownDiagnostics));
            MergedFacts = mergedFacts ?? throw new ArgumentNullException(nameof(mergedFacts));
            MergedInvariantText = mergedInvariantText ?? throw new ArgumentNullException(nameof(mergedInvariantText));
            CandidateProgramPointCount = candidateProgramPointCount;
            UnreachableProgramPointCount = unreachableProgramPointCount;
            IsUnreachable = isUnreachable;
        }

        public IReadOnlyList<string> AlwaysFacts { get; }

        public IReadOnlyList<string> MaybeFacts { get; }

        public IReadOnlyList<string> ConservativeUnknowns { get; }

        public IReadOnlyList<SymbolicConservativeUnknownDiagnostic> ConservativeUnknownDiagnostics { get; }

        public int ConservativeUnknownCount => ConservativeUnknowns.Count;

        public IReadOnlyList<string> MergedFacts { get; }

        public string MergedInvariantText { get; }

        public int CandidateProgramPointCount { get; }

        public int UnreachableProgramPointCount { get; }

        public bool IsUnreachable { get; }

        public static SymbolicMergedPathFacts FromProgramPoints(
            IEnumerable<SymbolicSourceQueryResult> programPoints)
        {
            if (programPoints == null)
            {
                throw new ArgumentNullException(nameof(programPoints));
            }

            var points = programPoints.ToArray();
            if (points.Length == 0)
            {
                return new SymbolicMergedPathFacts(
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    Array.Empty<SymbolicConservativeUnknownDiagnostic>(),
                    Array.Empty<string>(),
                    "true",
                    0,
                    0,
                    isUnreachable: false);
            }

            var candidatePoints = points
                .Where(static point => point.Reachability != SymbolicReachability.Unreachable)
                .ToArray();
            var unreachableProgramPointCount = points.Length - candidatePoints.Length;
            if (candidatePoints.Length == 0)
            {
                return new SymbolicMergedPathFacts(
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    Array.Empty<SymbolicConservativeUnknownDiagnostic>(),
                    new[] { "false" },
                    "false",
                    0,
                    unreachableProgramPointCount,
                    isUnreachable: true);
            }

            var seenConditionTexts = new HashSet<string>(StringComparer.Ordinal);
            var orderedConditions = new List<SymbolicInvariantCondition>();
            var conditionSets = new List<HashSet<string>>();
            foreach (var point in candidatePoints)
            {
                var conditionSet = new HashSet<string>(StringComparer.Ordinal);
                foreach (var condition in point.Invariant.Conditions)
                {
                    if (string.IsNullOrWhiteSpace(condition.Text))
                    {
                        continue;
                    }

                    if (conditionSet.Add(condition.Text) &&
                        seenConditionTexts.Add(condition.Text))
                    {
                        orderedConditions.Add(condition);
                    }
                }

                conditionSets.Add(conditionSet);
            }

            var commonTexts = new HashSet<string>(conditionSets[0], StringComparer.Ordinal);
            for (var index = 1; index < conditionSets.Count; index++)
            {
                commonTexts.IntersectWith(conditionSets[index]);
            }

            var alwaysFacts = orderedConditions
                .Where(condition => commonTexts.Contains(condition.Text))
                .Select(static condition => condition.Text)
                .ToArray();
            var maybeConditions = orderedConditions
                .Where(condition => !commonTexts.Contains(condition.Text))
                .ToArray();
            var maybeFacts = maybeConditions
                .Select(static condition => condition.Text)
                .ToArray();
            var conservativeUnknownDiagnostics = CreateConservativeUnknownDiagnostics(
                maybeConditions,
                candidatePoints.Length,
                unreachableProgramPointCount);
            var conservativeUnknowns = conservativeUnknownDiagnostics
                .Select(static diagnostic => diagnostic.UnknownText)
                .ToArray();
            var mergedFacts = alwaysFacts
                .Concat(conservativeUnknowns)
                .ToArray();

            return new SymbolicMergedPathFacts(
                alwaysFacts,
                maybeFacts,
                conservativeUnknowns,
                conservativeUnknownDiagnostics,
                mergedFacts,
                SymbolicInvariantService.FormatMergedInvariantFacts(mergedFacts),
                candidatePoints.Length,
                unreachableProgramPointCount,
                isUnreachable: false);
        }

        private static IReadOnlyList<SymbolicConservativeUnknownDiagnostic> CreateConservativeUnknownDiagnostics(
            IReadOnlyList<SymbolicInvariantCondition> maybeConditions,
            int candidateProgramPointCount,
            int unreachableProgramPointCount)
        {
            var seenTargets = new HashSet<string>(StringComparer.Ordinal);
            var diagnostics = new List<SymbolicConservativeUnknownDiagnostic>();
            foreach (var condition in maybeConditions)
            {
                var target = string.IsNullOrWhiteSpace(condition.Target)
                    ? "path"
                    : condition.Target;
                if (seenTargets.Add(target))
                {
                    diagnostics.Add(new SymbolicConservativeUnknownDiagnostic(
                        target,
                        FormatConservativeUnknown(target),
                        "not_common_to_all_candidate_program_points",
                        maybeConditions
                            .Where(candidate => string.Equals(
                                string.IsNullOrWhiteSpace(candidate.Target) ? "path" : candidate.Target,
                                target,
                                StringComparison.Ordinal))
                            .Select(static candidate => candidate.Text)
                            .ToArray(),
                        candidateProgramPointCount,
                        unreachableProgramPointCount));
                }
            }

            return diagnostics;
        }

        internal static string FormatConservativeUnknown(string target)
        {
            return "unknown(" + (string.IsNullOrWhiteSpace(target) ? "path" : target) + ")";
        }
    }

    public sealed class SymbolicSourceQueryFilter
    {
        public static readonly SymbolicSourceQueryFilter Empty = new();

        public SymbolicSourceQueryFilter(
            IEnumerable<string>? nodeKinds = null,
            bool requireFacts = false,
            IEnumerable<SymbolicReachability>? reachability = null,
            IEnumerable<string>? methodNames = null,
            bool requirePathConditions = false,
            IEnumerable<string>? conditionTargets = null,
            IEnumerable<string>? conditionTexts = null,
            IEnumerable<string>? conditionTextContains = null,
            IEnumerable<string>? methodNameContains = null,
            IEnumerable<int>? lines = null,
            int? lineStart = null,
            int? lineEnd = null,
            IEnumerable<string>? programPointKinds = null,
            bool requireProofs = false,
            IEnumerable<SymbolicTruthValue>? proofOutcomes = null,
            IEnumerable<string>? proofConditions = null,
            IEnumerable<string>? proofConditionContains = null)
        {
            NodeKinds = nodeKinds?
                .Where(static kind => !string.IsNullOrWhiteSpace(kind))
                .Select(static kind => kind.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? Array.Empty<string>();
            RequireFacts = requireFacts;
            Reachability = reachability?
                .Distinct()
                .ToArray() ?? Array.Empty<SymbolicReachability>();
            MethodNames = NormalizeStrings(methodNames, StringComparer.OrdinalIgnoreCase);
            RequirePathConditions = requirePathConditions;
            ConditionTargets = NormalizeStrings(conditionTargets, StringComparer.OrdinalIgnoreCase);
            ConditionTexts = NormalizeStrings(conditionTexts, StringComparer.Ordinal);
            ConditionTextContains = NormalizeStrings(conditionTextContains, StringComparer.OrdinalIgnoreCase);
            MethodNameContains = NormalizeStrings(methodNameContains, StringComparer.OrdinalIgnoreCase);
            Lines = NormalizePositiveIntegers(lines, nameof(lines));
            LineStart = ValidatePositiveLine(lineStart, nameof(lineStart));
            LineEnd = ValidatePositiveLine(lineEnd, nameof(lineEnd));
            if (LineStart.HasValue && LineEnd.HasValue && LineStart.Value > LineEnd.Value)
            {
                throw new ArgumentException("LineStart cannot be greater than LineEnd.", nameof(lineStart));
            }

            ProgramPointKinds = NormalizeProgramPointKinds(programPointKinds);
            RequireProofs = requireProofs;
            ProofOutcomes = proofOutcomes?
                .Distinct()
                .ToArray() ?? Array.Empty<SymbolicTruthValue>();
            ProofConditions = NormalizeStrings(proofConditions, StringComparer.Ordinal);
            ProofConditionContains = NormalizeStrings(proofConditionContains, StringComparer.OrdinalIgnoreCase);
        }

        public IReadOnlyList<string> NodeKinds { get; }

        public bool RequireFacts { get; }

        public IReadOnlyList<SymbolicReachability> Reachability { get; }

        public IReadOnlyList<string> MethodNames { get; }

        public bool RequirePathConditions { get; }

        public IReadOnlyList<string> ConditionTargets { get; }

        public IReadOnlyList<string> ConditionTexts { get; }

        public IReadOnlyList<string> ConditionTextContains { get; }

        public IReadOnlyList<string> MethodNameContains { get; }

        public IReadOnlyList<int> Lines { get; }

        public int? LineStart { get; }

        public int? LineEnd { get; }

        public IReadOnlyList<string> ProgramPointKinds { get; }

        public bool RequireProofs { get; }

        public IReadOnlyList<SymbolicTruthValue> ProofOutcomes { get; }

        public IReadOnlyList<string> ProofConditions { get; }

        public IReadOnlyList<string> ProofConditionContains { get; }

        public bool IsEmpty =>
            NodeKinds.Count == 0 &&
            !RequireFacts &&
            Reachability.Count == 0 &&
            MethodNames.Count == 0 &&
            !RequirePathConditions &&
            ConditionTargets.Count == 0 &&
            ConditionTexts.Count == 0 &&
            ConditionTextContains.Count == 0 &&
            MethodNameContains.Count == 0 &&
            Lines.Count == 0 &&
            !LineStart.HasValue &&
            !LineEnd.HasValue &&
            ProgramPointKinds.Count == 0 &&
            !RequireProofs &&
            ProofOutcomes.Count == 0 &&
            ProofConditions.Count == 0 &&
            ProofConditionContains.Count == 0;

        public bool Matches(SymbolicSourceQueryResult result)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            if (RequireFacts && result.Facts.Count == 0)
            {
                return false;
            }

            if (NodeKinds.Count != 0 &&
                !NodeKinds.Any(kind => string.Equals(kind, result.NodeKind, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            if (ProgramPointKinds.Count != 0 &&
                !ProgramPointKinds.Any(kind => string.Equals(kind, result.ProgramPointKind, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            if (Lines.Count != 0 && !Lines.Contains(result.Line))
            {
                return false;
            }

            if (LineStart.HasValue && result.Line < LineStart.Value)
            {
                return false;
            }

            if (LineEnd.HasValue && result.Line > LineEnd.Value)
            {
                return false;
            }

            if (Reachability.Count != 0 && !Reachability.Contains(result.Reachability))
            {
                return false;
            }

            if (MethodNames.Count != 0 &&
                (string.IsNullOrWhiteSpace(result.MethodName) ||
                 !MethodNames.Any(methodName => string.Equals(methodName, result.MethodName, StringComparison.OrdinalIgnoreCase))))
            {
                return false;
            }

            var resultMethodName = result.MethodName;
            if (MethodNameContains.Count != 0 &&
                (string.IsNullOrWhiteSpace(resultMethodName) ||
                 !MethodNameContains.Any(text => resultMethodName!.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)))
            {
                return false;
            }

            if (RequirePathConditions && result.PathConditionCount == 0)
            {
                return false;
            }

            if (ConditionTargets.Count != 0 &&
                !result.Invariant.Conditions.Any(condition =>
                    ConditionTargets.Any(target => string.Equals(target, condition.Target, StringComparison.OrdinalIgnoreCase))))
            {
                return false;
            }

            if (ConditionTexts.Count != 0 &&
                !result.Invariant.Conditions.Any(condition =>
                    ConditionTexts.Any(text => string.Equals(text, condition.Text, StringComparison.Ordinal))))
            {
                return false;
            }

            if (ConditionTextContains.Count != 0 &&
                !result.Invariant.Conditions.Any(condition =>
                    ConditionTextContains.Any(text => condition.Text.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)))
            {
                return false;
            }

            if (RequireProofs && result.ConditionProofs.Count == 0)
            {
                return false;
            }

            if (ProofOutcomes.Count != 0 &&
                !result.ConditionProofs.Any(proof => ProofOutcomes.Contains(proof.TruthValue)))
            {
                return false;
            }

            if (ProofConditions.Count != 0 &&
                !result.ConditionProofs.Any(proof =>
                    ProofConditions.Any(condition => string.Equals(condition, proof.Condition, StringComparison.Ordinal))))
            {
                return false;
            }

            if (ProofConditionContains.Count != 0 &&
                !result.ConditionProofs.Any(proof =>
                    ProofConditionContains.Any(text => proof.Condition.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)))
            {
                return false;
            }

            return true;
        }

        private static IReadOnlyList<string> NormalizeStrings(
            IEnumerable<string>? values,
            StringComparer comparer)
        {
            return values?
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value.Trim())
                .Distinct(comparer)
                .ToArray() ?? Array.Empty<string>();
        }

        private static IReadOnlyList<string> NormalizeProgramPointKinds(IEnumerable<string>? values)
        {
            return values?
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => NormalizeProgramPointKindFilter(value.Trim()))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? Array.Empty<string>();
        }

        private static string NormalizeProgramPointKindFilter(string value)
        {
            if (string.Equals(value, SymbolicProgramPointKinds.Statement, StringComparison.OrdinalIgnoreCase))
            {
                return SymbolicProgramPointKinds.Statement;
            }

            if (string.Equals(value, SymbolicProgramPointKinds.Expression, StringComparison.OrdinalIgnoreCase))
            {
                return SymbolicProgramPointKinds.Expression;
            }

            if (string.Equals(value, SymbolicProgramPointKinds.Other, StringComparison.OrdinalIgnoreCase))
            {
                return SymbolicProgramPointKinds.Other;
            }

            return value;
        }

        private static IReadOnlyList<int> NormalizePositiveIntegers(IEnumerable<int>? values, string paramName)
        {
            if (values == null)
            {
                return Array.Empty<int>();
            }

            var normalized = new SortedSet<int>();
            foreach (var value in values)
            {
                if (value < 1)
                {
                    throw new ArgumentOutOfRangeException(paramName, "Line filters must be 1 or greater.");
                }

                normalized.Add(value);
            }

            return normalized.ToArray();
        }

        private static int? ValidatePositiveLine(int? value, string paramName)
        {
            if (value.HasValue && value.Value < 1)
            {
                throw new ArgumentOutOfRangeException(paramName, "Line filters must be 1 or greater.");
            }

            return value;
        }
    }

    public sealed class SymbolicReachabilitySummary
    {
        public SymbolicReachabilitySummary(
            int notCheckedCount,
            int unknownCount,
            int reachableCount,
            int unreachableCount)
        {
            NotCheckedCount = notCheckedCount;
            UnknownCount = unknownCount;
            ReachableCount = reachableCount;
            UnreachableCount = unreachableCount;
        }

        public int NotCheckedCount { get; }

        public int UnknownCount { get; }

        public int ReachableCount { get; }

        public int UnreachableCount { get; }

        public static SymbolicReachabilitySummary FromProgramPoints(
            IEnumerable<SymbolicSourceQueryResult> programPoints)
        {
            if (programPoints == null)
            {
                throw new ArgumentNullException(nameof(programPoints));
            }

            var notCheckedCount = 0;
            var unknownCount = 0;
            var reachableCount = 0;
            var unreachableCount = 0;
            foreach (var point in programPoints)
            {
                switch (point.Reachability)
                {
                    case SymbolicReachability.NotChecked:
                        notCheckedCount++;
                        break;
                    case SymbolicReachability.Unknown:
                        unknownCount++;
                        break;
                    case SymbolicReachability.Reachable:
                        reachableCount++;
                        break;
                    case SymbolicReachability.Unreachable:
                        unreachableCount++;
                        break;
                }
            }

            return new SymbolicReachabilitySummary(
                notCheckedCount,
                unknownCount,
                reachableCount,
                unreachableCount);
        }
    }

    public sealed class SymbolicProofOutcomeSummary
    {
        public SymbolicProofOutcomeSummary(
            int totalCount,
            int unknownCount,
            int provenTrueCount,
            int provenFalseCount,
            int unreachableCount)
        {
            TotalCount = totalCount;
            UnknownCount = unknownCount;
            ProvenTrueCount = provenTrueCount;
            ProvenFalseCount = provenFalseCount;
            UnreachableCount = unreachableCount;
        }

        public int TotalCount { get; }

        public int UnknownCount { get; }

        public int ProvenTrueCount { get; }

        public int ProvenFalseCount { get; }

        public int UnreachableCount { get; }

        public static SymbolicProofOutcomeSummary FromProofs(
            IEnumerable<SymbolicConditionProofResult> proofs)
        {
            if (proofs == null)
            {
                throw new ArgumentNullException(nameof(proofs));
            }

            var totalCount = 0;
            var unknownCount = 0;
            var provenTrueCount = 0;
            var provenFalseCount = 0;
            var unreachableCount = 0;
            foreach (var proof in proofs)
            {
                totalCount++;
                switch (proof.TruthValue)
                {
                    case SymbolicTruthValue.Unknown:
                        unknownCount++;
                        break;
                    case SymbolicTruthValue.ProvenTrue:
                        provenTrueCount++;
                        break;
                    case SymbolicTruthValue.ProvenFalse:
                        provenFalseCount++;
                        break;
                    case SymbolicTruthValue.Unreachable:
                        unreachableCount++;
                        break;
                }
            }

            return new SymbolicProofOutcomeSummary(
                totalCount,
                unknownCount,
                provenTrueCount,
                provenFalseCount,
                unreachableCount);
        }
    }

    public sealed class SymbolicProgramPointSummary
    {
        public SymbolicProgramPointSummary(
            int programPointCount,
            int totalPathConditionCount,
            int maxPathConditionCount,
            SymbolicReachabilitySummary reachability,
            SymbolicProofOutcomeSummary proofOutcomes)
        {
            ProgramPointCount = programPointCount;
            TotalPathConditionCount = totalPathConditionCount;
            MaxPathConditionCount = maxPathConditionCount;
            Reachability = reachability ?? throw new ArgumentNullException(nameof(reachability));
            ProofOutcomes = proofOutcomes ?? throw new ArgumentNullException(nameof(proofOutcomes));
        }

        public int ProgramPointCount { get; }

        public int TotalPathConditionCount { get; }

        public int MaxPathConditionCount { get; }

        public SymbolicReachabilitySummary Reachability { get; }

        public SymbolicProofOutcomeSummary ProofOutcomes { get; }

        public static SymbolicProgramPointSummary FromProgramPoints(
            IEnumerable<SymbolicSourceQueryResult> programPoints)
        {
            if (programPoints == null)
            {
                throw new ArgumentNullException(nameof(programPoints));
            }

            var points = programPoints.ToArray();
            var totalPathConditionCount = 0;
            var maxPathConditionCount = 0;
            foreach (var point in points)
            {
                var pathConditionCount = point.PathConditionCount;
                totalPathConditionCount += pathConditionCount;
                if (pathConditionCount > maxPathConditionCount)
                {
                    maxPathConditionCount = pathConditionCount;
                }
            }

            return new SymbolicProgramPointSummary(
                points.Length,
                totalPathConditionCount,
                maxPathConditionCount,
                SymbolicReachabilitySummary.FromProgramPoints(points),
                SymbolicProofOutcomeSummary.FromProofs(points.SelectMany(static point => point.ConditionProofs)));
        }
    }

    public sealed class SymbolicConditionProofSummary
    {
        public SymbolicConditionProofSummary(
            string condition,
            int unknownCount,
            int provenTrueCount,
            int provenFalseCount,
            int unreachableCount,
            int? totalCount = null,
            IReadOnlyList<SymbolicConditionProofReasonSummary>? reasons = null,
            string? target = null,
            string? formulaKind = null,
            string? valueKind = null,
            string? formulaText = null,
            bool isSolverBacked = false)
        {
            Condition = condition ?? throw new ArgumentNullException(nameof(condition));
            Target = target ?? string.Empty;
            FormulaKind = formulaKind ?? "Unknown";
            ValueKind = valueKind ?? "Unknown";
            FormulaText = string.IsNullOrWhiteSpace(formulaText) ? Condition : formulaText!;
            IsSolverBacked = isSolverBacked;
            DisplayKind = FormulaKind;
            UnknownCount = unknownCount;
            ProvenTrueCount = provenTrueCount;
            ProvenFalseCount = provenFalseCount;
            UnreachableCount = unreachableCount;
            TotalCount = totalCount ?? unknownCount + provenTrueCount + provenFalseCount + unreachableCount;
            Reasons = reasons ?? Array.Empty<SymbolicConditionProofReasonSummary>();
            ReachableCount = TotalCount - UnreachableCount;
            ResolvedCount = ProvenTrueCount + ProvenFalseCount + UnreachableCount;
            Status = ResolveStatus(TotalCount, ReachableCount, UnknownCount, ProvenTrueCount, ProvenFalseCount, UnreachableCount);
            Summary = CreateSummary(Status);
            Proof = new SymbolicProofInfo(
                MapProofStatus(Status),
                ResolveProofBackend(Status, IsSolverBacked),
                ResolveUnknownReason(Status, Reasons),
                Summary,
                cacheHit: false,
                budget: null,
                Target,
                FormulaText,
                FormulaKind);
        }

        public string Condition { get; }

        public string Target { get; }

        public string DisplayKind { get; }

        internal string FormulaKind { get; }

        public string ValueKind { get; }

        internal string FormulaText { get; }

        internal bool IsSolverBacked { get; }

        public int TotalCount { get; }

        public int UnknownCount { get; }

        public int ProvenTrueCount { get; }

        public int ProvenFalseCount { get; }

        public int UnreachableCount { get; }

        public int ReachableCount { get; }

        public int ResolvedCount { get; }

        public SymbolicConditionProofSummaryStatus Status { get; }

        public string Summary { get; }

        public SymbolicProofInfo Proof { get; }

        public bool HoldsOnAllReachablePoints => Status == SymbolicConditionProofSummaryStatus.AlwaysTrue;

        public bool RefutedOnAllReachablePoints => Status == SymbolicConditionProofSummaryStatus.AlwaysFalse;

        public bool HasMixedReachableOutcomes => Status == SymbolicConditionProofSummaryStatus.Mixed;

        public IReadOnlyList<SymbolicConditionProofReasonSummary> Reasons { get; }

        public static IReadOnlyList<SymbolicConditionProofSummary> FromProgramPoints(
            IEnumerable<SymbolicSourceQueryResult> programPoints)
        {
            if (programPoints == null)
            {
                throw new ArgumentNullException(nameof(programPoints));
            }

            return programPoints
                .SelectMany(static point => point.ConditionProofs)
                .GroupBy(static proof => proof.Condition, StringComparer.Ordinal)
                .OrderBy(static group => group.Key, StringComparer.Ordinal)
                .Select(static group => Create(group.Key, group))
                .ToArray();
        }

        private static SymbolicConditionProofSummary Create(
            string condition,
            IEnumerable<SymbolicConditionProofResult> proofs)
        {
            var proofArray = proofs.ToArray();
            var unknownCount = 0;
            var provenTrueCount = 0;
            var provenFalseCount = 0;
            var unreachableCount = 0;
            foreach (var proof in proofArray)
            {
                switch (proof.TruthValue)
                {
                    case SymbolicTruthValue.Unknown:
                        unknownCount++;
                        break;
                    case SymbolicTruthValue.ProvenTrue:
                        provenTrueCount++;
                        break;
                    case SymbolicTruthValue.ProvenFalse:
                        provenFalseCount++;
                        break;
                    case SymbolicTruthValue.Unreachable:
                        unreachableCount++;
                        break;
                }
            }

            var metadataProof = proofArray.FirstOrDefault(static proof => proof.IsSolverBacked) ??
                proofArray.FirstOrDefault();
            return new SymbolicConditionProofSummary(
                condition,
                unknownCount,
                provenTrueCount,
                provenFalseCount,
                unreachableCount,
                reasons: proofArray
                    .GroupBy(
                        static proof => new ProofReasonKey(proof.TruthValue, proof.Reason),
                        ProofReasonKeyComparer.Instance)
                    .OrderBy(static group => group.Key.TruthValue)
                    .ThenBy(static group => group.Key.Reason, StringComparer.Ordinal)
                    .Select(static group => new SymbolicConditionProofReasonSummary(
                        group.Key.TruthValue,
                        group.Key.Reason,
                        group.Count()))
                    .ToArray(),
                target: metadataProof?.Target,
                formulaKind: metadataProof?.FormulaKind,
                valueKind: metadataProof?.ValueKind,
                formulaText: metadataProof?.FormulaText,
                isSolverBacked: metadataProof?.IsSolverBacked ?? false);
        }

        private static SymbolicConditionProofSummaryStatus ResolveStatus(
            int totalCount,
            int reachableCount,
            int unknownCount,
            int provenTrueCount,
            int provenFalseCount,
            int unreachableCount)
        {
            if (totalCount == 0)
            {
                return SymbolicConditionProofSummaryStatus.None;
            }

            if (unreachableCount == totalCount)
            {
                return SymbolicConditionProofSummaryStatus.UnreachableOnly;
            }

            if (unknownCount != 0)
            {
                return SymbolicConditionProofSummaryStatus.Unknown;
            }

            if (provenFalseCount == 0 && provenTrueCount == reachableCount)
            {
                return SymbolicConditionProofSummaryStatus.AlwaysTrue;
            }

            if (provenTrueCount == 0 && provenFalseCount == reachableCount)
            {
                return SymbolicConditionProofSummaryStatus.AlwaysFalse;
            }

            return SymbolicConditionProofSummaryStatus.Mixed;
        }

        private static string CreateSummary(SymbolicConditionProofSummaryStatus status)
        {
            switch (status)
            {
                case SymbolicConditionProofSummaryStatus.None:
                    return "No implication proof results were requested for this condition.";
                case SymbolicConditionProofSummaryStatus.UnreachableOnly:
                    return "Every candidate program point for this condition was unreachable.";
                case SymbolicConditionProofSummaryStatus.AlwaysTrue:
                    return "The condition is proven true at every reachable candidate program point.";
                case SymbolicConditionProofSummaryStatus.AlwaysFalse:
                    return "The condition is proven false at every reachable candidate program point.";
                case SymbolicConditionProofSummaryStatus.Mixed:
                    return "The condition has both true and false reachable proof outcomes.";
                default:
                    return "The condition has at least one unresolved reachable proof outcome.";
            }
        }

        private static SymbolicProofStatus MapProofStatus(SymbolicConditionProofSummaryStatus status)
        {
            return status switch
            {
                SymbolicConditionProofSummaryStatus.AlwaysTrue => SymbolicProofStatus.ProvenTrue,
                SymbolicConditionProofSummaryStatus.AlwaysFalse => SymbolicProofStatus.ProvenFalse,
                SymbolicConditionProofSummaryStatus.UnreachableOnly => SymbolicProofStatus.Unreachable,
                _ => SymbolicProofStatus.Unknown,
            };
        }

        private static SymbolicProofBackend ResolveProofBackend(
            SymbolicConditionProofSummaryStatus status,
            bool isSolverBacked)
        {
            if (isSolverBacked)
            {
                return SymbolicProofBackend.Smt;
            }

            return status is SymbolicConditionProofSummaryStatus.AlwaysTrue or
                    SymbolicConditionProofSummaryStatus.AlwaysFalse or
                    SymbolicConditionProofSummaryStatus.UnreachableOnly
                ? SymbolicProofBackend.Syntactic
                : SymbolicProofBackend.None;
        }

        private static SymbolicUnknownReason ResolveUnknownReason(
            SymbolicConditionProofSummaryStatus status,
            IReadOnlyList<SymbolicConditionProofReasonSummary> reasons)
        {
            if (status != SymbolicConditionProofSummaryStatus.Unknown)
            {
                return SymbolicUnknownReason.None;
            }

            var reason = reasons
                .FirstOrDefault(static item => item.TruthValue == SymbolicTruthValue.Unknown)
                ?.Reason;
            return SymbolicUnknownReasonClassifier.Classify(reason ?? string.Empty);
        }

        private readonly struct ProofReasonKey
        {
            public ProofReasonKey(SymbolicTruthValue truthValue, string? reason)
            {
                TruthValue = truthValue;
                Reason = reason ?? string.Empty;
            }

            public SymbolicTruthValue TruthValue { get; }

            public string Reason { get; }
        }

        private sealed class ProofReasonKeyComparer : IEqualityComparer<ProofReasonKey>
        {
            public static readonly ProofReasonKeyComparer Instance = new ProofReasonKeyComparer();

            public bool Equals(ProofReasonKey x, ProofReasonKey y)
            {
                return x.TruthValue == y.TruthValue &&
                    string.Equals(x.Reason, y.Reason, StringComparison.Ordinal);
            }

            public int GetHashCode(ProofReasonKey obj)
            {
                unchecked
                {
                    return ((int)obj.TruthValue * 397) ^ StringComparer.Ordinal.GetHashCode(obj.Reason);
                }
            }
        }
    }

    public sealed class SymbolicConditionProofReasonSummary
    {
        public SymbolicConditionProofReasonSummary(
            SymbolicTruthValue truthValue,
            string reason,
            int count)
        {
            TruthValue = truthValue;
            Reason = reason ?? string.Empty;
            Count = count;
        }

        public SymbolicTruthValue TruthValue { get; }

        public string Reason { get; }

        public int Count { get; }

        public string GetDisplayReason()
        {
            if (string.IsNullOrWhiteSpace(Reason))
            {
                return Reason ?? string.Empty;
            }

            return Reason switch
            {
                "not_common_to_all_candidate_program_points" => "not common to all candidate program points",
                "smt_disabled" => "SMT disabled",
                "smt_disposed" => "SMT solver disposed",
                "smt_timeout" => "SMT solver timed out",
                "smt_unavailable" => "SMT solver unavailable",
                "smt_encoding_failure" => "SMT formula encoding failed",
                "smt_expression_budget_exceeded" => "SMT expression node budget exceeded",
                "smt_path_condition_budget_exceeded" => "SMT path condition budget exceeded",
                "smt_method_budget_exceeded" => "SMT method-level budget exceeded",
                "unsupported_formula_fallback" => "unsupported formula fallback; legacy translated trigger was not trusted as proof",
                _ => Reason,
            };
        }
    }

    public enum SymbolicConditionProofSummaryStatus
    {
        None,
        UnreachableOnly,
        AlwaysTrue,
        AlwaysFalse,
        Mixed,
        Unknown,
    }

    internal static class SymbolicFormulaDisplay
    {
        internal static string FormatMergedInvariant(IReadOnlyList<SmtFormula> pathConditions)
        {
            if (pathConditions == null)
            {
                throw new ArgumentNullException(nameof(pathConditions));
            }

            if (pathConditions.Count == 0)
            {
                return "true";
            }

            if (pathConditions.Count == 1)
            {
                return Format(pathConditions[0]);
            }

            return string.Join(" && ", pathConditions.Select(static condition => "(" + Format(condition) + ")"));
        }

        internal static string Format(SmtFormula formula)
        {
            if (formula == null)
            {
                throw new ArgumentNullException(nameof(formula));
            }

            switch (formula)
            {
                case SmtBooleanConstant boolean:
                    return boolean.Value ? "true" : "false";
                case SmtIntegerConstant integer:
                    return integer.Value.ToString(CultureInfo.InvariantCulture);
                case SmtStringConstant text:
                    return "\"" + EscapeString(text.Value) + "\"";
                case SmtNullConstant:
                    return "null";
                case SmtVariable variable:
                    return FormatVariableName(variable.Name);
                case SmtUnaryFormula unary:
                    return "!(" + Format(unary.Operand) + ")";
                case SmtBinaryFormula binary:
                    return FormatBinary(binary);
                case SmtIntegerUnaryTerm unary:
                    return "-" + FormatTerm(unary.Operand);
                case SmtIntegerBinaryTerm binary:
                    return FormatIntegerBinary(binary);
                case SmtStringLengthTerm length:
                    return FormatTerm(length.Value) + ".Length";
                case SmtStringConcatTerm concat:
                    return FormatTerm(concat.Left) + " + " + FormatTerm(concat.Right);
                case SmtStringContainsFormula contains:
                    return FormatTerm(contains.Value) + ".Contains(" + Format(contains.Search) + ")";
                case SmtStringStartsWithFormula startsWith:
                    return FormatTerm(startsWith.Value) + ".StartsWith(" + Format(startsWith.Prefix) + ")";
                case SmtStringEndsWithFormula endsWith:
                    return FormatTerm(endsWith.Value) + ".EndsWith(" + Format(endsWith.Suffix) + ")";
                case SmtRegexMatchFormula regex:
                    return "Regex.IsMatch(" + FormatTerm(regex.Value) + ", \"" + EscapeString(regex.Pattern) + "\")";
                case SmtRuntimeTypeTestFormula runtimeTypeTest:
                    return FormatTerm(runtimeTypeTest.Value) + " is " + runtimeTypeTest.TypeKey;
                case SmtConditionalFormula conditional:
                    return "(" +
                        Format(conditional.Condition) +
                        " ? " +
                        Format(conditional.WhenTrue) +
                        " : " +
                        Format(conditional.WhenFalse) +
                        ")";
                default:
                    return "?";
            }
        }

        internal static string GetMergeTarget(SmtFormula formula)
        {
            if (formula == null)
            {
                throw new ArgumentNullException(nameof(formula));
            }

            switch (formula)
            {
                case SmtUnaryFormula unary:
                    return GetMergeTarget(unary.Operand);
                case SmtBinaryFormula binary when IsComparison(binary.Operator):
                    return GetComparisonTarget(binary);
                case SmtStringContainsFormula contains:
                    return FormatTerm(contains.Value);
                case SmtStringStartsWithFormula startsWith:
                    return FormatTerm(startsWith.Value);
                case SmtStringEndsWithFormula endsWith:
                    return FormatTerm(endsWith.Value);
                case SmtRegexMatchFormula regex:
                    return FormatTerm(regex.Value);
                case SmtRuntimeTypeTestFormula runtimeTypeTest:
                    return FormatTerm(runtimeTypeTest.Value);
                case SmtVariable variable:
                    return FormatVariableName(variable.Name);
                default:
                    return Format(formula);
            }
        }

        private static string FormatBinary(SmtBinaryFormula binary)
        {
            var op = binary.Operator switch
            {
                SmtBinaryOperator.And => "&&",
                SmtBinaryOperator.Or => "||",
                SmtBinaryOperator.Equal => "==",
                SmtBinaryOperator.NotEqual => "!=",
                SmtBinaryOperator.LessThan => "<",
                SmtBinaryOperator.LessThanOrEqual => "<=",
                SmtBinaryOperator.GreaterThan => ">",
                SmtBinaryOperator.GreaterThanOrEqual => ">=",
                _ => binary.Operator.ToString(),
            };

            if (binary.Operator == SmtBinaryOperator.And ||
                binary.Operator == SmtBinaryOperator.Or)
            {
                return FormatConditionTerm(binary.Left) + " " + op + " " + FormatConditionTerm(binary.Right);
            }

            return FormatTerm(binary.Left) + " " + op + " " + FormatTerm(binary.Right);
        }

        private static string FormatIntegerBinary(SmtIntegerBinaryTerm binary)
        {
            var op = binary.Operator switch
            {
                SmtIntegerBinaryOperator.Add => "+",
                SmtIntegerBinaryOperator.Subtract => "-",
                SmtIntegerBinaryOperator.Multiply => "*",
                SmtIntegerBinaryOperator.Divide => "/",
                SmtIntegerBinaryOperator.Remainder => "%",
                _ => binary.Operator.ToString(),
            };

            return FormatTerm(binary.Left) + " " + op + " " + FormatTerm(binary.Right);
        }

        private static string FormatConditionTerm(SmtFormula formula)
        {
            return formula is SmtBinaryFormula or SmtConditionalFormula
                ? "(" + Format(formula) + ")"
                : Format(formula);
        }

        private static string FormatTerm(SmtFormula formula)
        {
            return formula is SmtBinaryFormula or SmtIntegerBinaryTerm or SmtConditionalFormula
                ? "(" + Format(formula) + ")"
                : Format(formula);
        }

        private static string GetComparisonTarget(SmtBinaryFormula binary)
        {
            var leftTarget = TryGetTermTarget(binary.Left);
            var rightTarget = TryGetTermTarget(binary.Right);
            if (leftTarget != null && IsConstant(binary.Right))
            {
                return leftTarget;
            }

            if (rightTarget != null && IsConstant(binary.Left))
            {
                return rightTarget;
            }

            if (leftTarget != null && rightTarget != null)
            {
                return leftTarget + "," + rightTarget;
            }

            return Format(binary);
        }

        private static string? TryGetTermTarget(SmtFormula formula)
        {
            switch (formula)
            {
                case SmtVariable variable:
                    return FormatVariableName(variable.Name);
                case SmtStringLengthTerm length:
                    return FormatTerm(length.Value) + ".Length";
                case SmtStringConcatTerm:
                case SmtIntegerBinaryTerm:
                case SmtIntegerUnaryTerm:
                    return Format(formula);
                default:
                    return null;
            }
        }

        private static bool IsComparison(SmtBinaryOperator op)
        {
            return op == SmtBinaryOperator.Equal ||
                op == SmtBinaryOperator.NotEqual ||
                op == SmtBinaryOperator.LessThan ||
                op == SmtBinaryOperator.LessThanOrEqual ||
                op == SmtBinaryOperator.GreaterThan ||
                op == SmtBinaryOperator.GreaterThanOrEqual;
        }

        private static bool IsConstant(SmtFormula formula)
        {
            return formula is SmtBooleanConstant or SmtIntegerConstant or SmtStringConstant or SmtNullConstant;
        }

        private static string FormatVariableName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return name ?? string.Empty;
            }

            const string recordPrefix = "SmtVariable {";
            if (name.StartsWith(recordPrefix, StringComparison.Ordinal))
            {
                var nameMarker = "Name = ";
                var nameIndex = name.IndexOf(nameMarker, StringComparison.Ordinal);
                var closeIndex = name.IndexOf(" }", StringComparison.Ordinal);
                if (nameIndex >= 0 && closeIndex > nameIndex)
                {
                    var innerNameStart = nameIndex + nameMarker.Length;
                    var innerName = name.Substring(innerNameStart, closeIndex - innerNameStart).Trim();
                    var suffix = closeIndex + 2 < name.Length
                        ? name.Substring(closeIndex + 2)
                        : string.Empty;
                    return FormatVariableName(innerName) + suffix;
                }
            }

            name = name.Replace(".String", string.Empty);
            var hashIndex = name.LastIndexOf('#');
            if (hashIndex > 0 && hashIndex + 1 < name.Length)
            {
                var index = hashIndex + 1;
                while (index < name.Length && char.IsDigit(name[index]))
                {
                    index++;
                }

                if (index > hashIndex + 1)
                {
                    return name.Substring(0, hashIndex) + name.Substring(index);
                }
            }

            return name;
        }

        private static string EscapeString(string value)
        {
            return (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t");
        }
    }

    public static class SymbolicProgramPointKinds
    {
        public const string Statement = "Statement";
        public const string Expression = "Expression";
        public const string Other = "Other";

        public static string Normalize(string? programPointKind, string? nodeKind = null)
        {
            if (string.Equals(programPointKind, Statement, StringComparison.OrdinalIgnoreCase))
            {
                return Statement;
            }

            if (string.Equals(programPointKind, Expression, StringComparison.OrdinalIgnoreCase))
            {
                return Expression;
            }

            if (string.Equals(programPointKind, Other, StringComparison.OrdinalIgnoreCase))
            {
                return Other;
            }

            return InferFromNodeKind(nodeKind);
        }

        private static string InferFromNodeKind(string? nodeKind)
        {
            if (string.IsNullOrWhiteSpace(nodeKind))
            {
                return Other;
            }

            var nonEmptyNodeKind = nodeKind!;
            if (nonEmptyNodeKind.EndsWith(Statement, StringComparison.Ordinal))
            {
                return Statement;
            }

            if (nonEmptyNodeKind.EndsWith(Expression, StringComparison.Ordinal))
            {
                return Expression;
            }

            return Other;
        }
    }

    public sealed class SymbolicSourceQueryResult
    {
        internal SymbolicSourceQueryResult(
            string filePath,
            int line,
            int column,
            int position,
            int nodeSpanStart,
            string nodeKind,
            IReadOnlyList<string> facts,
            SymbolicReachability reachability = SymbolicReachability.NotChecked,
            string reachabilityReason = "reachability_not_checked",
            IReadOnlyList<SymbolicConditionProofResult>? conditionProofs = null,
            SymbolicSmtDiagnostics? smtDiagnostics = null,
            string? mergedInvariantText = null,
            SymbolicInvariantResult? invariant = null,
            int? nodeSpanEnd = null,
            int? nodeStartLine = null,
            int? nodeStartColumn = null,
            int? nodeEndLine = null,
            int? nodeEndColumn = null,
            string? methodName = null,
            string? programPointKind = null,
            int? requestedLine = null,
            int? requestedColumn = null,
            int? requestedPosition = null,
            int? requestedPositionDistance = null,
            bool? containsRequestedPosition = null,
            IReadOnlyList<SymbolicFactInfo>? symbolicFacts = null)
        {
            FilePath = filePath;
            Line = line;
            Column = column;
            Position = position;
            RequestedLine = requestedLine;
            RequestedColumn = requestedColumn;
            RequestedPosition = requestedPosition;
            RequestedPositionDistance = requestedPositionDistance;
            ContainsRequestedPosition = containsRequestedPosition;
            NodeSpanStart = nodeSpanStart;
            NodeSpanEnd = nodeSpanEnd ?? nodeSpanStart;
            NodeSpanLength = Math.Max(0, NodeSpanEnd - NodeSpanStart);
            NodeStartLine = nodeStartLine ?? line;
            NodeStartColumn = nodeStartColumn ?? column;
            NodeEndLine = nodeEndLine ?? NodeStartLine;
            NodeEndColumn = nodeEndColumn ?? NodeStartColumn + NodeSpanLength;
            NodeKind = nodeKind;
            MethodName = string.IsNullOrWhiteSpace(methodName) ? null : methodName;
            ProgramPointKind = SymbolicProgramPointKinds.Normalize(programPointKind, nodeKind);
            Facts = facts ?? Array.Empty<string>();
            SymbolicFacts = symbolicFacts ?? Array.Empty<SymbolicFactInfo>();
            MergedInvariantText = mergedInvariantText ?? invariant?.MergedInvariantText ?? FormatMergedInvariantText(Facts);
            Invariant = invariant == null
                ? SymbolicInvariantResult.FromFacts(
                    Facts,
                    MergedInvariantText,
                    SymbolicInvariantMergeKind.Conjunction)
                : invariant;
            Reachability = reachability;
            ReachabilityReason = reachabilityReason;
            ConditionProofs = AttachProgramPointMetadata(conditionProofs ?? Array.Empty<SymbolicConditionProofResult>());
            ProofOutcomes = SymbolicProofOutcomeSummary.FromProofs(ConditionProofs);
            InvariantInfo = new SymbolicInvariantInfo(
                MergedInvariantText,
                SymbolicFacts,
                ConditionProofs.Select(static proof => proof.Proof).ToArray(),
                Invariant.MergeKind,
                Invariant.ConditionCount);
            SmtDiagnostics = smtDiagnostics ?? SymbolicSmtDiagnostics.NotConfigured;
            InvariantQuery = SymbolicInvariantQueryView.FromPoint(this);
        }

        public string FilePath { get; }

        public int Line { get; }

        public int Column { get; }

        public int Position { get; }

        public int? RequestedLine { get; }

        public int? RequestedColumn { get; }

        public int? RequestedPosition { get; }

        public int? RequestedPositionDistance { get; }

        public bool? ContainsRequestedPosition { get; }

        public int NodeSpanStart { get; }

        public int NodeSpanEnd { get; }

        public int NodeSpanLength { get; }

        public int NodeStartLine { get; }

        public int NodeStartColumn { get; }

        public int NodeEndLine { get; }

        public int NodeEndColumn { get; }

        public string NodeKind { get; }

        public string? MethodName { get; }

        public string ProgramPointKind { get; }

        public IReadOnlyList<string> Facts { get; }

        public IReadOnlyList<SymbolicFactInfo> SymbolicFacts { get; }

        public string MergedInvariantText { get; }

        public SymbolicInvariantResult Invariant { get; }

        public SymbolicInvariantInfo InvariantInfo { get; }

        public int PathConditionCount => InvariantInfo.ConditionCount;

        public SymbolicReachability Reachability { get; }

        public string ReachabilityReason { get; }

        public IReadOnlyList<SymbolicConditionProofResult> ConditionProofs { get; }

        public SymbolicProofOutcomeSummary ProofOutcomes { get; }

        public SymbolicSmtDiagnostics SmtDiagnostics { get; }

        public SymbolicInvariantQueryView InvariantQuery { get; }

        public SymbolicCompactQueryResult ToCompactResult(SymbolicCompactQueryOptions? options = null)
        {
            return SymbolicCompactQueryResult.FromPoint(this, options);
        }

        public SymbolicInvariantQueryResult ToInvariantQueryResult(SymbolicCompactQueryOptions? options = null)
        {
            return SymbolicInvariantQueryResult.FromPoint(this, options);
        }

        private static string FormatMergedInvariantText(IReadOnlyList<string> facts)
        {
            if (facts.Count == 0)
            {
                return "true";
            }

            if (facts.Count == 1)
            {
                return facts[0];
            }

            return string.Join(" && ", facts.Select(static fact => "(" + fact + ")"));
        }

        private IReadOnlyList<SymbolicConditionProofResult> AttachProgramPointMetadata(
            IReadOnlyList<SymbolicConditionProofResult> proofs)
        {
            if (proofs.Count == 0)
            {
                return proofs;
            }

            return proofs
                .Select(proof => proof.WithProgramPointMetadata(
                    FilePath,
                    Line,
                    Column,
                    Position,
                    NodeSpanStart,
                    NodeSpanEnd,
                    NodeStartLine,
                    NodeStartColumn,
                    NodeEndLine,
                    NodeEndColumn,
                    NodeKind,
                    MethodName,
                    ProgramPointKind,
                    RequestedLine,
                    RequestedColumn,
                    RequestedPosition,
                    RequestedPositionDistance,
                    ContainsRequestedPosition))
                .ToArray();
        }
    }

    public sealed class SymbolicInvariantResult
    {
        private SymbolicInvariantResult(
            IReadOnlyList<SymbolicInvariantCondition> conditions,
            string mergedInvariantText,
            SymbolicInvariantMergeKind mergeKind)
        {
            Conditions = conditions ?? throw new ArgumentNullException(nameof(conditions));
            MergedInvariantText = mergedInvariantText ?? throw new ArgumentNullException(nameof(mergedInvariantText));
            MergeKind = mergeKind;
        }

        public IReadOnlyList<SymbolicInvariantCondition> Conditions { get; }

        public int ConditionCount => Conditions.Count;

        public int ConservativeUnknownCount => Conditions.Count(static condition => condition.IsConservativeUnknown);

        public bool HasConservativeUnknowns => ConservativeUnknownCount != 0;

        public string MergedInvariantText { get; }

        public SymbolicInvariantMergeKind MergeKind { get; }

        public bool IsTrivial => Conditions.Count == 0 && string.Equals(MergedInvariantText, "true", StringComparison.Ordinal);

        public static SymbolicInvariantResult FromFacts(
            IReadOnlyList<string> facts,
            string? mergedInvariantText = null,
            SymbolicInvariantMergeKind mergeKind = SymbolicInvariantMergeKind.DistinctFactUnion)
        {
            if (facts == null)
            {
                throw new ArgumentNullException(nameof(facts));
            }

            return new SymbolicInvariantResult(
                facts
                    .Select(static (fact, index) => SymbolicInvariantCondition.FromText(index, fact))
                    .ToArray(),
                mergedInvariantText ?? SymbolicInvariantService.FormatMergedInvariantFacts(facts),
                mergeKind);
        }

        internal static SymbolicInvariantResult FromFormulas(
            IReadOnlyList<SmtFormula> formulas,
            string? mergedInvariantText = null,
            SymbolicInvariantMergeKind mergeKind = SymbolicInvariantMergeKind.Conjunction)
        {
            if (formulas == null)
            {
                throw new ArgumentNullException(nameof(formulas));
            }

            return new SymbolicInvariantResult(
                formulas
                    .Select(static (formula, index) => SymbolicInvariantCondition.FromFormula(index, formula))
                    .ToArray(),
                mergedInvariantText ?? SymbolicFormulaDisplay.FormatMergedInvariant(formulas),
                mergeKind);
        }

        public static SymbolicInvariantResult FromMergedPathFacts(SymbolicMergedPathFacts facts)
        {
            if (facts == null)
            {
                throw new ArgumentNullException(nameof(facts));
            }

            var conditions = new List<SymbolicInvariantCondition>();
            foreach (var fact in facts.AlwaysFacts)
            {
                conditions.Add(SymbolicInvariantCondition.FromText(conditions.Count, fact));
            }

            foreach (var unknown in facts.ConservativeUnknowns)
            {
                conditions.Add(SymbolicInvariantCondition.FromConservativeUnknown(conditions.Count, unknown));
            }

            if (facts.IsUnreachable)
            {
                conditions.Add(SymbolicInvariantCondition.FromText(conditions.Count, "false"));
            }

            return new SymbolicInvariantResult(
                conditions,
                facts.MergedInvariantText,
                SymbolicInvariantMergeKind.ConservativeFactMerge);
        }
    }

    public sealed class SymbolicInvariantCondition
    {
        private SymbolicInvariantCondition(
            int index,
            string text,
            string formulaKind,
            string valueKind,
            bool isSolverBacked,
            string target,
            bool isConservativeUnknown)
        {
            Index = index;
            Text = text ?? throw new ArgumentNullException(nameof(text));
            FormulaKind = formulaKind ?? throw new ArgumentNullException(nameof(formulaKind));
            ValueKind = valueKind ?? throw new ArgumentNullException(nameof(valueKind));
            IsSolverBacked = isSolverBacked;
            DisplayKind = FormulaKind;
            Target = target ?? string.Empty;
            IsConservativeUnknown = isConservativeUnknown;
        }

        public int Index { get; }

        public string Text { get; }

        public string DisplayKind { get; }

        public string ValueKind { get; }

        public bool IsSolverBacked { get; }

        internal string FormulaKind { get; }

        public string Target { get; }

        public bool IsConservativeUnknown { get; }

        public static SymbolicInvariantCondition FromText(int index, string text)
        {
            var normalizedText = text ?? string.Empty;
            return new SymbolicInvariantCondition(
                index,
                normalizedText,
                "Text",
                "Unknown",
                isSolverBacked: false,
                TextFactTargetExtraction.Extract(normalizedText),
                isConservativeUnknown: false);
        }

        internal static SymbolicInvariantCondition FromFormula(int index, SmtFormula formula)
        {
            if (formula == null)
            {
                throw new ArgumentNullException(nameof(formula));
            }

            return new SymbolicInvariantCondition(
                index,
                SymbolicFormulaDisplay.Format(formula),
                GetFormulaKind(formula),
                formula.Kind.ToString(),
                isSolverBacked: true,
                SymbolicFormulaDisplay.GetMergeTarget(formula),
                isConservativeUnknown: false);
        }

        public static SymbolicInvariantCondition FromConservativeUnknown(int index, string text)
        {
            var target = ExtractConservativeUnknownTarget(text);
            return new SymbolicInvariantCondition(
                index,
                text ?? string.Empty,
                "ConservativeUnknown",
                "Unknown",
                isSolverBacked: false,
                target,
                isConservativeUnknown: true);
        }

        private static string ExtractConservativeUnknownTarget(string? text)
        {
            const string prefix = "unknown(";
            if (text != null &&
                text.StartsWith(prefix, StringComparison.Ordinal) &&
                text.EndsWith(")", StringComparison.Ordinal) &&
                text.Length > prefix.Length + 1)
            {
                return text.Substring(prefix.Length, text.Length - prefix.Length - 1);
            }

            return text ?? string.Empty;
        }

        private static string GetFormulaKind(SmtFormula formula)
        {
            var typeName = formula.GetType().Name;
            return typeName.EndsWith("Formula", StringComparison.Ordinal)
                ? typeName.Substring(0, typeName.Length - "Formula".Length)
                : typeName;
        }



    }

    internal static class TextFactTargetExtraction
    {
        internal static string? TryExtract(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            return ScanIdentifierTarget(Unwrap(text!.Trim()));
        }

        internal static string Extract(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var value = Unwrap(text.Trim());
            return ScanIdentifierTarget(value) ?? value;
        }

        private static string Unwrap(string value)
        {
            while (value.StartsWith("!", StringComparison.Ordinal) ||
                (value.StartsWith("(", StringComparison.Ordinal) && value.EndsWith(")", StringComparison.Ordinal)))
            {
                value = value.StartsWith("!", StringComparison.Ordinal)
                    ? value.Substring(1).TrimStart()
                    : value.Substring(1, value.Length - 2).Trim();
            }

            return value;
        }

        private static string? ScanIdentifierTarget(string value)
        {
            for (var index = 0; index < value.Length; index++)
            {
                if (!SyntaxFacts.IsIdentifierStartCharacter(value[index]) && value[index] != '@')
                {
                    continue;
                }

                var start = index;
                index++;
                while (index < value.Length && SyntaxFacts.IsIdentifierPartCharacter(value[index]))
                {
                    index++;
                }

                var target = value.Substring(start, index - start);
                if (index + ".Length".Length <= value.Length &&
                    string.Equals(value.Substring(index, ".Length".Length), ".Length", StringComparison.Ordinal))
                {
                    target += ".Length";
                }

                return target;
            }

            return null;
        }
    }

    public enum SymbolicInvariantMergeKind
    {
        Conjunction,
        DistinctFactUnion,
        ConservativeFactMerge,
    }

    internal sealed class SymbolicProgramPointQueryResult
    {
        public SymbolicProgramPointQueryResult(
            string filePath,
            int line,
            int column,
            int position,
            int nodeSpanStart,
            string nodeKind,
            SymbolicProgramPointAnalysis analysis,
            string? programPointKind = null)
        {
            FilePath = filePath;
            Line = line;
            Column = column;
            Position = position;
            NodeSpanStart = nodeSpanStart;
            NodeKind = nodeKind;
            ProgramPointKind = SymbolicProgramPointKinds.Normalize(programPointKind, nodeKind);
            Analysis = analysis ?? throw new ArgumentNullException(nameof(analysis));
        }

        public string FilePath { get; }

        public int Line { get; }

        public int Column { get; }

        public int Position { get; }

        public int NodeSpanStart { get; }

        public string NodeKind { get; }

        public string ProgramPointKind { get; }

        public SymbolicProgramPointAnalysis Analysis { get; }

        public IReadOnlyList<string> Facts => Analysis.Facts;

        public IReadOnlyList<SymbolicFactInfo> SymbolicFacts => SymbolicFactInfo.FromState(Analysis.PathState);

        public string MergedInvariantText => Analysis.MergedInvariantText;

        public SymbolicReachability Reachability => Analysis.Reachability;

        public string ReachabilityReason => Analysis.ReachabilityReason;

        public SymbolicSmtDiagnostics SmtDiagnostics => Analysis.SmtDiagnostics;
    }

    public sealed class SymbolicConditionProofResult
    {
        internal SymbolicConditionProofResult(
            string condition,
            SymbolicTruthValue truthValue,
            string reason,
            SmtFormula? formula = null,
            string? target = null,
            string? formulaKind = null,
            string? valueKind = null,
            string? formulaText = null,
            bool? isSolverBacked = null,
            string? filePath = null,
            int? line = null,
            int? column = null,
            int? position = null,
            int? nodeSpanStart = null,
            int? nodeSpanEnd = null,
            int? nodeStartLine = null,
            int? nodeStartColumn = null,
            int? nodeEndLine = null,
            int? nodeEndColumn = null,
            string? nodeKind = null,
            string? methodName = null,
            string? programPointKind = null,
            int? requestedLine = null,
            int? requestedColumn = null,
            int? requestedPosition = null,
            int? requestedPositionDistance = null,
            bool? containsRequestedPosition = null)
        {
            Condition = condition ?? string.Empty;
            TruthValue = truthValue;
            Reason = reason ?? string.Empty;
            IsSolverBacked = isSolverBacked ?? formula != null;
            FormulaText = string.IsNullOrWhiteSpace(formulaText)
                ? formula == null
                    ? Condition
                    : SymbolicFormulaDisplay.Format(formula)
                : formulaText!;
            FormulaKind = string.IsNullOrWhiteSpace(formulaKind)
                ? formula == null
                    ? "Unknown"
                    : GetFormulaKind(formula)
                : formulaKind!;
            ValueKind = string.IsNullOrWhiteSpace(valueKind)
                ? formula == null
                    ? "Unknown"
                    : formula.Kind.ToString()
                : valueKind!;
            Target = string.IsNullOrWhiteSpace(target)
                ? formula == null
                    ? string.Empty
                    : SymbolicFormulaDisplay.GetMergeTarget(formula)
                : target!;
            DisplayKind = FormulaKind;
            FilePath = string.IsNullOrWhiteSpace(filePath) ? null : filePath;
            Line = line;
            Column = column;
            Position = position;
            NodeSpanStart = nodeSpanStart;
            NodeSpanEnd = nodeSpanEnd;
            NodeSpanLength = nodeSpanStart.HasValue && nodeSpanEnd.HasValue
                ? Math.Max(0, nodeSpanEnd.Value - nodeSpanStart.Value)
                : null;
            NodeStartLine = nodeStartLine;
            NodeStartColumn = nodeStartColumn;
            NodeEndLine = nodeEndLine;
            NodeEndColumn = nodeEndColumn;
            NodeKind = string.IsNullOrWhiteSpace(nodeKind) ? null : nodeKind;
            MethodName = string.IsNullOrWhiteSpace(methodName) ? null : methodName;
            ProgramPointKind = string.IsNullOrWhiteSpace(programPointKind) ? null : programPointKind;
            RequestedLine = requestedLine;
            RequestedColumn = requestedColumn;
            RequestedPosition = requestedPosition;
            RequestedPositionDistance = requestedPositionDistance;
            ContainsRequestedPosition = containsRequestedPosition;
            Proof = new SymbolicProofInfo(
                MapProofStatus(TruthValue),
                ResolveProofBackend(TruthValue, IsSolverBacked),
                ResolveUnknownReason(TruthValue, Reason),
                Reason,
                cacheHit: false,
                budget: null,
                Target,
                FormulaText,
                FormulaKind);
        }

        public string Condition { get; }

        public string Target { get; }

        public string DisplayKind { get; }

        public string ValueKind { get; }

        internal string FormulaKind { get; }

        internal string FormulaText { get; }

        internal bool IsSolverBacked { get; }

        public SymbolicTruthValue TruthValue { get; }

        public string Reason { get; }

        public SymbolicProofInfo Proof { get; }

        public string? FilePath { get; }

        public int? Line { get; }

        public int? Column { get; }

        public int? Position { get; }

        public int? NodeSpanStart { get; }

        public int? NodeSpanEnd { get; }

        public int? NodeSpanLength { get; }

        public int? NodeStartLine { get; }

        public int? NodeStartColumn { get; }

        public int? NodeEndLine { get; }

        public int? NodeEndColumn { get; }

        public string? NodeKind { get; }

        public string? MethodName { get; }

        public string? ProgramPointKind { get; }

        public int? RequestedLine { get; }

        public int? RequestedColumn { get; }

        public int? RequestedPosition { get; }

        public int? RequestedPositionDistance { get; }

        public bool? ContainsRequestedPosition { get; }

        public string GetDisplayReason()
        {
            if (string.IsNullOrWhiteSpace(Reason))
            {
                return Reason;
            }

            return Reason switch
            {
                "unsupported_formula_fallback" => "unsupported formula fallback; legacy translated trigger was not trusted as proof",
                "smt_disabled" => "SMT disabled",
                "smt_disposed" => "SMT solver disposed",
                "smt_timeout" => "SMT solver timed out",
                "smt_unavailable" => "SMT solver unavailable",
                "smt_encoding_failure" => "SMT formula encoding failed",
                "smt_expression_budget_exceeded" => "SMT expression node budget exceeded",
                "smt_path_condition_budget_exceeded" => "SMT path condition budget exceeded",
                "smt_method_budget_exceeded" => "SMT method-level budget exceeded",
                "trigger_always_true" => "trigger condition is always true",
                "trigger_always_false" => "trigger condition is always false",
                "path_unsatisfiable" => "path condition is unsatisfiable",
                "condition_parse_failure" => "condition could not be parsed",
                "not_common_to_all_candidate_program_points" => "not common to all candidate program points",
                _ => Reason,
            };
        }

        internal SymbolicConditionProofResult WithProgramPointMetadata(
            string filePath,
            int line,
            int column,
            int position,
            int nodeSpanStart,
            int nodeSpanEnd,
            int nodeStartLine,
            int nodeStartColumn,
            int nodeEndLine,
            int nodeEndColumn,
            string nodeKind,
            string? methodName,
            string programPointKind,
            int? requestedLine,
            int? requestedColumn,
            int? requestedPosition,
            int? requestedPositionDistance,
            bool? containsRequestedPosition)
        {
            return new SymbolicConditionProofResult(
                Condition,
                TruthValue,
                Reason,
                target: Target,
                formulaKind: FormulaKind,
                valueKind: ValueKind,
                formulaText: FormulaText,
                isSolverBacked: IsSolverBacked,
                filePath: FilePath ?? filePath,
                line: Line ?? line,
                column: Column ?? column,
                position: Position ?? position,
                nodeSpanStart: NodeSpanStart ?? nodeSpanStart,
                nodeSpanEnd: NodeSpanEnd ?? nodeSpanEnd,
                nodeStartLine: NodeStartLine ?? nodeStartLine,
                nodeStartColumn: NodeStartColumn ?? nodeStartColumn,
                nodeEndLine: NodeEndLine ?? nodeEndLine,
                nodeEndColumn: NodeEndColumn ?? nodeEndColumn,
                nodeKind: NodeKind ?? nodeKind,
                methodName: MethodName ?? methodName,
                programPointKind: ProgramPointKind ?? programPointKind,
                requestedLine: RequestedLine ?? requestedLine,
                requestedColumn: RequestedColumn ?? requestedColumn,
                requestedPosition: RequestedPosition ?? requestedPosition,
                requestedPositionDistance: RequestedPositionDistance ?? requestedPositionDistance,
                containsRequestedPosition: ContainsRequestedPosition ?? containsRequestedPosition);
        }

        private static string GetFormulaKind(SmtFormula formula)
        {
            var name = formula.GetType().Name;
            return name.EndsWith("Formula", StringComparison.Ordinal)
                ? name.Substring(0, name.Length - "Formula".Length)
                : name;
        }

        private static SymbolicProofStatus MapProofStatus(SymbolicTruthValue truthValue)
        {
            return truthValue switch
            {
                SymbolicTruthValue.ProvenTrue => SymbolicProofStatus.ProvenTrue,
                SymbolicTruthValue.ProvenFalse => SymbolicProofStatus.ProvenFalse,
                SymbolicTruthValue.Unreachable => SymbolicProofStatus.Unreachable,
                _ => SymbolicProofStatus.Unknown,
            };
        }

        private static SymbolicProofBackend ResolveProofBackend(
            SymbolicTruthValue truthValue,
            bool isSolverBacked)
        {
            if (isSolverBacked)
            {
                return SymbolicProofBackend.Smt;
            }

            return truthValue == SymbolicTruthValue.Unknown
                ? SymbolicProofBackend.None
                : SymbolicProofBackend.Syntactic;
        }

        private static SymbolicUnknownReason ResolveUnknownReason(
            SymbolicTruthValue truthValue,
            string reason)
        {
            if (truthValue != SymbolicTruthValue.Unknown)
            {
                return SymbolicUnknownReason.None;
            }

            return SymbolicUnknownReasonClassifier.Classify(reason);
        }
    }

    public enum SymbolicTruthValue
    {
        Unknown,
        ProvenTrue,
        ProvenFalse,
        Unreachable,
    }
}

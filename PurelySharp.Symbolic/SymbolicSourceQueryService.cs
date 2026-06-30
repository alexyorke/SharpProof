using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using PurelySharp.Symbolic.Smt;
using SearchLib.Purity;
using SearchLib.Smt;

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

        public SymbolicProgramPointQueryResult AnalyzeFileAtPosition(
            string filePath,
            int position,
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

            return AnalyzeSourceAtPosition(
                File.ReadAllText(filePath),
                Path.GetFullPath(filePath),
                position,
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
            var nodeSourceSpan = GetNodeSourceSpan(syntaxTree, query.Node.Span, cancellationToken);
            var conditionProofs = ProveConditions(
                query.SemanticModel,
                query.Position,
                query.Analysis,
                impliedConditions,
                smtAnalysis,
                cancellationToken);

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
                SymbolicSmtDiagnostics.FromService(smtAnalysis),
                SymbolicFormulaDisplay.FormatMergedInvariant(query.Analysis.PathConditions),
                query.Analysis.PathConditions,
                query.Node.Span.End,
                nodeSourceSpan.StartLine,
                nodeSourceSpan.StartColumn,
                nodeSourceSpan.EndLine,
                nodeSourceSpan.EndColumn,
                GetContainingMethodName(query.Node),
                GetProgramPointKind(query.Node));
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
                    var lineColumn = GetLineAndColumn(syntaxTree, query.Position, cancellationToken);
                    var nodeSourceSpan = GetNodeSourceSpan(syntaxTree, query.Node.Span, cancellationToken);
                    var conditionProofs = ProveConditions(
                        query.SemanticModel,
                        query.Position,
                        query.Analysis,
                        impliedConditions,
                        smtAnalysis,
                        cancellationToken);

                    return new SymbolicSourceQueryResult(
                        syntaxTree.FilePath,
                        lineColumn.Line,
                        lineColumn.Column,
                        query.Position,
                        query.Node.SpanStart,
                        query.Node.Kind().ToString(),
                        query.Analysis.Facts,
                        query.Analysis.Reachability,
                        query.Analysis.ReachabilityReason,
                        conditionProofs,
                        SymbolicSmtDiagnostics.FromService(smtAnalysis),
                        SymbolicFormulaDisplay.FormatMergedInvariant(query.Analysis.PathConditions),
                        query.Analysis.PathConditions,
                        query.Node.Span.End,
                        nodeSourceSpan.StartLine,
                        nodeSourceSpan.StartColumn,
                        nodeSourceSpan.EndLine,
                        nodeSourceSpan.EndColumn,
                        GetContainingMethodName(query.Node),
                        GetProgramPointKind(query.Node));
                })
                .ToArray();

            return new SymbolicLineQueryResult(
                syntaxTree.FilePath,
                line,
                results,
                SymbolicSmtDiagnostics.FromService(smtAnalysis));
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

            var sourceSpan = GetSourceSpan(syntaxTree, spanStart, spanEnd, cancellationToken);
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
                    var lineColumn = GetLineAndColumn(syntaxTree, query.Position, cancellationToken);
                    var nodeSourceSpan = GetNodeSourceSpan(syntaxTree, query.Node.Span, cancellationToken);
                    var conditionProofs = ProveConditions(
                        query.SemanticModel,
                        query.Position,
                        query.Analysis,
                        impliedConditions,
                        smtAnalysis,
                        cancellationToken);

                    return new SymbolicSourceQueryResult(
                        syntaxTree.FilePath,
                        lineColumn.Line,
                        lineColumn.Column,
                        query.Position,
                        query.Node.SpanStart,
                        query.Node.Kind().ToString(),
                        query.Analysis.Facts,
                        query.Analysis.Reachability,
                        query.Analysis.ReachabilityReason,
                        conditionProofs,
                        SymbolicSmtDiagnostics.FromService(smtAnalysis),
                        SymbolicFormulaDisplay.FormatMergedInvariant(query.Analysis.PathConditions),
                        query.Analysis.PathConditions,
                        query.Node.Span.End,
                        nodeSourceSpan.StartLine,
                        nodeSourceSpan.StartColumn,
                        nodeSourceSpan.EndLine,
                        nodeSourceSpan.EndColumn,
                        GetContainingMethodName(query.Node),
                        GetProgramPointKind(query.Node));
                })
                .ToArray();
            var startLineColumn = GetLineAndColumn(syntaxTree, sourceSpan.Start, cancellationToken);
            var endLineColumn = GetLineAndColumn(syntaxTree, sourceSpan.End, cancellationToken);

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
            var lineColumn = GetLineAndColumn(syntaxTree, position, cancellationToken);
            var nodeSourceSpan = GetNodeSourceSpan(syntaxTree, query.Node.Span, cancellationToken);
            var conditionProofs = ProveConditions(
                query.SemanticModel,
                query.Position,
                query.Analysis,
                impliedConditions,
                smtAnalysis,
                cancellationToken);

            return new SymbolicSourceQueryResult(
                syntaxTree.FilePath,
                lineColumn.Line,
                lineColumn.Column,
                query.Position,
                query.Node.SpanStart,
                query.Node.Kind().ToString(),
                query.Analysis.Facts,
                query.Analysis.Reachability,
                query.Analysis.ReachabilityReason,
                conditionProofs,
                SymbolicSmtDiagnostics.FromService(smtAnalysis),
                SymbolicFormulaDisplay.FormatMergedInvariant(query.Analysis.PathConditions),
                query.Analysis.PathConditions,
                query.Node.Span.End,
                nodeSourceSpan.StartLine,
                nodeSourceSpan.StartColumn,
                nodeSourceSpan.EndLine,
                nodeSourceSpan.EndColumn,
                GetContainingMethodName(query.Node),
                GetProgramPointKind(query.Node));
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
                GetProgramPointKind(query.Node));
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
            var lineColumn = GetLineAndColumn(syntaxTree, position, cancellationToken);
            return new SymbolicProgramPointQueryResult(
                syntaxTree.FilePath,
                lineColumn.Line,
                lineColumn.Column,
                query.Position,
                query.Node.SpanStart,
                query.Node.Kind().ToString(),
                query.Analysis,
                GetProgramPointKind(query.Node));
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
                smtAnalysis,
                cancellationToken);
            return ProveCondition(
                query.SemanticModel,
                query.Position,
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
            var position = GetPosition(syntaxTree, line, column, cancellationToken);
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

            if (!CSharpConditionToFormula.TryTranslate(
                    condition,
                    conditionSemanticModel,
                    cancellationToken,
                    out var conditionFormula) ||
                conditionFormula == null)
            {
                return new SymbolicConditionProofResult(
                    conditionText,
                    SymbolicTruthValue.Unknown,
                    "condition_not_supported");
            }

            var trueProof = smtAnalysis.ClassifyImplication(analysis.PathConditions, conditionFormula);
            if (trueProof.Outcome == PurityProofOutcome.ProvablyPure)
            {
                return new SymbolicConditionProofResult(
                    conditionText,
                    SymbolicTruthValue.ProvenTrue,
                    trueProof.Reason);
            }

            var falseProof = smtAnalysis.ClassifyImplication(
                analysis.PathConditions,
                new SmtUnaryFormula(SmtUnaryOperator.Not, conditionFormula));
            if (falseProof.Outcome == PurityProofOutcome.ProvablyPure)
            {
                return new SymbolicConditionProofResult(
                    conditionText,
                    SymbolicTruthValue.ProvenFalse,
                    falseProof.Reason);
            }

            return new SymbolicConditionProofResult(
                conditionText,
                SymbolicTruthValue.Unknown,
                falseProof.Reason);
        }

        public static ImmutableArray<MetadataReference> GetTrustedPlatformReferences()
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
            var lineSpan = GetLineSpan(syntaxTree, line, cancellationToken);
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

        private static TextSpan GetLineSpan(
            SyntaxTree syntaxTree,
            int line,
            CancellationToken cancellationToken)
        {
            if (line < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(line), "--line must be 1 or greater.");
            }

            var text = syntaxTree.GetText(cancellationToken);
            if (line > text.Lines.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(line), "--line exceeds the file line count.");
            }

            return text.Lines[line - 1].Span;
        }

        private static TextSpan GetSourceSpan(
            SyntaxTree syntaxTree,
            int spanStart,
            int spanEnd,
            CancellationToken cancellationToken)
        {
            var text = syntaxTree.GetText(cancellationToken);
            if (spanStart < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(spanStart), "--span-start must be zero or greater.");
            }

            if (spanEnd < spanStart)
            {
                throw new ArgumentOutOfRangeException(nameof(spanEnd), "--span-end cannot be less than --span-start.");
            }

            if (spanEnd > text.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(spanEnd), "--span-end exceeds the source text length.");
            }

            return TextSpan.FromBounds(spanStart, spanEnd);
        }

        private static LineColumn GetLineAndColumn(
            SyntaxTree syntaxTree,
            int position,
            CancellationToken cancellationToken)
        {
            var text = syntaxTree.GetText(cancellationToken);
            if (position < 0 || position > text.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(position), "--position must be within the source text span.");
            }

            var line = text.Lines.GetLineFromPosition(position);
            return new LineColumn(line.LineNumber + 1, position - line.Start + 1);
        }

        private static NodeSourceSpan GetNodeSourceSpan(
            SyntaxTree syntaxTree,
            TextSpan span,
            CancellationToken cancellationToken)
        {
            var text = syntaxTree.GetText(cancellationToken);
            var startLine = text.Lines.GetLineFromPosition(span.Start);
            var endLine = text.Lines.GetLineFromPosition(span.End);
            return new NodeSourceSpan(
                startLine.LineNumber + 1,
                span.Start - startLine.Start + 1,
                endLine.LineNumber + 1,
                span.End - endLine.Start + 1);
        }

        private static string? GetContainingMethodName(SyntaxNode node)
        {
            foreach (var ancestor in node.AncestorsAndSelf())
            {
                switch (ancestor)
                {
                    case MethodDeclarationSyntax method:
                        return method.Identifier.ValueText;
                    case LocalFunctionStatementSyntax localFunction:
                        return localFunction.Identifier.ValueText;
                    case ConstructorDeclarationSyntax constructor:
                        return constructor.Identifier.ValueText;
                    case DestructorDeclarationSyntax destructor:
                        return "~" + destructor.Identifier.ValueText;
                    case OperatorDeclarationSyntax operatorDeclaration:
                        return "operator " + operatorDeclaration.OperatorToken.ValueText;
                    case ConversionOperatorDeclarationSyntax conversionOperator:
                        return "operator " + conversionOperator.Type;
                }
            }

            return null;
        }

        private static string GetProgramPointKind(SyntaxNode node)
        {
            return node switch
            {
                StatementSyntax => SymbolicProgramPointKinds.Statement,
                ExpressionSyntax => SymbolicProgramPointKinds.Expression,
                _ => SymbolicProgramPointKinds.Other,
            };
        }

        private readonly struct LineColumn
        {
            public LineColumn(int line, int column)
            {
                Line = line;
                Column = column;
            }

            public int Line { get; }

            public int Column { get; }
        }

        private readonly struct NodeSourceSpan
        {
            public NodeSourceSpan(
                int startLine,
                int startColumn,
                int endLine,
                int endColumn)
            {
                StartLine = startLine;
                StartColumn = startColumn;
                EndLine = endLine;
                EndColumn = endColumn;
            }

            public int StartLine { get; }

            public int StartColumn { get; }

            public int EndLine { get; }

            public int EndColumn { get; }
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

    public sealed class SymbolicFileQuery
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
            References = references?.ToImmutableArray() ?? ImmutableArray<MetadataReference>.Empty;
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
        public SymbolicLineQueryResult(
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
            SmtDiagnostics = smtDiagnostics ?? SymbolicSmtDiagnostics.NotConfigured;
            InvariantQuery = SymbolicInvariantQueryView.FromMergedPathFacts(
                MergedInvariant,
                MergedPathFacts,
                Reachability,
                ProgramPointSummary.ProofOutcomes,
                SmtDiagnostics);
        }

        public string FilePath { get; }

        public int Line { get; }

        public IReadOnlyList<SymbolicSourceQueryResult> ProgramPoints { get; }

        public IReadOnlyList<string> Facts { get; }

        public int ObservedFactCount { get; }

        public SymbolicInvariantResult ObservedInvariant { get; }

        public SymbolicMergedPathFacts MergedPathFacts { get; }

        public string MergedInvariantText { get; }

        public SymbolicInvariantResult MergedInvariant { get; }

        public SymbolicProgramPointSummary ProgramPointSummary { get; }

        public SymbolicReachabilitySummary Reachability { get; }

        public IReadOnlyList<SymbolicConditionProofSummary> ConditionProofs { get; }

        public SymbolicSmtDiagnostics SmtDiagnostics { get; }

        public SymbolicInvariantQueryView InvariantQuery { get; }

        public SymbolicCompactQueryResult ToCompactResult(SymbolicCompactQueryOptions? options = null)
        {
            return SymbolicCompactQueryResult.FromLine(this, options);
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
        public SymbolicSpanQueryResult(
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
            SmtDiagnostics = smtDiagnostics ?? SymbolicSmtDiagnostics.NotConfigured;
            InvariantQuery = SymbolicInvariantQueryView.FromMergedPathFacts(
                MergedInvariant,
                MergedPathFacts,
                Reachability,
                ProgramPointSummary.ProofOutcomes,
                SmtDiagnostics);
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

        public SymbolicInvariantResult MergedInvariant { get; }

        public SymbolicProgramPointSummary ProgramPointSummary { get; }

        public SymbolicReachabilitySummary Reachability { get; }

        public IReadOnlyList<SymbolicConditionProofSummary> ConditionProofs { get; }

        public SymbolicSmtDiagnostics SmtDiagnostics { get; }

        public SymbolicInvariantQueryView InvariantQuery { get; }

        public SymbolicCompactQueryResult ToCompactResult(SymbolicCompactQueryOptions? options = null)
        {
            return SymbolicCompactQueryResult.FromSpan(this, options);
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
        public SymbolicFileQueryResult(
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
            SmtDiagnostics = smtDiagnostics ?? SymbolicSmtDiagnostics.NotConfigured;
            InvariantQuery = SymbolicInvariantQueryView.FromMergedPathFacts(
                MergedInvariant,
                MergedPathFacts,
                Reachability,
                ProgramPointSummary.ProofOutcomes,
                SmtDiagnostics);
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

        public SymbolicInvariantResult MergedInvariant { get; }

        public SymbolicReachabilitySummary Reachability { get; }

        public IReadOnlyList<SymbolicConditionProofSummary> ConditionProofs { get; }

        public SymbolicSmtDiagnostics SmtDiagnostics { get; }

        public SymbolicInvariantQueryView InvariantQuery { get; }

        public SymbolicCompactQueryResult ToCompactResult(SymbolicCompactQueryOptions? options = null)
        {
            return SymbolicCompactQueryResult.FromFile(this, options);
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
                result.PathConditions.Select(static condition => condition.Text).ToArray(),
                Array.Empty<string>(),
                result.Invariant.Conditions
                    .Where(static condition => condition.IsConservativeUnknown)
                    .Select(static condition => condition.Text)
                    .ToArray(),
                Array.Empty<SymbolicConservativeUnknownDiagnostic>(),
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
            SymbolicSmtDiagnostics smtDiagnostics)
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
                    "PS-SYM-UNREACHABLE",
                    "Info",
                    "No reachable candidate program points contributed invariant facts.",
                    UnreachableProgramPointCount,
                    new[] { "UnreachableProgramPoints=" + UnreachableProgramPointCount.ToString(CultureInfo.InvariantCulture) }));
            }

            if (MaybeFacts.Count != 0)
            {
                diagnostics.Add(SymbolicInvariantQueryDiagnostic.Create(
                    "PS-SYM-MAYBE-FACTS",
                    "Info",
                    "Some path facts are present on only a subset of candidate program points.",
                    MaybeFacts.Count,
                    MaybeFacts));
            }

            if (UnknownFacts.Count != 0)
            {
                diagnostics.Add(SymbolicInvariantQueryDiagnostic.Create(
                    "PS-SYM-CONSERVATIVE-UNKNOWN",
                    "Warning",
                    "The merged invariant contains conservative unknown placeholders for path-varying targets.",
                    UnknownFacts.Count,
                    UnknownFacts));
            }

            if (Reachability.UnknownCount != 0 || Reachability.NotCheckedCount != 0)
            {
                diagnostics.Add(SymbolicInvariantQueryDiagnostic.Create(
                    "PS-SYM-REACHABILITY",
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
                    "PS-SYM-PROOF-UNKNOWN",
                    "Warning",
                    "Some requested implication proofs were not resolved by bounded SMT.",
                    ProofOutcomes.UnknownCount,
                    new[] { "UnknownProofs=" + ProofOutcomes.UnknownCount.ToString(CultureInfo.InvariantCulture) }));
            }

            if (SmtDiagnostics.IsConfigured && !SmtDiagnostics.IsEnabled)
            {
                diagnostics.Add(SymbolicInvariantQueryDiagnostic.Create(
                    "PS-SYM-SMT-DISABLED",
                    "Warning",
                    "SMT is configured but disabled, so solver-backed reachability and implication proofs are conservative.",
                    1,
                    new[] { "Mode=" + SmtDiagnostics.Mode.ToString() }));
            }

            return diagnostics;
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
            int maxProofs = DefaultMaxProofs)
        {
            MaxLines = ValidateNonNegative(maxLines, nameof(maxLines));
            MaxProgramPoints = ValidateNonNegative(maxProgramPoints, nameof(maxProgramPoints));
            MaxFacts = ValidateNonNegative(maxFacts, nameof(maxFacts));
            MaxConditions = ValidateNonNegative(maxConditions, nameof(maxConditions));
            MaxProofs = ValidateNonNegative(maxProofs, nameof(maxProofs));
        }

        public int MaxLines { get; }

        public int MaxProgramPoints { get; }

        public int MaxFacts { get; }

        public int MaxConditions { get; }

        public int MaxProofs { get; }

        private static int ValidateNonNegative(int value, string paramName)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(paramName, "Compact output limits cannot be negative.");
            }

            return value;
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
            SymbolicCompactOutputTruncation truncation)
        {
            Kind = kind ?? throw new ArgumentNullException(nameof(kind));
            FilePath = filePath ?? string.Empty;
            Line = line;
            Column = column;
            Position = position;
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
            var conditionProofSummaries = SymbolicConditionProofSummary.FromProgramPoints(sourcePoints);
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
                    SymbolicCompactOutputTruncation.FromInvariant(conservativeInvariant)));
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
            var conditionProofs = SymbolicCompactProjection.Take(
                result.ConditionProofs,
                normalizedOptions.MaxProofs);
            var truncation = SymbolicCompactOutputTruncation.Combine(
                new SymbolicCompactOutputTruncation(
                    false,
                    result.ProgramPoints.Count > programPoints.Length,
                    false,
                    false,
                    result.ConditionProofs.Count > normalizedOptions.MaxProofs),
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
            var selectedProgramPointCount = lineResults.Sum(static line => line.ProgramPoints.Count);
            var truncation = SymbolicCompactOutputTruncation.Combine(
                new SymbolicCompactOutputTruncation(
                    result.Lines.Count > lineResults.Count,
                    result.ProgramPointCount > selectedProgramPointCount,
                    false,
                    false,
                    result.ConditionProofs.Count > normalizedOptions.MaxProofs),
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
                SymbolicCompactProjection.Take(result.ConditionProofs, normalizedOptions.MaxProofs),
                lineResults,
                Array.Empty<SymbolicCompactProgramPointResult>(),
                SymbolicCompactSmtDiagnostics.FromDiagnostics(result.SmtDiagnostics),
                truncation);
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
            var proofSummaries = SymbolicConditionProofSummary.FromProgramPoints(result.ProgramPoints);
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
            SymbolicCompactOutputTruncation truncation)
        {
            FilePath = filePath ?? string.Empty;
            Line = line;
            Column = column;
            Position = position;
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
            ObservedInvariant = observedInvariant ?? throw new ArgumentNullException(nameof(observedInvariant));
            ConservativeInvariant = conservativeInvariant ?? throw new ArgumentNullException(nameof(conservativeInvariant));
            InvariantQuery = invariantQuery ?? throw new ArgumentNullException(nameof(invariantQuery));
            MergedInvariantText = ConservativeInvariant.Text;
            PathConditionCount = pathConditionCount;
            PathConditions = pathConditions ?? throw new ArgumentNullException(nameof(pathConditions));
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

        public SymbolicCompactInvariantSummary ObservedInvariant { get; }

        public SymbolicCompactInvariantSummary ConservativeInvariant { get; }

        public SymbolicCompactInvariantQueryView InvariantQuery { get; }

        public string MergedInvariantText { get; }

        public int PathConditionCount { get; }

        public IReadOnlyList<SymbolicInvariantCondition> PathConditions { get; }

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
            var facts = SymbolicCompactProjection.Take(result.Facts, options.MaxFacts);
            var pathConditions = SymbolicCompactProjection.Take(result.PathConditions, options.MaxConditions);
            var conditionProofs = SymbolicCompactProjection.Take(result.ConditionProofs, options.MaxProofs);
            var truncation = SymbolicCompactOutputTruncation.Combine(
                new SymbolicCompactOutputTruncation(
                    false,
                    false,
                    result.Facts.Count > facts.Count,
                    result.PathConditions.Count > pathConditions.Count,
                    result.ConditionProofs.Count > conditionProofs.Count),
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
                result.Facts.Count,
                facts,
                observedInvariant,
                conservativeInvariant,
                SymbolicCompactInvariantQueryView.FromQueryView(result.InvariantQuery, options),
                result.PathConditionCount,
                pathConditions,
                result.Reachability.ToString(),
                result.ReachabilityReason,
                conditionProofs,
                result.ProofOutcomes,
                SymbolicCompactSmtDiagnostics.FromDiagnostics(result.SmtDiagnostics),
                truncation);
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

        public bool DiagnosticsTruncated { get; }

        public bool IsTruncated =>
            MustFactsTruncated ||
            MaybeFactsTruncated ||
            UnknownFactsTruncated ||
            UnknownDiagnosticsTruncated ||
            DiagnosticsTruncated ||
            Diagnostics.Any(static diagnostic => diagnostic.EvidenceTruncated) ||
            UnknownDiagnostics.Any(static diagnostic => diagnostic.MaybeFactsTruncated);

        internal static SymbolicCompactInvariantQueryView FromQueryView(
            SymbolicInvariantQueryView query,
            SymbolicCompactQueryOptions options)
        {
            if (query == null)
            {
                throw new ArgumentNullException(nameof(query));
            }

            var unknownDiagnostics = SymbolicCompactProjection
                .Take(query.UnknownDiagnostics, options.MaxConditions)
                .Select(diagnostic => SymbolicCompactConservativeUnknownDiagnostic.FromDiagnostic(diagnostic, options))
                .ToArray();
            var diagnostics = SymbolicCompactProjection
                .Take(query.Diagnostics, options.MaxConditions)
                .Select(diagnostic => SymbolicCompactInvariantQueryDiagnostic.FromDiagnostic(diagnostic, options))
                .ToArray();
            return new SymbolicCompactInvariantQueryView(
                query.Text,
                query.MergeKind.ToString(),
                query.MustFactCount,
                SymbolicCompactProjection.Take(query.MustFacts, options.MaxConditions),
                query.MaybeFactCount,
                SymbolicCompactProjection.Take(query.MaybeFacts, options.MaxConditions),
                query.UnknownFactCount,
                SymbolicCompactProjection.Take(query.UnknownFacts, options.MaxConditions),
                unknownDiagnostics,
                query.DiagnosticCount,
                diagnostics,
                query.CandidateProgramPointCount,
                query.UnreachableProgramPointCount,
                query.IsUnreachable,
                query.Status.ToString(),
                query.StatusReason,
                query.Summary,
                query.HasMaybeFacts,
                query.HasUnknowns,
                query.HasUnresolvedAnalysis,
                query.MustFactCount > options.MaxConditions,
                query.MaybeFactCount > options.MaxConditions,
                query.UnknownFactCount > options.MaxConditions,
                query.UnknownDiagnostics.Count > options.MaxConditions,
                query.Diagnostics.Count > options.MaxConditions);
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
                foreach (var condition in point.PathConditions)
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
                !result.PathConditions.Any(condition =>
                    ConditionTargets.Any(target => string.Equals(target, condition.Target, StringComparison.OrdinalIgnoreCase))))
            {
                return false;
            }

            if (ConditionTexts.Count != 0 &&
                !result.PathConditions.Any(condition =>
                    ConditionTexts.Any(text => string.Equals(text, condition.Text, StringComparison.Ordinal))))
            {
                return false;
            }

            if (ConditionTextContains.Count != 0 &&
                !result.PathConditions.Any(condition =>
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
            IReadOnlyList<SymbolicConditionProofReasonSummary>? reasons = null)
        {
            Condition = condition ?? throw new ArgumentNullException(nameof(condition));
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
        }

        public string Condition { get; }

        public int TotalCount { get; }

        public int UnknownCount { get; }

        public int ProvenTrueCount { get; }

        public int ProvenFalseCount { get; }

        public int UnreachableCount { get; }

        public int ReachableCount { get; }

        public int ResolvedCount { get; }

        public SymbolicConditionProofSummaryStatus Status { get; }

        public string Summary { get; }

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
            var unknownCount = 0;
            var provenTrueCount = 0;
            var provenFalseCount = 0;
            var unreachableCount = 0;
            foreach (var proof in proofs)
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

            return new SymbolicConditionProofSummary(
                condition,
                unknownCount,
                provenTrueCount,
                provenFalseCount,
                unreachableCount,
                reasons: proofs
                    .GroupBy(
                        static proof => new ProofReasonKey(proof.TruthValue, proof.Reason),
                        ProofReasonKeyComparer.Instance)
                    .OrderBy(static group => group.Key.TruthValue)
                    .ThenBy(static group => group.Key.Reason, StringComparer.Ordinal)
                    .Select(static group => new SymbolicConditionProofReasonSummary(
                        group.Key.TruthValue,
                        group.Key.Reason,
                        group.Count()))
                    .ToArray());
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
        public static string FormatMergedInvariant(IReadOnlyList<SmtFormula> pathConditions)
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

        public static string Format(SmtFormula formula)
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
                case SmtConditionalFormula conditional:
                    return "(" +
                        Format(conditional.Condition) +
                        " ? " +
                        Format(conditional.WhenTrue) +
                        " : " +
                        Format(conditional.WhenFalse) +
                        ")";
                default:
                    return formula.ToString() ?? string.Empty;
            }
        }

        public static string GetMergeTarget(SmtFormula formula)
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

            var hashIndex = name.LastIndexOf('#');
            if (hashIndex > 0 && hashIndex + 1 < name.Length)
            {
                var allDigits = true;
                for (var index = hashIndex + 1; index < name.Length; index++)
                {
                    if (!char.IsDigit(name[index]))
                    {
                        allDigits = false;
                        break;
                    }
                }

                if (allDigits)
                {
                    return name.Substring(0, hashIndex);
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
        public SymbolicSourceQueryResult(
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
            IReadOnlyList<SmtFormula>? pathConditions = null,
            int? nodeSpanEnd = null,
            int? nodeStartLine = null,
            int? nodeStartColumn = null,
            int? nodeEndLine = null,
            int? nodeEndColumn = null,
            string? methodName = null,
            string? programPointKind = null)
        {
            FilePath = filePath;
            Line = line;
            Column = column;
            Position = position;
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
            MergedInvariantText = mergedInvariantText ??
                (pathConditions == null
                    ? FormatMergedInvariantText(Facts)
                    : SymbolicFormulaDisplay.FormatMergedInvariant(pathConditions));
            Invariant = pathConditions == null
                ? SymbolicInvariantResult.FromFacts(
                    Facts,
                    MergedInvariantText,
                    SymbolicInvariantMergeKind.Conjunction)
                : SymbolicInvariantResult.FromPathConditions(pathConditions, MergedInvariantText);
            Reachability = reachability;
            ReachabilityReason = reachabilityReason;
            ConditionProofs = conditionProofs ?? Array.Empty<SymbolicConditionProofResult>();
            ProofOutcomes = SymbolicProofOutcomeSummary.FromProofs(ConditionProofs);
            SmtDiagnostics = smtDiagnostics ?? SymbolicSmtDiagnostics.NotConfigured;
            InvariantQuery = SymbolicInvariantQueryView.FromPoint(this);
        }

        public string FilePath { get; }

        public int Line { get; }

        public int Column { get; }

        public int Position { get; }

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

        public string MergedInvariantText { get; }

        public SymbolicInvariantResult Invariant { get; }

        public IReadOnlyList<SymbolicInvariantCondition> PathConditions => Invariant.Conditions;

        public int PathConditionCount => PathConditions.Count;

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

        public static SymbolicInvariantResult FromPathConditions(
            IReadOnlyList<SmtFormula> pathConditions,
            string? mergedInvariantText = null)
        {
            if (pathConditions == null)
            {
                throw new ArgumentNullException(nameof(pathConditions));
            }

            return new SymbolicInvariantResult(
                pathConditions
                    .Select(static (condition, index) => SymbolicInvariantCondition.FromFormula(index, condition))
                    .ToArray(),
                mergedInvariantText ?? SymbolicFormulaDisplay.FormatMergedInvariant(pathConditions),
                SymbolicInvariantMergeKind.Conjunction);
        }

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
            bool hasSmtFormula,
            string target,
            bool isConservativeUnknown)
        {
            Index = index;
            Text = text ?? throw new ArgumentNullException(nameof(text));
            FormulaKind = formulaKind ?? throw new ArgumentNullException(nameof(formulaKind));
            ValueKind = valueKind ?? throw new ArgumentNullException(nameof(valueKind));
            HasSmtFormula = hasSmtFormula;
            Target = target ?? string.Empty;
            IsConservativeUnknown = isConservativeUnknown;
        }

        public int Index { get; }

        public string Text { get; }

        public string FormulaKind { get; }

        public string ValueKind { get; }

        public bool HasSmtFormula { get; }

        public string Target { get; }

        public bool IsConservativeUnknown { get; }

        public static SymbolicInvariantCondition FromFormula(int index, SmtFormula formula)
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
                hasSmtFormula: true,
                SymbolicFormulaDisplay.GetMergeTarget(formula),
                isConservativeUnknown: false);
        }

        public static SymbolicInvariantCondition FromText(int index, string text)
        {
            return new SymbolicInvariantCondition(
                index,
                text ?? string.Empty,
                "Text",
                "Unknown",
                hasSmtFormula: false,
                text ?? string.Empty,
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
                hasSmtFormula: false,
                target,
                isConservativeUnknown: true);
        }

        private static string GetFormulaKind(SmtFormula formula)
        {
            var name = formula.GetType().Name;
            return name.EndsWith("Formula", StringComparison.Ordinal)
                ? name.Substring(0, name.Length - "Formula".Length)
                : name;
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
    }

    public enum SymbolicInvariantMergeKind
    {
        Conjunction,
        DistinctFactUnion,
        ConservativeFactMerge,
    }

    public sealed class SymbolicProgramPointQueryResult
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

        public IReadOnlyList<SmtFormula> PathConditions => Analysis.PathConditions;

        public IReadOnlyList<string> Facts => Analysis.Facts;

        public SmtFormula MergedInvariant => Analysis.MergedInvariant;

        public string MergedInvariantText => Analysis.MergedInvariantText;

        public SymbolicReachability Reachability => Analysis.Reachability;

        public string ReachabilityReason => Analysis.ReachabilityReason;

        public SymbolicSmtDiagnostics SmtDiagnostics => Analysis.SmtDiagnostics;
    }

    public sealed class SymbolicConditionProofResult
    {
        public SymbolicConditionProofResult(
            string condition,
            SymbolicTruthValue truthValue,
            string reason)
        {
            Condition = condition;
            TruthValue = truthValue;
            Reason = reason;
        }

        public string Condition { get; }

        public SymbolicTruthValue TruthValue { get; }

        public string Reason { get; }
    }

    public enum SymbolicTruthValue
    {
        Unknown,
        ProvenTrue,
        ProvenFalse,
        Unreachable,
    }
}

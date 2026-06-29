using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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

            return QuerySourceLine(
                File.ReadAllText(filePath),
                Path.GetFullPath(filePath),
                line,
                references,
                cancellationToken,
                smtAnalysis,
                impliedConditions);
        }

        public SymbolicFileQueryResult QueryFileAllLines(
            string filePath,
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

            return QuerySourceAllLines(
                File.ReadAllText(filePath),
                Path.GetFullPath(filePath),
                references,
                cancellationToken,
                smtAnalysis,
                impliedConditions);
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
            return QuerySyntaxTreeLine(
                syntaxTree,
                compilation,
                line,
                cancellationToken,
                smtAnalysis,
                impliedConditions);
        }

        public SymbolicFileQueryResult QuerySourceAllLines(
            string sourceText,
            string filePath,
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
            return QuerySyntaxTreeAllLines(
                syntaxTree,
                compilation,
                cancellationToken,
                smtAnalysis,
                impliedConditions);
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
                query.Analysis.MergedInvariantText,
                query.Analysis.PathConditions);
        }

        public SymbolicLineQueryResult QuerySyntaxTreeLine(
            SyntaxTree syntaxTree,
            Compilation compilation,
            int line,
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

            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var root = syntaxTree.GetRoot(cancellationToken);
            var nodes = FindQueryNodesOnLine(root, syntaxTree, line, cancellationToken);
            var results = nodes
                .Select(node =>
                {
                    var query = AnalyzeProgramPointNode(
                        semanticModel,
                        node.SpanStart,
                        node,
                        smtAnalysis,
                        cancellationToken);
                    var lineColumn = GetLineAndColumn(syntaxTree, query.Position, cancellationToken);
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
                        query.Analysis.MergedInvariantText,
                        query.Analysis.PathConditions);
                })
                .ToArray();

            return new SymbolicLineQueryResult(
                syntaxTree.FilePath,
                line,
                results,
                SymbolicSmtDiagnostics.FromService(smtAnalysis));
        }

        public SymbolicFileQueryResult QuerySyntaxTreeAllLines(
            SyntaxTree syntaxTree,
            Compilation compilation,
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
                    impliedConditions);
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
                query.Analysis.MergedInvariantText,
                query.Analysis.PathConditions);
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
                query.Analysis);
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
                query.Analysis);
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
            CancellationToken cancellationToken)
        {
            var analysis = node is ForStatementSyntax forStatement
                ? _invariantService.AnalyzeForInitialEntry(forStatement, semanticModel, smtAnalysis, cancellationToken)
                : _invariantService.AnalyzeAt(node, semanticModel, smtAnalysis, cancellationToken);

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
            CancellationToken cancellationToken)
        {
            var lineSpan = GetLineSpan(syntaxTree, line, cancellationToken);
            if (lineSpan.Length == 0)
            {
                return Array.Empty<SyntaxNode>();
            }

            var seen = new HashSet<string>();
            return root
                .DescendantTokens(descendIntoTrivia: false)
                .Where(token => token.Span.Length > 0 && token.Span.IntersectsWith(lineSpan))
                .Select(token => FindQueryNode(root, token.SpanStart))
                .Where(static node => node is StatementSyntax or ExpressionSyntax)
                .Where(node => node.Span.IntersectsWith(lineSpan))
                .Where(node => seen.Add(node.RawKind.ToString() + ":" + node.SpanStart.ToString() + ":" + node.Span.End.ToString()))
                .OrderBy(static node => node.SpanStart)
                .ThenBy(static node => node.Span.Length)
                .ToArray();
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
            MergedInvariantText = factSummary.MergedInvariantText;
            MergedInvariant = SymbolicInvariantResult.FromFacts(
                Facts,
                MergedInvariantText,
                SymbolicInvariantMergeKind.DistinctFactUnion);
            SmtDiagnostics = smtDiagnostics ?? SymbolicSmtDiagnostics.NotConfigured;
        }

        public string FilePath { get; }

        public int Line { get; }

        public IReadOnlyList<SymbolicSourceQueryResult> ProgramPoints { get; }

        public IReadOnlyList<string> Facts { get; }

        public string MergedInvariantText { get; }

        public SymbolicInvariantResult MergedInvariant { get; }

        public SymbolicSmtDiagnostics SmtDiagnostics { get; }

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
            Reachability = SymbolicReachabilitySummary.FromProgramPoints(programPoints);
            ConditionProofs = SymbolicConditionProofSummary.FromProgramPoints(programPoints);
            SmtDiagnostics = smtDiagnostics ?? SymbolicSmtDiagnostics.NotConfigured;
        }

        public string FilePath { get; }

        public int LineCount { get; }

        public int LinesWithProgramPoints { get; }

        public int ProgramPointCount { get; }

        public IReadOnlyList<SymbolicLineQueryResult> Lines { get; }

        public IReadOnlyList<string> ObservedFacts { get; }

        public int ObservedFactCount { get; }

        public SymbolicInvariantResult ObservedInvariant { get; }

        public SymbolicReachabilitySummary Reachability { get; }

        public IReadOnlyList<SymbolicConditionProofSummary> ConditionProofs { get; }

        public SymbolicSmtDiagnostics SmtDiagnostics { get; }

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

    public sealed class SymbolicSourceQueryFilter
    {
        public static readonly SymbolicSourceQueryFilter Empty = new();

        public SymbolicSourceQueryFilter(
            IEnumerable<string>? nodeKinds = null,
            bool requireFacts = false,
            IEnumerable<SymbolicReachability>? reachability = null)
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
        }

        public IReadOnlyList<string> NodeKinds { get; }

        public bool RequireFacts { get; }

        public IReadOnlyList<SymbolicReachability> Reachability { get; }

        public bool IsEmpty =>
            NodeKinds.Count == 0 &&
            !RequireFacts &&
            Reachability.Count == 0;

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

            if (Reachability.Count != 0 && !Reachability.Contains(result.Reachability))
            {
                return false;
            }

            return true;
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

    public sealed class SymbolicConditionProofSummary
    {
        public SymbolicConditionProofSummary(
            string condition,
            int unknownCount,
            int provenTrueCount,
            int provenFalseCount,
            int unreachableCount)
        {
            Condition = condition ?? throw new ArgumentNullException(nameof(condition));
            UnknownCount = unknownCount;
            ProvenTrueCount = provenTrueCount;
            ProvenFalseCount = provenFalseCount;
            UnreachableCount = unreachableCount;
        }

        public string Condition { get; }

        public int UnknownCount { get; }

        public int ProvenTrueCount { get; }

        public int ProvenFalseCount { get; }

        public int UnreachableCount { get; }

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
                unreachableCount);
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
            IReadOnlyList<SmtFormula>? pathConditions = null)
        {
            FilePath = filePath;
            Line = line;
            Column = column;
            Position = position;
            NodeSpanStart = nodeSpanStart;
            NodeKind = nodeKind;
            Facts = facts;
            MergedInvariantText = mergedInvariantText ?? FormatMergedInvariantText(facts);
            Invariant = pathConditions == null
                ? SymbolicInvariantResult.FromFacts(
                    Facts,
                    MergedInvariantText,
                    SymbolicInvariantMergeKind.Conjunction)
                : SymbolicInvariantResult.FromPathConditions(pathConditions, MergedInvariantText);
            Reachability = reachability;
            ReachabilityReason = reachabilityReason;
            ConditionProofs = conditionProofs ?? Array.Empty<SymbolicConditionProofResult>();
            SmtDiagnostics = smtDiagnostics ?? SymbolicSmtDiagnostics.NotConfigured;
        }

        public string FilePath { get; }

        public int Line { get; }

        public int Column { get; }

        public int Position { get; }

        public int NodeSpanStart { get; }

        public string NodeKind { get; }

        public IReadOnlyList<string> Facts { get; }

        public string MergedInvariantText { get; }

        public SymbolicInvariantResult Invariant { get; }

        public IReadOnlyList<SymbolicInvariantCondition> PathConditions => Invariant.Conditions;

        public SymbolicReachability Reachability { get; }

        public string ReachabilityReason { get; }

        public IReadOnlyList<SymbolicConditionProofResult> ConditionProofs { get; }

        public SymbolicSmtDiagnostics SmtDiagnostics { get; }

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
                mergedInvariantText ?? SymbolicInvariantService.FormatMergedInvariant(pathConditions),
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
    }

    public sealed class SymbolicInvariantCondition
    {
        private SymbolicInvariantCondition(
            int index,
            string text,
            string formulaKind,
            string valueKind,
            bool hasSmtFormula)
        {
            Index = index;
            Text = text ?? throw new ArgumentNullException(nameof(text));
            FormulaKind = formulaKind ?? throw new ArgumentNullException(nameof(formulaKind));
            ValueKind = valueKind ?? throw new ArgumentNullException(nameof(valueKind));
            HasSmtFormula = hasSmtFormula;
        }

        public int Index { get; }

        public string Text { get; }

        public string FormulaKind { get; }

        public string ValueKind { get; }

        public bool HasSmtFormula { get; }

        public static SymbolicInvariantCondition FromFormula(int index, SmtFormula formula)
        {
            if (formula == null)
            {
                throw new ArgumentNullException(nameof(formula));
            }

            return new SymbolicInvariantCondition(
                index,
                formula.ToString() ?? string.Empty,
                GetFormulaKind(formula),
                formula.Kind.ToString(),
                hasSmtFormula: true);
        }

        public static SymbolicInvariantCondition FromText(int index, string text)
        {
            return new SymbolicInvariantCondition(
                index,
                text ?? string.Empty,
                "Text",
                "Unknown",
                hasSmtFormula: false);
        }

        private static string GetFormulaKind(SmtFormula formula)
        {
            var name = formula.GetType().Name;
            return name.EndsWith("Formula", StringComparison.Ordinal)
                ? name.Substring(0, name.Length - "Formula".Length)
                : name;
        }
    }

    public enum SymbolicInvariantMergeKind
    {
        Conjunction,
        DistinctFactUnion,
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
            SymbolicProgramPointAnalysis analysis)
        {
            FilePath = filePath;
            Line = line;
            Column = column;
            Position = position;
            NodeSpanStart = nodeSpanStart;
            NodeKind = nodeKind;
            Analysis = analysis ?? throw new ArgumentNullException(nameof(analysis));
        }

        public string FilePath { get; }

        public int Line { get; }

        public int Column { get; }

        public int Position { get; }

        public int NodeSpanStart { get; }

        public string NodeKind { get; }

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

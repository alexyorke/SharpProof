using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using PurelySharp.Symbolic;
using PurelySharp.Symbolic.Smt;

namespace PurelySharp.Test
{
    internal sealed class SymbolicSourceQueryTestSession : IDisposable
    {
        private readonly SyntaxTree _syntaxTree;
        private readonly Compilation _compilation;
        private readonly SymbolicSourceQueryService _service = new SymbolicSourceQueryService();
        private readonly SmtAnalysisService _smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        public SymbolicSourceQueryTestSession(
            string source,
            string filePath,
            bool allowUnsafe = false)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
            _syntaxTree = CSharpSyntaxTree.ParseText(
                Source,
                new CSharpParseOptions(LanguageVersion.Preview),
                FilePath);
            _compilation = CSharpCompilation.Create(
                "PurelySharp.Test.SymbolicSourceQuery",
                new[] { _syntaxTree },
                AnalyzerTestHost.GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: allowUnsafe));
        }

        public string Source { get; }

        public string FilePath { get; }

        public SymbolicSourceQueryResult AnalyzeAtPosition(int position)
        {
            return _service.QuerySyntaxTreeAtPosition(
                _syntaxTree,
                _compilation,
                position,
                smtAnalysis: _smtAnalysis);
        }

        public SymbolicConditionProofResult ProveAtMarker((int Line, int Column, int Position) marker, string condition)
        {
            return _service.ProveConditionAtSyntaxTree(
                _syntaxTree,
                _compilation,
                marker.Line,
                marker.Column,
                condition,
                _smtAnalysis);
        }

        public int FindLine(string text)
        {
            var lines = Source.Split('\n');
            for (var index = 0; index < lines.Length; index++)
            {
                if (lines[index].Contains(text, StringComparison.Ordinal))
                {
                    return index + 1;
                }
            }

            throw new InvalidOperationException("Text not found: " + text);
        }

        public (int Line, int Column, int Position) FindMarker(string marker)
        {
            var position = Source.IndexOf(marker, StringComparison.Ordinal);
            if (position < 0)
            {
                throw new InvalidOperationException("Marker was not found in source.");
            }

            var lines = Source.Split('\n');
            var currentPosition = 0;
            for (var index = 0; index < lines.Length; index++)
            {
                var nextPosition = currentPosition + lines[index].Length + 1;
                if (position < nextPosition)
                {
                    return (index + 1, position - currentPosition + 1, position);
                }

                currentPosition = nextPosition;
            }

            throw new InvalidOperationException("Marker line was not found in source.");
        }

        public void Dispose()
        {
            _smtAnalysis.Dispose();
        }
    }
}

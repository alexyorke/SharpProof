using System;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace PurelySharp.Symbolic
{
    internal static class SymbolicSourceLocation
    {
        public static LineColumn GetLineAndColumn(
            SyntaxTree syntaxTree,
            int position,
            CancellationToken cancellationToken,
            bool validatePosition = false)
        {
            var text = syntaxTree.GetText(cancellationToken);
            if (validatePosition && (position < 0 || position > text.Length))
            {
                throw new ArgumentOutOfRangeException(nameof(position), "--position must be within the source text span.");
            }

            var line = text.Lines.GetLineFromPosition(position);
            return new LineColumn(line.LineNumber + 1, position - line.Start + 1);
        }

        public static NodeSourceSpan GetNodeSourceSpan(
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
    }

    internal readonly struct LineColumn
    {
        public LineColumn(int line, int column)
        {
            Line = line;
            Column = column;
        }

        public int Line { get; }

        public int Column { get; }
    }

    internal readonly struct NodeSourceSpan
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
}

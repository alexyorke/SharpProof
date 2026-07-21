namespace SharpProof.Symbolic;

internal static class SymbolicSourceLocation {
    public static TextSpan GetLineSpan(
        SyntaxTree syntaxTree,
        int line,
        CancellationToken cancellationToken) {
        return GetTextLine(syntaxTree, line, cancellationToken).Span;
    }

    public static TextSpan GetSourceSpan(
        SyntaxTree syntaxTree,
        int spanStart,
        int spanEnd,
        CancellationToken cancellationToken) {
        var text = syntaxTree.GetText(cancellationToken);
        if (spanStart < 0)
            throw new ArgumentOutOfRangeException(nameof(spanStart), "--span-start must be zero or greater.");

        if (spanEnd < spanStart)
            throw new ArgumentOutOfRangeException(nameof(spanEnd), "--span-end cannot be less than --span-start.");

        if (spanEnd > text.Length)
            throw new ArgumentOutOfRangeException(nameof(spanEnd), "--span-end exceeds the source text length.");

        return TextSpan.FromBounds(spanStart, spanEnd);
    }

    public static int GetPosition(
        SyntaxTree syntaxTree,
        int line,
        int column,
        CancellationToken cancellationToken) {
        if (line < 1) throw new ArgumentOutOfRangeException(nameof(line), "--line must be 1 or greater.");

        if (column < 1) throw new ArgumentOutOfRangeException(nameof(column), "--column must be 1 or greater.");

        var textLine = GetTextLine(syntaxTree, line, cancellationToken);
        var zeroBasedColumn = column - 1;
        if (zeroBasedColumn > textLine.Span.Length)
            throw new ArgumentOutOfRangeException(nameof(column), "--column exceeds the line length.");

        return textLine.Start + zeroBasedColumn;
    }

    private static TextLine GetTextLine(
        SyntaxTree syntaxTree,
        int line,
        CancellationToken cancellationToken) {
        if (line < 1) throw new ArgumentOutOfRangeException(nameof(line), "--line must be 1 or greater.");

        var text = syntaxTree.GetText(cancellationToken);
        if (line > text.Lines.Count)
            throw new ArgumentOutOfRangeException(nameof(line), "--line exceeds the file line count.");

        return text.Lines[line - 1];
    }

    public static LineColumn GetLineAndColumn(
        SyntaxTree syntaxTree,
        int position,
        CancellationToken cancellationToken,
        bool validatePosition = false) {
        var text = syntaxTree.GetText(cancellationToken);
        if (validatePosition && (position < 0 || position > text.Length))
            throw new ArgumentOutOfRangeException(nameof(position), "--position must be within the source text span.");

        var line = text.Lines.GetLineFromPosition(position);
        return new LineColumn(line.LineNumber + 1, position - line.Start + 1);
    }

}

internal readonly record struct LineColumn(int Line, int Column);

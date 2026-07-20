namespace SharpProof.Symbolic;

internal static class SharpProofTargetFactory
{
    internal static SharpProofTarget Point(int line, int column = 1)
    {
        ValidatePositive(line, nameof(line));
        ValidatePositive(column, nameof(column));
        return new SharpProofTarget(SharpProofTargetKind.Point, Line: line, Column: column);
    }

    internal static SharpProofTarget AtPosition(int position)
    {
        if (position < 0) throw new ArgumentOutOfRangeException(nameof(position));
        return new SharpProofTarget(SharpProofTargetKind.Position, Position: position);
    }

    internal static SharpProofTarget LineNumber(int line)
    {
        ValidatePositive(line, nameof(line));
        return new SharpProofTarget(SharpProofTargetKind.Line, Line: line);
    }

    internal static SharpProofTarget Span(int start, int end)
    {
        if (start < 0) throw new ArgumentOutOfRangeException(nameof(start));
        if (end < start) throw new ArgumentOutOfRangeException(nameof(end));
        return new SharpProofTarget(SharpProofTargetKind.Span, SpanStart: start, SpanEnd: end);
    }

    internal static SharpProofTarget LineSpan(
        int startLine,
        int startColumn,
        int endLine,
        int endColumn)
    {
        ValidatePositive(startLine, nameof(startLine));
        ValidatePositive(startColumn, nameof(startColumn));
        ValidatePositive(endLine, nameof(endLine));
        ValidatePositive(endColumn, nameof(endColumn));
        if (endLine < startLine) throw new ArgumentOutOfRangeException(nameof(endLine));
        if (endLine == startLine && endColumn < startColumn)
            throw new ArgumentOutOfRangeException(nameof(endColumn));
        return new SharpProofTarget(
            SharpProofTargetKind.LineSpan,
            StartLine: startLine,
            StartColumn: startColumn,
            EndLine: endLine,
            EndColumn: endColumn);
    }

    internal static SharpProofTarget AllLines() => new(SharpProofTargetKind.AllLines);

    internal static SharpProofTarget Node(bool includeNestedCallables = false) =>
        new(SharpProofTargetKind.Node, IncludeNestedCallables: includeNestedCallables);

    private static void ValidatePositive(int value, string parameterName)
    {
        if (value <= 0) throw new ArgumentOutOfRangeException(parameterName);
    }
}

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace SharpProof.Symbolic;

internal sealed class SymbolicQueryTarget
{
    private SymbolicQueryTarget(
        SymbolicQueryTargetKind kind,
        int? line = null,
        int? column = null,
        int? position = null,
        int? spanStart = null,
        int? spanEnd = null,
        int? startLine = null,
        int? startColumn = null,
        int? endLine = null,
        int? endColumn = null,
        bool includeNestedCallables = false)
    {
        Kind = kind;
        LineNumber = line;
        ColumnNumber = column;
        PositionOffset = position;
        SpanStart = spanStart;
        SpanEnd = spanEnd;
        StartLine = startLine;
        StartColumn = startColumn;
        EndLine = endLine;
        EndColumn = endColumn;
        IncludeNestedCallables = includeNestedCallables;
    }

    public SymbolicQueryTargetKind Kind { get; }

    public int? LineNumber { get; }

    public int? ColumnNumber { get; }

    public int? PositionOffset { get; }

    public int? SpanStart { get; }

    public int? SpanEnd { get; }

    public int? StartLine { get; }

    public int? StartColumn { get; }

    public int? EndLine { get; }

    public int? EndColumn { get; }

    public bool IncludeNestedCallables { get; }

    public static SymbolicQueryTarget Point(int line, int column = 1)
    {
        ValidatePositive(line, nameof(line));
        ValidatePositive(column, nameof(column));
        return new SymbolicQueryTarget(SymbolicQueryTargetKind.Point, line, column);
    }

    public static SymbolicQueryTarget Position(int position)
    {
        ValidateNonNegative(position, nameof(position));
        return new SymbolicQueryTarget(SymbolicQueryTargetKind.Position, position: position);
    }

    public static SymbolicQueryTarget Line(int line)
    {
        ValidatePositive(line, nameof(line));
        return new SymbolicQueryTarget(SymbolicQueryTargetKind.Line, line);
    }

    public static SymbolicQueryTarget Span(int spanStart, int spanEnd)
    {
        ValidateNonNegative(spanStart, nameof(spanStart));
        if (spanEnd < spanStart)
            throw new ArgumentOutOfRangeException(nameof(spanEnd), "Span end cannot be less than span start.");

        return new SymbolicQueryTarget(SymbolicQueryTargetKind.Span, spanStart: spanStart, spanEnd: spanEnd);
    }

    public static SymbolicQueryTarget LineSpan(int startLine, int startColumn, int endLine, int endColumn)
    {
        ValidatePositive(startLine, nameof(startLine));
        ValidatePositive(startColumn, nameof(startColumn));
        ValidatePositive(endLine, nameof(endLine));
        ValidatePositive(endColumn, nameof(endColumn));
        if (endLine < startLine)
            throw new ArgumentOutOfRangeException(nameof(endLine), "End line cannot be before start line.");

        if (endLine == startLine && endColumn < startColumn)
            throw new ArgumentOutOfRangeException(nameof(endColumn),
                "End column cannot be before start column on the same line.");

        return new SymbolicQueryTarget(
            SymbolicQueryTargetKind.LineSpan,
            startLine: startLine,
            startColumn: startColumn,
            endLine: endLine,
            endColumn: endColumn);
    }

    public static SymbolicQueryTarget AllLines()
    {
        return new SymbolicQueryTarget(SymbolicQueryTargetKind.AllLines);
    }

    public static SymbolicQueryTarget Node(bool includeNestedCallables = false)
    {
        return new SymbolicQueryTarget(
            SymbolicQueryTargetKind.Node,
            includeNestedCallables: includeNestedCallables);
    }

    private static void ValidatePositive(int value, string paramName)
    {
        if (value <= 0) throw new ArgumentOutOfRangeException(paramName, "Value must be positive.");
    }

    private static void ValidateNonNegative(int value, string paramName)
    {
        if (value < 0) throw new ArgumentOutOfRangeException(paramName, "Value cannot be negative.");
    }
}

internal enum SymbolicQueryTargetKind
{
    Point,
    Position,
    Line,
    Span,
    LineSpan,
    AllLines,
    Node
}

internal enum SymbolicQueryScopeKind
{
    Point,
    Line,
    Span,
    File
}

internal sealed class SymbolicQueryScope
{
    internal SymbolicQueryScope(
        SymbolicQueryScopeKind kind,
        string filePath,
        int? line = null,
        int? column = null,
        int? position = null,
        int? spanStart = null,
        int? spanEnd = null,
        int? lineCount = null,
        int? startLine = null,
        int? startColumn = null,
        int? endLine = null,
        int? endColumn = null)
    {
        Kind = kind;
        FilePath = filePath ?? string.Empty;
        Line = line;
        Column = column;
        Position = position;
        SpanStart = spanStart;
        SpanEnd = spanEnd;
        LineCount = lineCount;
        StartLine = startLine;
        StartColumn = startColumn;
        EndLine = endLine;
        EndColumn = endColumn;
    }

    public SymbolicQueryScopeKind Kind { get; }

    public string FilePath { get; }

    public int? Line { get; }

    public int? Column { get; }

    public int? Position { get; }

    public int? SpanStart { get; }

    public int? SpanEnd { get; }

    public int? LineCount { get; }

    internal int? StartLine { get; }

    internal int? StartColumn { get; }

    internal int? EndLine { get; }

    internal int? EndColumn { get; }
}

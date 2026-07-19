using System.Text.Json.Serialization;

namespace SharpProof.Symbolic;

internal enum SymbolicQueryScopeKind
{
    Point,
    Line,
    Span,
    File
}

internal sealed record SymbolicQueryScope(
    SymbolicQueryScopeKind Kind,
    string FilePath,
    int? Line = null,
    int? Column = null,
    int? Position = null,
    int? SpanStart = null,
    int? SpanEnd = null,
    int? LineCount = null,
    [property: JsonIgnore] int? StartLine = null,
    [property: JsonIgnore] int? StartColumn = null,
    [property: JsonIgnore] int? EndLine = null,
    [property: JsonIgnore] int? EndColumn = null);

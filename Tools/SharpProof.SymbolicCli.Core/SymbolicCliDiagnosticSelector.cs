using Microsoft.CodeAnalysis;

namespace SharpProof.Symbolic;

internal readonly record struct SymbolicCliSelectedDiagnostic(
    Diagnostic Diagnostic,
    bool IsTarget);

internal static class SymbolicCliDiagnosticSelector
{
    internal static SymbolicCliSelectedDiagnostic[] SelectRelevant(
        IEnumerable<Diagnostic> diagnostics,
        SyntaxTree syntaxTree,
        int? position,
        int line)
    {
        return diagnostics
            .Where(diagnostic =>
                diagnostic.Location == Location.None ||
                ReferenceEquals(diagnostic.Location.SourceTree, syntaxTree))
            .Select(diagnostic => new SymbolicCliSelectedDiagnostic(
                diagnostic,
                IsTarget(diagnostic, syntaxTree, position, line)))
            .OrderByDescending(static item => item.IsTarget)
            .ThenBy(static item => item.Diagnostic.Location == Location.None
                ? int.MaxValue
                : item.Diagnostic.Location.SourceSpan.Start)
            .ThenBy(static item => item.Diagnostic.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsTarget(
        Diagnostic diagnostic,
        SyntaxTree syntaxTree,
        int? position,
        int line)
    {
        if (!ReferenceEquals(diagnostic.Location.SourceTree, syntaxTree)) return false;

        var span = diagnostic.Location.SourceSpan;
        if (position.HasValue) return span.Contains(position.Value) || span.End == position.Value;

        var requestedLine = line - 1;
        var lineSpan = diagnostic.Location.GetLineSpan().Span;
        return lineSpan.Start.Line <= requestedLine && lineSpan.End.Line >= requestedLine;
    }
}

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace SharpProof.CodeFixes;

internal static class CodeFixCompilationValidation
{
    // Compare the complete compilation, including errors introduced in existing
    // documents by a newly generated declaration. Unrelated existing errors do
    // not prevent an otherwise safe edit.
    internal static async Task<bool> HasNewErrorsAsync(Project original, Project changed, CancellationToken cancellationToken)
    {
        original = original ?? throw new ArgumentNullException(nameof(original));
        changed = changed ?? throw new ArgumentNullException(nameof(changed));
        var before = await original.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
        var after = await changed.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
        if (before == null || after == null)
        { return true; }
        var changes = new Dictionary<DocumentId, ImmutableArray<TextChange>>();
        var existing = new Dictionary<(DocumentId? Document, string Path, string Id, string Message, TextSpan Span), int>();
        foreach (var diagnostic in before.GetDiagnostics(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (diagnostic.Severity != DiagnosticSeverity.Error)
            { continue; }
            var span = diagnostic.Location.SourceSpan;
            if (diagnostic.Location.SourceTree is { } tree && original.Solution.GetDocument(tree) is { } document)
            {
                if (changed.GetDocument(document.Id) is not { } updated)
                { continue; }
                if (!changes.TryGetValue(document.Id, out var edits))
                {
                    edits = (await updated.GetTextChangesAsync(document, cancellationToken).ConfigureAwait(false)).ToImmutableArray();
                    changes.Add(document.Id, edits);
                }
                if (!TryMap(span, edits, out span))
                { continue; }
            }
            var key = Identity(original, diagnostic, span);
            existing.TryGetValue(key, out var count);
            existing[key] = count + 1;
        }
        foreach (var diagnostic in after.GetDiagnostics(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (diagnostic.Severity != DiagnosticSeverity.Error)
            { continue; }
            var key = Identity(changed, diagnostic, diagnostic.Location.SourceSpan);
            if (!existing.TryGetValue(key, out var count) || count == 0)
            { return true; }
            existing[key] = count - 1;
        }
        return false;
    }

    private static (DocumentId? Document, string Path, string Id, string Message, TextSpan Span) Identity(
        Project project, Diagnostic diagnostic, TextSpan span /* SPINT001: all values valid - empty source spans are valid diagnostic locations */)
    {
        project = project ?? throw new ArgumentNullException(nameof(project));
        diagnostic = diagnostic ?? throw new ArgumentNullException(nameof(diagnostic));
        var document = diagnostic.Location.SourceTree is { } tree ? project.Solution.GetDocument(tree)?.Id : null;
        return (document, diagnostic.Location.GetLineSpan().Path, diagnostic.Id,
            diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), span);
    }

    private static bool TryMap(TextSpan span /* SPINT001: all values valid - empty source spans are valid diagnostic locations */,
        ImmutableArray<TextChange> changes, out TextSpan mapped)
    {
        if (changes.IsDefault)
        { throw new ArgumentException("Text changes must be initialized.", nameof(changes)); }
        var offset = 0;
        foreach (var change in changes)
        {
            if (change.Span.End <= span.Start)
            { offset += (change.NewText?.Length ?? 0) - change.Span.Length; }
            else if (change.Span.Start < span.End)
            { mapped = default; return false; }
        }
        mapped = new TextSpan(span.Start + offset, span.Length);
        return true;
    }
}

using System.Collections.Immutable;
using SharpProof.CompilerArtifact;

namespace SharpProof.Worker.Test;

internal static class PortableGraphChanges
{
    internal static int FindIndex<T>(ImmutableArray<T> rows, Predicate<T> predicate)
    {
        if (rows.IsDefault)
        { throw new ArgumentNullException(nameof(rows)); }
        ArgumentNullException.ThrowIfNull(predicate);
        for (var index = 0; index < rows.Length; index++)
        {
            if (predicate(rows[index]))
            { return index; }
        }
        return -1;
    }

    internal static (T Row, PortableIrGraph Graph) ChangeRow<T>(
        this PortableIrGraph graph, T original, T replacement) where T : class
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(replacement);
        return (replacement, graph.ReplaceRow(original, replacement));
    }

    // Fixture changes replace every occurrence of the selected row by reference,
    // preserving the alias behavior exercised by the former mutable wire DTOs.
    internal static PortableIrGraph ReplaceRow<T>(
        this PortableIrGraph graph, T original, T replacement) where T : class
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(replacement);
        return graph with
        {
            Types = ReplaceRows(graph.Types, original, replacement),
            Variables = ReplaceRows(graph.Variables, original, replacement),
            Members = ReplaceRows(graph.Members, original, replacement),
            Operations = ReplaceRows(graph.Operations, original, replacement),
            Terms = ReplaceRows(graph.Terms, original, replacement),
            Blocks = ReplaceRows(graph.Blocks, original, replacement)
        };
    }

    private static ImmutableArray<TRow> ReplaceRows<TRow, T>(
        ImmutableArray<TRow> rows, T original, T replacement)
        where TRow : class where T : class
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(replacement);
        if (rows.IsDefault)
        { return rows; }
        ImmutableArray<TRow>.Builder? replacements = null;
        for (var index = 0; index < rows.Length; index++)
        {
            var row = rows[index];
            if (ReferenceEquals(row, original))
            {
                replacements ??= rows.ToBuilder();
                replacements[index] = (TRow)(object)replacement;
            }
            else if (row is PortableIrOperation operation && ReferenceEquals(operation.SourceSpan, original))
            {
                replacements ??= rows.ToBuilder();
                replacements[index] = (TRow)(object)(operation with
                {
                    SourceSpan = (PortableIrSourceSpan)(object)replacement
                });
            }
            else if (row is PortableIrInstruction instruction && ReferenceEquals(instruction.Location, original))
            {
                replacements ??= rows.ToBuilder();
                replacements[index] = (TRow)(object)(instruction with
                {
                    Location = (PortableIrLocation)(object)replacement
                });
            }
            else if (row is PortableIrBlock block)
            {
                var instructions = ReplaceRows(block.Instructions, original, replacement);
                if (!instructions.Equals(block.Instructions))
                {
                    replacements ??= rows.ToBuilder();
                    replacements[index] = (TRow)(object)(block with { Instructions = instructions });
                }
            }
        }
        return replacements?.ToImmutable() ?? rows;
    }
}

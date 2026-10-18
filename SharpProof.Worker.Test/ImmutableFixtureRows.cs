using System.Collections.Immutable;

namespace SharpProof.Worker.Test;

internal static class ImmutableFixtureRows
{
    internal static ImmutableArray<T> ReplaceReference<T>(
        this ImmutableArray<T> rows, T original, T replacement) where T : class
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(replacement);
        if (rows.IsDefault)
        { throw new ArgumentException("Array must be initialized.", nameof(rows)); }
        for (var index = 0; index < rows.Length; index++)
        {
            if (ReferenceEquals(rows[index], original))
            { rows = rows.SetItem(index, replacement); }
        }
        return rows;
    }
}

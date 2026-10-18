using System.Collections.Immutable;

internal static class ImmutableFixtureRows
{
    internal static T[] ReplaceReference<T>(this T[] rows, T original, T replacement) where T : class
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(replacement);
        return rows.ToImmutableArray().ReplaceReference(original, replacement).ToArray();
    }

    internal static T[] ReplaceAt<T>(this T[] rows, int index, T replacement /* SPINT001: all values valid - fixtures preserve malformed null elements */)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return rows.ToImmutableArray().SetItem(index, replacement).ToArray();
    }

    internal static ImmutableArray<T> ReplaceAt<T>(this ImmutableArray<T> rows, int index, T replacement /* SPINT001: all values valid - fixtures preserve malformed null elements */)
    {
        if (rows.IsDefault)
        { throw new ArgumentException("Array must be initialized.", nameof(rows)); }
        return rows.SetItem(index, replacement);
    }
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

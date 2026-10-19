using System.Collections.Immutable;

internal static class ImmutableFixtureRows
{
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
        ImmutableArray<T>.Builder? updated = null;
        for (var index = 0; index < rows.Length; index++)
        {
            if (ReferenceEquals(rows[index], original))
            {
                updated ??= rows.ToBuilder();
                updated[index] = replacement;
            }
        }
        return updated is null ? rows : updated.ToImmutable();
    }
}

namespace SharpProof.Ir;

// Preserve Dictionary.Add's rejection of identical duplicate key/value pairs.
internal static class ImmutableDictionaryCapture
{
    internal static ImmutableDictionary<TKey, TValue> AddUnique<TKey, TValue>(
        this ImmutableDictionary<TKey, TValue> map,
        TKey key,
        TValue value /* SPINT001: all values valid - maps support null values when TValue permits them */)
        where TKey : notnull
    {
        map = RepositoryParameterGuard.NotNull(map, nameof(map));
        if (map.ContainsKey(key))
        {
            throw new ArgumentException("An item with the same key has already been added. Key: " + key);
        }
        return map.Add(key, value);
    }

    internal static ImmutableDictionary<TKey, TValue> Create<TSource, TKey, TValue>(
        IEnumerable<TSource> source,
        Func<TSource, TKey> keySelector,
        Func<TSource, TValue> valueSelector,
        IEqualityComparer<TKey>? comparer = null)
        where TKey : notnull
    {
        source = RepositoryParameterGuard.NotNull(source, nameof(source));
        keySelector = RepositoryParameterGuard.NotNull(keySelector, nameof(keySelector));
        valueSelector = RepositoryParameterGuard.NotNull(valueSelector, nameof(valueSelector));
        var result = ImmutableDictionary.Create<TKey, TValue>(comparer);
        foreach (var item in source)
        {
            var key = keySelector(item);
            var value = valueSelector(item);
            if (result.ContainsKey(key))
            {
                throw new ArgumentException("An item with the same key has already been added. Key: " + key);
            }
            result = result.Add(key, value);
        }
        return result;
    }
}

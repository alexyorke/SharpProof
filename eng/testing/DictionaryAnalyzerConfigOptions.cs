using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharpProof.Testing;

internal sealed class DictionaryAnalyzerConfigOptions(
    ImmutableDictionary<string, string> values) : AnalyzerConfigOptions
{
    private readonly ImmutableDictionary<string, string> _guarded_values = values ?? throw new System.ArgumentNullException(nameof(values));
    internal static DictionaryAnalyzerConfigOptions Empty { get; } =
        new(ImmutableDictionary<string, string>.Empty);

    internal DictionaryAnalyzerConfigOptions(
        ImmutableArray<(string Key, string Value)> values)
        : this(Capture(values))
    {
        if (values.IsDefault)
        { throw new ArgumentNullException(nameof(values)); }
    }

    private static ImmutableDictionary<string, string> Capture(
        ImmutableArray<(string Key, string Value)> values)
    {
        if (values.IsDefault)
        { throw new ArgumentNullException(nameof(values)); }
        var builder = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in values)
        {
            if (builder.ContainsKey(pair.Key))
            { throw new ArgumentException("An item with the same key has already been added. Key: " + pair.Key); }
            builder.Add(pair.Key, pair.Value);
        }
        return builder.ToImmutable();
    }
    internal DictionaryAnalyzerConfigOptions(Dictionary<string, string> values)
        : this(values, values?.Comparer ?? throw new ArgumentNullException(nameof(values)))
    {
        System.ArgumentNullException.ThrowIfNull(values);
    }

    internal DictionaryAnalyzerConfigOptions(
        IReadOnlyDictionary<string, string> values, IEqualityComparer<string> comparer)
        : this(Capture(values, comparer))
    {
        System.ArgumentNullException.ThrowIfNull(values);
        System.ArgumentNullException.ThrowIfNull(comparer);
    }

    private static ImmutableDictionary<string, string> Capture(
        IReadOnlyDictionary<string, string> values, IEqualityComparer<string> comparer)
    {
        System.ArgumentNullException.ThrowIfNull(values);
        System.ArgumentNullException.ThrowIfNull(comparer);
        return values.ToImmutableDictionary(comparer);
    }
    public override bool TryGetValue(string key, out string value)
    {
        if (_guarded_values.TryGetValue(key, out var found))
        {
            value = found;
            return true;
        }

        value = string.Empty;
        return false;
    }
}

internal sealed class DictionaryAnalyzerConfigOptionsProvider
    : AnalyzerConfigOptionsProvider
{
    private readonly AnalyzerConfigOptions _fileOptions;

    internal DictionaryAnalyzerConfigOptionsProvider(ImmutableDictionary<string, string> globalValues)
        : this(new DictionaryAnalyzerConfigOptions(globalValues), globalForFiles: false)
    {
        System.ArgumentNullException.ThrowIfNull(globalValues);
    }

    internal DictionaryAnalyzerConfigOptionsProvider(Dictionary<string, string> globalValues)
        : this(new DictionaryAnalyzerConfigOptions(globalValues), globalForFiles: false)
    {
        System.ArgumentNullException.ThrowIfNull(globalValues);
    }

    internal DictionaryAnalyzerConfigOptionsProvider(
        IReadOnlyDictionary<string, string> globalValues, IEqualityComparer<string> comparer)
        : this(new DictionaryAnalyzerConfigOptions(globalValues, comparer), globalForFiles: false)
    {
        System.ArgumentNullException.ThrowIfNull(globalValues);
        System.ArgumentNullException.ThrowIfNull(comparer);
    }
    internal DictionaryAnalyzerConfigOptionsProvider(
        AnalyzerConfigOptions globalOptions,
        bool globalForFiles = true)
    {
        System.ArgumentNullException.ThrowIfNull(globalOptions);
        GlobalOptions = globalOptions;
        _fileOptions = globalForFiles
            ? globalOptions
            : DictionaryAnalyzerConfigOptions.Empty;
    }

    public override AnalyzerConfigOptions GlobalOptions { get; }

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree)
    {
        System.ArgumentNullException.ThrowIfNull(tree);
        return _fileOptions;
    }

    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile)
    {
        System.ArgumentNullException.ThrowIfNull(textFile);
        return _fileOptions;
    }
}

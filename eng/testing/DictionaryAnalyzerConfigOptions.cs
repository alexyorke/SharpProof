using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharpProof.Testing;

internal sealed class DictionaryAnalyzerConfigOptions(
    IReadOnlyDictionary<string, string> values) : AnalyzerConfigOptions
{
    private IReadOnlyDictionary<string, string> _guarded_values = values ?? throw new System.ArgumentNullException(nameof(values));
    internal static DictionaryAnalyzerConfigOptions Empty { get; } =
        new(new Dictionary<string, string>());

    internal DictionaryAnalyzerConfigOptions(
        params (string Key, string Value)[] values)
        : this(values.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value,
            StringComparer.OrdinalIgnoreCase))
    {
        System.ArgumentNullException.ThrowIfNull(values, nameof(values));
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

    internal DictionaryAnalyzerConfigOptionsProvider(
        IReadOnlyDictionary<string, string> globalValues)
        : this(new DictionaryAnalyzerConfigOptions(globalValues), globalForFiles: false)
    {
        System.ArgumentNullException.ThrowIfNull(globalValues, nameof(globalValues));
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

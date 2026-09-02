using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharpProof.Testing;

internal sealed class DictionaryAnalyzerConfigOptions(
    IReadOnlyDictionary<string, string> values) : AnalyzerConfigOptions
{
    internal static DictionaryAnalyzerConfigOptions Empty { get; } =
        new(new Dictionary<string, string>());

    internal DictionaryAnalyzerConfigOptions(
        params (string Key, string Value)[] values)
        : this(values.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value,
            StringComparer.OrdinalIgnoreCase))
    {
    }

    public override bool TryGetValue(string key, out string value)
    {
        if (values.TryGetValue(key, out var found))
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
    }

    internal DictionaryAnalyzerConfigOptionsProvider(
        AnalyzerConfigOptions globalOptions,
        bool globalForFiles = true)
    {
        GlobalOptions = globalOptions;
        _fileOptions = globalForFiles
            ? globalOptions
            : DictionaryAnalyzerConfigOptions.Empty;
    }

    public override AnalyzerConfigOptions GlobalOptions { get; }

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree)
    {
        return _fileOptions;
    }

    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile)
    {
        return _fileOptions;
    }
}

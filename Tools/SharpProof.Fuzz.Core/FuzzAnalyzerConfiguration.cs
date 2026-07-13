namespace SharpProof.Tools.Fuzz;

internal sealed record AnalyzerRunResult(
    ImmutableArray<Diagnostic> Diagnostics,
    ImmutableArray<string> Exceptions);

internal sealed class FixedAnalyzerConfigOptionsProvider : AnalyzerConfigOptionsProvider
{
    private readonly AnalyzerConfigOptions _emptyOptions =
        new FixedAnalyzerConfigOptions(ImmutableDictionary<string, string>.Empty);

    public FixedAnalyzerConfigOptionsProvider(ImmutableDictionary<string, string> globalOptions)
    {
        GlobalOptions = new FixedAnalyzerConfigOptions(globalOptions);
    }

    public override AnalyzerConfigOptions GlobalOptions { get; }

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree)
    {
        return _emptyOptions;
    }

    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile)
    {
        return _emptyOptions;
    }
}

internal sealed class FixedAnalyzerConfigOptions : AnalyzerConfigOptions
{
    private readonly ImmutableDictionary<string, string> _values;

    public FixedAnalyzerConfigOptions(ImmutableDictionary<string, string> values)
    {
        _values = values;
    }

    public override bool TryGetValue(string key, out string value)
    {
        return _values.TryGetValue(key, out value!);
    }
}

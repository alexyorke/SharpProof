namespace SharpProof.Tools.Fuzz;

internal sealed record AnalyzerRunResult(
    ImmutableArray<Diagnostic> Diagnostics,
    ImmutableArray<string> Exceptions);

internal sealed class FixedAnalyzerConfigOptionsProvider(ImmutableDictionary<string, string> globalOptions) : AnalyzerConfigOptionsProvider {
    private readonly AnalyzerConfigOptions _emptyOptions =
        new FixedAnalyzerConfigOptions(ImmutableDictionary<string, string>.Empty);

    public override AnalyzerConfigOptions GlobalOptions { get; } = new FixedAnalyzerConfigOptions(globalOptions);

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) =>
        _emptyOptions;

    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) =>
        _emptyOptions;
}

internal sealed class FixedAnalyzerConfigOptions(ImmutableDictionary<string, string> values) : AnalyzerConfigOptions {
    private readonly ImmutableDictionary<string, string> _values = values;

    public override bool TryGetValue(string key, out string value) =>
        _values.TryGetValue(key, out value!);
}

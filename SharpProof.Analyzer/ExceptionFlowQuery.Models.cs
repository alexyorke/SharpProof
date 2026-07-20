namespace SharpProof.Analyzer;

internal static partial class ExceptionFlowEngine
{
    internal sealed record ExceptionFlowResult(
        ImmutableArray<ExceptionFlowSite> Sites,
        ImmutableArray<SymbolicRuntimeHazard> RawHazards)
    {
        public ExceptionEvidenceProjection Evidence { get; } = new(Sites);
    }

    internal sealed record ExceptionFlowSite(
        SyntaxNode Site,
        IMethodSymbol Method,
        ITypeSymbol? Type,
        string ExceptionType,
        string Category,
        string Source,
        string? ExceptionSymbol,
        ImmutableArray<ExceptionFlowEdge> Edges);

    internal sealed class ExceptionEvidenceProjection(IEnumerable<ExceptionFlowSite> sites)
    {
        private readonly ImmutableArray<ExceptionFlowSite> _sites = sites.ToImmutableArray();

        public string[] Types => _sites.Select(static site => site.ExceptionType)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static type => type, StringComparer.Ordinal)
            .ToArray();

        public int Count => Types.Length;

        public string FormatCategories() => string.Join(
            ";",
            _sites.Select(static site => site.Category)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static category => category, StringComparer.Ordinal));

        public string FormatSources() => string.Join(
            ";",
            _sites.GroupBy(static site => site.ExceptionType, StringComparer.Ordinal)
                .OrderBy(static group => group.Key, StringComparer.Ordinal)
                .SelectMany(group => group
                    .Select(static site => site.Category + ":" + site.Source)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(static source => source, StringComparer.Ordinal)
                    .Select(source => group.Key + "=" + source)));

        public string? FormatEdges()
        {
            var edges = _sites.SelectMany(static site => site.Edges)
                .Select((edge, index) => (edge, index))
                .GroupBy(static item => item.edge.CreateKey(), StringComparer.Ordinal)
                .OrderBy(static group => group.Key, StringComparer.Ordinal)
                .Select(static group => group.OrderByDescending(static item => item.index).First().edge)
                .ToArray();
            return edges.Length == 0 ? null : JsonSerializer.Serialize(edges);
        }
    }

    internal sealed record ExceptionFlowEdge(
        string ExceptionType,
        string Category,
        string? SourcePath,
        string[] CallChain,
        string? CalleeIdentity,
        int Depth)
    {
        internal string CreateKey() => ExceptionType + "|" + Category + "|" +
                                       (SourcePath ?? string.Empty) + "|" +
                                       string.Join(">", CallChain) + "|" +
                                       (CalleeIdentity ?? string.Empty) + "|" +
                                       Depth.ToString(CultureInfo.InvariantCulture);
    }
}

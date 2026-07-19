using System.Globalization;
using System.Text.Json;

namespace SharpProof.Analyzer;

internal static partial class ExceptionFlowEngine
{
    internal sealed record ExceptionFlowResult(
        ImmutableArray<ExceptionFlowSite> Sites,
        ImmutableArray<SymbolicRuntimeHazard> RawHazards)
    {
        public ExceptionEvidenceProjection Evidence { get; } = new(Sites);
    }

    internal sealed class ExceptionFlowSite(
        SyntaxNode site,
        IMethodSymbol method,
        ITypeSymbol? type,
        string exceptionType,
        string category,
        string source,
        string? exceptionSymbol = null,
        ImmutableArray<ExceptionFlowEdge> edges = default)
    {
        public SyntaxNode Site { get; } = site;
        public IMethodSymbol Method { get; } = method;
        public ITypeSymbol? Type { get; } = type;
        public string ExceptionType { get; } = exceptionType;
        public string Category { get; } = category;
        public string Source { get; } = source;
        public string? ExceptionSymbol { get; } = exceptionSymbol;
        public ImmutableArray<ExceptionFlowEdge> Edges { get; } =
            edges.IsDefault ? ImmutableArray<ExceptionFlowEdge>.Empty : edges;
    }

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

    internal sealed class ExceptionFlowEdge(
        string exceptionType,
        string category,
        string? sourcePath,
        IEnumerable<string> callChain,
        string? calleeIdentity,
        int depth)
    {
        public string ExceptionType { get; } = exceptionType;
        public string Category { get; } = category;
        public string? SourcePath { get; } = sourcePath;
        public string[] CallChain { get; } = callChain.ToArray();
        public string? CalleeIdentity { get; } = calleeIdentity;
        public int Depth { get; } = depth;

        internal string CreateKey() => ExceptionType + "|" + Category + "|" +
                                       (SourcePath ?? string.Empty) + "|" +
                                       string.Join(">", CallChain) + "|" +
                                       (CalleeIdentity ?? string.Empty) + "|" +
                                       Depth.ToString(CultureInfo.InvariantCulture);
    }
}

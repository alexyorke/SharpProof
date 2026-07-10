using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using Microsoft.CodeAnalysis;

namespace SharpProof.Analyzer;

internal static partial class ExceptionFlowQuery
{
    internal sealed class MethodExceptionQueryResult
    {
        public MethodExceptionQueryResult(
            ExceptionEvidenceSet exceptionEvidence,
            ImmutableArray<UncaughtExceptionSiteEntry> siteEntries)
        {
            ExceptionEvidence = exceptionEvidence;
            SiteEntries = siteEntries;
        }

        public ExceptionEvidenceSet ExceptionEvidence { get; }

        public ImmutableArray<UncaughtExceptionSiteEntry> SiteEntries { get; }
    }

    internal sealed class ExceptionCandidate
    {
        public ExceptionCandidate(
            ITypeSymbol? type,
            string displayName,
            string category,
            string source,
            ImmutableArray<ExceptionEdgeDiagnosticEntry> edges = default)
        {
            Type = type;
            DisplayName = displayName;
            Category = category;
            Source = source;
            Edges = edges.IsDefault ? ImmutableArray<ExceptionEdgeDiagnosticEntry>.Empty : edges;
        }

        public ITypeSymbol? Type { get; }

        public string DisplayName { get; }

        public string Category { get; }

        public string Source { get; }

        public ImmutableArray<ExceptionEdgeDiagnosticEntry> Edges { get; }
    }

    internal sealed class UncaughtExceptionSiteEntry
    {
        public UncaughtExceptionSiteEntry(
            SyntaxNode site,
            IMethodSymbol method,
            ExceptionCandidate exception,
            string? exceptionSymbol = null)
        {
            Site = site;
            Method = method;
            Exception = exception;
            ExceptionSymbol = exceptionSymbol;
        }

        public SyntaxNode Site { get; }

        public IMethodSymbol Method { get; }

        public ExceptionCandidate Exception { get; }

        public string? ExceptionSymbol { get; }
    }

    internal sealed class ExceptionEvidenceEntry
    {
        public ExceptionEvidenceEntry(
            string exceptionType,
            string[] categories,
            string[] sources,
            ExceptionEdgeDiagnosticEntry[] edges)
        {
            ExceptionType = exceptionType;
            Categories = categories;
            Sources = sources;
            Edges = edges;
        }

        public string ExceptionType { get; }

        public string[] Categories { get; }

        public string[] Sources { get; }

        public ExceptionEdgeDiagnosticEntry[] Edges { get; }
    }

    internal sealed class ExceptionEvidenceSet
    {
        private readonly Dictionary<string, SortedSet<string>> _categoriesByType = new(StringComparer.Ordinal);

        private readonly Dictionary<string, SortedDictionary<string, ExceptionEdgeDiagnosticEntry>> _edgesByType =
            new(StringComparer.Ordinal);

        private readonly Dictionary<string, SortedSet<string>> _sourcesByType = new(StringComparer.Ordinal);

        public int Count => _categoriesByType.Count;

        public string[] Types => _categoriesByType.Keys.OrderBy(type => type, StringComparer.Ordinal).ToArray();

        public void Add(ExceptionCandidate candidate)
        {
            var exceptionType = candidate.DisplayName;
            var category = candidate.Category;
            var source = candidate.Source;
            if (!_categoriesByType.TryGetValue(exceptionType, out var categories))
            {
                categories = new SortedSet<string>(StringComparer.Ordinal);
                _categoriesByType.Add(exceptionType, categories);
            }

            categories.Add(category);

            if (!_sourcesByType.TryGetValue(exceptionType, out var sources))
            {
                sources = new SortedSet<string>(StringComparer.Ordinal);
                _sourcesByType.Add(exceptionType, sources);
            }

            sources.Add(category + ":" + source);

            if (!candidate.Edges.IsDefaultOrEmpty)
            {
                if (!_edgesByType.TryGetValue(exceptionType, out var edges))
                {
                    edges = new SortedDictionary<string, ExceptionEdgeDiagnosticEntry>(StringComparer.Ordinal);
                    _edgesByType.Add(exceptionType, edges);
                }

                foreach (var edge in candidate.Edges) edges[edge.CreateKey()] = edge;
            }
        }

        public ExceptionEvidenceEntry[] EnumerateEntries()
        {
            return _categoriesByType.Keys
                .OrderBy(type => type, StringComparer.Ordinal)
                .Select(type => new ExceptionEvidenceEntry(
                    type,
                    _categoriesByType.TryGetValue(type, out var categories)
                        ? categories.ToArray()
                        : Array.Empty<string>(),
                    _sourcesByType.TryGetValue(type, out var sources)
                        ? sources.ToArray()
                        : Array.Empty<string>(),
                    _edgesByType.TryGetValue(type, out var edges)
                        ? edges.Values.ToArray()
                        : Array.Empty<ExceptionEdgeDiagnosticEntry>()))
                .ToArray();
        }

        public string FormatCategories()
        {
            return string.Join(
                ";",
                _categoriesByType.Values
                    .SelectMany(categories => categories)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(category => category, StringComparer.Ordinal));
        }

        public string FormatSources()
        {
            return string.Join(
                ";",
                _sourcesByType
                    .OrderBy(item => item.Key, StringComparer.Ordinal)
                    .SelectMany(item => item.Value.Select(source => item.Key + "=" + source)));
        }

        public string? FormatEdges()
        {
            var edges = _edgesByType
                .OrderBy(item => item.Key, StringComparer.Ordinal)
                .SelectMany(item => item.Value.Values)
                .ToArray();
            if (edges.Length == 0) return null;

            return JsonSerializer.Serialize(edges);
        }
    }

    internal sealed class ExceptionEdgeDiagnosticEntry
    {
        public ExceptionEdgeDiagnosticEntry(
            string exceptionType,
            string category,
            string sourcePath,
            string? calleeExactSymbolKey,
            int depth)
        {
            ExceptionType = exceptionType;
            Category = category;
            SourcePath = sourcePath;
            CalleeExactSymbolKey = calleeExactSymbolKey;
            Depth = depth;
        }

        public string ExceptionType { get; }

        public string Category { get; }

        public string SourcePath { get; }

        public string? CalleeExactSymbolKey { get; }

        public int Depth { get; }

        public string CreateKey()
        {
            return ExceptionType + "|" +
                   Category + "|" +
                   SourcePath + "|" +
                   (CalleeExactSymbolKey ?? string.Empty) + "|" +
                   Depth.ToString(CultureInfo.InvariantCulture);
        }
    }
}
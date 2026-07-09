using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharpProof.Analyzer.Configuration
{
    internal sealed class DiagnosticBaseline
    {
        private const string BaselineFileName = "SharpProof.Baseline.json";
        private static readonly JsonDocumentOptions BaselineJsonOptions = new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        };

        public static readonly DiagnosticBaseline Empty = new DiagnosticBaseline(ImmutableArray<BaselineEntry>.Empty);

        private readonly ImmutableArray<BaselineEntry> _entries;

        private DiagnosticBaseline(ImmutableArray<BaselineEntry> entries)
        {
            _entries = entries;
        }

        public static DiagnosticBaseline FromOptions(AnalyzerOptions options, CancellationToken cancellationToken)
        {
            var builder = ImmutableArray.CreateBuilder<BaselineEntry>();
            foreach (var additionalFile in options.AdditionalFiles)
            {
                if (!string.Equals(System.IO.Path.GetFileName(additionalFile.Path), BaselineFileName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var text = additionalFile.GetText(cancellationToken)?.ToString();
                if (text == null || string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                foreach (var entry in ParseEntries(text, additionalFile.Path))
                {
                    builder.Add(entry);
                }
            }

            return builder.Count == 0 ? Empty : new DiagnosticBaseline(builder.ToImmutable());
        }

        public bool IsSuppressed(string diagnosticId, ISymbol symbol, SyntaxTree syntaxTree)
        {
            if (_entries.IsDefaultOrEmpty)
            {
                return false;
            }

            var symbolIds = GetSymbolIds(symbol);
            var sourcePath = syntaxTree.FilePath ?? string.Empty;

            foreach (var entry in _entries)
            {
                foreach (var symbolId in symbolIds)
                {
                    if (entry.Matches(diagnosticId, symbolId, sourcePath))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        internal static ImmutableArray<string> GetSymbolIds(ISymbol symbol)
        {
            var builder = ImmutableArray.CreateBuilder<string>();
            var documentationId = DocumentationCommentId.CreateDeclarationId(symbol.OriginalDefinition);
            if (!string.IsNullOrWhiteSpace(documentationId))
            {
                builder.Add(documentationId!);
            }

            builder.Add(symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat));

            if (symbol is IMethodSymbol methodSymbol && methodSymbol.ContainingType != null)
            {
                builder.Add(GetCompactMethodId(methodSymbol));
            }

            return builder.Distinct(StringComparer.Ordinal).ToImmutableArray();
        }

        internal static string GetPreferredSymbolId(ISymbol symbol)
        {
            if (symbol is IMethodSymbol methodSymbol && methodSymbol.ContainingType != null)
            {
                return GetCompactMethodId(methodSymbol);
            }

            var documentationId = DocumentationCommentId.CreateDeclarationId(symbol.OriginalDefinition);
            if (!string.IsNullOrWhiteSpace(documentationId))
            {
                return documentationId!;
            }

            return symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        }

        internal static string NormalizePath(string path)
        {
            var normalized = path.Replace('\\', '/').Trim();
            while (normalized.StartsWith("./", StringComparison.Ordinal))
            {
                normalized = normalized.Substring(2);
            }

            return normalized;
        }

        private static string GetCompactMethodId(IMethodSymbol methodSymbol)
        {
            var containingType = methodSymbol.ContainingType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
            var methodName = methodSymbol.MetadataName == ".ctor" ? "#ctor" : methodSymbol.MetadataName;
            return "M:" + containingType + "." + methodName;
        }

        private static ImmutableArray<BaselineEntry> ParseEntries(string json, string baselinePath)
        {
            var builder = ImmutableArray.CreateBuilder<BaselineEntry>();
            var baseDirectory = GetBaseDirectory(baselinePath);
            try
            {
                using var document = JsonDocument.Parse(json, BaselineJsonOptions);
                AddEntries(document.RootElement, baseDirectory, builder);
            }
            catch (JsonException)
            {
            }

            return builder.ToImmutable();
        }

        private static void AddEntries(
            JsonElement element,
            string baseDirectory,
            ImmutableArray<BaselineEntry>.Builder builder)
        {
            if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                {
                    AddEntries(item, baseDirectory, builder);
                }

                return;
            }

            if (element.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            TryAddEntry(element, baseDirectory, builder);
            foreach (var property in element.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Array ||
                    property.Value.ValueKind == JsonValueKind.Object)
                {
                    AddEntries(property.Value, baseDirectory, builder);
                }
            }
        }

        private static void TryAddEntry(
            JsonElement element,
            string baseDirectory,
            ImmutableArray<BaselineEntry>.Builder builder)
        {
            string? id = null;
            string? symbol = null;
            string? path = null;

            foreach (var property in element.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var value = property.Value.GetString();
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                value = value!.Trim();
                if (string.Equals(property.Name, "id", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(property.Name, "diagnosticId", StringComparison.OrdinalIgnoreCase))
                {
                    id = value;
                }
                else if (string.Equals(property.Name, "symbol", StringComparison.OrdinalIgnoreCase))
                {
                    symbol = value;
                }
                else if (string.Equals(property.Name, "path", StringComparison.OrdinalIgnoreCase))
                {
                    path = value;
                }
            }

            if (!string.IsNullOrWhiteSpace(id) &&
                !string.IsNullOrWhiteSpace(symbol) &&
                !string.IsNullOrWhiteSpace(path))
            {
                builder.Add(new BaselineEntry(id!, symbol!, path!, baseDirectory));
            }
        }

        private static string GetBaseDirectory(string baselinePath)
        {
            if (string.IsNullOrWhiteSpace(baselinePath))
            {
                return string.Empty;
            }

            var directory = System.IO.Path.GetDirectoryName(baselinePath);
            return string.IsNullOrWhiteSpace(directory) ? string.Empty : NormalizePath(directory!);
        }

        private readonly struct BaselineEntry
        {
            public BaselineEntry(string diagnosticId, string symbolId, string path, string baseDirectory)
            {
                DiagnosticId = diagnosticId;
                SymbolId = symbolId;
                Path = NormalizePath(path);
                AbsolutePath = MakeAbsolutePath(path, baseDirectory);
            }

            private string DiagnosticId { get; }
            private string SymbolId { get; }
            private string Path { get; }
            private string AbsolutePath { get; }

            public bool Matches(string diagnosticId, string symbolId, string sourcePath)
            {
                return string.Equals(DiagnosticId, diagnosticId, StringComparison.Ordinal) &&
                       string.Equals(SymbolId, symbolId, StringComparison.Ordinal) &&
                       MatchesPath(sourcePath);
            }

            private bool MatchesPath(string sourcePath)
            {
                var normalizedSourcePath = NormalizePath(sourcePath);
                return string.Equals(Path, normalizedSourcePath, StringComparison.OrdinalIgnoreCase) ||
                       (!string.IsNullOrWhiteSpace(AbsolutePath) &&
                        string.Equals(AbsolutePath, normalizedSourcePath, StringComparison.OrdinalIgnoreCase));
            }

            private static string MakeAbsolutePath(string path, string baseDirectory)
            {
                if (string.IsNullOrWhiteSpace(baseDirectory))
                {
                    return string.Empty;
                }

                if (System.IO.Path.IsPathRooted(path))
                {
                    return NormalizePath(path);
                }

                return NormalizePath(System.IO.Path.Combine(baseDirectory, path));
            }
        }
    }
}

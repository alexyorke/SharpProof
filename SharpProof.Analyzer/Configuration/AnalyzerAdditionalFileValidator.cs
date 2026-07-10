using System;
using System.Collections.Immutable;
using System.IO;
using System.Text.Json;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharpProof.Analyzer.Configuration
{
    internal static class AnalyzerAdditionalFileValidator
    {
        private const int MaxSupportedEffectSummarySchemaVersion = 4;
        private static readonly JsonDocumentOptions BaselineJsonOptions = new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
        };

        internal static ImmutableArray<AnalyzerAdditionalFileIssue> Validate(
            AnalyzerOptions options,
            CancellationToken cancellationToken)
        {
            var issues = ImmutableArray.CreateBuilder<AnalyzerAdditionalFileIssue>();
            foreach (var additionalFile in options.AdditionalFiles)
            {
                var fileName = Path.GetFileName(additionalFile.Path);
                if (string.Equals(fileName, "SharpProof.Baseline.json", StringComparison.OrdinalIgnoreCase))
                {
                    ValidateBaseline(additionalFile, cancellationToken, issues);
                }
                else if (BuiltInEffectSummaryLoader.IsSummaryFile(fileName))
                {
                    ValidateEffectSummary(additionalFile, cancellationToken, issues);
                }
            }

            return issues.ToImmutable();
        }

        private static void ValidateBaseline(
            AdditionalText additionalFile,
            CancellationToken cancellationToken,
            ImmutableArray<AnalyzerAdditionalFileIssue>.Builder issues)
        {
            if (!TryGetText(additionalFile, cancellationToken, issues, out var text))
            {
                return;
            }

            try
            {
                using var document = JsonDocument.Parse(text, BaselineJsonOptions);
                if (document.RootElement.ValueKind is not (JsonValueKind.Array or JsonValueKind.Object))
                {
                    AddIssue(issues, additionalFile, "unsupported baseline root; expected an object or array");
                    return;
                }

                var counts = CountBaselineEntries(document.RootElement);
                if (counts.CandidateCount == 0)
                {
                    AddIssue(issues, additionalFile, "baseline contains no diagnostic entries");
                }
                else if (counts.ValidCount == 0)
                {
                    AddIssue(issues, additionalFile, "baseline contains no usable entries; each entry needs id, symbol, and path");
                }

                if (counts.InvalidCount != 0)
                {
                    AddIssue(
                        issues,
                        additionalFile,
                        $"baseline partially ignored {counts.InvalidCount} malformed entr{(counts.InvalidCount == 1 ? "y" : "ies")}");
                }
            }
            catch (JsonException)
            {
                AddIssue(issues, additionalFile, "malformed baseline JSON");
            }
        }

        private static void ValidateEffectSummary(
            AdditionalText additionalFile,
            CancellationToken cancellationToken,
            ImmutableArray<AnalyzerAdditionalFileIssue>.Builder issues)
        {
            if (!TryGetText(additionalFile, cancellationToken, issues, out var text))
            {
                return;
            }

            try
            {
                using var document = JsonDocument.Parse(text);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    AddIssue(issues, additionalFile, "unsupported effect-summary root; expected an object");
                    return;
                }

                if (!root.TryGetProperty("SchemaVersion", out var schemaVersionElement) ||
                    schemaVersionElement.ValueKind != JsonValueKind.Number ||
                    !schemaVersionElement.TryGetInt32(out var schemaVersion))
                {
                    AddIssue(issues, additionalFile, "effect-summary is missing a numeric SchemaVersion");
                    return;
                }
                else if (schemaVersion < 1 || schemaVersion > MaxSupportedEffectSummarySchemaVersion)
                {
                    AddIssue(
                        issues,
                        additionalFile,
                        $"unsupported effect-summary SchemaVersion '{schemaVersion}'; supported versions are 1-{MaxSupportedEffectSummarySchemaVersion}");
                    return;
                }

                if (root.TryGetProperty("GeneratedPurityCatalog", out var generatedCatalog) &&
                    generatedCatalog.ValueKind == JsonValueKind.Object)
                {
                    ValidateGeneratedPurityCatalog(additionalFile, generatedCatalog, issues);
                    return;
                }

                if (!root.TryGetProperty("Assemblies", out var assemblies) ||
                    assemblies.ValueKind != JsonValueKind.Array)
                {
                    AddIssue(issues, additionalFile, "unsupported effect-summary layout; expected Assemblies or GeneratedPurityCatalog");
                    return;
                }

                var assemblyCount = 0;
                var invalidAssemblyCount = 0;
                var invalidMethodCount = 0;
                var validMethodCount = 0;
                foreach (var assembly in assemblies.EnumerateArray())
                {
                    if (assembly.ValueKind != JsonValueKind.Object ||
                        !assembly.TryGetProperty("Methods", out var methods) ||
                        methods.ValueKind != JsonValueKind.Array)
                    {
                        invalidAssemblyCount++;
                        continue;
                    }

                    assemblyCount++;
                    foreach (var method in methods.EnumerateArray())
                    {
                        if (method.ValueKind == JsonValueKind.Object &&
                            method.TryGetProperty("Symbol", out var symbol) &&
                            symbol.ValueKind == JsonValueKind.String &&
                            !string.IsNullOrWhiteSpace(symbol.GetString()))
                        {
                            validMethodCount++;
                        }
                        else
                        {
                            invalidMethodCount++;
                        }
                    }
                }

                if (assemblies.GetArrayLength() == 0 || assemblyCount == 0 || validMethodCount == 0)
                {
                    AddIssue(issues, additionalFile, "effect-summary contains no usable assembly method entries");
                }

                var invalidCount = invalidAssemblyCount + invalidMethodCount;
                if (invalidCount != 0)
                {
                    AddIssue(
                        issues,
                        additionalFile,
                        $"effect-summary partially ignored {invalidCount} malformed entr{(invalidCount == 1 ? "y" : "ies")}");
                }
            }
            catch (JsonException)
            {
                AddIssue(issues, additionalFile, "malformed effect-summary JSON");
            }
        }

        private static void ValidateGeneratedPurityCatalog(
            AdditionalText additionalFile,
            JsonElement catalog,
            ImmutableArray<AnalyzerAdditionalFileIssue>.Builder issues)
        {
            if (!catalog.TryGetProperty("Entries", out var entries) ||
                entries.ValueKind != JsonValueKind.Array)
            {
                AddIssue(issues, additionalFile, "unsupported GeneratedPurityCatalog layout; expected an Entries array");
                return;
            }

            var validCount = 0;
            var invalidCount = 0;
            foreach (var entry in entries.EnumerateArray())
            {
                var hasSymbol = entry.ValueKind == JsonValueKind.Object &&
                    ((entry.TryGetProperty("ExactSymbolKey", out var exactSymbol) &&
                      exactSymbol.ValueKind == JsonValueKind.String &&
                      !string.IsNullOrWhiteSpace(exactSymbol.GetString())) ||
                     (entry.TryGetProperty("Symbol", out var symbol) &&
                      symbol.ValueKind == JsonValueKind.String &&
                      !string.IsNullOrWhiteSpace(symbol.GetString())));
                var hasClassification = entry.ValueKind == JsonValueKind.Object &&
                    entry.TryGetProperty("Classification", out var classification) &&
                    classification.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(classification.GetString());

                if (hasSymbol && hasClassification)
                {
                    validCount++;
                }
                else
                {
                    invalidCount++;
                }
            }

            if (entries.GetArrayLength() == 0 || validCount == 0)
            {
                AddIssue(issues, additionalFile, "GeneratedPurityCatalog contains no usable entries");
            }

            if (invalidCount != 0)
            {
                AddIssue(
                    issues,
                    additionalFile,
                    $"GeneratedPurityCatalog partially ignored {invalidCount} malformed entr{(invalidCount == 1 ? "y" : "ies")}");
            }
        }

        private static BaselineEntryCounts CountBaselineEntries(JsonElement element)
        {
            var counts = new BaselineEntryCounts();
            CountBaselineEntries(element, ref counts);
            return counts;
        }

        private static void CountBaselineEntries(JsonElement element, ref BaselineEntryCounts counts)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                var hasCandidateProperty = false;
                var hasId = false;
                var hasSymbol = false;
                var hasPath = false;
                foreach (var property in element.EnumerateObject())
                {
                    if (string.Equals(property.Name, "id", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(property.Name, "diagnosticId", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(property.Name, "symbol", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(property.Name, "path", StringComparison.OrdinalIgnoreCase))
                    {
                        hasCandidateProperty = true;
                    }

                    if (property.Value.ValueKind == JsonValueKind.String &&
                        !string.IsNullOrWhiteSpace(property.Value.GetString()))
                    {
                        if (string.Equals(property.Name, "id", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(property.Name, "diagnosticId", StringComparison.OrdinalIgnoreCase))
                        {
                            hasId = true;
                        }
                        else if (string.Equals(property.Name, "symbol", StringComparison.OrdinalIgnoreCase))
                        {
                            hasSymbol = true;
                        }
                        else if (string.Equals(property.Name, "path", StringComparison.OrdinalIgnoreCase))
                        {
                            hasPath = true;
                        }
                    }
                }

                if (hasCandidateProperty)
                {
                    counts.CandidateCount++;
                    if (hasId && hasSymbol && hasPath)
                    {
                        counts.ValidCount++;
                    }
                    else
                    {
                        counts.InvalidCount++;
                    }
                }

                foreach (var property in element.EnumerateObject())
                {
                    if (property.Value.ValueKind is JsonValueKind.Array or JsonValueKind.Object)
                    {
                        CountBaselineEntries(property.Value, ref counts);
                    }
                }

                return;
            }

            if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                {
                    CountBaselineEntries(item, ref counts);
                }
            }
        }

        private static bool TryGetText(
            AdditionalText additionalFile,
            CancellationToken cancellationToken,
            ImmutableArray<AnalyzerAdditionalFileIssue>.Builder issues,
            out string text)
        {
            try
            {
                text = additionalFile.GetText(cancellationToken)?.ToString() ?? string.Empty;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                text = string.Empty;
                AddIssue(issues, additionalFile, "file contents could not be read");
                return false;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                AddIssue(issues, additionalFile, "file is empty");
                return false;
            }

            return true;
        }

        private static void AddIssue(
            ImmutableArray<AnalyzerAdditionalFileIssue>.Builder issues,
            AdditionalText additionalFile,
            string reason)
        {
            var issue = new AnalyzerAdditionalFileIssue(additionalFile.Path ?? string.Empty, reason);
            if (!issues.Contains(issue))
            {
                issues.Add(issue);
            }
        }

        private struct BaselineEntryCounts
        {
            public int CandidateCount;
            public int ValidCount;
            public int InvalidCount;
        }
    }

    internal readonly record struct AnalyzerAdditionalFileIssue(string Path, string Reason);
}

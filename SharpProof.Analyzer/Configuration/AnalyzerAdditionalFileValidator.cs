using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpProof.Schema;
using SharpProof.Symbolic;

namespace SharpProof.Analyzer.Configuration;

internal static class AnalyzerAdditionalFileValidator
{
    internal static ImmutableArray<AnalyzerAdditionalFileIssue> Validate(
        AnalyzerOptions options,
        CancellationToken cancellationToken)
    {
        var issues = ImmutableArray.CreateBuilder<AnalyzerAdditionalFileIssue>();
        foreach (var additionalFile in options.AdditionalFiles)
        {
            var fileName = Path.GetFileName(additionalFile.Path);
            if (string.Equals(fileName, "SharpProof.Baseline.json", StringComparison.OrdinalIgnoreCase))
                ValidateBaseline(additionalFile, cancellationToken, issues);
            else if (BuiltInEffectSummaryLoader.IsSummaryFile(fileName))
                ValidateEffectSummary(additionalFile, cancellationToken, issues);
        }

        return issues.ToImmutable();
    }

    private static void ValidateBaseline(
        AdditionalText additionalFile,
        CancellationToken cancellationToken,
        ImmutableArray<AnalyzerAdditionalFileIssue>.Builder issues)
    {
        if (!TryGetText(additionalFile, cancellationToken, issues, out var text)) return;

        try
        {
            using var document = JsonDocument.Parse(text, BaselineJsonCompatibility.DocumentOptions);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                AddIssue(issues, additionalFile, "unsupported baseline root; evidence v2 requires an object");
                return;
            }

            if (!ValidateBaselineEvidenceSchemas(additionalFile, document.RootElement, issues))
                return;

            var counts = CountBaselineEntries(document.RootElement);
            if (counts.CandidateCount == 0)
                AddIssue(issues, additionalFile, "baseline contains no diagnostic entries");
            else if (counts.ValidCount == 0)
                AddIssue(issues, additionalFile,
                    "baseline contains no usable entries; each entry needs id, symbol, and path");

            if (counts.InvalidCount != 0)
                AddIssue(
                    issues,
                    additionalFile,
                    $"baseline partially ignored {counts.InvalidCount} malformed entr{(counts.InvalidCount == 1 ? "y" : "ies")}");
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
        if (!TryGetText(additionalFile, cancellationToken, issues, out var text)) return;

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

            if (schemaVersion != EffectSummarySchemaContract.CurrentVersion)
            {
                AddIssue(
                    issues,
                    additionalFile,
                    $"unsupported effect-summary SchemaVersion '{schemaVersion}'; supported version is " +
                    EffectSummarySchemaContract.CurrentVersion);
                return;
            }

            if (!ValidateEvidenceSchema(
                    additionalFile,
                    root,
                    "EvidenceSchemaVersion",
                    "EvidenceSchemaCompatibility",
                    "effect-summary",
                    issues,
                    required: true))
                return;

            if (root.TryGetProperty("GeneratedPurityCatalog", out var generatedCatalog) &&
                generatedCatalog.ValueKind == JsonValueKind.Object)
            {
                ValidateGeneratedPurityCatalog(additionalFile, generatedCatalog, issues);
                return;
            }

            if (!root.TryGetProperty("Assemblies", out var assemblies) ||
                assemblies.ValueKind != JsonValueKind.Array)
            {
                AddIssue(issues, additionalFile,
                    "unsupported effect-summary layout; expected Assemblies or GeneratedPurityCatalog");
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
                    if (method.ValueKind == JsonValueKind.Object &&
                        StructuralMethodIdentityJson.TryReadMethod(method, out _, out _))
                        validMethodCount++;
                    else
                        invalidMethodCount++;
            }

            if (assemblies.GetArrayLength() == 0 || assemblyCount == 0 || validMethodCount == 0)
                AddIssue(issues, additionalFile, "effect-summary contains no usable assembly method entries");

            var invalidCount = invalidAssemblyCount + invalidMethodCount;
            if (invalidCount != 0)
                AddIssue(
                    issues,
                    additionalFile,
                    $"effect-summary partially ignored {invalidCount} malformed entr{(invalidCount == 1 ? "y" : "ies")}");
        }
        catch (JsonException)
        {
            AddIssue(issues, additionalFile, "malformed effect-summary JSON");
        }
    }

    private static bool ValidateEvidenceSchema(
        AdditionalText additionalFile,
        JsonElement element,
        string versionPropertyName,
        string compatibilityPropertyName,
        string surfaceName,
        ImmutableArray<AnalyzerAdditionalFileIssue>.Builder issues,
        bool required = false)
    {
        if (BaselineJsonCompatibility.TryValidateEvidenceSchema(
                element,
                versionPropertyName,
                compatibilityPropertyName,
                required,
                out var failure))
            return true;

        AddEvidenceSchemaIssue(
            issues,
            additionalFile,
            failure,
            versionPropertyName,
            compatibilityPropertyName,
            surfaceName);
        return false;
    }

    private static bool ValidateBaselineEvidenceSchemas(
        AdditionalText additionalFile,
        JsonElement element,
        ImmutableArray<AnalyzerAdditionalFileIssue>.Builder issues)
    {
        if (BaselineJsonCompatibility.TryValidateEvidenceSchemaTree(
                element,
                "evidenceSchemaVersion",
                "evidenceSchemaCompatibility",
                requireRootSchema: true,
                static candidate =>
                    BaselineJsonCompatibility.HasPropertyIgnoreCase(candidate, "id") &&
                    BaselineJsonCompatibility.HasPropertyIgnoreCase(candidate, "symbol") &&
                    BaselineJsonCompatibility.HasPropertyIgnoreCase(candidate, "path"),
                out var failure))
            return true;

        AddEvidenceSchemaIssue(
            issues,
            additionalFile,
            failure,
            "evidenceSchemaVersion",
            "evidenceSchemaCompatibility",
            failure.IsRoot ? "baseline" : "baseline entry");
        return false;
    }

    private static void AddEvidenceSchemaIssue(
        ImmutableArray<AnalyzerAdditionalFileIssue>.Builder issues,
        AdditionalText additionalFile,
        EvidenceSchemaValidationFailure failure,
        string versionPropertyName,
        string compatibilityPropertyName,
        string surfaceName)
    {
        var message = failure.Kind switch
        {
            EvidenceSchemaValidationFailureKind.Missing =>
                surfaceName + " is missing required " + versionPropertyName + " and " +
                compatibilityPropertyName,
            EvidenceSchemaValidationFailureKind.NonNumericVersion =>
                surfaceName + " has a non-numeric " + versionPropertyName,
            EvidenceSchemaValidationFailureKind.UnsupportedVersion =>
                $"unsupported {surfaceName} {versionPropertyName} '{failure.Version}'; supported versions are " +
                $"{SharpProofEvidenceSchema.MinimumReadCompatibleVersion}-{SharpProofEvidenceSchema.CurrentVersion}",
            EvidenceSchemaValidationFailureKind.InvalidCompatibility =>
                surfaceName + " " + compatibilityPropertyName + " must be '" +
                SharpProofEvidenceSchema.CompatibilityPolicy + "'",
            _ => throw new InvalidOperationException("Unknown evidence-schema validation failure.")
        };
        AddIssue(issues, additionalFile, message);
    }

    private static void ValidateGeneratedPurityCatalog(
        AdditionalText additionalFile,
        JsonElement catalog,
        ImmutableArray<AnalyzerAdditionalFileIssue>.Builder issues)
    {
        if (!catalog.TryGetProperty("SchemaVersion", out var schemaVersion) ||
            schemaVersion.ValueKind != JsonValueKind.Number ||
            !schemaVersion.TryGetInt32(out var catalogSchemaVersion) ||
            catalogSchemaVersion != EffectSummarySchemaContract.CurrentVersion)
        {
            AddIssue(
                issues,
                additionalFile,
                "GeneratedPurityCatalog SchemaVersion must be " +
                EffectSummarySchemaContract.CurrentVersion);
            return;
        }

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
                            StructuralMethodIdentityJson.TryReadMethod(entry, out _, out _);
            var hasClassification = entry.ValueKind == JsonValueKind.Object &&
                                    entry.TryGetProperty("Classification", out var classification) &&
                                    classification.ValueKind == JsonValueKind.String &&
                                    !string.IsNullOrWhiteSpace(classification.GetString());

            if (hasSymbol && hasClassification)
                validCount++;
            else
                invalidCount++;
        }

        if (entries.GetArrayLength() == 0 || validCount == 0)
            AddIssue(issues, additionalFile, "GeneratedPurityCatalog contains no usable entries");

        if (invalidCount != 0)
            AddIssue(
                issues,
                additionalFile,
                $"GeneratedPurityCatalog partially ignored {invalidCount} malformed entr{(invalidCount == 1 ? "y" : "ies")}");
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
            var fields = BaselineJsonCompatibility.ReadEntryFields(element);
            if (fields.HasCandidateProperty)
            {
                counts.CandidateCount++;
                if (fields.IsValid)
                    counts.ValidCount++;
                else
                    counts.InvalidCount++;
            }

            foreach (var property in element.EnumerateObject())
                if (property.Value.ValueKind is JsonValueKind.Array or JsonValueKind.Object)
                    CountBaselineEntries(property.Value, ref counts);

            return;
        }

        if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray())
                CountBaselineEntries(item, ref counts);
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
        if (!issues.Contains(issue)) issues.Add(issue);
    }

    private struct BaselineEntryCounts
    {
        public int CandidateCount;
        public int ValidCount;
        public int InvalidCount;
    }
}

internal readonly record struct AnalyzerAdditionalFileIssue(
    string Path,
    string Reason,
    string ReasonCode = "invalid_additional_file");

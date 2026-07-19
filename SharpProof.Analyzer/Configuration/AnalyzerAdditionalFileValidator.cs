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
            using var document = JsonDocument.Parse(text, BaselineSchemaContract.DocumentOptions);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                AddIssue(issues, additionalFile, "unsupported baseline root; evidence v2 requires an object");
                return;
            }

            if (!BaselineSchemaContract.TryValidateTree(document.RootElement, out var baselineFailure))
            {
                AddIssue(issues, additionalFile, BaselineSchemaContract.FormatValidationIssue(
                    baselineFailure,
                    "evidenceSchemaVersion",
                    "evidenceSchemaCompatibility",
                    baselineFailure.IsRoot ? "baseline" : "baseline entry"));
                return;
            }

            var counts = BaselineSchemaContract.CountEntries(document.RootElement);
            if (counts.CandidateCount == 0)
                AddIssue(issues, additionalFile, "baseline contains no diagnostic entries");
            else if (counts.ValidCount == 0)
                AddIssue(issues, additionalFile,
                    "baseline contains no usable entries; each entry needs id, symbol, and path");

            AddMalformedEntriesIssue(issues, additionalFile, "baseline", counts.InvalidCount);
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

        if (!EffectSummaryJsonParser.TryParse(text, out var document, out var parseFailure))
        {
            var reason = parseFailure.Kind switch
            {
                EffectSummaryJsonFailureKind.MalformedJson => "malformed effect-summary JSON",
                EffectSummaryJsonFailureKind.NonObjectRoot =>
                    "unsupported effect-summary root; expected an object",
                EffectSummaryJsonFailureKind.MissingSchemaVersion =>
                    "effect-summary is missing a numeric SchemaVersion",
                EffectSummaryJsonFailureKind.UnsupportedSchemaVersion =>
                    $"unsupported effect-summary SchemaVersion '{parseFailure.SchemaVersion}'; supported version is " +
                    EffectSummarySchemaContract.CurrentVersion,
                _ => "invalid effect-summary JSON"
            };
            AddIssue(issues, additionalFile, reason);
            return;
        }

        using (document)
        {
            var root = document.RootElement;

            if (!BaselineSchemaContract.TryValidate(
                    root,
                    "EvidenceSchemaVersion",
                    "EvidenceSchemaCompatibility",
                    true,
                    out var effectSummaryFailure))
            {
                AddIssue(issues, additionalFile, BaselineSchemaContract.FormatValidationIssue(
                    effectSummaryFailure,
                    "EvidenceSchemaVersion",
                    "EvidenceSchemaCompatibility",
                    "effect-summary"));
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
            AddMalformedEntriesIssue(issues, additionalFile, "effect-summary", invalidCount);
        }
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

        AddMalformedEntriesIssue(issues, additionalFile, "GeneratedPurityCatalog", invalidCount);
    }

    private static void AddMalformedEntriesIssue(ImmutableArray<AnalyzerAdditionalFileIssue>.Builder issues,
        AdditionalText additionalFile, string source, int count)
    {
        if (count != 0)
            AddIssue(issues, additionalFile,
                $"{source} partially ignored {count} malformed entr{(count == 1 ? "y" : "ies")}");
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

}

internal readonly record struct AnalyzerAdditionalFileIssue(
    string Path,
    string Reason,
    string ReasonCode = "invalid_additional_file");

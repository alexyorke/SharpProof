using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using SharpProof.Symbolic;

internal sealed class SymbolicCliJsonRequest
{
    public int SchemaVersion { get; set; }

    public string? Mode { get; set; }

    public SymbolicCliJsonSource? Source { get; set; }

    public SymbolicCliJsonTarget? Target { get; set; }

    public string[]? References { get; set; }

    public SymbolicCliJsonParseOptions? ParseOptions { get; set; }

    public string[]? ImpliedConditions { get; set; }

    public SymbolicCliJsonSmtOptions? Smt { get; set; }

    public Dictionary<string, int>? AnalysisLimits { get; set; }

    public SymbolicCliJsonQueryOptions? Query { get; set; }

    public SymbolicCliJsonOutputOptions? Output { get; set; }

    public SymbolicCliJsonGateOptions? Gates { get; set; }

    public static async Task<string[]> ExpandArgumentsAsync(
        string[] arguments,
        TextReader standardInput,
        CancellationToken cancellationToken = default)
    {
        if (arguments == null) throw new ArgumentNullException(nameof(arguments));
        if (standardInput == null) throw new ArgumentNullException(nameof(standardInput));

        var requestIndexes = arguments
            .Select(static (argument, index) => new { argument, index })
            .Where(static item =>
                item.argument is "--request-json" or "--request-json-stdin")
            .Select(static item => item.index)
            .ToArray();
        if (requestIndexes.Length == 0) return arguments;

        if (requestIndexes.Length != 1 || requestIndexes[0] != 0)
            throw new ArgumentException(
                "A JSON request selector must be the only CLI input and must appear first.");

        string json;
        if (arguments[0] == "--request-json")
        {
            if (arguments.Length != 2)
                throw new ArgumentException("--request-json requires exactly one JSON value and no other options.");

            json = arguments[1];
        }
        else
        {
            if (arguments.Length != 1)
                throw new ArgumentException("--request-json-stdin cannot be combined with other options.");

            json = await standardInput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        }

        SymbolicCliJsonRequest request;
        try
        {
            request = JsonSerializer.Deserialize<SymbolicCliJsonRequest>(json, SerializerOptions) ??
                      throw new ArgumentException("The JSON request envelope cannot be null.");
        }
        catch (JsonException exception)
        {
            throw SymbolicCliErrorWriter.CreateException(
                SymbolicErrorCodes.ParseFailed,
                SymbolicErrorCategory.Parse,
                "Invalid JSON request envelope: " + exception.Message,
                SymbolicErrorExitCodes.InvalidData,
                "input",
                "request-json",
                exception);
        }

        return request.ToArguments();
    }

    private string[] ToArguments()
    {
        if (SchemaVersion != 1)
            throw new ArgumentException("JSON request schemaVersion must be 1.");

        if (Source?.Text == null)
            throw new ArgumentException("JSON request source.text is required.");

        if (Target == null)
            throw new ArgumentException("JSON request target is required.");

        var mode = NormalizeMode(Mode);
        var arguments = new List<string>();
        AddMode(arguments, mode);
        AddSource(arguments, Source);
        AddTarget(arguments, Target, mode);
        AddReferences(arguments, References);
        AddParseOptions(arguments, ParseOptions);
        AddRepeated(arguments, "--implies", ImpliedConditions, "impliedConditions");
        AddSmtOptions(arguments, Smt);
        AddAnalysisLimits(arguments, AnalysisLimits);
        AddQueryOptions(arguments, Query);
        AddOutputOptions(arguments, Output);
        AddGateOptions(arguments, Gates);
        return arguments.ToArray();
    }

    private static string NormalizeMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode)) return "query";

        return mode.Trim().ToLowerInvariant() switch
        {
            "query" or "invariants" => "query",
            "explain" => "explain",
            "runtimehazards" or "runtime-hazards" => "runtime-hazards",
            "complexity" => "complexity",
            "capabilities" => "capabilities",
            _ => throw new ArgumentException(
                "JSON request mode must be query, explain, runtimeHazards, complexity, or capabilities.")
        };
    }

    private static void AddMode(List<string> arguments, string mode)
    {
        switch (mode)
        {
            case "query":
                return;
            case "explain":
                arguments.Add("explain");
                return;
            default:
                arguments.Add("--" + mode);
                return;
        }
    }

    private static void AddSource(List<string> arguments, SymbolicCliJsonSource source)
    {
        AddValue(arguments, "--source-text", source.Text!);
        AddOptionalValue(arguments, "--source-file-name", source.FilePath);
        if (source.SourceMap == null) return;

        AddValue(arguments, "--source-map-uri", RequireText(source.SourceMap.SourceUri, "source.sourceMap.sourceUri"));
        AddOptionalPositiveInt(
            arguments,
            "--source-map-original-line",
            source.SourceMap.OriginalStartLine,
            "source.sourceMap.originalStartLine");
        AddOptionalPositiveInt(
            arguments,
            "--source-map-original-column",
            source.SourceMap.OriginalStartColumn,
            "source.sourceMap.originalStartColumn");
    }

    private static void AddTarget(
        List<string> arguments,
        SymbolicCliJsonTarget target,
        string mode)
    {
        var kind = RequireText(target.Kind, "target.kind").ToLowerInvariant();
        switch (kind)
        {
            case "point":
                AddPositiveInt(arguments, "--line", target.Line, "target.line");
                AddOptionalPositiveInt(arguments, "--column", target.Column, "target.column");
                return;
            case "line":
                AddPositiveInt(arguments, "--line", target.Line, "target.line");
                if (mode == "query") arguments.Add("--line-invariants");
                return;
            case "position":
                AddNonNegativeInt(arguments, "--position", target.Position, "target.position");
                return;
            case "span":
                AddNonNegativeInt(arguments, "--span-start", target.SpanStart, "target.spanStart");
                AddNonNegativeInt(arguments, "--span-end", target.SpanEnd, "target.spanEnd");
                return;
            case "linespan":
            case "line-span":
                AddPositiveInt(arguments, "--span-start-line", target.StartLine, "target.startLine");
                AddPositiveInt(arguments, "--span-start-column", target.StartColumn, "target.startColumn");
                AddPositiveInt(arguments, "--span-end-line", target.EndLine, "target.endLine");
                AddPositiveInt(arguments, "--span-end-column", target.EndColumn, "target.endColumn");
                return;
            case "alllines":
            case "all-lines":
                arguments.Add("--all-lines");
                return;
            default:
                throw new ArgumentException(
                    "JSON request target.kind must be point, line, position, span, lineSpan, or allLines.");
        }
    }

    private static void AddReferences(List<string> arguments, string[]? references)
    {
        AddRepeated(arguments, "--reference", references, "references");
    }

    private static void AddParseOptions(
        List<string> arguments,
        SymbolicCliJsonParseOptions? options)
    {
        if (options == null) return;

        AddOptionalValue(arguments, "--language-version", options.LanguageVersion);
        AddRepeated(arguments, "--define", options.PreprocessorSymbols, "parseOptions.preprocessorSymbols");
        AddOptionalValue(arguments, "--nullable", options.Nullable);
        if (options.AllowUnsafe == true) arguments.Add("--allow-unsafe");
        AddOptionalValue(arguments, "--documentation-mode", options.DocumentationMode);
        AddOptionalValue(arguments, "--platform", options.Platform);
        AddOptionalValue(arguments, "--optimization", options.Optimization);
        AddOptionalValue(arguments, "--assembly-name", options.AssemblyName);
    }

    private static void AddSmtOptions(List<string> arguments, SymbolicCliJsonSmtOptions? options)
    {
        if (options == null) return;

        AddOptionalValue(arguments, "--smt-mode", options.Mode);
        AddOptionalPositiveInt(arguments, "--smt-timeout-ms", options.TimeoutMs, "smt.timeoutMs");
        AddOptionalPositiveInt(
            arguments,
            "--smt-method-budget-ms",
            options.MethodBudgetMs,
            "smt.methodBudgetMs");
        AddOptionalPositiveInt(
            arguments,
            "--smt-max-path-conditions",
            options.MaxPathConditions,
            "smt.maxPathConditions");
        AddOptionalPositiveInt(
            arguments,
            "--smt-max-expression-nodes",
            options.MaxExpressionNodes,
            "smt.maxExpressionNodes");
        AddOptionalNonNegativeInt(
            arguments,
            "--smt-transient-retries",
            options.TransientRetries,
            "smt.transientRetries");
        if (options.RecycleContextOnTransientFailure == false)
            arguments.Add("--smt-keep-context-on-transient-failure");
        if (options.DisposeContextOnExit == true) arguments.Add("--smt-dispose-context-on-exit");
    }

    private static void AddAnalysisLimits(
        List<string> arguments,
        Dictionary<string, int>? limits)
    {
        if (limits == null) return;

        foreach (var pair in limits.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
                throw new ArgumentException("JSON request analysisLimits cannot contain an empty name.");

            if (pair.Value <= 0)
                throw new ArgumentException(
                    $"JSON request analysisLimits.{pair.Key} must be a positive integer.");

            AddValue(
                arguments,
                "--analysis-limit",
                pair.Key + "=" + pair.Value.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static void AddQueryOptions(
        List<string> arguments,
        SymbolicCliJsonQueryOptions? options)
    {
        if (options == null) return;

        if (options.CheckReachability == true) arguments.Add("--check-reachability");
        if (options.IncludeExpressionProgramPoints == true) arguments.Add("--line-expressions");
        if (options.IncludeCurrentStatementCompletionFacts == true)
            arguments.Add("--post-line-invariants");
        AddRepeated(arguments, "--invariant-target", options.InvariantTargets, "query.invariantTargets");
        if (options.IncludeUnprovenHazards == true) arguments.Add("--include-unproven-hazards");
        if (options.FailOnHazard == true) arguments.Add("--fail-on-hazard");
        AddRepeated(arguments, "--hazard-kind", options.HazardKinds, "query.hazardKinds");
    }

    private static void AddOutputOptions(
        List<string> arguments,
        SymbolicCliJsonOutputOptions? options)
    {
        if (options == null) return;

        var format = options.Format?.Trim().ToLowerInvariant();
        switch (format)
        {
            case null:
            case "":
            case "text":
                break;
            case "json":
                arguments.Add("--json");
                break;
            case "compactjson":
            case "compact-json":
                arguments.Add("--compact-json");
                break;
            case "invariantjson":
            case "invariant-json":
                arguments.Add("--invariant-json");
                break;
            default:
                throw new ArgumentException(
                    "JSON request output.format must be text, json, compactJson, or invariantJson.");
        }

        if (options.SummaryOnly == true) arguments.Add("--summary-only");
        AddOptionalNonNegativeInt(arguments, "--max-lines", options.MaxLines, "output.maxLines");
        AddOptionalNonNegativeInt(arguments, "--max-points", options.MaxPoints, "output.maxPoints");
        AddOptionalNonNegativeInt(arguments, "--max-hazards", options.MaxHazards, "output.maxHazards");
        AddOptionalNonNegativeInt(arguments, "--max-facts", options.MaxFacts, "output.maxFacts");
        AddOptionalNonNegativeInt(
            arguments,
            "--max-conditions",
            options.MaxConditions,
            "output.maxConditions");
        AddOptionalNonNegativeInt(arguments, "--max-proofs", options.MaxProofs, "output.maxProofs");
    }

    private static void AddGateOptions(
        List<string> arguments,
        SymbolicCliJsonGateOptions? options)
    {
        if (options == null) return;

        if (options.FailOnHazard == true) arguments.Add("--fail-on-hazard");
        if (options.FailOnUnprovenImplies == true) arguments.Add("--fail-on-unproven-implies");
        AddRepeated(
            arguments,
            "--allowed-capability",
            options.AllowedCapabilities,
            "gates.allowedCapabilities");
        if (options.FailOnCapabilityViolation == true)
            arguments.Add("--fail-on-capability-violation");
        if (options.FailOnCapabilityUnknown == true) arguments.Add("--fail-on-capability-unknown");
        AddOptionalValue(
            arguments,
            "--fail-on-complexity-exceeded",
            options.MaximumComplexity);
        if (options.FailOnComplexityUnknown == true) arguments.Add("--fail-on-complexity-unknown");
        AddOptionalNonNegativeInt(
            arguments,
            "--max-conservative-unknowns",
            options.MaxConservativeUnknowns,
            "gates.maxConservativeUnknowns");
        if (options.FailOnCompactTruncation == true) arguments.Add("--fail-on-compact-truncation");
        if (options.CompactThresholds == null) return;

        foreach (var threshold in options.CompactThresholds.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(threshold.Key))
                throw new ArgumentException("JSON request gates.compactThresholds cannot contain an empty metric.");

            if (threshold.Value < 0)
                throw new ArgumentException(
                    "JSON request gates.compactThresholds." + threshold.Key + " must be non-negative.");

            AddValue(
                arguments,
                "--fail-on-compact-threshold",
                threshold.Key + "=" + threshold.Value.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static void AddRepeated(
        List<string> arguments,
        string option,
        IEnumerable<string>? values,
        string propertyName)
    {
        if (values == null) return;

        foreach (var value in values)
            AddValue(arguments, option, RequireText(value, propertyName));
    }

    private static void AddOptionalValue(List<string> arguments, string option, string? value)
    {
        if (value == null) return;
        AddValue(arguments, option, RequireText(value, option));
    }

    private static void AddValue(List<string> arguments, string option, string value)
    {
        arguments.Add(option);
        arguments.Add(value);
    }

    private static void AddPositiveInt(
        List<string> arguments,
        string option,
        int? value,
        string propertyName)
    {
        if (!value.HasValue)
            throw new ArgumentException("JSON request " + propertyName + " is required.");
        AddOptionalPositiveInt(arguments, option, value, propertyName);
    }

    private static void AddOptionalPositiveInt(
        List<string> arguments,
        string option,
        int? value,
        string propertyName)
    {
        if (!value.HasValue) return;
        if (value.Value <= 0)
            throw new ArgumentException("JSON request " + propertyName + " must be a positive integer.");
        AddValue(arguments, option, value.Value.ToString(CultureInfo.InvariantCulture));
    }

    private static void AddNonNegativeInt(
        List<string> arguments,
        string option,
        int? value,
        string propertyName)
    {
        if (!value.HasValue)
            throw new ArgumentException("JSON request " + propertyName + " is required.");
        AddOptionalNonNegativeInt(arguments, option, value, propertyName);
    }

    private static void AddOptionalNonNegativeInt(
        List<string> arguments,
        string option,
        int? value,
        string propertyName)
    {
        if (!value.HasValue) return;
        if (value.Value < 0)
            throw new ArgumentException("JSON request " + propertyName + " must be non-negative.");
        AddValue(arguments, option, value.Value.ToString(CultureInfo.InvariantCulture));
    }

    private static string RequireText(string? value, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("JSON request " + propertyName + " cannot be empty.");
        return value;
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
}

internal sealed class SymbolicCliJsonSource
{
    public string? Text { get; set; }

    public string? FilePath { get; set; }

    public SymbolicCliJsonSourceMap? SourceMap { get; set; }
}

internal sealed class SymbolicCliJsonSourceMap
{
    public string? SourceUri { get; set; }

    public int? OriginalStartLine { get; set; }

    public int? OriginalStartColumn { get; set; }
}

internal sealed class SymbolicCliJsonTarget
{
    public string? Kind { get; set; }

    public int? Line { get; set; }

    public int? Column { get; set; }

    public int? Position { get; set; }

    public int? SpanStart { get; set; }

    public int? SpanEnd { get; set; }

    public int? StartLine { get; set; }

    public int? StartColumn { get; set; }

    public int? EndLine { get; set; }

    public int? EndColumn { get; set; }
}

internal sealed class SymbolicCliJsonParseOptions
{
    public string? LanguageVersion { get; set; }

    public string[]? PreprocessorSymbols { get; set; }

    public string? Nullable { get; set; }

    public bool? AllowUnsafe { get; set; }

    public string? DocumentationMode { get; set; }

    public string? Platform { get; set; }

    public string? Optimization { get; set; }

    public string? AssemblyName { get; set; }
}

internal sealed class SymbolicCliJsonSmtOptions
{
    public string? Mode { get; set; }

    public int? TimeoutMs { get; set; }

    public int? MethodBudgetMs { get; set; }

    public int? MaxPathConditions { get; set; }

    public int? MaxExpressionNodes { get; set; }

    public int? TransientRetries { get; set; }

    public bool? RecycleContextOnTransientFailure { get; set; }

    public bool? DisposeContextOnExit { get; set; }
}

internal sealed class SymbolicCliJsonQueryOptions
{
    public bool? CheckReachability { get; set; }

    public bool? IncludeExpressionProgramPoints { get; set; }

    public bool? IncludeCurrentStatementCompletionFacts { get; set; }

    public string[]? InvariantTargets { get; set; }

    public bool? IncludeUnprovenHazards { get; set; }

    public bool? FailOnHazard { get; set; }

    public string[]? HazardKinds { get; set; }
}

internal sealed class SymbolicCliJsonOutputOptions
{
    public string? Format { get; set; }

    public bool? SummaryOnly { get; set; }

    public int? MaxLines { get; set; }

    public int? MaxPoints { get; set; }

    public int? MaxHazards { get; set; }

    public int? MaxFacts { get; set; }

    public int? MaxConditions { get; set; }

    public int? MaxProofs { get; set; }
}

internal sealed class SymbolicCliJsonGateOptions
{
    public bool? FailOnHazard { get; set; }

    public bool? FailOnUnprovenImplies { get; set; }

    public string[]? AllowedCapabilities { get; set; }

    public bool? FailOnCapabilityViolation { get; set; }

    public bool? FailOnCapabilityUnknown { get; set; }

    public string? MaximumComplexity { get; set; }

    public bool? FailOnComplexityUnknown { get; set; }

    public int? MaxConservativeUnknowns { get; set; }

    public bool? FailOnCompactTruncation { get; set; }

    public Dictionary<string, int>? CompactThresholds { get; set; }
}

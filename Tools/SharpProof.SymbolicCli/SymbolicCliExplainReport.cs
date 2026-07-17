using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using SharpProof.Analyzer;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Smt;

internal sealed record SymbolicCliExplainReport(
    SymbolicCliExplainSource Source,
    SymbolicCliExplainTarget Target,
    SymbolicCliExplainProject? Project,
    SymbolicQueryResult Invariant,
    IReadOnlyList<SymbolicRuntimeHazard> RuntimeHazards,
    SymbolicCapabilityResult Capabilities,
    SymbolicComplexityResult Complexity,
    SymbolicCliExplainDiagnostics Diagnostics,
    SymbolicCliExplainTruncation Truncation)
{
    [JsonPropertyOrder(-4)]
    public string Kind => "explain";

    [JsonPropertyOrder(-3)]
    public int SchemaVersion => 3;

    [JsonPropertyOrder(-2)]
    public int EvidenceSchemaVersion => SharpProofEvidenceSchema.CurrentVersion;

    [JsonPropertyOrder(-1)]
    public string EvidenceSchemaCompatibility => SharpProofEvidenceSchema.CompatibilityPolicy;

    internal static async Task<SymbolicCliExplainReport> CreateAsync(
        SymbolicCliOptions options,
        SymbolicCliInputContext inputContext,
        SmtAnalysisService smtAnalysis,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(inputContext);
        ArgumentNullException.ThrowIfNull(smtAnalysis);

        var service = new SymbolicQueryService();
        var source = inputContext.SourceInput;
        var queryOptions = options.CreateQueryOptions(smtAnalysis, false);
        var requestedTarget = options.Position.HasValue
            ? SymbolicQueryTarget.Position(options.Position.Value)
            : SymbolicQueryTarget.Point(options.Line, options.Column);
        var invariant = service.Query(new SymbolicQueryContext(source, requestedTarget, queryOptions));
        if (invariant.ProgramPoints.Count != 1)
            throw SymbolicCliErrorWriter.CreateException(
                SymbolicErrorCodes.UnsupportedTarget,
                SymbolicErrorCategory.Unsupported,
                $"Explain requires one source program point; the query returned {invariant.ScopeKind}.",
                SymbolicErrorExitCodes.InvalidData,
                "resultKind",
                invariant.ScopeKind);

        var point = invariant.ProgramPoints[0];
        var hazardResult = service.QueryRuntimeHazards(
            new SymbolicQueryContext(
                source,
                SymbolicQueryTarget.Point(point.Line, point.Column),
                queryOptions),
            options.CreateRuntimeHazardOptions());
        var hazards = hazardResult.Hazards.Take(options.ReportMaxHazards).ToArray();
        var capabilities = service.QueryCapabilities(
            new SymbolicQueryContext(source, requestedTarget, queryOptions));
        var complexity = service.QueryComplexity(
            new SymbolicQueryContext(source, requestedTarget, queryOptions));
        var diagnostics = await SymbolicCliExplainDiagnostics.CreateAsync(
            options,
            inputContext.ProjectContext,
            options.ReportMaxDiagnostics,
            cancellationToken).ConfigureAwait(false);
        var project = SymbolicCliExplainProject.Create(inputContext, options.ReportMaxItems);

        return new SymbolicCliExplainReport(
            new SymbolicCliExplainSource(source.FilePath ?? point.FilePath, source.Kind.ToString(), source.SourceMap),
            SymbolicCliExplainTarget.Create(options, point),
            project,
            invariant,
            hazards,
            capabilities,
            complexity,
            diagnostics,
            new SymbolicCliExplainTruncation(
                invariant.AnalysisTruncation.IsTruncated,
                hazardResult.Hazards.Count > hazards.Length,
                diagnostics.IsTruncated,
                project?.IsTruncated == true,
                invariant.AnalysisTruncation.IsTruncated || hazardResult.AnalysisTruncation.IsTruncated));
    }

    internal IReadOnlyDictionary<string, object?> ToSarif()
    {
        var results = Diagnostics.Items
            .Select(static diagnostic => CreateSarifResult(
                diagnostic.Id,
                ToSarifLevel(diagnostic.Severity),
                diagnostic.Message,
                diagnostic.FilePath,
                diagnostic.StartLine,
                diagnostic.StartColumn,
                diagnostic.EndLine,
                diagnostic.EndColumn))
            .Concat(RuntimeHazards.Select(static hazard => CreateSarifResult(
                "SPQ-HZ-" + ToKebabCase(hazard.Kind.ToString()).ToUpperInvariant(),
                hazard.Status == SymbolicRuntimeHazardStatus.Proven ? "error" : "warning",
                $"{hazard.Kind}: {hazard.OperationText} ({hazard.StatusReason})",
                hazard.FilePath,
                hazard.Line,
                hazard.Column,
                hazard.NodeEndLine,
                hazard.NodeEndColumn)))
            .ToList();
        if (Truncation.IsTruncated)
            results.Add(CreateSarifResult(
                "SPQ-REPORT-TRUNCATED",
                "warning",
                "The explain report or its analysis reached a configured bound.",
                Source.FilePath,
                Target.ResolvedLine,
                Target.ResolvedColumn,
                Target.ResolvedEndLine,
                Target.ResolvedEndColumn));

        return new Dictionary<string, object?>
        {
            ["$schema"] = "https://json.schemastore.org/sarif-2.1.0.json",
            ["version"] = "2.1.0",
            ["runs"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["tool"] = new Dictionary<string, object?>
                    {
                        ["driver"] = new Dictionary<string, object?> { ["name"] = "SharpProof" }
                    },
                    ["results"] = results,
                    ["properties"] = new Dictionary<string, object?>
                    {
                        ["explainSchemaVersion"] = SchemaVersion,
                        ["evidenceSchemaVersion"] = EvidenceSchemaVersion,
                        ["reportTruncated"] = Truncation.IsTruncated
                    }
                }
            }
        };
    }

    internal string ToMarkdown()
    {
        var point = Invariant.ProgramPoints.Single();
        var builder = new StringBuilder()
            .AppendLine("# SharpProof explanation")
            .AppendLine()
            .AppendLine($"- File: `{Escape(Source.FilePath)}`")
            .AppendLine($"- Target: {Target.DisplayText}")
            .AppendLine($"- Resolved point: {Target.ResolvedLine}:{Target.ResolvedColumn} (`{Target.NodeKind}`)")
            .AppendLine()
            .AppendLine("## Invariant and reachability")
            .AppendLine()
            .AppendLine($"- Invariant: `{Escape(point.MergedInvariantText)}`")
            .AppendLine($"- Reachability: `{point.Reachability}` - {Escape(point.ReachabilityReason)}")
            .AppendLine($"- Proof outcomes: {point.ConditionProofs.Count}")
            .AppendLine()
            .AppendLine("## Runtime hazards")
            .AppendLine();
        foreach (var hazard in RuntimeHazards)
            builder.AppendLine($"- `{hazard.Kind}` at {hazard.Line}:{hazard.Column}: {Escape(hazard.OperationText)}");

        builder.AppendLine()
            .AppendLine("## Capabilities")
            .AppendLine()
            .AppendLine($"- Method: `{Escape(Capabilities.MethodDisplayName)}`")
            .AppendLine($"- Capability set: `{Escape(Capabilities.CapabilityText)}`")
            .AppendLine($"- Conservative: {Capabilities.HasUnknowns.ToString().ToLowerInvariant()}")
            .AppendLine()
            .AppendLine("## Complexity")
            .AppendLine()
            .AppendLine($"- Method: `{Escape(Complexity.MethodDisplayName)}`")
            .AppendLine($"- Bound: `{Escape(Complexity.Complexity.Text)}`")
            .AppendLine()
            .AppendLine("## Analyzer diagnostics")
            .AppendLine()
            .AppendLine($"Total: {Diagnostics.TotalCount}; target: {Diagnostics.TargetCount}");
        foreach (var diagnostic in Diagnostics.Items)
            builder.AppendLine($"- `{diagnostic.Id}` {Escape(diagnostic.Message)}");

        if (Truncation.IsTruncated)
            builder.AppendLine().AppendLine("## Truncation").AppendLine()
                .AppendLine("The report or its analysis reached a configured bound.");
        return builder.ToString();
    }

    internal string ToText()
    {
        var point = Invariant.ProgramPoints.Single();
        var builder = new StringBuilder()
            .AppendLine("SharpProof explanation")
            .AppendLine($"File: {Source.FilePath}")
            .AppendLine($"Source input: {Source.Kind}");
        if (Source.SourceMap is { } sourceMap)
            builder.AppendLine($"Source map URI: {sourceMap.SourceUri}")
                .AppendLine($"Source map origin: line {sourceMap.OriginalStartLine}, column {sourceMap.OriginalStartColumn}");
        builder.AppendLine($"Target: {Target.DisplayText}");
        AppendProject(builder, Project);
        builder.AppendLine()
            .AppendLine("Invariant proof")
            .AppendLine($"Node: {point.NodeKind}")
            .AppendLine($"Method: {point.MethodName ?? "<unknown>"}")
            .AppendLine($"Merged invariant: {point.MergedInvariantText}")
            .AppendLine($"Reachability: {point.Reachability}")
            .AppendLine($"Proof outcomes: {point.ConditionProofs.Count}")
            .AppendLine()
            .AppendLine("Runtime hazards");
        foreach (var hazard in RuntimeHazards)
            builder.AppendLine($"  - {hazard.Kind} {hazard.Status} at {hazard.Line}:{hazard.Column}: {hazard.OperationText}");
        builder.AppendLine()
            .AppendLine("Capabilities")
            .AppendLine($"Capabilities: {Capabilities.CapabilityText}")
            .AppendLine()
            .AppendLine("Complexity")
            .AppendLine($"Complexity: {Complexity.Complexity.Text}")
            .AppendLine()
            .AppendLine("Build diagnostics")
            .AppendLine($"File/project diagnostics: {Diagnostics.TotalCount}")
            .AppendLine($"Target diagnostics: {Diagnostics.TargetCount}");
        foreach (var diagnostic in Diagnostics.Items)
            builder.AppendLine($"  - {diagnostic.Id} {diagnostic.Severity}: {diagnostic.Message}");
        if (Invariant.SmtDiagnostics.IsConfigured)
            builder.AppendLine($"Query timeout ms: {Invariant.SmtDiagnostics.QueryTimeoutMs}");
        return builder.ToString();
    }

    private static void AppendProject(StringBuilder builder, SymbolicCliExplainProject? project)
    {
        if (project == null) return;
        builder.AppendLine($"Project: {project.Name}")
            .AppendLine($"Project file: {project.ProjectFilePath}");
        if (project.SolutionFilePath != null)
            builder.AppendLine($"Solution file: {project.SolutionFilePath}");
        builder.AppendLine($"Analyzer config files: {project.AnalyzerConfigCount}")
            .AppendLine($"Additional files: {project.AdditionalFileCount}")
            .AppendLine($"Baseline loaded: {project.HasBaseline}")
            .AppendLine($"Effect summaries: {project.EffectSummaryFileCount}")
            .AppendLine($"Workspace diagnostics: {project.WorkspaceDiagnosticCount}");
    }

    private static Dictionary<string, object?> CreateSarifResult(
        string ruleId,
        string level,
        string message,
        string? filePath,
        int? startLine,
        int? startColumn,
        int? endLine,
        int? endColumn)
    {
        var result = new Dictionary<string, object?>
        {
            ["ruleId"] = ruleId,
            ["level"] = level,
            ["message"] = new Dictionary<string, object?> { ["text"] = message }
        };
        if (string.IsNullOrWhiteSpace(filePath) || !startLine.HasValue || !startColumn.HasValue) return result;

        result["locations"] = new object[]
        {
            new Dictionary<string, object?>
            {
                ["physicalLocation"] = new Dictionary<string, object?>
                {
                    ["artifactLocation"] = new Dictionary<string, object?>
                    {
                        ["uri"] = Path.IsPathRooted(filePath)
                            ? new Uri(CliHost.GetFullPath(filePath)).AbsoluteUri
                            : filePath.Replace('\\', '/')
                    },
                    ["region"] = new Dictionary<string, object?>
                    {
                        ["startLine"] = Math.Max(1, startLine.Value),
                        ["startColumn"] = Math.Max(1, startColumn.Value),
                        ["endLine"] = endLine,
                        ["endColumn"] = endColumn
                    }
                }
            }
        };
        return result;
    }

    private static string ToSarifLevel(string severity) => severity.ToLowerInvariant() switch
    {
        "error" => "error",
        "warning" => "warning",
        "info" => "note",
        _ => "none"
    };

    private static string ToKebabCase(string value) => string.Concat(value.Select((c, index) =>
        index != 0 && char.IsUpper(c) && !char.IsUpper(value[index - 1]) ? "-" + c : c.ToString()));

    private static string Escape(string? value) => (value ?? string.Empty)
        .Replace("`", "\\`", StringComparison.Ordinal)
        .Replace("\r", " ", StringComparison.Ordinal)
        .Replace("\n", " ", StringComparison.Ordinal);
}

internal sealed record SymbolicCliExplainSource(string FilePath, string Kind, SymbolicSourceMap? SourceMap);

internal sealed record SymbolicCliExplainTarget(
    int? RequestedLine,
    int? RequestedColumn,
    int? RequestedPosition,
    int ResolvedLine,
    int ResolvedColumn,
    int ResolvedEndLine,
    int ResolvedEndColumn,
    string NodeKind,
    string? MethodName,
    string ProgramPointKind)
{
    internal string DisplayText => RequestedPosition.HasValue
        ? $"position {RequestedPosition.Value}"
        : $"line {RequestedLine}, column {RequestedColumn}";

    internal static SymbolicCliExplainTarget Create(SymbolicCliOptions options, SymbolicProgramPointResult point) =>
        new(
            options.Position.HasValue ? null : options.Line,
            options.Position.HasValue ? null : options.Column,
            options.Position,
            point.Line,
            point.Column,
            point.NodeEndLine,
            point.NodeEndColumn,
            point.NodeKind,
            point.MethodName,
            point.ProgramPointKind);
}

internal sealed record SymbolicCliExplainProject(
    string Name,
    string? ProjectFilePath,
    string? SolutionFilePath,
    bool HasBaseline,
    int EffectSummaryFileCount,
    int AnalyzerConfigCount,
    IReadOnlyList<string> AnalyzerConfigPaths,
    int AdditionalFileCount,
    IReadOnlyList<string> AdditionalFilePaths,
    int WorkspaceDiagnosticCount,
    IReadOnlyList<string> WorkspaceDiagnostics,
    int ConfigurationIssueCount,
    IReadOnlyList<SharpProofProjectConfigurationIssue> ConfigurationIssues,
    bool IsTruncated)
{
    internal static SymbolicCliExplainProject? Create(SymbolicCliInputContext input, int limit)
    {
        var context = input.ProjectContext;
        if (context == null) return null;
        var analyzerConfigs = SymbolicCompactProjection.Project(context.AnalyzerConfigPaths, limit);
        var additionalFiles = SymbolicCompactProjection.Project(context.AdditionalFilePaths, limit);
        var workspaceDiagnostics = SymbolicCompactProjection.Project(input.WorkspaceDiagnostics, limit);
        var configurationIssues = SymbolicCompactProjection.Project(context.ConfigurationIssues, limit);
        return new SymbolicCliExplainProject(
            context.ProjectName,
            context.ProjectFilePath,
            context.SolutionFilePath,
            context.HasBaseline,
            context.EffectSummaryFileCount,
            analyzerConfigs.TotalCount,
            analyzerConfigs.Items,
            additionalFiles.TotalCount,
            additionalFiles.Items,
            workspaceDiagnostics.TotalCount,
            workspaceDiagnostics.Items,
            configurationIssues.TotalCount,
            configurationIssues.Items,
            analyzerConfigs.IsTruncated || additionalFiles.IsTruncated ||
            workspaceDiagnostics.IsTruncated || configurationIssues.IsTruncated);
    }
}

internal sealed record SymbolicCliExplainDiagnostics(
    int TotalCount,
    int TargetCount,
    IReadOnlyList<SymbolicCliExplainDiagnostic> Items,
    bool IsTruncated)
{
    internal static async Task<SymbolicCliExplainDiagnostics> CreateAsync(
        SymbolicCliOptions options,
        SharpProofProjectAnalysisContext? context,
        int limit,
        CancellationToken cancellationToken)
    {
        if (context == null) return new SymbolicCliExplainDiagnostics(0, 0, Array.Empty<SymbolicCliExplainDiagnostic>(), false);
        var diagnostics = await context.GetAnalyzerDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
        var relevant = SymbolicCliDiagnosticSelector.SelectRelevant(
            diagnostics,
            context.SyntaxTree,
            options.Position,
            options.Line);
        var projection = SymbolicCompactProjection.Project(relevant, limit);
        return new SymbolicCliExplainDiagnostics(
            projection.TotalCount,
            relevant.Count(static item => item.IsTarget),
            projection.Items.Select(static item => SymbolicCliExplainDiagnostic.Create(item.Diagnostic, item.IsTarget)).ToArray(),
            projection.IsTruncated);
    }
}

internal sealed record SymbolicCliExplainDiagnostic(
    string Id,
    string Severity,
    string Message,
    bool IsTarget,
    string? FilePath,
    int? StartLine,
    int? StartColumn,
    int? EndLine,
    int? EndColumn)
{
    internal static SymbolicCliExplainDiagnostic Create(Diagnostic diagnostic, bool isTarget)
    {
        var location = diagnostic.Location;
        var hasLocation = location != Location.None && location.IsInSource;
        var span = hasLocation ? location.GetLineSpan() : default;
        return new SymbolicCliExplainDiagnostic(
            diagnostic.Id,
            diagnostic.Severity.ToString(),
            diagnostic.GetMessage(CultureInfo.InvariantCulture),
            isTarget,
            hasLocation ? span.Path : null,
            hasLocation ? span.StartLinePosition.Line + 1 : null,
            hasLocation ? span.StartLinePosition.Character + 1 : null,
            hasLocation ? span.EndLinePosition.Line + 1 : null,
            hasLocation ? span.EndLinePosition.Character + 1 : null);
    }
}

internal sealed record SymbolicCliExplainTruncation(
    bool InvariantAnalysis,
    bool RuntimeHazards,
    bool Diagnostics,
    bool Project,
    bool Analysis)
{
    public bool IsTruncated => InvariantAnalysis || RuntimeHazards || Diagnostics || Project || Analysis;
}

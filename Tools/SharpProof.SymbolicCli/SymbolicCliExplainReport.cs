using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using SharpProof.Analyzer;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Smt;

internal sealed class SymbolicCliExplainReport
{
    private SymbolicCliExplainReport(
        SymbolicCliExplainSource source,
        SymbolicCliExplainTarget target,
        SymbolicCliExplainProject? project,
        SymbolicCompactQueryResult invariant,
        SymbolicCompactRuntimeHazardQueryResult runtimeHazards,
        SymbolicCliExplainCapabilityResult capabilities,
        SymbolicCliExplainComplexityResult complexity,
        SymbolicCliExplainDiagnosticResult diagnostics,
        IReadOnlyList<SymbolicCliExplainCrossLink> crossLinks,
        SymbolicCliExplainTruncation truncation)
    {
        Source = source;
        Target = target;
        Project = project;
        Invariant = invariant;
        RuntimeHazards = runtimeHazards;
        Capabilities = capabilities;
        Complexity = complexity;
        Diagnostics = diagnostics;
        CrossLinks = crossLinks;
        Truncation = truncation;
    }

    public string Kind => "explain";

    public int SchemaVersion => 1;

    public int EvidenceSchemaVersion => SharpProofEvidenceSchema.CurrentVersion;

    public string EvidenceSchemaCompatibility => SharpProofEvidenceSchema.CompatibilityPolicy;

    public SymbolicCliExplainSource Source { get; }

    public SymbolicCliExplainTarget Target { get; }

    public SymbolicCliExplainProject? Project { get; }

    public SymbolicCompactQueryResult Invariant { get; }

    public SymbolicCompactRuntimeHazardQueryResult RuntimeHazards { get; }

    public SymbolicCliExplainCapabilityResult Capabilities { get; }

    public SymbolicCliExplainComplexityResult Complexity { get; }

    public SymbolicCliExplainDiagnosticResult Diagnostics { get; }

    public IReadOnlyList<SymbolicCliExplainCrossLink> CrossLinks { get; }

    public SymbolicCliExplainTruncation Truncation { get; }

    public static async Task<SymbolicCliExplainReport> CreateAsync(
        SymbolicCliOptions options,
        SymbolicCliInputContext inputContext,
        SmtAnalysisService smtAnalysis,
        CancellationToken cancellationToken = default)
    {
        if (options == null) throw new ArgumentNullException(nameof(options));
        if (inputContext == null) throw new ArgumentNullException(nameof(inputContext));
        if (smtAnalysis == null) throw new ArgumentNullException(nameof(smtAnalysis));

        var service = new SymbolicQueryService();
        var sourceInput = inputContext.SourceInput;
        var queryOptions = options.CreateQueryOptions(smtAnalysis, false);
        var pointTarget = options.Position.HasValue
            ? SymbolicQueryTarget.Position(options.Position.Value)
            : SymbolicQueryTarget.Point(options.Line, options.Column);
        var pointResult = service.Query(new SymbolicQueryContext(sourceInput, pointTarget, queryOptions));
        if (pointResult.ProgramPoints.Count != 1)
            throw SymbolicCliErrorWriter.CreateException(
                SymbolicErrorCodes.UnsupportedTarget,
                SymbolicErrorCategory.Unsupported,
                $"Explain requires a resolvable source program point, but the query returned {pointResult.ScopeKind}.",
                SymbolicErrorExitCodes.InvalidData,
                "resultKind",
                pointResult.ScopeKind);
        var point = pointResult.ProgramPoints[0];

        var itemLimit = options.ReportMaxItems;
        var compactOptions = new SymbolicCompactQueryOptions(
            maxLines: 0,
            maxProgramPoints: itemLimit == 0 ? 0 : 1,
            maxFacts: itemLimit,
            maxConditions: itemLimit,
            maxProofs: itemLimit,
            invariantTargets: options.InvariantTargets);
        var invariant = point.ToCompactResult(compactOptions);

        var runtimeHazards = service.QueryRuntimeHazards(
            new SymbolicQueryContext(
                sourceInput,
                SymbolicQueryTarget.Point(point.Line, point.Column),
                queryOptions),
            options.CreateRuntimeHazardOptions());
        var compactHazards = runtimeHazards.ToCompactResult(
            new SymbolicCompactRuntimeHazardQueryOptions(
                options.ReportMaxHazards,
                itemLimit));

        var capabilityResult = service.QueryCapabilities(
            new SymbolicQueryContext(sourceInput, pointTarget, queryOptions));
        var capabilities = SymbolicCliExplainCapabilityResult.FromResult(capabilityResult, itemLimit);
        var complexityResult = service.QueryComplexity(
            new SymbolicQueryContext(sourceInput, pointTarget, queryOptions));
        var complexity = SymbolicCliExplainComplexityResult.FromResult(complexityResult, itemLimit);

        var diagnostics = await CreateDiagnosticsAsync(
            options,
            inputContext.ProjectContext,
            options.ReportMaxDiagnostics,
            cancellationToken).ConfigureAwait(false);
        var project = SymbolicCliExplainProject.FromContext(inputContext, itemLimit);
        var source = new SymbolicCliExplainSource(
            sourceInput.FilePath ?? point.FilePath,
            sourceInput.Kind.ToString(),
            sourceInput.SourceMap);
        var target = SymbolicCliExplainTarget.FromPoint(options, point);
        var crossLinks = CreateCrossLinks(diagnostics.Items);
        var truncation = new SymbolicCliExplainTruncation(
            invariant.Truncation.IsTruncated,
            compactHazards.Truncation.Hazards || compactHazards.Truncation.PathConditions,
            capabilities.Truncation.IsTruncated,
            complexity.Truncation.IsTruncated,
            diagnostics.Truncated,
            project?.Truncation.IsTruncated == true,
            invariant.AnalysisTruncation.IsTruncated || compactHazards.AnalysisTruncation.IsTruncated);

        return new SymbolicCliExplainReport(
            source,
            target,
            project,
            invariant,
            compactHazards,
            capabilities,
            complexity,
            diagnostics,
            crossLinks,
            truncation);
    }

    public IReadOnlyDictionary<string, object?> ToSarif()
    {
        var results = new List<Dictionary<string, object?>>();
        foreach (var diagnostic in Diagnostics.Items)
        {
            results.Add(CreateSarifResult(
                diagnostic.Id,
                ToSarifLevel(diagnostic.Severity),
                diagnostic.Message,
                diagnostic.FilePath,
                diagnostic.StartLine,
                diagnostic.StartColumn,
                diagnostic.EndLine,
                diagnostic.EndColumn,
                diagnostic.CrossLinks,
                new Dictionary<string, object?>
                {
                    ["isTarget"] = diagnostic.IsTarget,
                    ["helpLinkUri"] = diagnostic.HelpLinkUri
                }));
        }

        foreach (var hazard in RuntimeHazards.Hazards)
        {
            results.Add(CreateSarifResult(
                "SPQ-HZ-" + ToKebabCase(hazard.Kind.ToString()).ToUpperInvariant(),
                hazard.Status switch
                {
                    SymbolicRuntimeHazardStatus.Proven => "error",
                    SymbolicRuntimeHazardStatus.Unknown => "warning",
                    SymbolicRuntimeHazardStatus.Unsupported => "note",
                    _ => "none"
                },
                $"{hazard.Kind}: {hazard.OperationText} ({hazard.StatusReason})",
                hazard.FilePath,
                hazard.Line,
                hazard.Column,
                hazard.NodeEndLine,
                hazard.NodeEndColumn,
                new[] { "#/runtimeHazards" },
                new Dictionary<string, object?>
                {
                    ["status"] = hazard.Status.ToString(),
                    ["exceptionType"] = hazard.ExceptionType,
                    ["category"] = hazard.Category,
                    ["triggerCondition"] = hazard.TriggerCondition
                }));
        }

        if (Invariant.InvariantQuery.HasUnresolvedAnalysis || Invariant.AnalysisTruncation.IsTruncated)
        {
            results.Add(CreateSarifResult(
                "SPQ-INVARIANT-UNKNOWN",
                "warning",
                Invariant.InvariantQuery.Summary,
                Source.FilePath,
                Target.ResolvedLine,
                Target.ResolvedColumn,
                Target.ResolvedEndLine,
                Target.ResolvedEndColumn,
                new[] { "#/invariant" }));
        }

        if (Capabilities.HasUnknowns)
        {
            results.Add(CreateSarifResult(
                "SPQ-CAPABILITY-UNKNOWN",
                "warning",
                "Capability analysis is conservative or contains unknown sites.",
                Capabilities.FilePath,
                Capabilities.StartLine,
                Capabilities.StartColumn,
                Capabilities.EndLine,
                Capabilities.EndColumn,
                new[] { "#/capabilities" }));
        }

        if (Complexity.Complexity.IsConservative || Complexity.Complexity.IsUnknown)
        {
            results.Add(CreateSarifResult(
                "SPQ-COMPLEXITY-UNKNOWN",
                "warning",
                $"Complexity analysis is conservative: {Complexity.Complexity.Text}.",
                Complexity.FilePath,
                Complexity.StartLine,
                Complexity.StartColumn,
                Complexity.EndLine,
                Complexity.EndColumn,
                new[] { "#/complexity" }));
        }

        if (Truncation.IsTruncated)
        {
            results.Add(CreateSarifResult(
                "SPQ-REPORT-TRUNCATED",
                "warning",
                "The explain report was bounded or analysis reached a configured limit; inspect report.truncation.",
                Source.FilePath,
                Target.ResolvedLine,
                Target.ResolvedColumn,
                Target.ResolvedEndLine,
                Target.ResolvedEndColumn,
                new[] { "#/truncation" }));
        }

        var ruleIds = results
            .Select(static result => (string)result["ruleId"]!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static id => id, StringComparer.Ordinal)
            .ToArray();
        var rules = ruleIds
            .Select(id => new Dictionary<string, object?>
            {
                ["id"] = id,
                ["shortDescription"] = new Dictionary<string, object?>
                {
                    ["text"] = DescribeRule(id)
                }
            })
            .ToArray();
        var runProperties = new Dictionary<string, object?>
        {
            ["explainSchemaVersion"] = SchemaVersion,
            ["evidenceSchemaVersion"] = EvidenceSchemaVersion,
            ["evidenceSchemaCompatibility"] = EvidenceSchemaCompatibility,
            ["reportTruncated"] = Truncation.IsTruncated,
            ["crossLinks"] = CrossLinks
        };

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
                        ["driver"] = new Dictionary<string, object?>
                        {
                            ["name"] = "SharpProof",
                            ["informationUri"] = "https://github.com/alexyorke/SharpProof",
                            ["rules"] = rules
                        }
                    },
                    ["results"] = results,
                    ["properties"] = runProperties
                }
            }
        };
    }

    public string ToMarkdown()
    {
        var builder = new StringBuilder();
        builder.AppendLine("# SharpProof explanation");
        builder.AppendLine();
        builder.AppendLine($"- File: `{EscapeInline(Source.FilePath)}`");
        builder.AppendLine($"- Source input: `{Source.Kind}`");
        builder.AppendLine($"- Target: {EscapeInline(Target.DisplayText)}");
        builder.AppendLine($"- Resolved point: {Target.ResolvedLine}:{Target.ResolvedColumn} (`{Target.NodeKind}`)");
        if (Project != null)
        {
            builder.AppendLine($"- Project: `{EscapeInline(Project.Name)}`");
            builder.AppendLine($"- Baseline loaded: {Project.HasBaseline.ToString().ToLowerInvariant()}");
            builder.AppendLine($"- Effect summaries: {Project.EffectSummaryFileCount}");
        }

        builder.AppendLine();
        builder.AppendLine("## Invariant and reachability");
        builder.AppendLine();
        builder.AppendLine($"- Invariant: `{EscapeInline(Invariant.MergedInvariantText)}`");
        builder.AppendLine($"- Invariant status: `{Invariant.InvariantQuery.Status}` - {EscapeInline(Invariant.InvariantQuery.StatusReason)}");
        builder.AppendLine($"- Reachability: `{Invariant.PointReachability}` - {EscapeInline(Invariant.ReachabilityReason ?? string.Empty)}");
        builder.AppendLine($"- Proof outcomes: {Invariant.ProofOutcomes.TotalCount} total, {Invariant.ProofOutcomes.ProvenTrueCount} true, {Invariant.ProofOutcomes.ProvenFalseCount} false, {Invariant.ProofOutcomes.UnknownCount} unknown");

        builder.AppendLine();
        builder.AppendLine("## Runtime hazards");
        builder.AppendLine();
        builder.AppendLine($"Total: {RuntimeHazards.HazardCount}");
        if (RuntimeHazards.Hazards.Count != 0)
        {
            builder.AppendLine();
            builder.AppendLine("| Kind | Status | Location | Operation |");
            builder.AppendLine("| --- | --- | --- | --- |");
            foreach (var hazard in RuntimeHazards.Hazards)
                builder.AppendLine($"| {EscapeCell(hazard.Kind.ToString())} | {EscapeCell(hazard.Status.ToString())} | {hazard.Line}:{hazard.Column} | {EscapeCell(hazard.OperationText)} |");
        }

        builder.AppendLine();
        builder.AppendLine("## Capabilities");
        builder.AppendLine();
        builder.AppendLine($"- Method: `{EscapeInline(Capabilities.MethodDisplayName)}`");
        builder.AppendLine($"- Capability set: `{EscapeInline(Capabilities.CapabilityText)}`");
        builder.AppendLine($"- Conservative: {Capabilities.HasUnknowns.ToString().ToLowerInvariant()}");
        if (Capabilities.Sites.Count != 0)
        {
            builder.AppendLine();
            builder.AppendLine("| Capability | Site | Location | Symbol or operation |");
            builder.AppendLine("| --- | --- | --- | --- |");
            foreach (var site in Capabilities.Sites)
            {
                var detail = string.IsNullOrWhiteSpace(site.SymbolDisplayName)
                    ? site.OperationText
                    : site.SymbolDisplayName;
                builder.AppendLine($"| {EscapeCell(site.IsUnknown ? "Unknown" : site.CapabilityText)} | {EscapeCell(site.SiteKind)} | {site.SourceLine}:{site.SourceColumn} | {EscapeCell(detail)} |");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Complexity");
        builder.AppendLine();
        builder.AppendLine($"- Method: `{EscapeInline(Complexity.MethodDisplayName)}`");
        builder.AppendLine($"- Bound: `{EscapeInline(Complexity.Complexity.Text)}` (`{Complexity.Complexity.Kind}`)");
        builder.AppendLine($"- Conservative: {Complexity.Complexity.IsConservative.ToString().ToLowerInvariant()}");
        if (Complexity.Drivers.Count != 0)
        {
            builder.AppendLine();
            builder.AppendLine("| Driver | Location | Description |");
            builder.AppendLine("| --- | --- | --- |");
            foreach (var driver in Complexity.Drivers)
                builder.AppendLine($"| {EscapeCell(driver.Kind)} | {driver.SourceLine}:{driver.SourceColumn} | {EscapeCell(driver.Description)} |");
        }

        builder.AppendLine();
        builder.AppendLine("## Analyzer diagnostics");
        builder.AppendLine();
        builder.AppendLine($"Total: {Diagnostics.TotalCount}; target: {Diagnostics.TargetCount}");
        if (Diagnostics.Items.Count != 0)
        {
            builder.AppendLine();
            builder.AppendLine("| ID | Severity | Target | Location | Message | Related evidence |");
            builder.AppendLine("| --- | --- | --- | --- | --- | --- |");
            foreach (var diagnostic in Diagnostics.Items)
            {
                var location = diagnostic.StartLine.HasValue
                    ? $"{diagnostic.StartLine}:{diagnostic.StartColumn}"
                    : "project";
                builder.AppendLine($"| {EscapeCell(diagnostic.Id)} | {EscapeCell(diagnostic.Severity)} | {(diagnostic.IsTarget ? "yes" : "no")} | {location} | {EscapeCell(diagnostic.Message)} | {EscapeCell(string.Join(", ", diagnostic.CrossLinks))} |");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Cross-links");
        builder.AppendLine();
        foreach (var link in CrossLinks)
            builder.AppendLine($"- `{EscapeInline(link.From)}` {EscapeInline(link.Relation)} `{EscapeInline(link.To)}`: {EscapeInline(link.Description)}");

        if (Truncation.IsTruncated)
        {
            builder.AppendLine();
            builder.AppendLine("## Truncation");
            builder.AppendLine();
            builder.AppendLine("This report is bounded. Inspect the machine-readable `truncation` object or increase the `--report-max-*` limits.");
        }

        return builder.ToString();
    }

    private static async Task<SymbolicCliExplainDiagnosticResult> CreateDiagnosticsAsync(
        SymbolicCliOptions options,
        SharpProofProjectAnalysisContext? context,
        int limit,
        CancellationToken cancellationToken)
    {
        if (context == null) return SymbolicCliExplainDiagnosticResult.Empty;

        var diagnostics = await context.GetAnalyzerDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
        var relevant = SymbolicCliDiagnosticSelector.SelectRelevant(
            diagnostics,
            context.SyntaxTree,
            options.Position,
            options.Line);
        var projection = SymbolicCompactProjection.Project(relevant, limit);
        var items = projection.Items
            .Select(static item => SymbolicCliExplainDiagnostic.FromDiagnostic(
                item.Diagnostic,
                item.IsTarget))
            .ToArray();
        return new SymbolicCliExplainDiagnosticResult(
            projection.TotalCount,
            relevant.Count(static item => item.IsTarget),
            items,
            projection.IsTruncated);
    }

    private static IReadOnlyList<SymbolicCliExplainCrossLink> CreateCrossLinks(
        IReadOnlyList<SymbolicCliExplainDiagnostic> diagnostics)
    {
        var links = new List<SymbolicCliExplainCrossLink>
        {
            new("#/target", "resolvesTo", "#/invariant/queryDescriptor", "The requested target resolves to the invariant program point."),
            new("#/invariant", "corroborates", "#/runtimeHazards", "Reachability and facts supply context for runtime hazards."),
            new("#/capabilities", "describes", "#/invariant/queryDescriptor/methodName", "Capabilities describe the containing method-like body."),
            new("#/complexity", "describes", "#/invariant/queryDescriptor/methodName", "Complexity describes the containing method-like body.")
        };
        for (var index = 0; index < diagnostics.Count; index++)
        {
            foreach (var target in diagnostics[index].CrossLinks)
                links.Add(new SymbolicCliExplainCrossLink(
                    $"#/diagnostics/items/{index}",
                    "explainedBy",
                    target,
                    $"{diagnostics[index].Id} maps to related symbolic evidence."));
        }

        return links;
    }

    private static Dictionary<string, object?> CreateSarifResult(
        string ruleId,
        string level,
        string message,
        string? filePath,
        int? startLine,
        int? startColumn,
        int? endLine,
        int? endColumn,
        IReadOnlyList<string> crossLinks,
        IReadOnlyDictionary<string, object?>? extraProperties = null)
    {
        var properties = new Dictionary<string, object?>
        {
            ["crossLinks"] = crossLinks
        };
        if (extraProperties != null)
        {
            foreach (var property in extraProperties)
                if (property.Value != null)
                    properties[property.Key] = property.Value;
        }

        var result = new Dictionary<string, object?>
        {
            ["ruleId"] = ruleId,
            ["level"] = level,
            ["message"] = new Dictionary<string, object?> { ["text"] = message },
            ["properties"] = properties
        };
        if (!string.IsNullOrWhiteSpace(filePath) && startLine.HasValue && startColumn.HasValue)
        {
            var region = new Dictionary<string, object?>
            {
                ["startLine"] = Math.Max(1, startLine.Value),
                ["startColumn"] = Math.Max(1, startColumn.Value)
            };
            if (endLine.HasValue) region["endLine"] = Math.Max(1, endLine.Value);
            if (endColumn.HasValue) region["endColumn"] = Math.Max(1, endColumn.Value);
            result["locations"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["physicalLocation"] = new Dictionary<string, object?>
                    {
                        ["artifactLocation"] = new Dictionary<string, object?>
                        {
                            ["uri"] = NormalizeArtifactUri(filePath)
                        },
                        ["region"] = region
                    }
                }
            };
        }

        return result;
    }

    private static string ToSarifLevel(string severity)
    {
        return severity.ToLowerInvariant() switch
        {
            "error" => "error",
            "warning" => "warning",
            "info" => "note",
            _ => "none"
        };
    }

    private static string NormalizeArtifactUri(string path)
    {
        if (Path.IsPathRooted(path)) return new Uri(CliHost.GetFullPath(path)).AbsoluteUri;

        return path.Replace('\\', '/');
    }

    private static string ToKebabCase(string value)
    {
        if (value.Length == 0) return value;

        var builder = new StringBuilder(value.Length + 8);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (index != 0 && char.IsUpper(character) && !char.IsUpper(value[index - 1])) builder.Append('-');
            builder.Append(character);
        }

        return builder.ToString();
    }

    private static string DescribeRule(string ruleId)
    {
        if (ruleId.StartsWith("SPQ-HZ-", StringComparison.Ordinal)) return "SharpProof runtime hazard";
        if (ruleId.StartsWith("SPQ-", StringComparison.Ordinal)) return "SharpProof explain report status";
        return "SharpProof analyzer diagnostic";
    }

    private static string EscapeInline(string value)
    {
        return (value ?? string.Empty)
            .Replace("`", "\\`", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
    }

    private static string EscapeCell(string value)
    {
        return EscapeInline(value).Replace("|", "\\|", StringComparison.Ordinal);
    }
}

internal sealed record SymbolicCliExplainSource(
    string FilePath,
    string Kind,
    SymbolicSourceMap? SourceMap);

internal sealed record SymbolicCliExplainTarget(
    string Kind,
    int? RequestedLine,
    int? RequestedColumn,
    int? RequestedPosition,
    int ResolvedLine,
    int ResolvedColumn,
    int ResolvedPosition,
    int ResolvedSpanStart,
    int ResolvedSpanEnd,
    int ResolvedEndLine,
    int ResolvedEndColumn,
    string NodeKind,
    string? MethodName,
    string ProgramPointKind)
{
    public string DisplayText => RequestedPosition.HasValue
        ? $"position {RequestedPosition.Value}"
        : $"line {RequestedLine}, column {RequestedColumn}";

    public static SymbolicCliExplainTarget FromPoint(
        SymbolicCliOptions options,
        SymbolicProgramPointResult point)
    {
        return new SymbolicCliExplainTarget(
            options.Position.HasValue ? "position" : "point",
            options.Position.HasValue ? null : options.Line,
            options.Position.HasValue ? null : options.Column,
            options.Position,
            point.Line,
            point.Column,
            point.Position,
            point.NodeSpanStart,
            point.NodeSpanEnd,
            point.NodeEndLine,
            point.NodeEndColumn,
            point.NodeKind,
            point.MethodName,
            point.ProgramPointKind);
    }
}

internal sealed class SymbolicCliExplainProject
{
    private SymbolicCliExplainProject(
        string name,
        string? projectFilePath,
        string? solutionFilePath,
        bool hasBaseline,
        int effectSummaryFileCount,
        int analyzerConfigCount,
        IReadOnlyList<string> analyzerConfigPaths,
        int additionalFileCount,
        IReadOnlyList<string> additionalFilePaths,
        int workspaceDiagnosticCount,
        IReadOnlyList<string> workspaceDiagnostics,
        int configurationIssueCount,
        IReadOnlyList<SharpProofProjectConfigurationIssue> configurationIssues,
        SymbolicCliExplainProjectTruncation truncation)
    {
        Name = name;
        ProjectFilePath = projectFilePath;
        SolutionFilePath = solutionFilePath;
        HasBaseline = hasBaseline;
        EffectSummaryFileCount = effectSummaryFileCount;
        AnalyzerConfigCount = analyzerConfigCount;
        AnalyzerConfigPaths = analyzerConfigPaths;
        AdditionalFileCount = additionalFileCount;
        AdditionalFilePaths = additionalFilePaths;
        WorkspaceDiagnosticCount = workspaceDiagnosticCount;
        WorkspaceDiagnostics = workspaceDiagnostics;
        ConfigurationIssueCount = configurationIssueCount;
        ConfigurationIssues = configurationIssues;
        Truncation = truncation;
    }

    public string Name { get; }

    public string? ProjectFilePath { get; }

    public string? SolutionFilePath { get; }

    public bool HasBaseline { get; }

    public int EffectSummaryFileCount { get; }

    public int AnalyzerConfigCount { get; }

    public IReadOnlyList<string> AnalyzerConfigPaths { get; }

    public int AdditionalFileCount { get; }

    public IReadOnlyList<string> AdditionalFilePaths { get; }

    public int WorkspaceDiagnosticCount { get; }

    public IReadOnlyList<string> WorkspaceDiagnostics { get; }

    public int ConfigurationIssueCount { get; }

    public IReadOnlyList<SharpProofProjectConfigurationIssue> ConfigurationIssues { get; }

    public SymbolicCliExplainProjectTruncation Truncation { get; }

    public static SymbolicCliExplainProject? FromContext(
        SymbolicCliInputContext inputContext,
        int limit)
    {
        var context = inputContext.ProjectContext;
        if (context == null) return null;

        var analyzerConfigPaths = SymbolicCompactProjection.Project(context.AnalyzerConfigPaths, limit);
        var additionalFilePaths = SymbolicCompactProjection.Project(context.AdditionalFilePaths, limit);
        var workspaceDiagnostics = SymbolicCompactProjection.Project(inputContext.WorkspaceDiagnostics, limit);
        var configurationIssues = SymbolicCompactProjection.Project(context.ConfigurationIssues, limit);
        return new SymbolicCliExplainProject(
            context.ProjectName,
            context.ProjectFilePath,
            context.SolutionFilePath,
            context.HasBaseline,
            context.EffectSummaryFileCount,
            analyzerConfigPaths.TotalCount,
            analyzerConfigPaths.Items,
            additionalFilePaths.TotalCount,
            additionalFilePaths.Items,
            workspaceDiagnostics.TotalCount,
            workspaceDiagnostics.Items,
            configurationIssues.TotalCount,
            configurationIssues.Items,
            new SymbolicCliExplainProjectTruncation(
                analyzerConfigPaths.IsTruncated,
                additionalFilePaths.IsTruncated,
                workspaceDiagnostics.IsTruncated,
                configurationIssues.IsTruncated));
    }
}

internal sealed record SymbolicCliExplainProjectTruncation(
    bool AnalyzerConfigPaths,
    bool AdditionalFilePaths,
    bool WorkspaceDiagnostics,
    bool ConfigurationIssues)
{
    public bool IsTruncated =>
        AnalyzerConfigPaths || AdditionalFilePaths || WorkspaceDiagnostics || ConfigurationIssues;
}

internal abstract class SymbolicCliExplainMethodTargetResult
{
    private readonly SymbolicMethodResult _target;

    protected SymbolicCliExplainMethodTargetResult(SymbolicMethodResult target)
    {
        _target = target;
    }

    public string FilePath => _target.FilePath;

    public string MethodDisplayName => _target.MethodDisplayName;

    public string DeclarationKind => _target.DeclarationKind;

    public int SpanStart => _target.SpanStart;

    public int SpanEnd => _target.SpanEnd;

    public int StartLine => _target.StartLine;

    public int StartColumn => _target.StartColumn;

    public int EndLine => _target.EndLine;

    public int EndColumn => _target.EndColumn;
}

internal sealed class SymbolicCliExplainCapabilityResult : SymbolicCliExplainMethodTargetResult
{
    private SymbolicCliExplainCapabilityResult(
        SymbolicCapabilityResult result,
        IReadOnlyList<SymbolicCapabilityUnknownReason> unknownReasons,
        IReadOnlyList<SymbolicUnknownReasonInfo> unknownReasonDetails,
        IReadOnlyList<SymbolicCapabilitySite> sites,
        SymbolicCliExplainCapabilityTruncation truncation)
        : base(result)
    {
        Capabilities = result.Capabilities;
        CapabilityText = result.CapabilityText;
        HasUnknowns = result.HasUnknowns;
        UnknownReasonCount = result.UnknownReasons.Count;
        UnknownReasons = unknownReasons;
        UnknownReasonDetails = unknownReasonDetails;
        SiteCount = result.Sites.Count;
        Sites = sites;
        Truncation = truncation;
    }

    public string Kind => "capabilities";

    public SymbolicCapability Capabilities { get; }

    public string CapabilityText { get; }

    public bool HasUnknowns { get; }

    public int UnknownReasonCount { get; }

    public IReadOnlyList<SymbolicCapabilityUnknownReason> UnknownReasons { get; }

    public IReadOnlyList<SymbolicUnknownReasonInfo> UnknownReasonDetails { get; }

    public int SiteCount { get; }

    public IReadOnlyList<SymbolicCapabilitySite> Sites { get; }

    public SymbolicCliExplainCapabilityTruncation Truncation { get; }

    public static SymbolicCliExplainCapabilityResult FromResult(SymbolicCapabilityResult result, int limit)
    {
        var unknownReasons = SymbolicCompactProjection.Project(result.UnknownReasons, limit);
        var unknownReasonDetails = SymbolicCompactProjection.Project(result.UnknownReasonDetails, limit);
        var sites = SymbolicCompactProjection.Project(result.Sites, limit);
        return new SymbolicCliExplainCapabilityResult(
            result,
            unknownReasons.Items,
            unknownReasonDetails.Items,
            sites.Items,
            new SymbolicCliExplainCapabilityTruncation(
                unknownReasons.IsTruncated,
                sites.IsTruncated));
    }
}

internal sealed record SymbolicCliExplainCapabilityTruncation(bool UnknownReasons, bool Sites)
{
    public bool IsTruncated => UnknownReasons || Sites;
}

internal sealed class SymbolicCliExplainComplexityResult : SymbolicCliExplainMethodTargetResult
{
    private SymbolicCliExplainComplexityResult(
        SymbolicComplexityResult result,
        IReadOnlyList<SymbolicComplexityDriverInfo> drivers,
        IReadOnlyList<SymbolicComplexityUnknownReason> unknownReasons,
        IReadOnlyList<SymbolicUnknownReasonInfo> unknownReasonDetails,
        IReadOnlyList<SymbolicComplexityCalleeInfo> calleeSummaries,
        SymbolicCliExplainComplexityTruncation truncation)
        : base(result)
    {
        Complexity = result.Complexity;
        DriverCount = result.Drivers.Count;
        Drivers = drivers;
        UnknownReasonCount = result.UnknownReasons.Count;
        UnknownReasons = unknownReasons;
        UnknownReasonDetails = unknownReasonDetails;
        CalleeSummaryCount = result.CalleeSummaries.Count;
        CalleeSummaries = calleeSummaries;
        Truncation = truncation;
    }

    public string Kind => "complexity";

    public SymbolicComplexityInfo Complexity { get; }

    public int DriverCount { get; }

    public IReadOnlyList<SymbolicComplexityDriverInfo> Drivers { get; }

    public int UnknownReasonCount { get; }

    public IReadOnlyList<SymbolicComplexityUnknownReason> UnknownReasons { get; }

    public IReadOnlyList<SymbolicUnknownReasonInfo> UnknownReasonDetails { get; }

    public int CalleeSummaryCount { get; }

    public IReadOnlyList<SymbolicComplexityCalleeInfo> CalleeSummaries { get; }

    public SymbolicCliExplainComplexityTruncation Truncation { get; }

    public static SymbolicCliExplainComplexityResult FromResult(SymbolicComplexityResult result, int limit)
    {
        var drivers = SymbolicCompactProjection.Project(result.Drivers, limit);
        var unknownReasons = SymbolicCompactProjection.Project(result.UnknownReasons, limit);
        var unknownReasonDetails = SymbolicCompactProjection.Project(result.UnknownReasonDetails, limit);
        var calleeSummaries = SymbolicCompactProjection.Project(result.CalleeSummaries, limit);
        return new SymbolicCliExplainComplexityResult(
            result,
            drivers.Items,
            unknownReasons.Items,
            unknownReasonDetails.Items,
            calleeSummaries.Items,
            new SymbolicCliExplainComplexityTruncation(
                drivers.IsTruncated,
                unknownReasons.IsTruncated,
                calleeSummaries.IsTruncated));
    }
}

internal sealed record SymbolicCliExplainComplexityTruncation(
    bool Drivers,
    bool UnknownReasons,
    bool CalleeSummaries)
{
    public bool IsTruncated => Drivers || UnknownReasons || CalleeSummaries;
}

internal sealed record SymbolicCliExplainDiagnosticResult(
    int TotalCount,
    int TargetCount,
    IReadOnlyList<SymbolicCliExplainDiagnostic> Items,
    bool Truncated)
{
    public static readonly SymbolicCliExplainDiagnosticResult Empty = new(
        0,
        0,
        Array.Empty<SymbolicCliExplainDiagnostic>(),
        false);
}

internal sealed record SymbolicCliExplainDiagnostic(
    string Id,
    string Severity,
    string Message,
    string? HelpLinkUri,
    bool IsTarget,
    string? FilePath,
    int? StartLine,
    int? StartColumn,
    int? EndLine,
    int? EndColumn,
    int? SpanStart,
    int? SpanLength,
    IReadOnlyList<string> CrossLinks)
{
    public static SymbolicCliExplainDiagnostic FromDiagnostic(Diagnostic diagnostic, bool isTarget)
    {
        var location = diagnostic.Location;
        var hasLocation = location != Location.None && location.IsInSource;
        var lineSpan = hasLocation ? location.GetLineSpan() : default;
        return new SymbolicCliExplainDiagnostic(
            diagnostic.Id,
            diagnostic.Severity.ToString(),
            diagnostic.GetMessage(CultureInfo.InvariantCulture),
            string.IsNullOrWhiteSpace(diagnostic.Descriptor.HelpLinkUri)
                ? null
                : diagnostic.Descriptor.HelpLinkUri,
            isTarget,
            hasLocation ? lineSpan.Path : null,
            hasLocation ? lineSpan.StartLinePosition.Line + 1 : null,
            hasLocation ? lineSpan.StartLinePosition.Character + 1 : null,
            hasLocation ? lineSpan.EndLinePosition.Line + 1 : null,
            hasLocation ? lineSpan.EndLinePosition.Character + 1 : null,
            hasLocation ? location.SourceSpan.Start : null,
            hasLocation ? location.SourceSpan.Length : null,
            MapDiagnosticCrossLinks(diagnostic.Id));
    }

    private static IReadOnlyList<string> MapDiagnosticCrossLinks(string diagnosticId)
    {
        return diagnosticId switch
        {
            "SP0015" or "SP0016" or "SP0017" => new[] { "#/capabilities" },
            "SP0021" or "SP0022" or "SP0023" => new[] { "#/complexity" },
            "SP0010" or "SP0030" or "SP0031" or "SP0033" => new[] { "#/runtimeHazards" },
            "SP0002" => new[] { "#/invariant", "#/capabilities" },
            _ => new[] { "#/invariant" }
        };
    }
}

internal sealed record SymbolicCliExplainCrossLink(
    string From,
    string Relation,
    string To,
    string Description);

internal sealed record SymbolicCliExplainTruncation(
    bool InvariantOutput,
    bool RuntimeHazardOutput,
    bool CapabilityOutput,
    bool ComplexityOutput,
    bool DiagnosticOutput,
    bool ProjectOutput,
    bool Analysis)
{
    public bool IsTruncated =>
        InvariantOutput ||
        RuntimeHazardOutput ||
        CapabilityOutput ||
        ComplexityOutput ||
        DiagnosticOutput ||
        ProjectOutput ||
        Analysis;
}

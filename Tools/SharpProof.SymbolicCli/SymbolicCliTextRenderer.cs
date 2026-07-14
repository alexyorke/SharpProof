internal static class SymbolicCliTextRenderer
{
    internal static void PrintFileResult(
    SymbolicFileQueryResult result,
    SymbolicCliOptions options)
{
    Console.WriteLine($"{result.FilePath}");
    Console.WriteLine($"Total lines: {result.LineCount}");
    Console.WriteLine($"Lines with program points: {result.LinesWithProgramPoints}");
    Console.WriteLine($"Program points: {result.ProgramPointCount}");
    PrintAnalysisTruncation(result.AnalysisTruncation);
    PrintProgramPointSummary(result.ProgramPointSummary, options);
    Console.WriteLine($"Merged invariant: {result.MergedInvariantText}");
    Console.WriteLine($"Merged invariant merge: {result.InvariantInfo.MergeKind}");
    Console.WriteLine($"Merged invariant conditions: {result.InvariantInfo.ConditionCount}");
    PrintInvariantQuery("Merged invariant query", result.InvariantQuery, options);
    PrintMergedPathFacts("Merged path facts", result.MergedPathFacts);
    Console.WriteLine($"Observed distinct facts: {result.ObservedFactCount}");
    Console.WriteLine($"Observed invariant merge: {result.ObservedInvariant.MergeKind}");
    Console.WriteLine($"Observed invariant conditions: {result.ObservedInvariant.ConditionCount}");
    if (options.CheckReachability)
        Console.WriteLine(
            "Reachability summary: " +
            $"Reachable={result.Reachability.ReachableCount}, " +
            $"Unreachable={result.Reachability.UnreachableCount}, " +
            $"Unknown={result.Reachability.UnknownCount}, " +
            $"NotChecked={result.Reachability.NotCheckedCount}");

    PrintConditionProofSummaries(FilterConditionProofSummaries(result.ConditionProofs, options));

    foreach (var lineResult in result.Lines)
    {
        Console.WriteLine();
        PrintLineResult(lineResult, options);
    }

    if (result.SmtDiagnostics.IsConfigured && result.Lines.Count == 0) PrintSmtDiagnostics(result.SmtDiagnostics);
}

    internal static void PrintLineResult(SymbolicLineQueryResult result, SymbolicCliOptions options)
{
    Console.WriteLine($"{result.FilePath}:{result.Line}");
    PrintScopedResult(result, "Line", options);
}

    internal static void PrintSpanResult(SymbolicSpanQueryResult result, SymbolicCliOptions options)
{
    Console.WriteLine($"{result.FilePath}:{result.SpanStart}-{result.SpanEnd}");
    Console.WriteLine($"Span lines: {result.StartLine}:{result.StartColumn}-{result.EndLine}:{result.EndColumn}");
    PrintScopedResult(result, "Span", options);
}

    private static void PrintScopedResult(
        SymbolicScopedQueryAggregate result,
        string scopeLabel,
        SymbolicCliOptions options)
{
    Console.WriteLine($"Program points: {result.ProgramPoints.Count}");
    PrintAnalysisTruncation(result.AnalysisTruncation);
    PrintProgramPointSummary(result.ProgramPointSummary, options);
    Console.WriteLine($"Observed distinct facts: {result.ObservedFactCount}");
    Console.WriteLine($"{scopeLabel} merged invariant: {result.MergedInvariantText}");
    Console.WriteLine($"{scopeLabel} invariant merge: {result.InvariantInfo.MergeKind}");
    Console.WriteLine($"{scopeLabel} invariant conditions: {result.InvariantInfo.ConditionCount}");
    PrintInvariantQuery(scopeLabel + " invariant query", result.InvariantQuery, options);
    PrintMergedPathFacts(scopeLabel + " merged path facts", result.MergedPathFacts);
    PrintConditionProofSummaries(FilterConditionProofSummaries(result.ConditionProofs, options));
    foreach (var point in result.ProgramPoints)
    {
        Console.WriteLine();
        PrintPointResult(point, options, true);
    }

    if (result.SmtDiagnostics.IsConfigured && result.ProgramPoints.Count == 0)
        PrintSmtDiagnostics(result.SmtDiagnostics);
}

    internal static void PrintRuntimeHazardResult(SymbolicRuntimeHazardQueryResult result)
{
    Console.WriteLine($"{result.FilePath}");
    if (result.Line.HasValue)
        Console.WriteLine($"Line: {result.Line.Value}");
    else if (result.ScopeStart.HasValue && result.ScopeEnd.HasValue)
        Console.WriteLine($"Span: {result.ScopeStart.Value}-{result.ScopeEnd.Value}");
    else
        Console.WriteLine($"Total lines: {result.LineCount}");

    Console.WriteLine($"Runtime hazards: {result.HazardCount}");
    PrintAnalysisTruncation(result.AnalysisTruncation);
    Console.WriteLine("Hazard status summary: " +
                      FormatCountSummary(CountBy(result.Hazards, static hazard => hazard.Status.ToString())));
    Console.WriteLine("Hazard exception summary: " +
                      FormatCountSummary(CountBy(result.Hazards, static hazard => hazard.ExceptionType)));
    Console.WriteLine("Hazard category summary: " +
                      FormatCountSummary(CountBy(result.Hazards, static hazard => hazard.Category)));
    foreach (var hazard in result.Hazards)
    {
        Console.WriteLine();
        Console.WriteLine($"{hazard.FilePath}:{hazard.Line}:{hazard.Column} {hazard.Kind} {hazard.Status}");
        Console.WriteLine($"Exception: {hazard.ExceptionType}");
        Console.WriteLine($"Category: {hazard.Category}");
        Console.WriteLine($"Reason: {hazard.GetDisplayStatusReason()}");
        Console.WriteLine($"Node: {hazard.NodeKind} {hazard.SpanStart}-{hazard.SpanEnd}");
        Console.WriteLine($"Operation: {hazard.OperationText}");
        Console.WriteLine($"Trigger: {hazard.TriggerCondition}");
        Console.WriteLine($"Invariant: {hazard.MergedInvariantText}");
    }

    if (result.SmtDiagnostics.IsConfigured) PrintSmtDiagnostics(result.SmtDiagnostics);
}

    internal static async Task PrintExplainResultAsync(
    SymbolicCliOptions options,
    SymbolicCliInputContext inputContext,
    SmtAnalysisService smtAnalysis)
{
    var service = new SymbolicQueryService();
    var source = inputContext.SourceInput;
    var queryOptions = options.CreateQueryOptions(smtAnalysis, false);
    var pointTarget = options.Position.HasValue
        ? SymbolicQueryTarget.Position(options.Position.Value)
        : SymbolicQueryTarget.Point(options.Line, options.Column);

    Console.WriteLine("SharpProof explanation");
    Console.WriteLine($"File: {source.FilePath}");
    Console.WriteLine($"Source input: {source.Kind}");
    if (source.SourceMap is { } sourceMap)
    {
        Console.WriteLine($"Source map URI: {sourceMap.SourceUri}");
        Console.WriteLine(
            $"Source map origin: line {sourceMap.OriginalStartLine}, column {sourceMap.OriginalStartColumn}");
    }
    Console.WriteLine(options.Position.HasValue
        ? $"Target: position {options.Position.Value}"
        : $"Target: line {options.Line}, column {options.Column}");

    if (inputContext.ProjectContext is { } projectContext)
    {
        Console.WriteLine($"Project: {projectContext.ProjectName}");
        Console.WriteLine($"Project file: {projectContext.ProjectFilePath}");
        if (projectContext.SolutionFilePath != null)
            Console.WriteLine($"Solution file: {projectContext.SolutionFilePath}");
        Console.WriteLine($"Analyzer config files: {projectContext.AnalyzerConfigPaths.Count}");
        Console.WriteLine($"Additional files: {projectContext.AdditionalFilePaths.Count}");
        Console.WriteLine($"Baseline loaded: {projectContext.HasBaseline}");
        Console.WriteLine($"Effect summaries: {projectContext.EffectSummaryFileCount}");
        Console.WriteLine($"Workspace diagnostics: {inputContext.WorkspaceDiagnostics.Length}");
        var workspaceDiagnostics = SymbolicCompactProjection.Project(
            inputContext.WorkspaceDiagnostics,
            options.ReportMaxDiagnostics);
        foreach (var diagnostic in workspaceDiagnostics.Items)
            Console.WriteLine("  - " + diagnostic);

        await PrintProjectAnalyzerDiagnosticsAsync(options, projectContext);
    }

    var pointResult = SymbolicCliQueryResultAdapter.ToLegacyResult(
        service.Query(new SymbolicQueryContext(source, pointTarget, queryOptions)));
    if (pointResult is SymbolicProgramPointResult point)
    {
        Console.WriteLine();
        Console.WriteLine("Invariant proof");
        Console.WriteLine($"Node: {point.NodeKind}");
        Console.WriteLine($"Method: {point.MethodName ?? "<unknown>"}");
        Console.WriteLine($"Program point: {point.ProgramPointKind}");
        Console.WriteLine($"Merged invariant: {point.MergedInvariantText}");
        Console.WriteLine($"Reachability: {point.Reachability}");
        Console.WriteLine($"Reachability reason: {point.ReachabilityReason}");
        PrintAnalysisTruncation(point.AnalysisTruncation);
        Console.WriteLine(
            "Proof outcomes: " +
            $"Total={point.ProofOutcomes.TotalCount}, " +
            $"ProvenTrue={point.ProofOutcomes.ProvenTrueCount}, " +
            $"ProvenFalse={point.ProofOutcomes.ProvenFalseCount}, " +
            $"Unreachable={point.ProofOutcomes.UnreachableCount}, " +
            $"Unknown={point.ProofOutcomes.UnknownCount}");
        foreach (var proof in point.ConditionProofs)
        {
            Console.WriteLine(
                $"Implies '{proof.Condition}' target={FormatProofTarget(proof.Target)} " +
                $"kind={proof.Proof.DisplayKind}: {proof.TruthValue}");
            Console.WriteLine($"Implication reason: {proof.GetDisplayReason()}");
        }
    }
    else
    {
        Console.WriteLine();
        Console.WriteLine("Invariant proof");
        Console.WriteLine($"Result kind: {pointResult.GetType().Name}");
    }

    if (pointResult is SymbolicProgramPointResult hazardPoint)
    {
        var hazards = service.QueryRuntimeHazards(
            new SymbolicQueryContext(
                source,
                SymbolicQueryTarget.Point(hazardPoint.Line, hazardPoint.Column),
                queryOptions),
            options.CreateRuntimeHazardOptions());
        Console.WriteLine();
        Console.WriteLine("Runtime hazards");
        Console.WriteLine($"Count: {hazards.HazardCount}");
        PrintAnalysisTruncation(hazards.AnalysisTruncation);
        Console.WriteLine("Status summary: " +
                          FormatCountSummary(CountBy(hazards.Hazards, static hazard => hazard.Status.ToString())));
        var hazardProjection = SymbolicCompactProjection.Project(
            hazards.Hazards,
            options.ReportMaxHazards);
        foreach (var hazard in hazardProjection.Items)
            Console.WriteLine(
                $"  - {hazard.Kind} {hazard.Status} at {hazard.Line}:{hazard.Column}: " +
                $"{hazard.OperationText} ({hazard.GetDisplayStatusReason()})");
    }

    PrintExplainCapabilitySummary(service.QueryCapabilities(
        new SymbolicQueryContext(source, pointTarget, queryOptions)), options.ReportMaxItems);
    PrintExplainComplexitySummary(service.QueryComplexity(
        new SymbolicQueryContext(source, pointTarget, queryOptions)), options.ReportMaxItems);

    if (pointResult is SymbolicProgramPointResult finalPoint && finalPoint.SmtDiagnostics.IsConfigured)
    {
        Console.WriteLine();
        PrintSmtDiagnostics(finalPoint.SmtDiagnostics);
    }
}

    private static async Task PrintProjectAnalyzerDiagnosticsAsync(
    SymbolicCliOptions options,
    SharpProofProjectAnalysisContext context)
{
    var diagnostics = await context.GetAnalyzerDiagnosticsAsync(CancellationToken.None);
    var relevant = SymbolicCliDiagnosticSelector.SelectRelevant(
        diagnostics,
        context.SyntaxTree,
        options.Position,
        options.Line);

    Console.WriteLine();
    Console.WriteLine("Build diagnostics");
    Console.WriteLine($"File/project diagnostics: {relevant.Length}");
    Console.WriteLine($"Target diagnostics: {relevant.Count(static item => item.IsTarget)}");
    var projection = SymbolicCompactProjection.Project(relevant, options.ReportMaxDiagnostics);
    foreach (var item in projection.Items)
    {
        var location = item.Diagnostic.Location == Location.None
            ? "project"
            : FormatDiagnosticLocation(item.Diagnostic.Location);
        var targetMarker = item.IsTarget ? " target" : string.Empty;
        Console.WriteLine(
            $"  - {item.Diagnostic.Id} {item.Diagnostic.Severity} {location}{targetMarker}: " +
            item.Diagnostic.GetMessage(CultureInfo.InvariantCulture));
    }
}

    private static string FormatDiagnosticLocation(Location location)
{
    var lineSpan = location.GetLineSpan();
    return $"{lineSpan.Path}:{lineSpan.StartLinePosition.Line + 1}:{lineSpan.StartLinePosition.Character + 1}";
}

    private static void PrintExplainCapabilitySummary(SymbolicCapabilityResult result, int maxItems)
{
    Console.WriteLine();
    Console.WriteLine("Capabilities");
    Console.WriteLine($"Method: {result.MethodDisplayName}");
    Console.WriteLine($"Capabilities: {result.CapabilityText}");
    Console.WriteLine($"Conservative: {result.IsConservative}");
    if (result.UnknownReasons.Count != 0)
        Console.WriteLine("Unknown reasons: " + string.Join(", ", result.UnknownReasons));

    var siteProjection = SymbolicCompactProjection.Project(result.Sites, maxItems);
    foreach (var site in siteProjection.Items)
    {
        var prefix = site.IsUnknown ? "Unknown" : site.CapabilityText;
        var detail = string.IsNullOrWhiteSpace(site.SymbolDisplayName)
            ? site.OperationKind
            : site.SymbolDisplayName;
        Console.WriteLine($"  - {prefix} via {detail} @ {site.SourceLine}:{site.SourceColumn}");
    }
}

    private static void PrintExplainComplexitySummary(SymbolicComplexityResult result, int maxItems)
{
    Console.WriteLine();
    Console.WriteLine("Complexity");
    PrintComplexityDetails(result, maxItems, true, true);
}

    private static void PrintComplexityDetails(
    SymbolicComplexityResult result,
    int? maxDrivers,
    bool useInlineLists,
    bool includeMethod)
{
    if (includeMethod) Console.WriteLine($"Method: {result.MethodDisplayName}");
    Console.WriteLine($"Complexity: {result.Complexity.Text}");
    Console.WriteLine($"Kind: {result.Complexity.Kind}");
    Console.WriteLine($"Conservative: {result.Complexity.IsConservative}");
    if (result.UnknownReasons.Count != 0)
    {
        Console.WriteLine("Unknown reasons:" + (useInlineLists ? " " + string.Join(", ", result.UnknownReasons) : string.Empty));
        if (!useInlineLists)
            foreach (var reason in result.UnknownReasons)
                Console.WriteLine($"  - {reason}");
    }

    var drivers = maxDrivers.HasValue
        ? SymbolicCompactProjection.Project(result.Drivers, maxDrivers.Value).Items
        : result.Drivers;
    if (drivers.Count != 0)
    {
        if (!useInlineLists) Console.WriteLine("Drivers:");
        foreach (var driver in drivers)
            Console.WriteLine(
                $"  - [{driver.Kind}] {driver.Description} @ {driver.SourceLine}:{driver.SourceColumn}");
    }
}

    internal static void PrintComplexityResult(SymbolicComplexityResult result)
{
    Console.WriteLine(result.FilePath);
    Console.WriteLine($"Method: {result.MethodDisplayName}");
    Console.WriteLine($"Declaration kind: {result.DeclarationKind}");
    Console.WriteLine($"Span: {result.StartLine}:{result.StartColumn}-{result.EndLine}:{result.EndColumn}");
    PrintComplexityDetails(result, null, false, false);

    if (result.CalleeSummaries.Count != 0)
    {
        Console.WriteLine("Callee summaries:");
        foreach (var callee in result.CalleeSummaries)
            Console.WriteLine(
                $"  - {callee.MethodDisplayName}: {callee.ComplexityText} ({callee.Kind})");
    }
}

    internal static void PrintCapabilityResult(SymbolicCapabilityResult result)
{
    Console.WriteLine(result.FilePath);
    Console.WriteLine($"Method: {result.MethodDisplayName}");
    Console.WriteLine($"Declaration kind: {result.DeclarationKind}");
    Console.WriteLine($"Span: {result.StartLine}:{result.StartColumn}-{result.EndLine}:{result.EndColumn}");
    Console.WriteLine($"Capabilities: {result.CapabilityText}");
    Console.WriteLine($"Conservative: {result.IsConservative}");

    if (result.UnknownReasons.Count != 0)
    {
        Console.WriteLine("Unknown reasons:");
        foreach (var reason in result.UnknownReasons) Console.WriteLine($"  - {reason}");
    }

    if (result.Sites.Count != 0)
    {
        Console.WriteLine("Sites:");
        foreach (var site in result.Sites)
        {
            var prefix = site.IsUnknown ? "Unknown" : site.CapabilityText;
            var detail = string.IsNullOrWhiteSpace(site.SymbolDisplayName)
                ? site.OperationKind
                : site.SymbolDisplayName;
            var transitive = site.IsTransitive ? " transitive" : string.Empty;
            Console.WriteLine(
                $"  - [{site.SiteKind}] {prefix} via {detail} @ {site.SourceLine}:{site.SourceColumn}{transitive}");
        }
    }
}

    private static string FormatCountSummary(IReadOnlyDictionary<string, int> counts)
{
    return counts.Count == 0
        ? "<none>"
        : string.Join(", ", counts.Select(static pair => $"{pair.Key}={pair.Value}"));
}

    private static IReadOnlyDictionary<string, int> CountBy<T>(
    IEnumerable<T> values,
    Func<T, string> keySelector)
{
    return values
        .GroupBy(keySelector, StringComparer.Ordinal)
        .OrderBy(static group => group.Key, StringComparer.Ordinal)
        .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);
}

    private static void PrintMergedPathFacts(
    string label,
    SymbolicMergedPathFacts facts)
{
    Console.WriteLine(
        $"{label}: " +
        $"Always={facts.AlwaysFacts.Count}, " +
        $"Maybe={facts.MaybeFacts.Count}, " +
        $"Unknown={facts.ConservativeUnknownCount}, " +
        $"CandidatePoints={facts.CandidateProgramPointCount}, " +
        $"UnreachablePoints={facts.UnreachableProgramPointCount}");
    if (facts.ConservativeUnknownCount != 0)
    {
        Console.WriteLine(label + " unknowns: " + string.Join("; ", facts.ConservativeUnknowns));
        foreach (var diagnostic in facts.ConservativeUnknownDiagnostics)
            Console.WriteLine(
                label + " unknown diagnostic: " +
                $"{diagnostic.UnknownText} target={diagnostic.Target} reason={diagnostic.GetDisplayReason()} " +
                $"maybe={string.Join("; ", diagnostic.MaybeFacts)}");
    }
}

    private static void PrintInvariantQuery(
    string label,
    SymbolicInvariantQueryView query,
    SymbolicCliOptions options)
{
    var targetSummaries = FilterInvariantTargets(
        query.TargetSummaries,
        options,
        static target => target.Target);
    var targetPathSummaries = FilterInvariantTargets(
        query.TargetPathSummaries,
        options,
        static target => target.Target);
    var mustFacts = SelectInvariantFacts(
        query.MustFacts,
        targetSummaries,
        options,
        static target => target.MustFacts);
    var maybeFacts = SelectInvariantFacts(
        query.MaybeFacts,
        targetSummaries,
        options,
        static target => target.MaybeFacts);
    var unknownFacts = SelectInvariantFacts(
        query.UnknownFacts,
        targetSummaries,
        options,
        static target => target.UnknownFacts);
    var queryText = options.HasInvariantTargetFilter
        ? SymbolicInvariantService.FormatMergedInvariantFacts(mustFacts.Concat(unknownFacts).ToArray())
        : query.Text;

    Console.WriteLine(
        $"{label}: " +
        $"Must={mustFacts.Count}, " +
        $"Maybe={maybeFacts.Count}, " +
        $"Unknown={unknownFacts.Count}, " +
        $"CandidatePoints={query.CandidateProgramPointCount}, " +
        $"UnreachablePoints={query.UnreachableProgramPointCount}");
    Console.WriteLine(label + " text: " + queryText);
    Console.WriteLine(label + " status: " + query.Status);
    Console.WriteLine(label + " status reason: " + query.StatusReason);
    Console.WriteLine(label + " summary: " + query.Summary);
    if (mustFacts.Count != 0) Console.WriteLine(label + " must facts: " + string.Join("; ", mustFacts));

    if (maybeFacts.Count != 0) Console.WriteLine(label + " maybe facts: " + string.Join("; ", maybeFacts));

    if (unknownFacts.Count != 0) Console.WriteLine(label + " unknowns: " + string.Join("; ", unknownFacts));

    if (options.HasInvariantTargetFilter)
    {
        var matchedTargetFilters = GetMatchedInvariantTargetFilters(options, targetSummaries, targetPathSummaries);
        var unmatchedTargetFilters = GetUnmatchedInvariantTargetFilters(options, matchedTargetFilters);
        Console.WriteLine(label + " target filter: " + string.Join(", ", options.InvariantTargets));
        Console.WriteLine(
            label + " target filter matched: " +
            (matchedTargetFilters.Count != 0));
        PrintInvariantTargetFilterList(label, "matched target filters", matchedTargetFilters);
        PrintInvariantTargetFilterList(label, "unmatched target filters", unmatchedTargetFilters);
    }

    PrintInvariantTargetSummaries(label, targetSummaries);
    PrintInvariantTargetPathSummaries(label, targetPathSummaries);

    foreach (var diagnostic in query.Diagnostics)
    {
        Console.WriteLine(
            label + " diagnostic: " +
            $"{diagnostic.Code} {diagnostic.Severity} count={diagnostic.Count} " +
            diagnostic.Message);
        if (diagnostic.Evidence.Count != 0)
        {
            var suffix = diagnostic.EvidenceTruncated ? " ..." : string.Empty;
            Console.WriteLine(
                label + " diagnostic evidence: " +
                string.Join("; ", diagnostic.Evidence) +
                suffix);
        }
    }
}

    private static IReadOnlyList<TTarget> FilterInvariantTargets<TTarget>(
    IReadOnlyList<TTarget> targets,
    SymbolicCliOptions options,
    Func<TTarget, string> targetSelector)
{
    return SymbolicInvariantTargetFilter.ApplyToTargets(targets, options.InvariantTargets, targetSelector);
}

    private static IReadOnlyList<string> SelectInvariantFacts(
    IReadOnlyList<string> facts,
    IReadOnlyList<SymbolicInvariantTargetSummary> targetSummaries,
    SymbolicCliOptions options,
    Func<SymbolicInvariantTargetSummary, IReadOnlyList<string>> factSelector)
{
    return SymbolicInvariantTargetFilter.SelectFacts(
        facts,
        targetSummaries,
        options.InvariantTargets,
        factSelector);
}

    private static IReadOnlyList<string> GetMatchedInvariantTargetFilters(
    SymbolicCliOptions options,
    IReadOnlyList<SymbolicInvariantTargetSummary> targetSummaries,
    IReadOnlyList<SymbolicInvariantTargetPathSummary> targetPathSummaries)
{
    return SymbolicInvariantTargetFilter.GetMatchedTargetFilters(
        targetSummaries,
        targetPathSummaries,
        options.InvariantTargets);
}

    private static IReadOnlyList<string> GetUnmatchedInvariantTargetFilters(
    SymbolicCliOptions options,
    IReadOnlyList<string> matchedTargetFilters)
{
    return SymbolicInvariantTargetFilter.GetUnmatchedTargetFilters(
        options.InvariantTargets,
        matchedTargetFilters);
}

    private static void PrintInvariantTargetFilterList(
    string label,
    string name,
    IReadOnlyList<string> values)
{
    const int maxTextTargetFilters = 16;
    if (values.Count == 0) return;

    var projection = SymbolicCompactProjection.Project(values, maxTextTargetFilters);
    Console.WriteLine(
        label + " " + name + ": " +
        string.Join(", ", projection.Items) +
        SymbolicCliTruncationText.FormatInlineSuffix(projection));
}

    private static void PrintInvariantTargetSummaries(
    string label,
    IReadOnlyList<SymbolicInvariantTargetSummary> targetSummaries)
{
    const int maxTextTargets = 16;
    var projection = SymbolicCompactProjection.Project(targetSummaries, maxTextTargets);
    foreach (var target in projection.Items)
    {
        Console.WriteLine(
            label + " target: " +
            $"{target.Target} status={target.Status} " +
            $"reason={target.StatusReason} code={target.ReasonCode} " +
            $"must={target.MustFactCount} maybe={target.MaybeFactCount} unknown={target.UnknownFactCount}");
        Console.WriteLine(label + " target summary: " + target.Summary);
    }

    if (projection.IsTruncated)
        Console.WriteLine(SymbolicCliTruncationText.FormatTruncatedLine(
            label + " target summaries",
            projection));
}

    private static void PrintInvariantTargetPathSummaries(
    string label,
    IReadOnlyList<SymbolicInvariantTargetPathSummary> targetPathSummaries)
{
    const int maxTextTargets = 16;
    var projection = SymbolicCompactProjection.Project(targetPathSummaries, maxTextTargets);
    foreach (var target in projection.Items)
    {
        Console.WriteLine(
            label + " target path: " +
            $"{target.Target} conditions={target.PathConditionCount} " +
            $"smt={target.SmtConditionCount} points={target.ProgramPointCount} " +
            $"reachablePoints={target.ReachableProgramPointCount} " +
            $"proofs={target.ProofTotalCount} unknownProofs={target.ProofUnknownCount} " +
            $"reason={target.StatusReason} code={target.ReasonCode}");
        Console.WriteLine(label + " target path summary: " + target.Summary);
        if (target.Conditions.Count != 0)
        {
            var suffix = target.ConditionsTruncated ? " ..." : string.Empty;
            Console.WriteLine(
                label + " target path conditions: " +
                string.Join("; ", target.Conditions) +
                suffix);
        }
    }

    if (projection.IsTruncated)
        Console.WriteLine(SymbolicCliTruncationText.FormatTruncatedLine(
            label + " target path summaries",
            projection));
}

    internal static void PrintPointResult(
    SymbolicProgramPointResult result,
    SymbolicCliOptions options,
    bool includeLocation)
{
    if (includeLocation) Console.WriteLine($"{result.FilePath}:{result.Line}:{result.Column}");

    Console.WriteLine($"Node: {result.NodeKind}");
    Console.WriteLine($"Program point kind: {result.ProgramPointKind}");
    if (result.RequestedPosition.HasValue)
        Console.WriteLine(
            "Requested location: " +
            $"{result.FilePath}:{result.RequestedLine}:{result.RequestedColumn} " +
            $"position={result.RequestedPosition} " +
            $"distance={result.RequestedPositionDistance} " +
            $"contained={result.ContainsRequestedPosition}");

    if (!string.IsNullOrWhiteSpace(result.MethodName)) Console.WriteLine($"Method: {result.MethodName}");

    Console.WriteLine($"Merged invariant: {result.MergedInvariantText}");
    Console.WriteLine($"Invariant merge: {result.Invariant.MergeKind}");
    Console.WriteLine($"Path conditions: {result.PathConditionCount}");
    Console.WriteLine($"Conservative unknown conditions: {result.Invariant.ConservativeUnknownCount}");
    PrintAnalysisTruncation(result.AnalysisTruncation);
    PrintInvariantQuery("Invariant query", result.InvariantQuery, options);
    if (result.Invariant.ConditionCount != 0)
    {
        Console.WriteLine("Invariant conditions:");
        foreach (var condition in result.Invariant.Conditions)
        {
            var unknown = condition.IsConservativeUnknown ? " conservative-unknown" : string.Empty;
            Console.WriteLine(
                $"  [{condition.Index}] {condition.Text} " +
                $"target={condition.Target} kind={condition.DisplayKind}{unknown}");
        }
    }

    if (options.CheckReachability)
    {
        Console.WriteLine($"Reachability: {result.Reachability}");
        Console.WriteLine($"Reachability reason: {result.ReachabilityReason}");
    }

    foreach (var proof in FilterConditionProofResults(result.ConditionProofs, options))
    {
        Console.WriteLine(
            $"Implies '{proof.Condition}' target={FormatProofTarget(proof.Target)} " +
            $"kind={proof.Proof.DisplayKind}: {proof.TruthValue}");
        Console.WriteLine($"Implication formula: {proof.Proof.ConditionText}");
        if (proof.Line.HasValue && proof.Column.HasValue)
            Console.WriteLine(
                "Implication source: " +
                $"{proof.FilePath}:{proof.Line}:{proof.Column} " +
                $"position={proof.Position} " +
                $"node={proof.NodeKind} " +
                $"programPointKind={proof.ProgramPointKind} " +
                $"span={proof.NodeSpanStart}-{proof.NodeSpanEnd}");

        if (proof.RequestedPosition.HasValue)
            Console.WriteLine(
                "Implication requested location: " +
                $"{proof.FilePath}:{proof.RequestedLine}:{proof.RequestedColumn} " +
                $"position={proof.RequestedPosition} " +
                $"distance={proof.RequestedPositionDistance} " +
                $"contained={proof.ContainsRequestedPosition}");

        Console.WriteLine($"Implication reason: {proof.GetDisplayReason()}");
    }

    PrintProofOutcomeSummary(result.ProofOutcomes, "");

    if (result.SmtDiagnostics.IsConfigured) PrintSmtDiagnostics(result.SmtDiagnostics);

    Console.WriteLine("Facts:");
    if (result.Facts.Count == 0)
    {
        Console.WriteLine("  <none>");
        return;
    }

    foreach (var fact in result.Facts) Console.WriteLine("  " + fact);
}

    private static void PrintProgramPointSummary(
    SymbolicProgramPointSummary summary,
    SymbolicCliOptions options)
{
    Console.WriteLine("Program point summary:");
    Console.WriteLine($"  Points: {summary.ProgramPointCount}");
    Console.WriteLine(
        "  Path conditions: " +
        $"Total={summary.TotalPathConditionCount}, " +
        $"MaxPerPoint={summary.MaxPathConditionCount}");
    if (options.CheckReachability)
        Console.WriteLine(
            "  Reachability: " +
            $"Reachable={summary.Reachability.ReachableCount}, " +
            $"Unreachable={summary.Reachability.UnreachableCount}, " +
            $"Unknown={summary.Reachability.UnknownCount}, " +
            $"NotChecked={summary.Reachability.NotCheckedCount}");

    PrintProofOutcomeSummary(summary.ProofOutcomes, "  ");
}

    private static void PrintProofOutcomeSummary(
    SymbolicProofOutcomeSummary summary,
    string indent)
{
    Console.WriteLine(
        indent +
        "Proof outcomes: " +
        $"Total={summary.TotalCount}, " +
        $"ProvenTrue={summary.ProvenTrueCount}, " +
        $"ProvenFalse={summary.ProvenFalseCount}, " +
        $"Unreachable={summary.UnreachableCount}, " +
        $"Unknown={summary.UnknownCount}");
}

    private static void PrintConditionProofSummaries(
    IReadOnlyList<SymbolicConditionProofSummary> proofs)
{
    foreach (var proof in proofs)
    {
        Console.WriteLine(
            $"Implies '{proof.Condition}' target={FormatProofTarget(proof.Target)} " +
            $"kind={proof.Proof.DisplayKind} summary: " +
            $"Status={proof.Status}, " +
            $"ProvenTrue={proof.ProvenTrueCount}, " +
            $"ProvenFalse={proof.ProvenFalseCount}, " +
            $"Unreachable={proof.UnreachableCount}, " +
            $"Unknown={proof.UnknownCount}, " +
            $"Reachable={proof.ReachableCount}, " +
            $"Resolved={proof.ResolvedCount}");
        Console.WriteLine($"  Proof summary: {proof.Summary}");
        PrintProofReasonSummary(proof.Reasons, "  ");
    }
}

    private static IReadOnlyList<SymbolicConditionProofSummary> FilterConditionProofSummaries(
    IReadOnlyList<SymbolicConditionProofSummary> proofs,
    SymbolicCliOptions options)
{
    if (!options.HasInvariantTargetFilter) return proofs;

    return proofs
        .Where(proof => SymbolicInvariantTargetFilter.Matches(proof.Target, options.InvariantTargets))
        .ToArray();
}

    private static IReadOnlyList<SymbolicConditionProofResult> FilterConditionProofResults(
    IReadOnlyList<SymbolicConditionProofResult> proofs,
    SymbolicCliOptions options)
{
    if (!options.HasInvariantTargetFilter) return proofs;

    return proofs
        .Where(proof => SymbolicInvariantTargetFilter.Matches(proof.Target, options.InvariantTargets))
        .ToArray();
}

    private static string FormatProofTarget(string? target)
{
    return string.IsNullOrWhiteSpace(target) ? "<unknown>" : target!;
}

    private static void PrintProofReasonSummary(
    IReadOnlyList<SymbolicConditionProofReasonSummary> reasons,
    string indent)
{
    foreach (var reason in reasons)
        Console.WriteLine(
            indent +
            "Proof reason: " +
            $"{reason.TruthValue} count={reason.Count} reason={reason.GetDisplayReason()}");
}

    private static void PrintSmtDiagnostics(SymbolicSmtDiagnostics diagnostics)
{
    Console.WriteLine("SMT:");
    Console.WriteLine($"  Mode: {diagnostics.Mode}");
    Console.WriteLine($"  Enabled: {diagnostics.IsEnabled}");
    Console.WriteLine($"  Query timeout ms: {diagnostics.QueryTimeoutMs}");
    Console.WriteLine($"  Method budget ms: {diagnostics.MethodBudgetMs}");
    Console.WriteLine($"  Max path conditions: {diagnostics.MaxPathConditions}");
    Console.WriteLine($"  Max expression nodes: {diagnostics.MaxExpressionNodes}");
    Console.WriteLine($"  Executed queries: {diagnostics.ExecutedQueryCount}");
    Console.WriteLine($"  Cache entries: {diagnostics.CacheEntryCount}");
    Console.WriteLine($"  Health: {diagnostics.Health.State}");
    Console.WriteLine($"  Permanently unavailable: {diagnostics.Health.IsPermanentlyUnavailable}");
    if (!string.IsNullOrWhiteSpace(diagnostics.Health.LastFailureCode))
        Console.WriteLine($"  Last failure: {diagnostics.Health.LastFailureCode}");
    Console.WriteLine($"  Transient retries: {diagnostics.Health.TransientRetryCount}");
    Console.WriteLine($"  Recovered transient failures: {diagnostics.Health.RecoveredTransientFailureCount}");
    Console.WriteLine($"  Context recycles: {diagnostics.Health.ContextRecycleCount}");
    Console.WriteLine($"  Context generation: {diagnostics.Health.ContextGeneration}");
    Console.WriteLine($"  Max transient retries: {diagnostics.Lifecycle.MaxTransientRetries}");
    Console.WriteLine(
        $"  Recycle context on transient failure: {diagnostics.Lifecycle.RecycleContextOnTransientFailure}");
    Console.WriteLine(
        $"  Dispose context with service: {diagnostics.Lifecycle.DisposeCurrentThreadContextOnServiceDispose}");
}

    private static void PrintAnalysisTruncation(SymbolicAnalysisTruncationInfo truncation)
{
    if (!truncation.IsTruncated) return;

    Console.WriteLine($"Analysis limits hit: {truncation.Events.Count}");
    foreach (var item in truncation.Events)
    {
        var location = item.SourceSpanStart.HasValue
            ? " spanStart=" + item.SourceSpanStart.Value.ToString(CultureInfo.InvariantCulture)
            : string.Empty;
        Console.WriteLine(
            $"  - {item.Code} limit={item.Limit} observed={item.Observed}{location} " +
            $"provenance={item.Provenance}");
    }
}
}

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Smt;
using SymbolicCapability = SharpProof.Symbolic.SymbolicCapability;

try
{
    var options = SymbolicCliOptions.Parse(args);
    if (options.ShowHelp || options.FilePath == null)
    {
        Console.Error.WriteLine(SymbolicCliOptions.Usage);
        return options.ShowHelp ? 0 : 64;
    }

    var smtAnalysis = options.RequiresSmt
        ? new SmtAnalysisService(options.CreateSmtOptions())
        : null;

    if (options.Explain)
    {
        PrintExplainResult(options, smtAnalysis!);
        return 0;
    }

    object result;
    if (options.RuntimeHazards)
        result = new SymbolicQueryService().QueryRuntimeHazards(
            new SymbolicRuntimeHazardRequest(
                SymbolicSourceInput.FromFile(options.FilePath),
                options.CreateRuntimeHazardTarget(),
                options.CreateQueryOptions(smtAnalysis, false),
                options.CreateRuntimeHazardOptions()));
    else if (options.Complexity)
        result = new SymbolicQueryService().QueryComplexity(
            new SymbolicComplexityRequest(
                SymbolicSourceInput.FromFile(options.FilePath),
                options.CreateComplexityTarget(),
                options.CreateQueryOptions(smtAnalysis, false)));
    else if (options.Capabilities)
        result = new SymbolicQueryService().QueryCapabilities(
            new SymbolicCapabilityRequest(
                SymbolicSourceInput.FromFile(options.FilePath),
                options.CreateCapabilityTarget(),
                options.CreateQueryOptions(smtAnalysis, false)));
    else
        result = new SymbolicQueryService()
            .Query(new SymbolicQueryRequest(
                SymbolicSourceInput.FromFile(options.FilePath),
                options.CreateQueryTarget(),
                options.CreateQueryOptions(smtAnalysis, true)))
            .InnerResult;

    if (options.HasRuntimeHazardFilter && result is SymbolicRuntimeHazardQueryResult runtimeHazardResult)
        result = options.FilterRuntimeHazards(runtimeHazardResult);

    if (options.InvariantJson)
    {
        var invariantResult = result switch
        {
            SymbolicFileQueryResult fileResult => fileResult.ToInvariantQueryResult(options.CreateCompactOptions()),
            SymbolicLineQueryResult lineResult => lineResult.ToInvariantQueryResult(options.CreateCompactOptions()),
            SymbolicSpanQueryResult spanResult => spanResult.ToInvariantQueryResult(options.CreateCompactOptions()),
            SymbolicSourceQueryResult pointResult => (object)pointResult.ToInvariantQueryResult(
                options.CreateCompactOptions()),
            _ => throw new InvalidOperationException("Unexpected query result type.")
        };
        Console.WriteLine(JsonSerializer.Serialize(
            invariantResult,
            CreateCompactJsonOptions()));
    }
    else if (options.CompactJson)
    {
        var compactResult = result switch
        {
            SymbolicCapabilityResult capabilityResult => CompactSymbolicCapabilityResult.FromResult(capabilityResult),
            SymbolicComplexityResult complexityResult => CompactSymbolicComplexityResult.FromResult(complexityResult),
            SymbolicFileQueryResult fileResult => fileResult.ToCompactResult(options.CreateCompactOptions()),
            SymbolicLineQueryResult lineResult => lineResult.ToCompactResult(options.CreateCompactOptions()),
            SymbolicSpanQueryResult spanResult => spanResult.ToCompactResult(options.CreateCompactOptions()),
            SymbolicSourceQueryResult pointResult => pointResult.ToCompactResult(options.CreateCompactOptions()),
            SymbolicRuntimeHazardQueryResult hazardResult => (object)CompactRuntimeHazardQueryResult.FromResult(
                hazardResult,
                options.CreateCompactHazardOptions()),
            _ => throw new InvalidOperationException("Unexpected query result type.")
        };
        Console.WriteLine(JsonSerializer.Serialize(
            compactResult,
            CreateCompactJsonOptions()));
    }
    else if (options.Json)
    {
        var json = result switch
        {
            SymbolicCapabilityResult capabilityResult => JsonSerializer.Serialize(capabilityResult,
                CreateFullJsonOptions()),
            SymbolicComplexityResult complexityResult => JsonSerializer.Serialize(complexityResult,
                CreateFullJsonOptions()),
            SymbolicFileQueryResult fileResult => JsonSerializer.Serialize(fileResult, CreateFullJsonOptions()),
            SymbolicLineQueryResult lineResult => JsonSerializer.Serialize(lineResult, CreateFullJsonOptions()),
            SymbolicSpanQueryResult spanResult => JsonSerializer.Serialize(spanResult, CreateFullJsonOptions()),
            SymbolicSourceQueryResult pointResult => JsonSerializer.Serialize(pointResult, CreateFullJsonOptions()),
            SymbolicRuntimeHazardQueryResult hazardResult => JsonSerializer.Serialize(hazardResult,
                CreateFullJsonOptions()),
            _ => throw new InvalidOperationException("Unexpected query result type.")
        };
        Console.WriteLine(json);
    }
    else if (result is SymbolicRuntimeHazardQueryResult hazardResult)
    {
        PrintRuntimeHazardResult(hazardResult);
    }
    else if (result is SymbolicComplexityResult complexityResult)
    {
        PrintComplexityResult(complexityResult);
    }
    else if (result is SymbolicCapabilityResult capabilityResult)
    {
        PrintCapabilityResult(capabilityResult);
    }
    else if (result is SymbolicFileQueryResult fileResult)
    {
        PrintFileResult(fileResult, options);
    }
    else if (result is SymbolicLineQueryResult lineResult)
    {
        PrintLineResult(lineResult, options);
    }
    else if (result is SymbolicSpanQueryResult spanResult)
    {
        PrintSpanResult(spanResult, options);
    }
    else
    {
        PrintPointResult((SymbolicSourceQueryResult)result, options, true);
    }

    return options.FailOnHazard &&
           result is SymbolicRuntimeHazardQueryResult finalHazardResult &&
           finalHazardResult.HazardCount > 0
        ? 1
        : 0;
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine(SymbolicCliOptions.Usage);
    return 64;
}

static void PrintFileResult(
    SymbolicFileQueryResult result,
    SymbolicCliOptions options)
{
    Console.WriteLine($"{result.FilePath}");
    Console.WriteLine($"Total lines: {result.LineCount}");
    Console.WriteLine($"Lines with program points: {result.LinesWithProgramPoints}");
    Console.WriteLine($"Program points: {result.ProgramPointCount}");
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

static void PrintLineResult(SymbolicLineQueryResult result, SymbolicCliOptions options)
{
    Console.WriteLine($"{result.FilePath}:{result.Line}");
    Console.WriteLine($"Program points: {result.ProgramPoints.Count}");
    PrintProgramPointSummary(result.ProgramPointSummary, options);
    Console.WriteLine($"Observed distinct facts: {result.ObservedFactCount}");
    Console.WriteLine($"Line merged invariant: {result.MergedInvariantText}");
    Console.WriteLine($"Line invariant merge: {result.InvariantInfo.MergeKind}");
    Console.WriteLine($"Line invariant conditions: {result.InvariantInfo.ConditionCount}");
    PrintInvariantQuery("Line invariant query", result.InvariantQuery, options);
    PrintMergedPathFacts("Line merged path facts", result.MergedPathFacts);
    PrintConditionProofSummaries(FilterConditionProofSummaries(result.ConditionProofs, options));
    foreach (var point in result.ProgramPoints)
    {
        Console.WriteLine();
        PrintPointResult(point, options, true);
    }

    if (result.SmtDiagnostics.IsConfigured && result.ProgramPoints.Count == 0)
        PrintSmtDiagnostics(result.SmtDiagnostics);
}

static void PrintSpanResult(SymbolicSpanQueryResult result, SymbolicCliOptions options)
{
    Console.WriteLine($"{result.FilePath}:{result.SpanStart}-{result.SpanEnd}");
    Console.WriteLine($"Span lines: {result.StartLine}:{result.StartColumn}-{result.EndLine}:{result.EndColumn}");
    Console.WriteLine($"Program points: {result.ProgramPoints.Count}");
    PrintProgramPointSummary(result.ProgramPointSummary, options);
    Console.WriteLine($"Observed distinct facts: {result.ObservedFactCount}");
    Console.WriteLine($"Span merged invariant: {result.MergedInvariantText}");
    Console.WriteLine($"Span invariant merge: {result.InvariantInfo.MergeKind}");
    Console.WriteLine($"Span invariant conditions: {result.InvariantInfo.ConditionCount}");
    PrintInvariantQuery("Span invariant query", result.InvariantQuery, options);
    PrintMergedPathFacts("Span merged path facts", result.MergedPathFacts);
    PrintConditionProofSummaries(FilterConditionProofSummaries(result.ConditionProofs, options));
    foreach (var point in result.ProgramPoints)
    {
        Console.WriteLine();
        PrintPointResult(point, options, true);
    }

    if (result.SmtDiagnostics.IsConfigured && result.ProgramPoints.Count == 0)
        PrintSmtDiagnostics(result.SmtDiagnostics);
}

static void PrintRuntimeHazardResult(SymbolicRuntimeHazardQueryResult result)
{
    Console.WriteLine($"{result.FilePath}");
    if (result.Line.HasValue)
        Console.WriteLine($"Line: {result.Line.Value}");
    else if (result.ScopeStart.HasValue && result.ScopeEnd.HasValue)
        Console.WriteLine($"Span: {result.ScopeStart.Value}-{result.ScopeEnd.Value}");
    else
        Console.WriteLine($"Total lines: {result.LineCount}");

    Console.WriteLine($"Runtime hazards: {result.HazardCount}");
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

static void PrintExplainResult(SymbolicCliOptions options, SmtAnalysisService smtAnalysis)
{
    var service = new SymbolicQueryService();
    var source = SymbolicSourceInput.FromFile(options.FilePath!);
    var queryOptions = options.CreateQueryOptions(smtAnalysis, false);
    var pointTarget = options.Position.HasValue
        ? SymbolicQueryTarget.Position(options.Position.Value)
        : SymbolicQueryTarget.Point(options.Line, options.Column);

    Console.WriteLine("SharpProof explanation");
    Console.WriteLine($"File: {options.FilePath}");
    Console.WriteLine(options.Position.HasValue
        ? $"Target: position {options.Position.Value}"
        : $"Target: line {options.Line}, column {options.Column}");

    var pointResult = service
        .Query(new SymbolicQueryRequest(source, pointTarget, queryOptions))
        .InnerResult;
    if (pointResult is SymbolicSourceQueryResult point)
    {
        Console.WriteLine();
        Console.WriteLine("Invariant proof");
        Console.WriteLine($"Node: {point.NodeKind}");
        Console.WriteLine($"Method: {point.MethodName ?? "<unknown>"}");
        Console.WriteLine($"Program point: {point.ProgramPointKind}");
        Console.WriteLine($"Merged invariant: {point.MergedInvariantText}");
        Console.WriteLine($"Reachability: {point.Reachability}");
        Console.WriteLine($"Reachability reason: {point.ReachabilityReason}");
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

    if (!options.Position.HasValue)
    {
        var hazards = service.QueryRuntimeHazards(
            new SymbolicRuntimeHazardRequest(
                source,
                SymbolicQueryTarget.Line(options.Line),
                queryOptions,
                options.CreateRuntimeHazardOptions()));
        Console.WriteLine();
        Console.WriteLine("Runtime hazards");
        Console.WriteLine($"Count: {hazards.HazardCount}");
        Console.WriteLine("Status summary: " +
                          FormatCountSummary(CountBy(hazards.Hazards, static hazard => hazard.Status.ToString())));
        foreach (var hazard in hazards.Hazards.Take(5))
            Console.WriteLine(
                $"  - {hazard.Kind} {hazard.Status} at {hazard.Line}:{hazard.Column}: " +
                $"{hazard.OperationText} ({hazard.GetDisplayStatusReason()})");
    }

    PrintExplainCapabilitySummary(service.QueryCapabilities(
        new SymbolicCapabilityRequest(source, pointTarget, queryOptions)));
    PrintExplainComplexitySummary(service.QueryComplexity(
        new SymbolicComplexityRequest(source, pointTarget, queryOptions)));

    if (pointResult is SymbolicSourceQueryResult finalPoint && finalPoint.SmtDiagnostics.IsConfigured)
    {
        Console.WriteLine();
        PrintSmtDiagnostics(finalPoint.SmtDiagnostics);
    }
}

static void PrintExplainCapabilitySummary(SymbolicCapabilityResult result)
{
    Console.WriteLine();
    Console.WriteLine("Capabilities");
    Console.WriteLine($"Method: {result.MethodDisplayName}");
    Console.WriteLine($"Capabilities: {result.CapabilityText}");
    Console.WriteLine($"Conservative: {result.IsConservative}");
    if (result.UnknownReasons.Count != 0)
        Console.WriteLine("Unknown reasons: " + string.Join(", ", result.UnknownReasons));

    foreach (var site in result.Sites.Take(5))
    {
        var prefix = site.IsUnknown ? "Unknown" : site.CapabilityText;
        var detail = string.IsNullOrWhiteSpace(site.SymbolDisplayName)
            ? site.OperationKind
            : site.SymbolDisplayName;
        Console.WriteLine($"  - {prefix} via {detail} @ {site.SourceLine}:{site.SourceColumn}");
    }
}

static void PrintExplainComplexitySummary(SymbolicComplexityResult result)
{
    Console.WriteLine();
    Console.WriteLine("Complexity");
    Console.WriteLine($"Method: {result.MethodDisplayName}");
    Console.WriteLine($"Complexity: {result.Complexity.Text}");
    Console.WriteLine($"Kind: {result.Complexity.Kind}");
    Console.WriteLine($"Conservative: {result.Complexity.IsConservative}");
    if (result.UnknownReasons.Count != 0)
        Console.WriteLine("Unknown reasons: " + string.Join(", ", result.UnknownReasons));

    foreach (var driver in result.Drivers.Take(5))
        Console.WriteLine($"  - [{driver.Kind}] {driver.Description} @ {driver.SourceLine}:{driver.SourceColumn}");
}

static void PrintComplexityResult(SymbolicComplexityResult result)
{
    Console.WriteLine(result.FilePath);
    Console.WriteLine($"Method: {result.MethodDisplayName}");
    Console.WriteLine($"Declaration kind: {result.DeclarationKind}");
    Console.WriteLine($"Span: {result.StartLine}:{result.StartColumn}-{result.EndLine}:{result.EndColumn}");
    Console.WriteLine($"Complexity: {result.Complexity.Text}");
    Console.WriteLine($"Kind: {result.Complexity.Kind}");
    Console.WriteLine($"Conservative: {result.Complexity.IsConservative}");

    if (result.UnknownReasons.Count != 0)
    {
        Console.WriteLine("Unknown reasons:");
        foreach (var reason in result.UnknownReasons) Console.WriteLine($"  - {reason}");
    }

    if (result.Drivers.Count != 0)
    {
        Console.WriteLine("Drivers:");
        foreach (var driver in result.Drivers)
            Console.WriteLine(
                $"  - [{driver.Kind}] {driver.Description} @ {driver.SourceLine}:{driver.SourceColumn}");
    }

    if (result.CalleeSummaries.Count != 0)
    {
        Console.WriteLine("Callee summaries:");
        foreach (var callee in result.CalleeSummaries)
            Console.WriteLine(
                $"  - {callee.MethodDisplayName}: {callee.ComplexityText} ({callee.Kind})");
    }
}

static void PrintCapabilityResult(SymbolicCapabilityResult result)
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

static string FormatCountSummary(IReadOnlyDictionary<string, int> counts)
{
    return counts.Count == 0
        ? "<none>"
        : string.Join(", ", counts.Select(static pair => $"{pair.Key}={pair.Value}"));
}

static IReadOnlyDictionary<string, int> CountBy<T>(
    IEnumerable<T> values,
    Func<T, string> keySelector)
{
    return values
        .GroupBy(keySelector, StringComparer.Ordinal)
        .OrderBy(static group => group.Key, StringComparer.Ordinal)
        .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);
}

static void PrintMergedPathFacts(
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

static void PrintInvariantQuery(
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

static IReadOnlyList<TTarget> FilterInvariantTargets<TTarget>(
    IReadOnlyList<TTarget> targets,
    SymbolicCliOptions options,
    Func<TTarget, string> targetSelector)
{
    return SymbolicInvariantTargetFilter.ApplyToTargets(targets, options.InvariantTargets, targetSelector);
}

static IReadOnlyList<string> SelectInvariantFacts(
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

static IReadOnlyList<string> GetMatchedInvariantTargetFilters(
    SymbolicCliOptions options,
    IReadOnlyList<SymbolicInvariantTargetSummary> targetSummaries,
    IReadOnlyList<SymbolicInvariantTargetPathSummary> targetPathSummaries)
{
    return SymbolicInvariantTargetFilter.GetMatchedTargetFilters(
        targetSummaries,
        targetPathSummaries,
        options.InvariantTargets);
}

static IReadOnlyList<string> GetUnmatchedInvariantTargetFilters(
    SymbolicCliOptions options,
    IReadOnlyList<string> matchedTargetFilters)
{
    return SymbolicInvariantTargetFilter.GetUnmatchedTargetFilters(
        options.InvariantTargets,
        matchedTargetFilters);
}

static void PrintInvariantTargetFilterList(
    string label,
    string name,
    IReadOnlyList<string> values)
{
    const int maxTextTargetFilters = 16;
    if (values.Count == 0) return;

    var visibleValues = values.Take(maxTextTargetFilters).ToArray();
    var suffix = values.Count > visibleValues.Length
        ? " ... " + (values.Count - visibleValues.Length).ToString(CultureInfo.InvariantCulture) + " omitted"
        : string.Empty;
    Console.WriteLine(label + " " + name + ": " + string.Join(", ", visibleValues) + suffix);
}

static void PrintInvariantTargetSummaries(
    string label,
    IReadOnlyList<SymbolicInvariantTargetSummary> targetSummaries)
{
    const int maxTextTargets = 16;
    foreach (var target in targetSummaries.Take(maxTextTargets))
    {
        Console.WriteLine(
            label + " target: " +
            $"{target.Target} status={target.Status} " +
            $"reason={target.StatusReason} code={target.ReasonCode} " +
            $"must={target.MustFactCount} maybe={target.MaybeFactCount} unknown={target.UnknownFactCount}");
        Console.WriteLine(label + " target summary: " + target.Summary);
    }

    if (targetSummaries.Count > maxTextTargets)
        Console.WriteLine(
            label + " target summaries truncated: " +
            (targetSummaries.Count - maxTextTargets).ToString(CultureInfo.InvariantCulture) +
            " omitted");
}

static void PrintInvariantTargetPathSummaries(
    string label,
    IReadOnlyList<SymbolicInvariantTargetPathSummary> targetPathSummaries)
{
    const int maxTextTargets = 16;
    foreach (var target in targetPathSummaries.Take(maxTextTargets))
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

    if (targetPathSummaries.Count > maxTextTargets)
        Console.WriteLine(
            label + " target path summaries truncated: " +
            (targetPathSummaries.Count - maxTextTargets).ToString(CultureInfo.InvariantCulture) +
            " omitted");
}

static void PrintPointResult(
    SymbolicSourceQueryResult result,
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

static void PrintProgramPointSummary(
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

static void PrintProofOutcomeSummary(
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

static void PrintConditionProofSummaries(
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

static IReadOnlyList<SymbolicConditionProofSummary> FilterConditionProofSummaries(
    IReadOnlyList<SymbolicConditionProofSummary> proofs,
    SymbolicCliOptions options)
{
    if (!options.HasInvariantTargetFilter) return proofs;

    return proofs
        .Where(proof => SymbolicInvariantTargetFilter.Matches(proof.Target, options.InvariantTargets))
        .ToArray();
}

static IReadOnlyList<SymbolicConditionProofResult> FilterConditionProofResults(
    IReadOnlyList<SymbolicConditionProofResult> proofs,
    SymbolicCliOptions options)
{
    if (!options.HasInvariantTargetFilter) return proofs;

    return proofs
        .Where(proof => SymbolicInvariantTargetFilter.Matches(proof.Target, options.InvariantTargets))
        .ToArray();
}

static string FormatProofTarget(string? target)
{
    return string.IsNullOrWhiteSpace(target) ? "<unknown>" : target!;
}

static void PrintProofReasonSummary(
    IReadOnlyList<SymbolicConditionProofReasonSummary> reasons,
    string indent)
{
    foreach (var reason in reasons)
        Console.WriteLine(
            indent +
            "Proof reason: " +
            $"{reason.TruthValue} count={reason.Count} reason={reason.GetDisplayReason()}");
}

static void PrintSmtDiagnostics(SymbolicSmtDiagnostics diagnostics)
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
}

static JsonSerializerOptions CreateCompactJsonOptions()
{
    var options = new JsonSerializerOptions
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    options.Converters.Add(new JsonStringEnumConverter());
    return options;
}

static JsonSerializerOptions CreateFullJsonOptions()
{
    var options = new JsonSerializerOptions
    {
        WriteIndented = true
    };
    options.Converters.Add(new JsonStringEnumConverter());
    return options;
}

internal sealed class SymbolicCliOptions
{
    public const string Usage = """
                                Usage: SharpProof.SymbolicCli [explain] --file <path> (--line <n> [--column <n>] [--line-invariants] | --position <n> | --span-start <n> --span-end <n> | --all-lines) [--json|--compact-json|--invariant-json]

                                Options:
                                  --file <path>       C# source file to query.
                                  explain             Print a contract-oriented explanation for one line or position by composing invariant, hazard, capability, and complexity queries.
                                  --line <n>          1-based source line to query.
                                  --column <n>        1-based source column to query. With --line-invariants, selects the nearest program point on the line.
                                  --line-invariants   Query every statement/expression program point on the line, or the nearest point when --column is supplied.
                                  --span-start <n>    0-based inclusive source span start to query.
                                  --span-end <n>      0-based exclusive source span end to query.
                                  --span-start-line <n>
                                                      1-based span start line for line/column span queries.
                                  --span-start-column <n>
                                                      1-based span start column for line/column span queries.
                                  --span-end-line <n> 1-based span end line for line/column span queries.
                                  --span-end-column <n>
                                                      1-based span end column for line/column span queries.
                                  --all-lines         Query every line that contains statement/expression program points.
                                  --line-expressions  Include expression program points in --line-invariants, --span-start/--span-end, or --all-lines.
                                  --post-line-invariants
                                                      Include facts established by completed declaration/assignment statements on queried lines.
                                  --position <n>      0-based absolute source position to query.
                                  --reference <path>  Metadata reference path. Can be repeated.
                                  --node-kind <kind>  Keep only matching Roslyn node kinds in --line-invariants or --all-lines output. Can be repeated.
                                  --program-point-kind <kind>
                                                      Keep only Statement, Expression, or Other program points. Can be repeated.
                                  --filter-line <n>   Keep only program points on this 1-based line in aggregate output. Can be repeated.
                                  --line-start <n>    Keep only program points at or after this 1-based line.
                                  --line-end <n>      Keep only program points at or before this 1-based line.
                                  --with-facts        Keep only program points that have at least one reported fact.
                                  --with-conditions   Keep only program points that have at least one path condition.
                                  --method <name>     Keep only program points inside a matching method/local function. Can be repeated.
                                  --method-contains <text>
                                                      Keep only program points inside a method/local function containing text. Can be repeated.
                                  --condition-target <target>
                                                      Keep only program points with a path condition for the target. Can be repeated.
                                  --invariant-target <target>
                                                      In compact, invariant, or text output, show per-target invariant summaries only for this target. Can be repeated.
                                  --condition <expr>  Keep only program points with an exact source-like path condition. Can be repeated.
                                  --condition-contains <text>
                                                      Keep only program points with a path condition containing text. Can be repeated.
                                  --reachability <r>  Keep only program points with reachability NotChecked, Unknown, Reachable, or Unreachable. Can be repeated.
                                  --with-proofs       Keep only program points with at least one implication proof result.
                                  --proof-outcome <v> Keep only program points with proof outcome Unknown, ProvenTrue, ProvenFalse, or Unreachable. Can be repeated.
                                  --proof-condition <expr>
                                                      Keep only program points with an exact implication condition. Can be repeated.
                                  --proof-condition-contains <text>
                                                      Keep only program points with an implication condition containing text. Can be repeated.
                                  --check-reachability
                                                      Use bounded SMT to classify whether the queried program point is reachable.
                                  --implies <expr>    Use bounded SMT to prove whether invariants at the queried point imply expr. Can be repeated.
                                  --runtime-hazards   Query proven runtime hazards instead of invariant program points.
                                  --complexity        Query the containing method-like body's conservative time complexity instead of invariants.
                                  --capabilities      Query the containing method-like body's proven capability categories instead of invariants.
                                  --fail-on-hazard    Exit with code 1 when final runtime hazard output contains hazards.
                                  --hazard-kind <k>   Keep only DirectThrow, Rethrow, DivideByZero, NullDereference, NullableValueWithoutValue, IndexOutOfRange, ArgumentOutOfRange, CheckedIntegralOverflow, ArrayTypeMismatch, UnboxNull, InvalidCast, DynamicNullBinding, or NegativeArrayLength hazards. Can be repeated.
                                  --hazard-status <s> Keep only Proven, Unreachable, Unknown, or Unsupported runtime hazards. Can be repeated.
                                  --hazard-exception-type <type>
                                                      Keep only runtime hazards with this exception type. Can be repeated.
                                  --hazard-category <category>
                                                      Keep only runtime hazards with this category. Can be repeated.
                                  --include-unproven-hazards
                                                      Include unknown, unreachable, and unsupported hazard candidates in runtime hazard output.
                                  --smt-mode <mode>   SMT mode: off, bounded, or deep. Default: bounded.
                                  --smt-timeout-ms <n>
                                                      Per-query SMT timeout in milliseconds.
                                  --smt-method-budget-ms <n>
                                                      Total SMT budget for this CLI query in milliseconds.
                                  --smt-max-path-conditions <n>
                                                      Maximum path conditions before conservative fallback.
                                  --smt-max-expression-nodes <n>
                                                      Maximum formula nodes before conservative fallback.
                                  --json              Emit JSON instead of text.
                                  --compact-json      Emit compact bounded JSON for invariants or runtime hazards.
                                  --invariant-json    Emit only the compact invariant query answer, query/focus metadata, bounded reasons, proof summaries, and analysis summary.
                                  --max-lines <n>     Maximum lines included in --compact-json output. Default: 100.
                                  --max-points <n>    Maximum program points included in --compact-json output. Default: 250.
                                  --max-hazards <n>   Maximum runtime hazards included in --runtime-hazards --compact-json output. Default: 250.
                                  --max-facts <n>     Maximum raw SMT facts included in --compact-json output. Default: 50.
                                  --max-conditions <n>
                                                      Maximum condition strings included in --compact-json output. Default: 50.
                                  --max-proofs <n>    Maximum proof summaries/results included in --compact-json output. Default: 50.
                                  --summary-only      Shorthand for --compact-json with --max-lines 0, --max-points 0, and --max-hazards 0.

                                Runtime hazard notes:
                                  --runtime-hazards accepts --line, --span-start/--span-end, or --all-lines.
                                  Runtime hazard output includes only Proven hazards by default.
                                  Add --include-unproven-hazards to inspect Unknown, Unreachable, or Unsupported candidates.
                                  Use --hazard-kind, --hazard-status, --hazard-exception-type, or --hazard-category to narrow hazards.

                                Complexity notes:
                                  --complexity accepts --line, --line --column, or --position.
                                  Complexity queries resolve the containing method-like body and return a conservative Big-O result.

                                Capability notes:
                                  --capabilities accepts --line, --line --column, or --position.
                                  Capability queries resolve the containing method-like body and return proven capability categories plus unknown reasons.

                                Examples:
                                  SharpProof.SymbolicCli explain --file Example.cs --line 42 --implies "index >= 0"
                                  SharpProof.SymbolicCli --file Example.cs --line 42 --line-invariants --check-reachability --implies "index >= 0"
                                  SharpProof.SymbolicCli --file Example.cs --line 42 --line-invariants --invariant-json --invariant-target index
                                  SharpProof.SymbolicCli --file Example.cs --line 42 --runtime-hazards
                                  SharpProof.SymbolicCli --file Example.cs --line 42 --complexity
                                  SharpProof.SymbolicCli --file Example.cs --line 42 --capabilities --compact-json
                                  SharpProof.SymbolicCli --file Example.cs --all-lines --runtime-hazards --hazard-kind NullDereference --compact-json
                                  SharpProof.SymbolicCli --file Example.cs --all-lines --runtime-hazards --include-unproven-hazards --hazard-status Unknown --compact-json
                                """;

    public string? FilePath { get; private set; }

    public int Line { get; private set; }

    public int Column { get; private set; } = 1;

    public bool HasColumn { get; private set; }

    public int? Position { get; private set; }

    public int? SpanStart { get; private set; }

    public int? SpanEnd { get; private set; }

    public int? SpanStartLine { get; private set; }

    public int? SpanStartColumn { get; private set; }

    public int? SpanEndLine { get; private set; }

    public int? SpanEndColumn { get; private set; }

    public bool LineInvariants { get; private set; }

    public bool AllLines { get; private set; }

    public bool LineExpressions { get; private set; }

    public bool PostLineInvariants { get; private set; }

    public List<string> ReferencePaths { get; } = new();

    public List<string> NodeKinds { get; } = new();

    public List<string> ProgramPointKinds { get; } = new();

    public List<int> FilterLines { get; } = new();

    public int? FilterLineStart { get; private set; }

    public int? FilterLineEnd { get; private set; }

    public bool WithFacts { get; private set; }

    public bool WithConditions { get; private set; }

    public List<string> MethodNames { get; } = new();

    public List<string> MethodNameContains { get; } = new();

    public List<string> ConditionTargets { get; } = new();

    public List<string> InvariantTargets { get; } = new();

    public bool HasInvariantTargetFilter => InvariantTargets.Count != 0;

    public List<string> Conditions { get; } = new();

    public List<string> ConditionContains { get; } = new();

    public List<SymbolicReachability> ReachabilityFilters { get; } = new();

    public bool WithProofs { get; private set; }

    public List<SymbolicTruthValue> ProofOutcomes { get; } = new();

    public List<string> ProofConditions { get; } = new();

    public List<string> ProofConditionContains { get; } = new();

    public bool Json { get; private set; }

    public bool CompactJson { get; private set; }

    public bool InvariantJson { get; private set; }

    public bool CheckReachability { get; private set; }

    public List<string> ImpliedConditions { get; } = new();

    public bool RuntimeHazards { get; private set; }

    public bool Complexity { get; private set; }

    public bool Capabilities { get; private set; }

    public bool Explain { get; private set; }

    public bool FailOnHazard { get; private set; }

    public bool IncludeUnprovenHazards { get; private set; }

    public List<SymbolicRuntimeHazardKind> HazardKinds { get; } = new();

    public List<SymbolicRuntimeHazardStatus> HazardStatuses { get; } = new();

    public List<string> HazardExceptionTypes { get; } = new();

    public List<string> HazardCategories { get; } = new();

    public bool ShowHelp { get; private set; }

    public SmtAnalysisMode SmtMode { get; private set; } = SmtAnalysisOptions.Default.Mode;

    public int? SmtTimeoutMs { get; private set; }

    public int? SmtMethodBudgetMs { get; private set; }

    public int? SmtMaxPathConditions { get; private set; }

    public int? SmtMaxExpressionNodes { get; private set; }

    public int CompactMaxLines { get; private set; } = SymbolicCompactQueryOptions.DefaultMaxLines;

    public int CompactMaxProgramPoints { get; private set; } = SymbolicCompactQueryOptions.DefaultMaxProgramPoints;

    public int CompactMaxHazards { get; private set; } = CompactRuntimeHazardQueryOptions.DefaultMaxHazards;

    public int CompactMaxFacts { get; private set; } = SymbolicCompactQueryOptions.DefaultMaxFacts;

    public int CompactMaxConditions { get; private set; } = SymbolicCompactQueryOptions.DefaultMaxConditions;

    public int CompactMaxProofs { get; private set; } = SymbolicCompactQueryOptions.DefaultMaxProofs;

    public bool HasCompactOutputLimit { get; private set; }

    public bool HasCompactHazardOutputLimit { get; private set; }

    public bool CompactSummaryOnly { get; private set; }

    public bool RequiresSmt => Explain || CheckReachability || ImpliedConditions.Count != 0 || RuntimeHazards;

    public bool IsSpanQuery => SpanStart.HasValue || SpanEnd.HasValue;

    public bool IsLineColumnSpanQuery =>
        SpanStartLine.HasValue ||
        SpanStartColumn.HasValue ||
        SpanEndLine.HasValue ||
        SpanEndColumn.HasValue;

    public bool IsAnySpanQuery => IsSpanQuery || IsLineColumnSpanQuery;

    public bool HasRuntimeHazardFilter =>
        HazardStatuses.Count != 0 ||
        HazardExceptionTypes.Count != 0 ||
        HazardCategories.Count != 0;

    public bool HasResultFilter =>
        NodeKinds.Count != 0 ||
        ProgramPointKinds.Count != 0 ||
        FilterLines.Count != 0 ||
        FilterLineStart.HasValue ||
        FilterLineEnd.HasValue ||
        WithFacts ||
        WithConditions ||
        MethodNames.Count != 0 ||
        MethodNameContains.Count != 0 ||
        ConditionTargets.Count != 0 ||
        Conditions.Count != 0 ||
        ConditionContains.Count != 0 ||
        ReachabilityFilters.Count != 0 ||
        WithProofs ||
        ProofOutcomes.Count != 0 ||
        ProofConditions.Count != 0 ||
        ProofConditionContains.Count != 0;

    private static void NormalizeStringList(List<string> values)
    {
        if (values.Count == 0) return;

        var normalized = values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        values.Clear();
        values.AddRange(normalized);
    }

    public static SymbolicCliOptions Parse(string[] args)
    {
        var options = new SymbolicCliOptions();
        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            switch (arg)
            {
                case "explain":
                    options.Explain = true;
                    break;
                case "--help":
                case "-h":
                    options.ShowHelp = true;
                    break;
                case "--file":
                    options.FilePath = ReadString(args, ref index, arg);
                    break;
                case "--line":
                    options.Line = ReadPositiveInt(args, ref index, arg);
                    break;
                case "--column":
                    options.Column = ReadPositiveInt(args, ref index, arg);
                    options.HasColumn = true;
                    break;
                case "--position":
                    options.Position = ReadNonNegativeInt(args, ref index, arg);
                    break;
                case "--span-start":
                    options.SpanStart = ReadNonNegativeInt(args, ref index, arg);
                    break;
                case "--span-end":
                    options.SpanEnd = ReadNonNegativeInt(args, ref index, arg);
                    break;
                case "--span-start-line":
                    options.SpanStartLine = ReadPositiveInt(args, ref index, arg);
                    break;
                case "--span-start-column":
                    options.SpanStartColumn = ReadPositiveInt(args, ref index, arg);
                    break;
                case "--span-end-line":
                    options.SpanEndLine = ReadPositiveInt(args, ref index, arg);
                    break;
                case "--span-end-column":
                    options.SpanEndColumn = ReadPositiveInt(args, ref index, arg);
                    break;
                case "--line-invariants":
                case "--all-line-points":
                    options.LineInvariants = true;
                    break;
                case "--all-lines":
                case "--file-invariants":
                    options.AllLines = true;
                    break;
                case "--line-expressions":
                case "--include-expressions":
                    options.LineExpressions = true;
                    break;
                case "--post-line-invariants":
                    options.PostLineInvariants = true;
                    break;
                case "--reference":
                case "-r":
                    options.ReferencePaths.Add(ReadString(args, ref index, arg));
                    break;
                case "--node-kind":
                    options.NodeKinds.Add(ReadString(args, ref index, arg));
                    break;
                case "--program-point-kind":
                case "--point-kind":
                    options.ProgramPointKinds.Add(ReadProgramPointKind(args, ref index, arg));
                    break;
                case "--filter-line":
                    options.FilterLines.Add(ReadPositiveInt(args, ref index, arg));
                    break;
                case "--line-start":
                    options.FilterLineStart = ReadPositiveInt(args, ref index, arg);
                    break;
                case "--line-end":
                    options.FilterLineEnd = ReadPositiveInt(args, ref index, arg);
                    break;
                case "--with-facts":
                    options.WithFacts = true;
                    break;
                case "--with-conditions":
                    options.WithConditions = true;
                    break;
                case "--method":
                    options.MethodNames.Add(ReadString(args, ref index, arg));
                    break;
                case "--method-contains":
                    options.MethodNameContains.Add(ReadString(args, ref index, arg));
                    break;
                case "--condition-target":
                case "--target":
                    options.ConditionTargets.Add(ReadString(args, ref index, arg));
                    break;
                case "--invariant-target":
                case "--focus-target":
                    options.InvariantTargets.Add(ReadString(args, ref index, arg));
                    break;
                case "--condition":
                    options.Conditions.Add(ReadString(args, ref index, arg));
                    break;
                case "--condition-contains":
                    options.ConditionContains.Add(ReadString(args, ref index, arg));
                    break;
                case "--reachability":
                    options.ReachabilityFilters.Add(ReadReachability(args, ref index, arg));
                    break;
                case "--with-proofs":
                    options.WithProofs = true;
                    break;
                case "--proof-outcome":
                    options.ProofOutcomes.Add(ReadTruthValue(args, ref index, arg));
                    break;
                case "--proof-condition":
                    options.ProofConditions.Add(ReadString(args, ref index, arg));
                    break;
                case "--proof-condition-contains":
                    options.ProofConditionContains.Add(ReadString(args, ref index, arg));
                    break;
                case "--json":
                    options.Json = true;
                    break;
                case "--compact-json":
                case "--compact":
                    options.CompactJson = true;
                    break;
                case "--invariant-json":
                case "--invariant-query-json":
                    options.InvariantJson = true;
                    break;
                case "--max-lines":
                    options.CompactMaxLines = ReadNonNegativeInt(args, ref index, arg);
                    options.HasCompactOutputLimit = true;
                    break;
                case "--max-points":
                    options.CompactMaxProgramPoints = ReadNonNegativeInt(args, ref index, arg);
                    options.HasCompactOutputLimit = true;
                    break;
                case "--max-hazards":
                    options.CompactMaxHazards = ReadNonNegativeInt(args, ref index, arg);
                    options.HasCompactOutputLimit = true;
                    options.HasCompactHazardOutputLimit = true;
                    break;
                case "--max-facts":
                    options.CompactMaxFacts = ReadNonNegativeInt(args, ref index, arg);
                    options.HasCompactOutputLimit = true;
                    break;
                case "--max-conditions":
                    options.CompactMaxConditions = ReadNonNegativeInt(args, ref index, arg);
                    options.HasCompactOutputLimit = true;
                    break;
                case "--max-proofs":
                    options.CompactMaxProofs = ReadNonNegativeInt(args, ref index, arg);
                    options.HasCompactOutputLimit = true;
                    break;
                case "--summary-only":
                    options.CompactSummaryOnly = true;
                    options.CompactJson = true;
                    break;
                case "--check-reachability":
                    options.CheckReachability = true;
                    break;
                case "--implies":
                    options.ImpliedConditions.Add(ReadString(args, ref index, arg));
                    break;
                case "--runtime-hazards":
                    options.RuntimeHazards = true;
                    break;
                case "--complexity":
                    options.Complexity = true;
                    break;
                case "--capabilities":
                    options.Capabilities = true;
                    break;
                case "--fail-on-hazard":
                    options.FailOnHazard = true;
                    break;
                case "--hazard-kind":
                    options.HazardKinds.Add(ReadHazardKind(args, ref index, arg));
                    break;
                case "--hazard-status":
                    options.HazardStatuses.Add(ReadHazardStatus(args, ref index, arg));
                    break;
                case "--hazard-exception-type":
                case "--exception-type":
                    options.HazardExceptionTypes.Add(ReadString(args, ref index, arg));
                    break;
                case "--hazard-category":
                    options.HazardCategories.Add(ReadString(args, ref index, arg));
                    break;
                case "--include-unproven-hazards":
                    options.IncludeUnprovenHazards = true;
                    break;
                case "--smt-mode":
                    options.SmtMode = ReadSmtMode(args, ref index, arg);
                    break;
                case "--smt-timeout-ms":
                    options.SmtTimeoutMs = ReadPositiveInt(args, ref index, arg);
                    break;
                case "--smt-method-budget-ms":
                    options.SmtMethodBudgetMs = ReadPositiveInt(args, ref index, arg);
                    break;
                case "--smt-max-path-conditions":
                    options.SmtMaxPathConditions = ReadPositiveInt(args, ref index, arg);
                    break;
                case "--smt-max-expression-nodes":
                    options.SmtMaxExpressionNodes = ReadPositiveInt(args, ref index, arg);
                    break;
                default:
                    throw new ArgumentException($"Unknown option '{arg}'.");
            }
        }

        if (!options.ShowHelp)
        {
            NormalizeStringList(options.InvariantTargets);

            if (options.CompactSummaryOnly)
            {
                options.CompactMaxLines = SymbolicCompactQueryOptions.SummaryOnly.MaxLines;
                options.CompactMaxProgramPoints = SymbolicCompactQueryOptions.SummaryOnly.MaxProgramPoints;
                options.CompactMaxHazards = 0;
            }

            if (options.Json && options.CompactJson)
                throw new ArgumentException("--json cannot be combined with --compact-json.");

            if (options.Json && options.InvariantJson)
                throw new ArgumentException("--json cannot be combined with --invariant-json.");

            if (options.CompactJson && options.InvariantJson)
                throw new ArgumentException("--compact-json cannot be combined with --invariant-json.");

            if (options.Json && options.HasInvariantTargetFilter)
                throw new ArgumentException(
                    "--invariant-target cannot be combined with --json; use text, --compact-json, or --invariant-json.");

            if (options.HasCompactOutputLimit && !options.CompactJson && !options.InvariantJson)
                throw new ArgumentException(
                    "--max-lines, --max-points, --max-hazards, --max-facts, --max-conditions, and --max-proofs require --compact-json or --invariant-json.");

            if (options.HasCompactHazardOutputLimit && !options.RuntimeHazards)
                throw new ArgumentException("--max-hazards requires --runtime-hazards.");

            if (!options.RuntimeHazards &&
                (options.IncludeUnprovenHazards ||
                 options.FailOnHazard ||
                 options.HazardKinds.Count != 0 ||
                 options.HazardStatuses.Count != 0 ||
                 options.HazardExceptionTypes.Count != 0 ||
                 options.HazardCategories.Count != 0))
                throw new ArgumentException(
                    "--fail-on-hazard, --hazard-kind, --hazard-status, --hazard-exception-type, --hazard-category, and --include-unproven-hazards require --runtime-hazards.");

            if (options.HazardStatuses.Any(static status => status != SymbolicRuntimeHazardStatus.Proven) &&
                !options.IncludeUnprovenHazards)
                throw new ArgumentException(
                    "--hazard-status values other than Proven require --include-unproven-hazards.");

            if (options.FilePath == null) throw new ArgumentException("--file is required.");

            if (!File.Exists(options.FilePath)) throw new ArgumentException("--file does not exist.");

            if (options.Position.HasValue && options.Line != 0)
                throw new ArgumentException("--position cannot be combined with --line.");

            if (options.Position.HasValue && options.IsAnySpanQuery)
                throw new ArgumentException("--position cannot be combined with span query options.");

            if (options.IsAnySpanQuery && options.Line != 0)
                throw new ArgumentException("Span query options cannot be combined with --line.");

            if (options.IsAnySpanQuery && options.LineInvariants)
                throw new ArgumentException("Span query options cannot be combined with --line-invariants.");

            if (options.IsAnySpanQuery && options.Column != 1)
                throw new ArgumentException("Span query options cannot be combined with --column.");

            if (options.IsSpanQuery && (!options.SpanStart.HasValue || !options.SpanEnd.HasValue))
                throw new ArgumentException("--span-start and --span-end must be provided together.");

            if (options.IsLineColumnSpanQuery &&
                (!options.SpanStartLine.HasValue ||
                 !options.SpanStartColumn.HasValue ||
                 !options.SpanEndLine.HasValue ||
                 !options.SpanEndColumn.HasValue))
                throw new ArgumentException(
                    "--span-start-line, --span-start-column, --span-end-line, and --span-end-column must be provided together.");

            if (options.IsSpanQuery && options.IsLineColumnSpanQuery)
                throw new ArgumentException("Absolute span options cannot be combined with line/column span options.");

            if (options.SpanEnd.HasValue &&
                options.SpanStart.HasValue &&
                options.SpanEnd.Value < options.SpanStart.Value)
                throw new ArgumentException("--span-end cannot be less than --span-start.");

            if (options.SpanStartLine.HasValue &&
                options.SpanEndLine.HasValue &&
                (options.SpanEndLine.Value < options.SpanStartLine.Value ||
                 (options.SpanEndLine.Value == options.SpanStartLine.Value &&
                  options.SpanEndColumn!.Value < options.SpanStartColumn!.Value)))
                throw new ArgumentException("Line/column span end cannot be before span start.");

            if (options.AllLines &&
                (options.Position.HasValue || options.IsAnySpanQuery || options.Line != 0 || options.Column != 1 ||
                 options.LineInvariants))
                throw new ArgumentException(
                    "--all-lines cannot be combined with --line, --column, --position, span query options, or --line-invariants.");

            if (options.Position.HasValue && options.LineInvariants)
                throw new ArgumentException("--line-invariants cannot be combined with --position.");

            if (options.RuntimeHazards && options.Position.HasValue)
                throw new ArgumentException(
                    "--runtime-hazards supports --line, --span-start/--span-end, or --all-lines, not --position.");

            if (options.RuntimeHazards && options.InvariantJson)
                throw new ArgumentException("--invariant-json cannot be combined with --runtime-hazards.");

            if (options.RuntimeHazards && options.HasInvariantTargetFilter)
                throw new ArgumentException("--invariant-target cannot be combined with --runtime-hazards.");

            if (options.RuntimeHazards && (options.LineInvariants || options.LineExpressions ||
                                           options.PostLineInvariants || options.Column != 1 ||
                                           options.IsLineColumnSpanQuery))
                throw new ArgumentException(
                    "--runtime-hazards cannot be combined with --line-invariants, --line-expressions, --post-line-invariants, --column, or line/column span options.");

            if (options.RuntimeHazards && (options.ImpliedConditions.Count != 0 || options.CheckReachability ||
                                           options.HasResultFilter))
                throw new ArgumentException(
                    "--runtime-hazards cannot be combined with invariant proof, reachability, or program-point filters.");

            if (options.RuntimeHazards && options.Complexity)
                throw new ArgumentException("--runtime-hazards cannot be combined with --complexity.");

            if (options.RuntimeHazards && options.Capabilities)
                throw new ArgumentException("--runtime-hazards cannot be combined with --capabilities.");

            if (options.Complexity && options.InvariantJson)
                throw new ArgumentException("--invariant-json cannot be combined with --complexity.");

            if (options.Complexity && options.HasInvariantTargetFilter)
                throw new ArgumentException("--invariant-target cannot be combined with --complexity.");

            if (options.Complexity && options.HasCompactOutputLimit)
                throw new ArgumentException(
                    "--max-lines, --max-points, --max-hazards, --max-facts, --max-conditions, and --max-proofs are not supported with --complexity.");

            if (options.Complexity && (options.AllLines || options.IsAnySpanQuery || options.LineInvariants))
                throw new ArgumentException("--complexity supports --line, --line with --column, or --position only.");

            if (options.Complexity &&
                (options.LineExpressions || options.PostLineInvariants || options.HasResultFilter))
                throw new ArgumentException("--complexity cannot be combined with invariant program-point filters.");

            if (options.Complexity && (options.ImpliedConditions.Count != 0 || options.CheckReachability))
                throw new ArgumentException(
                    "--complexity cannot be combined with implied-condition proofs or reachability checks.");

            if (options.LineExpressions && !options.LineInvariants && !options.AllLines && !options.IsAnySpanQuery)
                throw new ArgumentException(
                    "--line-expressions requires --line-invariants, --span-start/--span-end, or --all-lines.");

            if (options.PostLineInvariants && !options.LineInvariants && !options.AllLines && !options.IsAnySpanQuery)
                throw new ArgumentException(
                    "--post-line-invariants requires --line-invariants, --span-start/--span-end, or --all-lines.");

            if (options.FilterLineStart.HasValue &&
                options.FilterLineEnd.HasValue &&
                options.FilterLineStart.Value > options.FilterLineEnd.Value)
                throw new ArgumentException("--line-start cannot be greater than --line-end.");

            if (!options.AllLines && !options.Position.HasValue && !options.IsAnySpanQuery && options.Line == 0)
                throw new ArgumentException("--line, --position, --span-start/--span-end, or --all-lines is required.");

            if (options.Explain)
            {
                options.CheckReachability = true;
                if (options.Json || options.CompactJson || options.InvariantJson)
                    throw new ArgumentException(
                        "explain emits text output and cannot be combined with JSON output options.");

                if (options.RuntimeHazards || options.Complexity || options.Capabilities)
                    throw new ArgumentException(
                        "explain cannot be combined with --runtime-hazards, --complexity, or --capabilities.");

                if (options.AllLines || options.IsAnySpanQuery || options.LineInvariants)
                    throw new ArgumentException("explain supports --line, --line with --column, or --position only.");
            }

            if (options.Complexity && options.Line == 0 && !options.Position.HasValue)
                throw new ArgumentException("--complexity requires --line or --position.");

            if (options.Complexity && options.Capabilities)
                throw new ArgumentException("--complexity cannot be combined with --capabilities.");

            if (options.Capabilities && options.InvariantJson)
                throw new ArgumentException("--invariant-json cannot be combined with --capabilities.");

            if (options.Capabilities && options.HasInvariantTargetFilter)
                throw new ArgumentException("--invariant-target cannot be combined with --capabilities.");

            if (options.Capabilities && options.HasCompactOutputLimit)
                throw new ArgumentException(
                    "--max-lines, --max-points, --max-hazards, --max-facts, --max-conditions, and --max-proofs are not supported with --capabilities.");

            if (options.Capabilities && (options.AllLines || options.IsAnySpanQuery || options.LineInvariants))
                throw new ArgumentException(
                    "--capabilities supports --line, --line with --column, or --position only.");

            if (options.Capabilities &&
                (options.LineExpressions || options.PostLineInvariants || options.HasResultFilter))
                throw new ArgumentException("--capabilities cannot be combined with invariant program-point filters.");

            if (options.Capabilities && (options.ImpliedConditions.Count != 0 || options.CheckReachability))
                throw new ArgumentException(
                    "--capabilities cannot be combined with implied-condition proofs or reachability checks.");

            if (options.Capabilities && options.Line == 0 && !options.Position.HasValue)
                throw new ArgumentException("--capabilities requires --line or --position.");

            if (options.HasResultFilter && !options.AllLines && !options.LineInvariants && !options.IsAnySpanQuery)
                throw new ArgumentException(
                    "Result filters require --line-invariants, --span-start/--span-end, or --all-lines.");

            foreach (var referencePath in options.ReferencePaths)
                if (!File.Exists(referencePath))
                    throw new ArgumentException("--reference does not exist: " + referencePath);
        }

        return options;
    }

    public SymbolicSourceQueryFilter CreateResultFilter()
    {
        return new SymbolicSourceQueryFilter(
            NodeKinds,
            WithFacts,
            ReachabilityFilters,
            MethodNames,
            WithConditions,
            ConditionTargets,
            Conditions,
            ConditionContains,
            MethodNameContains,
            FilterLines,
            FilterLineStart,
            FilterLineEnd,
            ProgramPointKinds,
            WithProofs,
            ProofOutcomes,
            ProofConditions,
            ProofConditionContains);
    }

    public SmtAnalysisOptions CreateSmtOptions()
    {
        return SmtAnalysisOptions.ForMode(SmtMode).WithOverrides(
            SmtTimeoutMs.HasValue ? TimeSpan.FromMilliseconds(SmtTimeoutMs.Value) : null,
            SmtMethodBudgetMs.HasValue ? TimeSpan.FromMilliseconds(SmtMethodBudgetMs.Value) : null,
            SmtMaxPathConditions,
            SmtMaxExpressionNodes);
    }

    public SymbolicQueryOptions CreateQueryOptions(
        SmtAnalysisService? smtAnalysis,
        bool includeResultFilter)
    {
        return new SymbolicQueryOptions(
            CreateReferences(),
            smtAnalysis,
            ImpliedConditions,
            LineExpressions,
            PostLineInvariants,
            includeResultFilter && HasResultFilter ? CreateResultFilter() : null);
    }

    public SymbolicQueryTarget CreateQueryTarget()
    {
        if (AllLines) return SymbolicQueryTarget.AllLines();

        if (LineInvariants)
            return HasColumn
                ? SymbolicQueryTarget.Point(Line, Column)
                : SymbolicQueryTarget.Line(Line);

        if (IsAnySpanQuery)
            return IsLineColumnSpanQuery
                ? SymbolicQueryTarget.LineSpan(
                    SpanStartLine!.Value,
                    SpanStartColumn!.Value,
                    SpanEndLine!.Value,
                    SpanEndColumn!.Value)
                : SymbolicQueryTarget.Span(SpanStart!.Value, SpanEnd!.Value);

        return Position.HasValue
            ? SymbolicQueryTarget.Position(Position.Value)
            : SymbolicQueryTarget.Point(Line, Column);
    }

    public SymbolicQueryTarget CreateRuntimeHazardTarget()
    {
        if (AllLines) return SymbolicQueryTarget.AllLines();

        return IsSpanQuery
            ? SymbolicQueryTarget.Span(SpanStart!.Value, SpanEnd!.Value)
            : SymbolicQueryTarget.Line(Line);
    }

    public SymbolicQueryTarget CreateComplexityTarget()
    {
        return Position.HasValue
            ? SymbolicQueryTarget.Position(Position.Value)
            : HasColumn
                ? SymbolicQueryTarget.Point(Line, Column)
                : SymbolicQueryTarget.Line(Line);
    }

    public SymbolicQueryTarget CreateCapabilityTarget()
    {
        return Position.HasValue
            ? SymbolicQueryTarget.Position(Position.Value)
            : HasColumn
                ? SymbolicQueryTarget.Point(Line, Column)
                : SymbolicQueryTarget.Line(Line);
    }

    public SymbolicCompactQueryOptions CreateCompactOptions()
    {
        return new SymbolicCompactQueryOptions(
            CompactMaxLines,
            CompactMaxProgramPoints,
            CompactMaxFacts,
            CompactMaxConditions,
            CompactMaxProofs,
            InvariantTargets);
    }

    public CompactRuntimeHazardQueryOptions CreateCompactHazardOptions()
    {
        return new CompactRuntimeHazardQueryOptions(
            CompactMaxHazards,
            CompactMaxConditions);
    }

    public SymbolicRuntimeHazardQueryOptions CreateRuntimeHazardOptions()
    {
        return new SymbolicRuntimeHazardQueryOptions(
            IncludeUnprovenHazards,
            HazardKinds);
    }

    public SymbolicRuntimeHazardQueryResult FilterRuntimeHazards(SymbolicRuntimeHazardQueryResult result)
    {
        if (!HasRuntimeHazardFilter) return result;

        var hazards = result.Hazards
            .Where(hazard =>
                (HazardStatuses.Count == 0 || HazardStatuses.Contains(hazard.Status)) &&
                (HazardExceptionTypes.Count == 0 ||
                 HazardExceptionTypes.Contains(hazard.ExceptionType, StringComparer.OrdinalIgnoreCase)) &&
                (HazardCategories.Count == 0 ||
                 HazardCategories.Contains(hazard.Category, StringComparer.OrdinalIgnoreCase)))
            .ToArray();

        return new SymbolicRuntimeHazardQueryResult(
            result.FilePath,
            result.LineCount,
            result.ScopeStart,
            result.ScopeEnd,
            result.Line,
            hazards,
            result.SmtDiagnostics);
    }

    public IEnumerable<MetadataReference>? CreateReferences()
    {
        if (ReferencePaths.Count == 0) return null;

        return ReferencePaths.Select(static path => MetadataReference.CreateFromFile(Path.GetFullPath(path)));
    }

    private static string ReadString(string[] args, ref int index, string optionName)
    {
        if (index + 1 >= args.Length) throw new ArgumentException(optionName + " requires a value.");

        return args[++index];
    }

    private static int ReadInt(string[] args, ref int index, string optionName)
    {
        var value = ReadString(args, ref index, optionName);
        if (!int.TryParse(value, out var parsed))
            throw new ArgumentException(optionName + " requires an integer value.");

        return parsed;
    }

    private static int ReadPositiveInt(string[] args, ref int index, string optionName)
    {
        var parsed = ReadInt(args, ref index, optionName);
        if (parsed <= 0) throw new ArgumentException(optionName + " requires a positive integer value.");

        return parsed;
    }

    private static int ReadNonNegativeInt(string[] args, ref int index, string optionName)
    {
        var parsed = ReadInt(args, ref index, optionName);
        if (parsed < 0) throw new ArgumentException(optionName + " requires a non-negative integer value.");

        return parsed;
    }

    private static SmtAnalysisMode ReadSmtMode(string[] args, ref int index, string optionName)
    {
        var value = ReadString(args, ref index, optionName).Trim().ToLowerInvariant();
        switch (value)
        {
            case "off":
            case "false":
            case "disabled":
                return SmtAnalysisMode.Off;
            case "bounded":
            case "default":
            case "true":
                return SmtAnalysisMode.Bounded;
            case "deep":
            case "aggressive":
                return SmtAnalysisMode.Deep;
            default:
                throw new ArgumentException(optionName + " must be off, bounded, or deep.");
        }
    }

    private static SymbolicRuntimeHazardKind ReadHazardKind(string[] args, ref int index, string optionName)
    {
        var value = ReadString(args, ref index, optionName);
        if (Enum.TryParse<SymbolicRuntimeHazardKind>(value, true, out var kind)) return kind;

        throw new ArgumentException(optionName + " must be one of: " +
                                    string.Join(", ", Enum.GetNames<SymbolicRuntimeHazardKind>()) + ".");
    }

    private static SymbolicRuntimeHazardStatus ReadHazardStatus(string[] args, ref int index, string optionName)
    {
        var value = ReadString(args, ref index, optionName);
        if (Enum.TryParse<SymbolicRuntimeHazardStatus>(value, true, out var status)) return status;

        throw new ArgumentException(optionName + " must be one of: " +
                                    string.Join(", ", Enum.GetNames<SymbolicRuntimeHazardStatus>()) + ".");
    }

    private static SymbolicReachability ReadReachability(string[] args, ref int index, string optionName)
    {
        var value = ReadString(args, ref index, optionName);
        if (Enum.TryParse<SymbolicReachability>(value, true, out var reachability)) return reachability;

        throw new ArgumentException(optionName + " must be NotChecked, Unknown, Reachable, or Unreachable.");
    }

    private static SymbolicTruthValue ReadTruthValue(string[] args, ref int index, string optionName)
    {
        var value = ReadString(args, ref index, optionName);
        if (Enum.TryParse<SymbolicTruthValue>(value, true, out var truthValue)) return truthValue;

        throw new ArgumentException(optionName + " must be Unknown, ProvenTrue, ProvenFalse, or Unreachable.");
    }

    private static string ReadProgramPointKind(string[] args, ref int index, string optionName)
    {
        var value = ReadString(args, ref index, optionName).Trim();
        if (string.Equals(value, SymbolicProgramPointKinds.Statement, StringComparison.OrdinalIgnoreCase))
            return SymbolicProgramPointKinds.Statement;

        if (string.Equals(value, SymbolicProgramPointKinds.Expression, StringComparison.OrdinalIgnoreCase))
            return SymbolicProgramPointKinds.Expression;

        if (string.Equals(value, SymbolicProgramPointKinds.Other, StringComparison.OrdinalIgnoreCase))
            return SymbolicProgramPointKinds.Other;

        throw new ArgumentException(optionName + " must be Statement, Expression, or Other.");
    }
}

internal sealed class CompactSymbolicComplexityResult
{
    private CompactSymbolicComplexityResult(
        string filePath,
        string methodDisplayName,
        string declarationKind,
        int spanStart,
        int spanEnd,
        int startLine,
        int startColumn,
        int endLine,
        int endColumn,
        SymbolicComplexityInfo complexity,
        IReadOnlyList<SymbolicComplexityDriverInfo> drivers,
        IReadOnlyList<SymbolicComplexityUnknownReason> unknownReasons,
        IReadOnlyList<SymbolicComplexityCalleeInfo> calleeSummaries)
    {
        FilePath = filePath;
        MethodDisplayName = methodDisplayName;
        DeclarationKind = declarationKind;
        SpanStart = spanStart;
        SpanEnd = spanEnd;
        StartLine = startLine;
        StartColumn = startColumn;
        EndLine = endLine;
        EndColumn = endColumn;
        Complexity = complexity;
        Drivers = drivers;
        UnknownReasons = unknownReasons;
        CalleeSummaries = calleeSummaries;
    }

    public int SchemaVersion => 1;

    public string Kind => "complexity";

    public string FilePath { get; }

    public string MethodDisplayName { get; }

    public string DeclarationKind { get; }

    public int SpanStart { get; }

    public int SpanEnd { get; }

    public int StartLine { get; }

    public int StartColumn { get; }

    public int EndLine { get; }

    public int EndColumn { get; }

    public SymbolicComplexityInfo Complexity { get; }

    public IReadOnlyList<SymbolicComplexityDriverInfo> Drivers { get; }

    public IReadOnlyList<SymbolicComplexityUnknownReason> UnknownReasons { get; }

    public IReadOnlyList<SymbolicComplexityCalleeInfo> CalleeSummaries { get; }

    public static CompactSymbolicComplexityResult FromResult(SymbolicComplexityResult result)
    {
        return new CompactSymbolicComplexityResult(
            result.FilePath,
            result.MethodDisplayName,
            result.DeclarationKind,
            result.SpanStart,
            result.SpanEnd,
            result.StartLine,
            result.StartColumn,
            result.EndLine,
            result.EndColumn,
            result.Complexity,
            result.Drivers,
            result.UnknownReasons,
            result.CalleeSummaries);
    }
}

internal sealed class CompactSymbolicCapabilityResult
{
    private CompactSymbolicCapabilityResult(
        string filePath,
        string methodDisplayName,
        string declarationKind,
        int spanStart,
        int spanEnd,
        int startLine,
        int startColumn,
        int endLine,
        int endColumn,
        SymbolicCapability capabilities,
        string capabilityText,
        bool hasUnknowns,
        IReadOnlyList<SymbolicCapabilityUnknownReason> unknownReasons,
        IReadOnlyList<SymbolicCapabilitySite> sites)
    {
        FilePath = filePath;
        MethodDisplayName = methodDisplayName;
        DeclarationKind = declarationKind;
        SpanStart = spanStart;
        SpanEnd = spanEnd;
        StartLine = startLine;
        StartColumn = startColumn;
        EndLine = endLine;
        EndColumn = endColumn;
        Capabilities = capabilities;
        CapabilityText = capabilityText;
        HasUnknowns = hasUnknowns;
        UnknownReasons = unknownReasons;
        Sites = sites;
    }

    public int SchemaVersion => 1;

    public string Kind => "capabilities";

    public string FilePath { get; }

    public string MethodDisplayName { get; }

    public string DeclarationKind { get; }

    public int SpanStart { get; }

    public int SpanEnd { get; }

    public int StartLine { get; }

    public int StartColumn { get; }

    public int EndLine { get; }

    public int EndColumn { get; }

    public SymbolicCapability Capabilities { get; }

    public string CapabilityText { get; }

    public bool HasUnknowns { get; }

    public IReadOnlyList<SymbolicCapabilityUnknownReason> UnknownReasons { get; }

    public IReadOnlyList<SymbolicCapabilitySite> Sites { get; }

    public static CompactSymbolicCapabilityResult FromResult(SymbolicCapabilityResult result)
    {
        return new CompactSymbolicCapabilityResult(
            result.FilePath,
            result.MethodDisplayName,
            result.DeclarationKind,
            result.SpanStart,
            result.SpanEnd,
            result.StartLine,
            result.StartColumn,
            result.EndLine,
            result.EndColumn,
            result.Capabilities,
            result.CapabilityText,
            result.HasUnknowns,
            result.UnknownReasons,
            result.Sites);
    }
}

internal sealed class CompactRuntimeHazardQueryOptions
{
    public const int DefaultMaxHazards = 250;

    public CompactRuntimeHazardQueryOptions(
        int maxHazards = DefaultMaxHazards,
        int maxConditions = SymbolicCompactQueryOptions.DefaultMaxConditions)
    {
        if (maxHazards < 0)
            throw new ArgumentOutOfRangeException(nameof(maxHazards),
                "Compact runtime hazard output limits cannot be negative.");

        if (maxConditions < 0)
            throw new ArgumentOutOfRangeException(nameof(maxConditions),
                "Compact runtime hazard output limits cannot be negative.");

        MaxHazards = maxHazards;
        MaxConditions = maxConditions;
    }

    public int MaxHazards { get; }

    public int MaxConditions { get; }
}

internal sealed class CompactRuntimeHazardQueryResult
{
    private CompactRuntimeHazardQueryResult(
        string filePath,
        int lineCount,
        int? line,
        int? scopeStart,
        int? scopeEnd,
        int hazardCount,
        IReadOnlyDictionary<string, int> statusCounts,
        IReadOnlyDictionary<string, int> kindCounts,
        IReadOnlyDictionary<string, int> exceptionTypeCounts,
        IReadOnlyDictionary<string, int> categoryCounts,
        CompactRuntimeHazardStatusSummary analysisSummary,
        IReadOnlyList<CompactRuntimeHazardResult> hazards,
        CompactRuntimeHazardOutputTruncation truncation,
        CompactRuntimeHazardSmtDiagnostics smtDiagnostics)
    {
        FilePath = filePath;
        LineCount = lineCount;
        Line = line;
        ScopeStart = scopeStart;
        ScopeEnd = scopeEnd;
        HazardCount = hazardCount;
        StatusCounts = statusCounts;
        KindCounts = kindCounts;
        ExceptionTypeCounts = exceptionTypeCounts;
        CategoryCounts = categoryCounts;
        AnalysisSummary = analysisSummary;
        Hazards = hazards;
        Truncation = truncation;
        SmtDiagnostics = smtDiagnostics;
    }

    public string Kind => "runtimeHazards";

    public int SchemaVersion => 1;

    public string FilePath { get; }

    public int LineCount { get; }

    public string ScopeKind => Line.HasValue
        ? "line"
        : ScopeStart.HasValue && ScopeEnd.HasValue
            ? "span"
            : "file";

    public int? Line { get; }

    public int? ScopeStart { get; }

    public int? ScopeEnd { get; }

    public int? ScopeLength => ScopeStart.HasValue && ScopeEnd.HasValue
        ? ScopeEnd.Value - ScopeStart.Value
        : null;

    public int HazardCount { get; }

    public IReadOnlyDictionary<string, int> StatusCounts { get; }

    public IReadOnlyDictionary<string, int> KindCounts { get; }

    public IReadOnlyDictionary<string, int> ExceptionTypeCounts { get; }

    public IReadOnlyDictionary<string, int> CategoryCounts { get; }

    public CompactRuntimeHazardStatusSummary AnalysisSummary { get; }

    public IReadOnlyList<CompactRuntimeHazardResult> Hazards { get; }

    public CompactRuntimeHazardOutputTruncation Truncation { get; }

    public CompactRuntimeHazardSmtDiagnostics SmtDiagnostics { get; }

    public static CompactRuntimeHazardQueryResult FromResult(
        SymbolicRuntimeHazardQueryResult result,
        CompactRuntimeHazardQueryOptions options)
    {
        var hazards = result.Hazards
            .Take(options.MaxHazards)
            .Select(hazard => CompactRuntimeHazardResult.FromHazard(hazard, options))
            .ToArray();

        return new CompactRuntimeHazardQueryResult(
            result.FilePath,
            result.LineCount,
            result.Line,
            result.ScopeStart,
            result.ScopeEnd,
            result.HazardCount,
            CountBy(result.Hazards, static hazard => hazard.Status.ToString()),
            CountBy(result.Hazards, static hazard => hazard.Kind.ToString()),
            CountBy(result.Hazards, static hazard => hazard.ExceptionType),
            CountBy(result.Hazards, static hazard => hazard.Category),
            CompactRuntimeHazardStatusSummary.FromHazards(result.Hazards, result.SmtDiagnostics),
            hazards,
            new CompactRuntimeHazardOutputTruncation(
                result.Hazards.Count > hazards.Length,
                hazards.Any(static hazard => hazard.Truncation.PathConditions)),
            CompactRuntimeHazardSmtDiagnostics.FromDiagnostics(result.SmtDiagnostics));
    }

    private static IReadOnlyDictionary<string, int> CountBy(
        IEnumerable<SymbolicRuntimeHazard> hazards,
        Func<SymbolicRuntimeHazard, string> keySelector)
    {
        return hazards
            .GroupBy(keySelector, StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);
    }
}

internal sealed class CompactRuntimeHazardStatusSummary
{
    private CompactRuntimeHazardStatusSummary(
        int hazardCount,
        int provenCount,
        int unknownCount,
        int unreachableCount,
        int unsupportedCount,
        string status,
        string summary,
        bool hasUnprovenHazards,
        bool smtConfigured,
        bool smtEnabled)
    {
        HazardCount = hazardCount;
        ProvenCount = provenCount;
        UnknownCount = unknownCount;
        UnreachableCount = unreachableCount;
        UnsupportedCount = unsupportedCount;
        Status = status;
        Summary = summary;
        HasUnprovenHazards = hasUnprovenHazards;
        SmtConfigured = smtConfigured;
        SmtEnabled = smtEnabled;
    }

    public int HazardCount { get; }

    public int ProvenCount { get; }

    public int UnknownCount { get; }

    public int UnreachableCount { get; }

    public int UnsupportedCount { get; }

    public string Status { get; }

    public string Summary { get; }

    public bool HasUnprovenHazards { get; }

    public bool SmtConfigured { get; }

    public bool SmtEnabled { get; }

    public static CompactRuntimeHazardStatusSummary FromHazards(
        IReadOnlyList<SymbolicRuntimeHazard> hazards,
        SymbolicSmtDiagnostics smtDiagnostics)
    {
        var provenCount = hazards.Count(static hazard => hazard.Status == SymbolicRuntimeHazardStatus.Proven);
        var unknownCount = hazards.Count(static hazard => hazard.Status == SymbolicRuntimeHazardStatus.Unknown);
        var unreachableCount = hazards.Count(static hazard => hazard.Status == SymbolicRuntimeHazardStatus.Unreachable);
        var unsupportedCount = hazards.Count(static hazard => hazard.Status == SymbolicRuntimeHazardStatus.Unsupported);
        var hasUnprovenHazards = unknownCount != 0 || unreachableCount != 0 || unsupportedCount != 0;
        var status = hazards.Count == 0
            ? "NoHazards"
            : hasUnprovenHazards
                ? "ContainsUnproven"
                : "ProvenOnly";
        var summary = hazards.Count == 0
            ? "No runtime hazards matched the query."
            : $"{provenCount} proven, {unknownCount} unknown, {unreachableCount} unreachable, {unsupportedCount} unsupported runtime hazards matched the query.";

        return new CompactRuntimeHazardStatusSummary(
            hazards.Count,
            provenCount,
            unknownCount,
            unreachableCount,
            unsupportedCount,
            status,
            summary,
            hasUnprovenHazards,
            smtDiagnostics.IsConfigured,
            smtDiagnostics.IsEnabled);
    }
}

internal sealed class CompactRuntimeHazardResult
{
    private CompactRuntimeHazardResult(
        SymbolicRuntimeHazardKind kind,
        SymbolicRuntimeHazardStatus status,
        string statusReason,
        string exceptionType,
        string category,
        string filePath,
        int line,
        int column,
        int spanStart,
        int spanEnd,
        int nodeStartLine,
        int nodeStartColumn,
        int nodeEndLine,
        int nodeEndColumn,
        string nodeKind,
        string operationText,
        string triggerCondition,
        SymbolicFactInfo? triggerPrecondition,
        string mergedInvariantText,
        int pathConditionCount,
        IReadOnlyList<string> pathConditions,
        SymbolicReachability reachability,
        string reachabilityReason,
        CompactRuntimeHazardItemTruncation truncation)
    {
        Kind = kind;
        Status = status;
        StatusReason = statusReason;
        ExceptionType = exceptionType;
        Category = category;
        FilePath = filePath;
        Line = line;
        Column = column;
        SpanStart = spanStart;
        SpanEnd = spanEnd;
        SpanLength = spanEnd - spanStart;
        NodeStartLine = nodeStartLine;
        NodeStartColumn = nodeStartColumn;
        NodeEndLine = nodeEndLine;
        NodeEndColumn = nodeEndColumn;
        NodeKind = nodeKind;
        OperationText = operationText;
        TriggerCondition = triggerCondition;
        TriggerPrecondition = triggerPrecondition;
        MergedInvariantText = mergedInvariantText;
        PathConditionCount = pathConditionCount;
        PathConditions = pathConditions;
        Reachability = reachability;
        ReachabilityReason = reachabilityReason;
        Truncation = truncation;
    }

    public SymbolicRuntimeHazardKind Kind { get; }

    public SymbolicRuntimeHazardStatus Status { get; }

    public string StatusReason { get; }

    public string ExceptionType { get; }

    public string Category { get; }

    public string FilePath { get; }

    public int Line { get; }

    public int Column { get; }

    public int SpanStart { get; }

    public int SpanEnd { get; }

    public int SpanLength { get; }

    public int NodeStartLine { get; }

    public int NodeStartColumn { get; }

    public int NodeEndLine { get; }

    public int NodeEndColumn { get; }

    public string NodeKind { get; }

    public string OperationText { get; }

    public string TriggerCondition { get; }

    public SymbolicFactInfo? TriggerPrecondition { get; }

    public string MergedInvariantText { get; }

    public int PathConditionCount { get; }

    public IReadOnlyList<string> PathConditions { get; }

    public SymbolicReachability Reachability { get; }

    public string ReachabilityReason { get; }

    public CompactRuntimeHazardItemTruncation Truncation { get; }

    public static CompactRuntimeHazardResult FromHazard(
        SymbolicRuntimeHazard hazard,
        CompactRuntimeHazardQueryOptions options)
    {
        var pathConditions = hazard.PathConditions
            .Take(options.MaxConditions)
            .ToArray();

        return new CompactRuntimeHazardResult(
            hazard.Kind,
            hazard.Status,
            hazard.StatusReason,
            hazard.ExceptionType,
            hazard.Category,
            hazard.FilePath,
            hazard.Line,
            hazard.Column,
            hazard.SpanStart,
            hazard.SpanEnd,
            hazard.NodeStartLine,
            hazard.NodeStartColumn,
            hazard.NodeEndLine,
            hazard.NodeEndColumn,
            hazard.NodeKind,
            hazard.OperationText,
            hazard.TriggerCondition,
            hazard.TriggerPrecondition,
            hazard.MergedInvariantText,
            hazard.PathConditionCount,
            pathConditions,
            hazard.Reachability,
            hazard.ReachabilityReason,
            new CompactRuntimeHazardItemTruncation(hazard.PathConditions.Count > pathConditions.Length));
    }
}

internal sealed class CompactRuntimeHazardSmtDiagnostics
{
    private CompactRuntimeHazardSmtDiagnostics(
        bool isConfigured,
        string mode,
        bool isEnabled,
        int queryTimeoutMs,
        int methodBudgetMs,
        int maxPathConditions,
        int maxExpressionNodes,
        int executedQueryCount,
        int cacheEntryCount)
    {
        IsConfigured = isConfigured;
        Mode = mode;
        IsEnabled = isEnabled;
        QueryTimeoutMs = queryTimeoutMs;
        MethodBudgetMs = methodBudgetMs;
        MaxPathConditions = maxPathConditions;
        MaxExpressionNodes = maxExpressionNodes;
        ExecutedQueryCount = executedQueryCount;
        CacheEntryCount = cacheEntryCount;
    }

    public bool IsConfigured { get; }

    public string Mode { get; }

    public bool IsEnabled { get; }

    public int QueryTimeoutMs { get; }

    public int MethodBudgetMs { get; }

    public int MaxPathConditions { get; }

    public int MaxExpressionNodes { get; }

    public int ExecutedQueryCount { get; }

    public int CacheEntryCount { get; }

    public static CompactRuntimeHazardSmtDiagnostics FromDiagnostics(SymbolicSmtDiagnostics diagnostics)
    {
        return new CompactRuntimeHazardSmtDiagnostics(
            diagnostics.IsConfigured,
            diagnostics.Mode.ToString(),
            diagnostics.IsEnabled,
            diagnostics.QueryTimeoutMs,
            diagnostics.MethodBudgetMs,
            diagnostics.MaxPathConditions,
            diagnostics.MaxExpressionNodes,
            diagnostics.ExecutedQueryCount,
            diagnostics.CacheEntryCount);
    }
}

internal sealed class CompactRuntimeHazardOutputTruncation
{
    public CompactRuntimeHazardOutputTruncation(
        bool hazards,
        bool pathConditions)
    {
        Hazards = hazards;
        PathConditions = pathConditions;
    }

    public bool Hazards { get; }

    public bool PathConditions { get; }
}

internal sealed class CompactRuntimeHazardItemTruncation
{
    public CompactRuntimeHazardItemTruncation(bool pathConditions)
    {
        PathConditions = pathConditions;
    }

    public bool PathConditions { get; }
}
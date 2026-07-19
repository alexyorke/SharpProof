internal static class SymbolicCliTextRenderer
{
    internal static void PrintFileResult(SymbolicQueryResult result, SymbolicCliOptions options)
    {
        Console.WriteLine(result.FilePath);
        Console.WriteLine($"Total lines: {result.LineCount}");
        Console.WriteLine($"Program points: {result.ProgramPointCount}");
        PrintScopedResult(result, "Merged", options);
    }

    internal static void PrintLineResult(SymbolicQueryResult result, SymbolicCliOptions options)
    {
        Console.WriteLine($"{result.FilePath}:{result.Line}");
        PrintScopedResult(result, "Line", options);
    }

    internal static void PrintSpanResult(SymbolicQueryResult result, SymbolicCliOptions options)
    {
        Console.WriteLine($"{result.FilePath}:{result.SpanStart}-{result.SpanEnd}");
        PrintScopedResult(result, "Span", options);
    }

    private static void PrintScopedResult(
        SymbolicQueryResult result,
        string label,
        SymbolicCliOptions options)
    {
        Console.WriteLine($"Program points: {result.ProgramPoints.Count}");
        Console.WriteLine($"{label} invariant: {result.MergedInvariantText}");
        PrintInvariantQuery(label + " invariant query", SymbolicInvariantQueryView.From(result), options);
        PrintConditionProofSummaries(result.ConditionProofs, options);
        PrintAnalysisTruncation(result.AnalysisTruncation);
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
        Console.WriteLine(result.FilePath);
        Console.WriteLine($"Runtime hazards: {result.HazardCount}");
        PrintCountSummary("Hazard status summary", result.Hazards, static hazard => hazard.Status.ToString());
        PrintCountSummary("Hazard exception summary", result.Hazards, static hazard => hazard.ExceptionType);
        PrintCountSummary("Hazard category summary", result.Hazards, static hazard => hazard.Category);
        PrintAnalysisTruncation(result.AnalysisTruncation);
        foreach (var hazard in result.Hazards)
        {
            Console.WriteLine();
            Console.WriteLine($"{hazard.FilePath}:{hazard.Line}:{hazard.Column} {hazard.Kind} {hazard.Status}");
            Console.WriteLine($"Exception: {hazard.ExceptionType}");
            Console.WriteLine($"Category: {hazard.Category}");
            Console.WriteLine($"Reason: {hazard.GetDisplayStatusReason()}");
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
        var report = await SymbolicCliExplainReport.CreateAsync(options, inputContext, smtAnalysis)
            .ConfigureAwait(false);
        Console.Out.Write(report.ToText());
    }

    internal static void PrintComplexityResult(SymbolicComplexityResult result)
    {
        Console.WriteLine(result.FilePath);
        Console.WriteLine($"Method: {result.MethodDisplayName}");
        Console.WriteLine($"Complexity: {result.Complexity.Text}");
        Console.WriteLine($"Kind: {result.Complexity.Kind}");
        Console.WriteLine($"Conservative: {result.Complexity.IsConservative}");
        if (result.UnknownReasons.Count != 0)
            Console.WriteLine("Unknown reasons: " + string.Join(", ", result.UnknownReasons));
        foreach (var driver in result.Drivers)
            Console.WriteLine($"Driver: {driver.Kind} at {driver.SourceLine}:{driver.SourceColumn} - {driver.Description}");
        foreach (var callee in result.CalleeSummaries)
            Console.WriteLine($"Callee: {callee.MethodDisplayName} => {callee.ComplexityText}");
    }

    internal static void PrintCapabilityResult(SymbolicCapabilityResult result)
    {
        Console.WriteLine(result.FilePath);
        Console.WriteLine($"Method: {result.MethodDisplayName}");
        Console.WriteLine($"Capabilities: {result.CapabilityText}");
        Console.WriteLine($"Conservative: {result.IsConservative}");
        if (result.UnknownReasons.Count != 0)
            Console.WriteLine("Unknown reasons: " + string.Join(", ", result.UnknownReasons));
        foreach (var site in result.Sites)
        {
            var value = site.IsUnknown ? "Unknown" : site.CapabilityText;
            var detail = string.IsNullOrWhiteSpace(site.SymbolDisplayName)
                ? site.OperationText
                : site.SymbolDisplayName;
            Console.WriteLine($"{value}: {site.SiteKind} at {site.SourceLine}:{site.SourceColumn} - {detail}");
        }
    }

    internal static void PrintPointResult(
        SymbolicProgramPointResult result,
        SymbolicCliOptions options,
        bool includeLocation)
    {
        if (includeLocation) Console.WriteLine($"{result.FilePath}:{result.Line}:{result.Column}");
        Console.WriteLine($"Node: {result.NodeKind}");
        Console.WriteLine($"Program point kind: {result.ProgramPointKind}");
        if (!string.IsNullOrWhiteSpace(result.MethodName)) Console.WriteLine($"Method: {result.MethodName}");
        Console.WriteLine($"Merged invariant: {result.MergedInvariantText}");
        Console.WriteLine($"Reachability: {result.Reachability}");
        Console.WriteLine($"Reachability reason: {result.ReachabilityReason}");
        PrintInvariantQuery("Invariant query", SymbolicInvariantQueryView.From(result), options);
        PrintAnalysisTruncation(result.AnalysisTruncation);
        foreach (var proof in result.ConditionProofs)
        {
            Console.WriteLine(
                $"Implies '{proof.Condition}' target={FormatTarget(proof.Target)} " +
                $"kind={proof.Proof.DisplayKind}: {proof.TruthValue}");
            Console.WriteLine($"Implication reason: {proof.GetDisplayReason()}");
        }
        var metrics = SymbolicQueryMetrics.FromProgramPoints(new[] { result });
        Console.WriteLine(
            "Proof outcomes: " +
            $"Total={metrics.ProofTotalCount}, " +
            $"ProvenTrue={metrics.ProofProvenTrueCount}, " +
            $"ProvenFalse={metrics.ProofProvenFalseCount}, " +
            $"Unreachable={metrics.ProofUnreachableCount}, " +
            $"Unknown={metrics.ProofUnknownCount}");
        if (result.SmtDiagnostics.IsConfigured) PrintSmtDiagnostics(result.SmtDiagnostics);
        Console.WriteLine("Facts:");
        foreach (var fact in result.Facts) Console.WriteLine("  " + fact);
        if (result.Facts.Count == 0) Console.WriteLine("  <none>");
    }

    private static void PrintInvariantQuery(
        string label,
        SymbolicInvariantQueryView query,
        SymbolicCliOptions options)
    {
        var targets = SymbolicInvariantTargetFilter.ApplyToTargets(
            query.TargetSummaries,
            options.InvariantTargets,
            static target => target.Target);
        var paths = SymbolicInvariantTargetFilter.ApplyToTargets(
            query.TargetPathSummaries,
            options.InvariantTargets,
            static target => target.Target);
        var must = SymbolicInvariantQueryView.SelectFacts(
            query.MustFacts,
            targets,
            options.InvariantTargets,
            static target => target.MustFacts);
        var maybe = SymbolicInvariantQueryView.SelectFacts(
            query.MaybeFacts,
            targets,
            options.InvariantTargets,
            static target => target.MaybeFacts);
        var unknown = SymbolicInvariantQueryView.SelectFacts(
            query.UnknownFacts,
            targets,
            options.InvariantTargets,
            static target => target.UnknownFacts);
        var text = options.HasInvariantTargetFilter
            ? SymbolicInvariantFactSummary.FormatMergedInvariantFacts(must.Concat(unknown).ToArray())
            : query.Text;
        Console.WriteLine($"{label}: Must={must.Count}, Maybe={maybe.Count}, Unknown={unknown.Count}");
        Console.WriteLine($"{label} text: {text}");
        Console.WriteLine($"{label} status: {query.Status}");
        Console.WriteLine($"{label} status reason: {query.StatusReason}");
        if (options.HasInvariantTargetFilter)
        {
            var matched = query.GetMatchedTargets(options.InvariantTargets);
            var unmatched = SymbolicInvariantTargetFilter.GetUnmatchedTargetFilters(options.InvariantTargets, matched);
            Console.WriteLine($"{label} target filter: {string.Join(", ", options.InvariantTargets)}");
            Console.WriteLine($"{label} target filter matched: {matched.Count != 0}");
            if (matched.Count != 0) Console.WriteLine($"{label} matched target filters: {string.Join(", ", matched)}");
            if (unmatched.Count != 0) Console.WriteLine($"{label} unmatched target filters: {string.Join(", ", unmatched)}");
        }
        foreach (var target in targets.Take(16))
        {
            Console.WriteLine(
                $"{label} target: {target.Target} status={target.Status} " +
                $"reason={target.StatusReason} code={target.ReasonCode}");
            Console.WriteLine($"{label} target summary: {target.Summary}");
        }
        foreach (var path in paths.Take(16))
        {
            Console.WriteLine(
                $"{label} target path: {path.Target} conditions={path.PathConditionCount} " +
                $"smt={path.SmtConditionCount} points={path.ProgramPointCount} " +
                $"reachablePoints={path.ReachableProgramPointCount} proofs={path.ProofTotalCount} " +
                $"unknownProofs={path.ProofUnknownCount} reason={path.StatusReason} code={path.ReasonCode}");
            Console.WriteLine($"{label} target path summary: {path.Summary}");
            if (path.Conditions.Count != 0)
                Console.WriteLine($"{label} target path conditions: {string.Join("; ", path.Conditions)}");
        }
        foreach (var diagnostic in query.Diagnostics)
            Console.WriteLine($"{label} diagnostic: {diagnostic.Code} {diagnostic.Severity} count={diagnostic.Count} {diagnostic.Message}");
    }

    private static void PrintConditionProofSummaries(
        IReadOnlyList<SymbolicConditionProofSummary> proofs,
        SymbolicCliOptions options)
    {
        foreach (var proof in proofs)
        {
            var target = proof.Target;
            if (options.InvariantTargets.Count != 0 &&
                !SymbolicInvariantTargetFilter.Matches(target, options.InvariantTargets))
                continue;
            Console.WriteLine(
                $"Implies '{proof.Condition}' target={FormatTarget(target)} " +
                $"kind={proof.DisplayKind} summary: Status={proof.Status}, " +
                $"ProvenTrue={proof.ProvenTrueCount}, ProvenFalse={proof.ProvenFalseCount}, " +
                $"Unreachable={proof.UnreachableCount}, Unknown={proof.UnknownCount}, " +
                $"Reachable={proof.ReachableCount}, Resolved={proof.ResolvedCount}");
            Console.WriteLine($"  Proof summary: {proof.Summary}");
        }
    }

    private static void PrintCountSummary<T>(string label, IEnumerable<T> values, Func<T, string> key)
    {
        var counts = values.GroupBy(key, StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .Select(static group => $"{group.Key}={group.Count()}");
        Console.WriteLine(label + ": " + (counts.Any() ? string.Join(", ", counts) : "<none>"));
    }

    private static string FormatTarget(string? target) =>
        string.IsNullOrWhiteSpace(target) ? "<unknown>" : target!;

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
        Console.WriteLine($"  Transient retries: {diagnostics.Health.TransientRetryCount}");
        Console.WriteLine($"  Context recycles: {diagnostics.Health.ContextRecycleCount}");
    }

    private static void PrintAnalysisTruncation(SymbolicAnalysisTruncationInfo truncation)
    {
        if (!truncation.IsTruncated) return;
        Console.WriteLine($"Analysis limits hit: {truncation.Events.Count}");
        foreach (var item in truncation.Events)
            Console.WriteLine(
                $"  - {item.Code} limit={item.Limit} observed={item.Observed} " +
                $"provenance={item.Provenance}");
    }
}

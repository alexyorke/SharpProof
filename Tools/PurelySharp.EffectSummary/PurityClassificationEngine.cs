using System.Collections.Immutable;
using System.Text.Json.Serialization;
using PurelySharp.Analyzer.Engine;

internal static class PurityClassificationEngine
{
    private static readonly ImmutableHashSet<string> SafeEffects = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "allocates_array",
        "allocates_box",
        "allocates_object",
        "calls_method");

    private static readonly ImmutableHashSet<string> ConservativeEffects = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "abstract",
        "indirect_call",
        "loads_method_pointer",
        "native_or_internal_call",
        "no_il_body",
        "reads_instance_field",
        "virtual_call");

    private static readonly ImmutableHashSet<string> ImpureRoots = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "caller_visible_memory_write",
        "global_state_read",
        "global_state_write",
        "object_state_write",
        "throw",
        "unsafe_or_block_memory_write");

    private static readonly ImmutableHashSet<string> ConservativeRoots = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "dynamic_dispatch",
        "metadata_only_or_external",
        "pinvoke",
        "runtime_native_or_internal");

    public static PurityClassificationOutput Classify(
        AssemblyEffectReport[] assemblies,
        bool includeCatalogComparison)
    {
        var classifiedAssemblies = assemblies
            .Select(ClassifyAssembly)
            .ToArray();
        var methods = classifiedAssemblies
            .SelectMany(assembly => assembly.Methods)
            .ToArray();
        var report = BuildReport(methods, includeCatalogComparison);
        return new PurityClassificationOutput(classifiedAssemblies, report);
    }

    private static AssemblyEffectReport ClassifyAssembly(AssemblyEffectReport assembly)
    {
        var bySymbol = assembly.Methods
            .GroupBy(method => method.Symbol, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var memo = new Dictionary<string, MethodPurityClassification>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);

        return assembly with
        {
            Methods = assembly.Methods
                .Select(method => method with
                {
                    PurityClassification = ClassifyMethod(method.Symbol, bySymbol, memo, visiting)
                })
                .ToArray()
        };
    }

    private static MethodPurityClassification ClassifyMethod(
        string symbol,
        IReadOnlyDictionary<string, MethodEffectSummary> bySymbol,
        Dictionary<string, MethodPurityClassification> memo,
        HashSet<string> visiting)
    {
        if (memo.TryGetValue(symbol, out var cached))
        {
            return cached;
        }

        if (!bySymbol.TryGetValue(symbol, out var summary))
        {
            return CreateUnknown(
                categories: new[] { "missing_summary" },
                callChain: Array.Empty<string>(),
                summary: null);
        }

        if (!visiting.Add(symbol))
        {
            return CreateUnknown(
                categories: new[] { "recursive_cycle" },
                callChain: new[] { symbol },
                summary: summary);
        }

        var impureCategories = new SortedSet<string>(StringComparer.Ordinal);
        var conservativeCategories = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var root in summary.RootCandidates)
        {
            if (ImpureRoots.Contains(root))
            {
                impureCategories.Add(root);
            }
            else if (ConservativeRoots.Contains(root))
            {
                conservativeCategories.Add(root);
            }
        }

        foreach (var effect in summary.Effects)
        {
            if (SafeEffects.Contains(effect))
            {
                continue;
            }

            if (ConservativeEffects.Contains(effect) || effect.StartsWith("unknown_opcode_at_", StringComparison.Ordinal))
            {
                conservativeCategories.Add(effect.StartsWith("unknown_opcode_at_", StringComparison.Ordinal)
                    ? "unknown_opcode"
                    : effect);
                continue;
            }

            conservativeCategories.Add("unsupported_effect:" + effect);
        }

        string[] blockingCallChain = Array.Empty<string>();
        foreach (var call in summary.Calls)
        {
            if (!bySymbol.ContainsKey(call))
            {
                continue;
            }

            var calleeClassification = ClassifyMethod(call, bySymbol, memo, visiting);
            if (string.Equals(calleeClassification.Classification, "impure", StringComparison.Ordinal))
            {
                impureCategories.Add("impure_callee");
                if (blockingCallChain.Length == 0)
                {
                    blockingCallChain = JoinCallChain(call, calleeClassification.FirstBlockingCallChain);
                }
            }
            else if (string.Equals(calleeClassification.Classification, "conservative_unknown", StringComparison.Ordinal))
            {
                conservativeCategories.Add("unknown_callee");
                if (blockingCallChain.Length == 0)
                {
                    blockingCallChain = JoinCallChain(call, calleeClassification.FirstBlockingCallChain);
                }
            }
        }

        visiting.Remove(symbol);

        MethodPurityClassification result;
        if (impureCategories.Count > 0)
        {
            result = new MethodPurityClassification(
                Classification: "impure",
                Categories: impureCategories.ToArray(),
                FirstBlockingCallChain: blockingCallChain,
                HasFreshArrayAllocationEvidence: summary.Effects.Contains("allocates_array", StringComparer.Ordinal),
                HasFreshObjectAllocationEvidence: summary.Effects.Contains("allocates_object", StringComparer.Ordinal),
                HasUnsupportedEffects: conservativeCategories.Count > 0,
                FreshnessClassification: GetFreshnessClassification(summary, "impure"));
        }
        else if (conservativeCategories.Count > 0)
        {
            result = new MethodPurityClassification(
                Classification: "conservative_unknown",
                Categories: conservativeCategories.ToArray(),
                FirstBlockingCallChain: blockingCallChain,
                HasFreshArrayAllocationEvidence: summary.Effects.Contains("allocates_array", StringComparer.Ordinal),
                HasFreshObjectAllocationEvidence: summary.Effects.Contains("allocates_object", StringComparer.Ordinal),
                HasUnsupportedEffects: true,
                FreshnessClassification: GetFreshnessClassification(summary, "conservative_unknown"));
        }
        else
        {
            result = new MethodPurityClassification(
                Classification: "pure",
                Categories: Array.Empty<string>(),
                FirstBlockingCallChain: Array.Empty<string>(),
                HasFreshArrayAllocationEvidence: summary.Effects.Contains("allocates_array", StringComparer.Ordinal),
                HasFreshObjectAllocationEvidence: summary.Effects.Contains("allocates_object", StringComparer.Ordinal),
                HasUnsupportedEffects: false,
                FreshnessClassification: GetFreshnessClassification(summary, "pure"));
        }

        memo[symbol] = result;
        return result;
    }

    private static string[] JoinCallChain(string callee, IReadOnlyList<string> nested)
    {
        if (nested.Count == 0)
        {
            return new[] { callee };
        }

        var chain = new string[nested.Count + 1];
        chain[0] = callee;
        for (var i = 0; i < nested.Count; i++)
        {
            chain[i + 1] = nested[i];
        }

        return chain;
    }

    private static MethodPurityClassification CreateUnknown(
        IEnumerable<string> categories,
        string[] callChain,
        MethodEffectSummary? summary)
    {
        return new MethodPurityClassification(
            Classification: "conservative_unknown",
            Categories: categories.ToArray(),
            FirstBlockingCallChain: callChain,
            HasFreshArrayAllocationEvidence: summary?.Effects.Contains("allocates_array", StringComparer.Ordinal) == true,
            HasFreshObjectAllocationEvidence: summary?.Effects.Contains("allocates_object", StringComparer.Ordinal) == true,
            HasUnsupportedEffects: true,
            FreshnessClassification: GetFreshnessClassification(summary, "conservative_unknown"));
    }

    private static PurityClassificationReport BuildReport(
        IReadOnlyList<MethodEffectSummary> methods,
        bool includeCatalogComparison)
    {
        var pureCount = methods.Count(static method => string.Equals(
            method.PurityClassification?.Classification,
            "pure",
            StringComparison.Ordinal));
        var impureCount = methods.Count(static method => string.Equals(
            method.PurityClassification?.Classification,
            "impure",
            StringComparison.Ordinal));
        var unknownCount = methods.Count - pureCount - impureCount;

        return new PurityClassificationReport(
            SchemaVersion: 1,
            MethodCount: methods.Count,
            PureCount: pureCount,
            ImpureCount: impureCount,
            ConservativeUnknownCount: unknownCount,
            CatalogComparison: includeCatalogComparison ? BuildCatalogComparison(methods) : null);
    }

    private static CatalogComparisonReport BuildCatalogComparison(IReadOnlyList<MethodEffectSummary> methods)
    {
        var bySymbol = methods.ToDictionary(method => method.Symbol, StringComparer.Ordinal);
        return new CatalogComparisonReport(
            KnownPureMembers: BuildRows(Constants.KnownPureBCLMembers, bySymbol, "known_pure"),
            KnownImpureMembers: BuildRows(Constants.KnownImpureMethods, bySymbol, "known_impure"),
            KnownFreshOwnedArrayReturningMembers: BuildRows(Constants.KnownFreshOwnedArrayReturningMembers, bySymbol, "known_fresh_owned_array"));
    }

    private static CatalogComparisonRow[] BuildRows(
        IEnumerable<string> symbols,
        IReadOnlyDictionary<string, MethodEffectSummary> bySymbol,
        string catalogName)
    {
        return symbols
            .Where(bySymbol.ContainsKey)
            .OrderBy(static symbol => symbol, StringComparer.Ordinal)
            .Select(symbol =>
            {
                var method = bySymbol[symbol];
                var classification = method.PurityClassification;
                var note = catalogName == "known_fresh_owned_array"
                    ? GetFreshArrayNote(classification)
                    : null;
                return new CatalogComparisonRow(
                    Symbol: symbol,
                    Catalog: catalogName,
                    Classification: classification?.Classification ?? "unclassified",
                    Categories: classification?.Categories ?? Array.Empty<string>(),
                    FirstBlockingCallChain: classification?.FirstBlockingCallChain ?? Array.Empty<string>(),
                    Note: note);
            })
            .ToArray();
    }

    private static string? GetFreshArrayNote(MethodPurityClassification? classification)
    {
        if (classification == null)
        {
            return "unclassified";
        }

        if (!string.IsNullOrWhiteSpace(classification.FreshnessClassification) &&
            !string.Equals(classification.FreshnessClassification, "none", StringComparison.Ordinal))
        {
            return classification.FreshnessClassification;
        }

        if (!classification.HasFreshArrayAllocationEvidence)
        {
            return "no_fresh_array_allocation_evidence";
        }

        return classification.Classification == "pure"
            ? "fresh_array_allocation_evidence_present"
            : "fresh_array_allocation_evidence_present_but_not_proven_pure";
    }

    private static string GetFreshnessClassification(MethodEffectSummary? summary, string classification)
    {
        if (summary == null || !summary.Effects.Contains("allocates_array", StringComparer.Ordinal))
        {
            return "none";
        }

        if (!string.Equals(classification, "pure", StringComparison.Ordinal))
        {
            return "fresh_array_candidate_requires_non_pure_resolution";
        }

        var hasDispatchOrOpaqueCall =
            summary.Effects.Contains("virtual_call", StringComparer.Ordinal) ||
            summary.Effects.Contains("indirect_call", StringComparer.Ordinal) ||
            summary.Effects.Contains("loads_method_pointer", StringComparer.Ordinal);
        var hasDirectMethodCall = summary.Effects.Contains("calls_method", StringComparer.Ordinal);
        var hasWrites =
            summary.Effects.Contains("writes_static_field", StringComparer.Ordinal) ||
            summary.Effects.Contains("writes_instance_field", StringComparer.Ordinal) ||
            summary.Effects.Contains("writes_indirect_memory", StringComparer.Ordinal) ||
            summary.Effects.Contains("block_memory_write", StringComparer.Ordinal);

        if (!hasDispatchOrOpaqueCall && !hasDirectMethodCall && !hasWrites)
        {
            return "direct_fresh_array_allocation";
        }

        if (!hasDispatchOrOpaqueCall && !hasWrites)
        {
            return "fresh_array_candidate_via_local_helpers";
        }

        return "fresh_array_candidate_with_unknown_escape_risk";
    }
}

internal sealed record PurityClassificationOutput(
    AssemblyEffectReport[] Assemblies,
    PurityClassificationReport Report);

internal sealed record PurityClassificationReport(
    int SchemaVersion,
    int MethodCount,
    int PureCount,
    int ImpureCount,
    int ConservativeUnknownCount,
    CatalogComparisonReport? CatalogComparison);

internal sealed record CatalogComparisonReport(
    CatalogComparisonRow[] KnownPureMembers,
    CatalogComparisonRow[] KnownImpureMembers,
    CatalogComparisonRow[] KnownFreshOwnedArrayReturningMembers);

internal sealed record CatalogComparisonRow(
    string Symbol,
    string Catalog,
    string Classification,
    string[] Categories,
    string[] FirstBlockingCallChain,
    string? Note);

internal sealed record MethodPurityClassification(
    string Classification,
    string[] Categories,
    string[] FirstBlockingCallChain,
    bool HasFreshArrayAllocationEvidence,
    bool HasFreshObjectAllocationEvidence,
    [property: JsonPropertyName("HasUnsupportedEffects")]
    bool HasUnsupportedEffects,
    string FreshnessClassification);

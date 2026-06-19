using System.Collections.Immutable;
using System.Text;
using System.Text.Json.Serialization;
using PurelySharp.Analyzer.Engine;

internal static class PurityClassificationEngine
{
    private static readonly IReadOnlyDictionary<string, string> EmptyTypeParameterOrdinals =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, string> SpecialTypeAliases =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["System.Boolean"] = "bool",
            ["System.Byte"] = "byte",
            ["System.Char"] = "char",
            ["System.Double"] = "double",
            ["System.Int16"] = "short",
            ["System.Int32"] = "int",
            ["System.Int64"] = "long",
            ["System.IntPtr"] = "nint",
            ["System.Object"] = "object",
            ["System.SByte"] = "sbyte",
            ["System.Single"] = "float",
            ["System.String"] = "string",
            ["System.UInt16"] = "ushort",
            ["System.UInt32"] = "uint",
            ["System.UInt64"] = "ulong",
            ["System.UIntPtr"] = "nuint",
            ["System.Void"] = "void",
        };

    private static readonly ImmutableHashSet<string> SafeEffects = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "allocates_array",
        "allocates_box",
        "allocates_object",
        "calls_method",
        "reads_instance_field");

    private static readonly ImmutableHashSet<string> ConservativeEffects = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "abstract",
        "indirect_call",
        "loads_method_pointer",
        "native_or_internal_call",
        "no_il_body",
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

    private static readonly ImmutableHashSet<string> InternalOnlyRoots = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "safe_static_cache_read",
        "safe_static_constant_read",
        "fresh_owned_memory_write",
        "fresh_owned_object_write");

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
        var generatedPurityCatalog = BuildGeneratedPurityCatalog(classifiedAssemblies);
        return new PurityClassificationOutput(classifiedAssemblies, report, generatedPurityCatalog);
    }

    private static AssemblyEffectReport ClassifyAssembly(AssemblyEffectReport assembly)
    {
        var bySymbol = assembly.Methods
            .GroupBy(method => method.ExactSymbolKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var memo = new Dictionary<string, MethodPurityClassification>(StringComparer.Ordinal);
        var freshOwnedInitializationMemo = new Dictionary<string, bool>(StringComparer.Ordinal);
        var validationThrowHelperMemo = new Dictionary<string, bool>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);

        return assembly with
        {
            Methods = assembly.Methods
                .Select(method => method with
                {
                    PurityClassification = ClassifyMethod(
                        method.ExactSymbolKey,
                        bySymbol,
                        memo,
                        freshOwnedInitializationMemo,
                        validationThrowHelperMemo,
                        visiting)
                })
                .ToArray()
        };
    }

    private static MethodPurityClassification ClassifyMethod(
        string symbol,
        IReadOnlyDictionary<string, MethodEffectSummary> bySymbol,
        Dictionary<string, MethodPurityClassification> memo,
        Dictionary<string, bool> freshOwnedInitializationMemo,
        Dictionary<string, bool> validationThrowHelperMemo,
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

        if (TryClassifyRuntimeIntrinsicStub(summary, out var intrinsicStubClassification))
        {
            visiting.Remove(symbol);
            memo[symbol] = intrinsicStubClassification;
            return intrinsicStubClassification;
        }

        var impureCategories = new SortedSet<string>(StringComparer.Ordinal);
        var conservativeCategories = new SortedSet<string>(StringComparer.Ordinal);

        var treatsObjectStateAsFreshOwned = IsFreshOwnedObjectConstructor(summary);
        var treatsVirtualDispatchAsResolved = HasOnlyResolvedVirtualCallTargets(summary, bySymbol);
        var treatsDeterministicStringComparisonDispatchAsSemantic = HasOnlyDeterministicStringComparisonDispatch(summary);
        var treatsArgumentGuardThrowHelpersAsPure = IsPureArgumentGuardWrapper(summary.Symbol);
        var treatsDelegateDispatchAsSemantic = IsSemanticallyCheckedDelegateInvokingBclMethod(summary.Symbol);
        foreach (var root in summary.RootCandidates)
        {
            if ((treatsVirtualDispatchAsResolved ||
                 treatsDeterministicStringComparisonDispatchAsSemantic ||
                 treatsDelegateDispatchAsSemantic) &&
                string.Equals(root, "dynamic_dispatch", StringComparison.Ordinal))
            {
                continue;
            }

            if (string.Equals(root, "caller_visible_memory_write", StringComparison.Ordinal) &&
                (HasFreshOwnedArrayWritePattern(summary) ||
                 HasFreshOwnedStringWritePattern(summary) ||
                 HasReturnValueInitializationPattern(summary) ||
                 HasLocalScratchMemoryWritePattern(summary) ||
                 HasByRefLikeViewConstructionPattern(summary)))
            {
                continue;
            }

            if (treatsObjectStateAsFreshOwned &&
                string.Equals(root, "object_state_write", StringComparison.Ordinal))
            {
                continue;
            }

            if (ImpureRoots.Contains(root))
            {
                impureCategories.Add(root);
            }
            else if (InternalOnlyRoots.Contains(root))
            {
                continue;
            }
            else if (ConservativeRoots.Contains(root))
            {
                conservativeCategories.Add(root);
            }
        }

        foreach (var effect in summary.Effects)
        {
            if ((treatsVirtualDispatchAsResolved ||
                 treatsDeterministicStringComparisonDispatchAsSemantic ||
                 treatsDelegateDispatchAsSemantic) &&
                string.Equals(effect, "virtual_call", StringComparison.Ordinal))
            {
                continue;
            }

            if (string.Equals(effect, "writes_indirect_memory", StringComparison.Ordinal) &&
                (summary.RootCandidates.Contains("fresh_owned_memory_write", StringComparer.Ordinal) ||
                 HasFreshOwnedArrayWritePattern(summary) ||
                 HasFreshOwnedStringWritePattern(summary) ||
                 HasReturnValueInitializationPattern(summary) ||
                 HasLocalScratchMemoryWritePattern(summary) ||
                 HasByRefLikeViewConstructionPattern(summary)))
            {
                continue;
            }

            if (string.Equals(effect, "writes_instance_field", StringComparison.Ordinal) &&
                (summary.RootCandidates.Contains("fresh_owned_object_write", StringComparer.Ordinal) ||
                 treatsObjectStateAsFreshOwned))
            {
                continue;
            }

            if (string.Equals(effect, "reads_static_field", StringComparison.Ordinal) &&
                (summary.RootCandidates.Contains("safe_static_cache_read", StringComparer.Ordinal) ||
                 summary.RootCandidates.Contains("safe_static_constant_read", StringComparer.Ordinal)))
            {
                continue;
            }

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
        var freshOwnedArrayCalleeSeen = false;
        var freshOwnedObjectCalleeSeen = false;
        foreach (var callSite in EnumerateCallSites(summary))
        {
            var call = callSite.ExactSymbolKey;
            if (IsPurityNeutralIntrinsicHelperCall(call))
            {
                continue;
            }

            if (!TryResolveCallSummary(call, bySymbol, out var resolvedCallKey, out var resolvedCallSummary))
            {
                if (TryClassifyUnresolvedInteropBoundaryCall(call, out var unresolvedInteropCategory))
                {
                    impureCategories.Add(unresolvedInteropCategory);
                    if (blockingCallChain.Length == 0)
                    {
                        blockingCallChain = new[] { call };
                    }
                }

                continue;
            }

            if (visiting.Contains(resolvedCallKey))
            {
                continue;
            }

            var calleeClassification = ClassifyMethod(
                resolvedCallKey,
                bySymbol,
                memo,
                freshOwnedInitializationMemo,
                validationThrowHelperMemo,
                visiting);
            if (ShouldTreatCallAsSemanticallyPure(summary, callSite, resolvedCallSummary, calleeClassification))
            {
                continue;
            }

            if (string.Equals(calleeClassification.Classification, "impure", StringComparison.Ordinal))
            {
                if (((treatsArgumentGuardThrowHelpersAsPure &&
                      IsArgumentGuardThrowHelper(resolvedCallSummary.Symbol)) ||
                     (treatsDelegateDispatchAsSemantic &&
                      IsSemanticallyNeutralValidationThrowHelper(resolvedCallSummary.Symbol))) ||
                    IsValidationThrowHelperCompatible(
                        resolvedCallKey,
                        bySymbol,
                        memo,
                        freshOwnedInitializationMemo,
                        validationThrowHelperMemo,
                        visiting) ||
                    (treatsObjectStateAsFreshOwned &&
                     IsFreshOwnedObjectInitializationCompatible(
                         resolvedCallKey,
                         bySymbol,
                         memo,
                         freshOwnedInitializationMemo,
                         validationThrowHelperMemo,
                         visiting)))
                {
                    continue;
                }

                impureCategories.Add("impure_callee");
                if (blockingCallChain.Length == 0)
                {
                    blockingCallChain = JoinCallChain(resolvedCallSummary.Symbol, calleeClassification.FirstBlockingCallChain);
                }
            }
            else if (string.Equals(calleeClassification.Classification, "conservative_unknown", StringComparison.Ordinal))
            {
                conservativeCategories.Add("unknown_callee");
                if (blockingCallChain.Length == 0)
                {
                    blockingCallChain = JoinCallChain(resolvedCallSummary.Symbol, calleeClassification.FirstBlockingCallChain);
                }
            }
            else if (string.Equals(calleeClassification.Classification, "pure", StringComparison.Ordinal))
            {
                if (string.Equals(calleeClassification.FreshnessClassification, "fresh_owned_array_write", StringComparison.Ordinal))
                {
                    freshOwnedArrayCalleeSeen = true;
                }

                if (string.Equals(calleeClassification.FreshnessClassification, "fresh_owned_object_write", StringComparison.Ordinal))
                {
                    freshOwnedObjectCalleeSeen = true;
                }
            }
        }

        visiting.Remove(symbol);
        MethodPurityClassification result;

        if (HasAllocateUninitializedArrayWrapperPattern(summary))
        {
            result = new MethodPurityClassification(
                Classification: "pure",
                Categories: Array.Empty<string>(),
                FirstBlockingCallChain: Array.Empty<string>(),
                HasFreshArrayAllocationEvidence: true,
                HasFreshObjectAllocationEvidence: summary.Effects.Contains("allocates_object", StringComparer.Ordinal),
                HasUnsupportedEffects: false,
                FreshnessClassification: "fresh_owned_array_write",
                EffectVisibilityClassification: "internal_only");
            memo[symbol] = result;
            return result;
        }

        var hasFreshObjectEvidence =
            summary.Effects.Contains("allocates_object", StringComparer.Ordinal) ||
            treatsObjectStateAsFreshOwned ||
            freshOwnedObjectCalleeSeen;

        if (impureCategories.Count > 0)
        {
            result = new MethodPurityClassification(
                Classification: "impure",
                Categories: impureCategories.ToArray(),
                FirstBlockingCallChain: blockingCallChain,
                HasFreshArrayAllocationEvidence: summary.Effects.Contains("allocates_array", StringComparer.Ordinal),
                HasFreshObjectAllocationEvidence: hasFreshObjectEvidence,
                HasUnsupportedEffects: conservativeCategories.Count > 0,
                FreshnessClassification: GetFreshnessClassification(summary, "impure"),
                EffectVisibilityClassification: GetEffectVisibilityClassification(summary, "impure"));
        }
        else if (conservativeCategories.Count > 0)
        {
            result = new MethodPurityClassification(
                Classification: "conservative_unknown",
                Categories: conservativeCategories.ToArray(),
                FirstBlockingCallChain: blockingCallChain,
                HasFreshArrayAllocationEvidence: summary.Effects.Contains("allocates_array", StringComparer.Ordinal),
                HasFreshObjectAllocationEvidence: hasFreshObjectEvidence,
                HasUnsupportedEffects: true,
                FreshnessClassification: GetFreshnessClassification(summary, "conservative_unknown"),
                EffectVisibilityClassification: GetEffectVisibilityClassification(summary, "conservative_unknown"));
        }
        else
        {
            var freshnessClassification = GetFreshnessClassification(summary, "pure");
            if (string.Equals(freshnessClassification, "none", StringComparison.Ordinal))
            {
                if (freshOwnedArrayCalleeSeen)
                {
                    freshnessClassification = "fresh_owned_array_write";
                }
                else if (freshOwnedObjectCalleeSeen || treatsObjectStateAsFreshOwned)
                {
                    freshnessClassification = "fresh_owned_object_write";
                }
            }

            var effectVisibilityClassification = GetEffectVisibilityClassification(summary, "pure");
            if (string.Equals(effectVisibilityClassification, "none", StringComparison.Ordinal) &&
                !string.Equals(freshnessClassification, "none", StringComparison.Ordinal))
            {
                effectVisibilityClassification = "internal_only";
            }

            result = new MethodPurityClassification(
                Classification: "pure",
                Categories: Array.Empty<string>(),
                FirstBlockingCallChain: Array.Empty<string>(),
                HasFreshArrayAllocationEvidence: HasFreshArrayAllocationEvidence(summary) || freshOwnedArrayCalleeSeen,
                HasFreshObjectAllocationEvidence: hasFreshObjectEvidence,
                HasUnsupportedEffects: false,
                FreshnessClassification: freshnessClassification,
                EffectVisibilityClassification: effectVisibilityClassification);
        }

        memo[symbol] = result;
        return result;
    }

    private static IEnumerable<CallSiteSummary> EnumerateCallSites(MethodEffectSummary summary)
    {
        if (summary.CallSites.Length != 0)
        {
            return summary.CallSites;
        }

        return summary.Calls.Select(static call => new CallSiteSummary(call));
    }

    private static bool ShouldTreatCallAsSemanticallyPure(
        MethodEffectSummary callerSummary,
        CallSiteSummary callSite,
        MethodEffectSummary resolvedCallSummary,
        MethodPurityClassification calleeClassification)
    {
        return IsStableOperatingSystemVersionProbe(callerSummary.Symbol, resolvedCallSummary.Symbol) ||
            !string.Equals(calleeClassification.Classification, "pure", StringComparison.Ordinal) &&
            HasDeterministicStringComparisonEvidence(callSite) &&
            IsContextSensitiveStringComparisonMethod(resolvedCallSummary.ExactSymbolKey);
    }

    private static bool IsStableOperatingSystemVersionProbe(string callerSymbol, string calleeSymbol)
    {
        return string.Equals(callerSymbol, "System.OperatingSystem.IsOSVersionAtLeast(int, int, int, int)", StringComparison.Ordinal) &&
            string.Equals(calleeSymbol, "System.Environment.get_OSVersion()", StringComparison.Ordinal);
    }

    private static bool TryClassifyRuntimeIntrinsicStub(
        MethodEffectSummary summary,
        out MethodPurityClassification classification)
    {
        classification = default!;
        if (string.IsNullOrWhiteSpace(summary.Symbol))
        {
            return false;
        }

        if (IsPureRuntimeIntrinsicStub(summary.Symbol))
        {
            classification = new MethodPurityClassification(
                Classification: "pure",
                Categories: Array.Empty<string>(),
                FirstBlockingCallChain: Array.Empty<string>(),
                HasFreshArrayAllocationEvidence: false,
                HasFreshObjectAllocationEvidence: false,
                HasUnsupportedEffects: false,
                FreshnessClassification: "none",
                EffectVisibilityClassification: "none");
            return true;
        }

        if (summary.Symbol.StartsWith("System.Runtime.CompilerServices.Unsafe.WriteUnaligned(", StringComparison.Ordinal))
        {
            classification = new MethodPurityClassification(
                Classification: "impure",
                Categories: new[] { "caller_visible_memory_write" },
                FirstBlockingCallChain: Array.Empty<string>(),
                HasFreshArrayAllocationEvidence: false,
                HasFreshObjectAllocationEvidence: false,
                HasUnsupportedEffects: false,
                FreshnessClassification: "none",
                EffectVisibilityClassification: "caller_visible");
            return true;
        }

        return false;
    }

    private static bool IsPureRuntimeIntrinsicStub(string symbol)
    {
        return symbol.StartsWith("System.Runtime.CompilerServices.Unsafe.As(", StringComparison.Ordinal) ||
            symbol.StartsWith("System.Runtime.CompilerServices.Unsafe.ReadUnaligned(", StringComparison.Ordinal) ||
            string.Equals(symbol, "System.Runtime.CompilerServices.Unsafe.SizeOf()", StringComparison.Ordinal) ||
            string.Equals(symbol, "System.Object.GetType()", StringComparison.Ordinal) ||
            string.Equals(symbol, "System.Reflection.MemberInfo.get_Name()", StringComparison.Ordinal);
    }

    private static bool HasOnlyDeterministicStringComparisonDispatch(MethodEffectSummary summary)
    {
        if (!summary.Effects.Contains("virtual_call", StringComparer.Ordinal))
        {
            return false;
        }

        var dynamicDispatchCallSites = EnumerateCallSites(summary)
            .Where(static callSite => callSite.UsesDynamicDispatch)
            .ToArray();
        if (dynamicDispatchCallSites.Length == 0)
        {
            return false;
        }

        return dynamicDispatchCallSites.All(static callSite =>
            HasDeterministicStringComparisonEvidence(callSite) &&
            IsContextSensitiveStringComparisonMethod(callSite.ExactSymbolKey));
    }

    private static bool HasDeterministicStringComparisonEvidence(CallSiteSummary callSite)
    {
        foreach (var argumentEvidence in callSite.ArgumentEvidence)
        {
            if (string.Equals(argumentEvidence.Type, "System.StringComparison", StringComparison.Ordinal) &&
                IsDeterministicStringComparisonValue(argumentEvidence.Value))
            {
                return true;
            }

            if (string.Equals(argumentEvidence.Type, "System.StringComparer", StringComparison.Ordinal) &&
                IsDeterministicStringComparerValue(argumentEvidence.Value))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDeterministicStringComparisonValue(string value)
    {
        return string.Equals(value, "System.StringComparison.InvariantCulture", StringComparison.Ordinal) ||
            string.Equals(value, "System.StringComparison.InvariantCultureIgnoreCase", StringComparison.Ordinal) ||
            string.Equals(value, "System.StringComparison.Ordinal", StringComparison.Ordinal) ||
            string.Equals(value, "System.StringComparison.OrdinalIgnoreCase", StringComparison.Ordinal);
    }

    private static bool IsDeterministicStringComparerValue(string value)
    {
        return string.Equals(value, "System.StringComparer.Ordinal", StringComparison.Ordinal) ||
            string.Equals(value, "System.StringComparer.OrdinalIgnoreCase", StringComparison.Ordinal) ||
            string.Equals(value, "System.StringComparer.InvariantCulture", StringComparison.Ordinal) ||
            string.Equals(value, "System.StringComparer.InvariantCultureIgnoreCase", StringComparison.Ordinal);
    }

    private static bool IsContextSensitiveStringComparisonMethod(string exactSymbolKey)
    {
        var methodBaseSymbol = GetMethodBaseSymbol(exactSymbolKey);
        var lastDotIndex = methodBaseSymbol.LastIndexOf('.');
        if (lastDotIndex <= 0 || lastDotIndex == methodBaseSymbol.Length - 1)
        {
            return false;
        }

        var containingType = methodBaseSymbol[..lastDotIndex];
        var methodName = methodBaseSymbol[(lastDotIndex + 1)..];
        return containingType switch
        {
            "string" or "System.String" => methodName is
                "Compare" or
                "Contains" or
                "EndsWith" or
                "Equals" or
                "GetHashCode" or
                "IndexOf" or
                "LastIndexOf" or
                "StartsWith",
            "System.MemoryExtensions" => methodName is
                "CompareTo" or
                "Contains" or
                "EndsWith" or
                "Equals" or
                "IndexOf" or
                "LastIndexOf" or
                "StartsWith",
            "System.StringComparer" or
            "System.OrdinalCaseSensitiveComparer" or
            "System.OrdinalIgnoreCaseComparer" or
            "System.CultureAwareComparer" => methodName is
                "Compare" or
                "Equals" or
                "GetHashCode",
            _ => false
        };
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
            FreshnessClassification: GetFreshnessClassification(summary, "conservative_unknown"),
            EffectVisibilityClassification: GetEffectVisibilityClassification(summary, "conservative_unknown"));
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
            SchemaVersion: 3,
            MethodCount: methods.Count,
            PureCount: pureCount,
            ImpureCount: impureCount,
            ConservativeUnknownCount: unknownCount,
            CatalogComparison: includeCatalogComparison ? BuildCatalogComparison(methods) : null);
    }

    private static CatalogComparisonReport BuildCatalogComparison(IReadOnlyList<MethodEffectSummary> methods)
    {
        var bySymbol = methods
            .GroupBy(method => NormalizeCatalogComparisonKey(method.Symbol), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        return new CatalogComparisonReport(
            KnownPureMembers: BuildRows(Constants.KnownPureBCLMembers, bySymbol, "known_pure"),
            KnownImpureMembers: BuildRows(Constants.KnownImpureMethods, bySymbol, "known_impure"),
            KnownFreshOwnedArrayReturningMembers: BuildRows(Constants.KnownFreshOwnedArrayReturningMembers, bySymbol, "known_fresh_owned_array"));
    }

    private static CatalogComparisonRow[] BuildRows(
        IEnumerable<string> symbols,
        IReadOnlyDictionary<string, MethodEffectSummary[]> bySymbol,
        string catalogName)
    {
        return symbols
            .Where(symbol => bySymbol.ContainsKey(NormalizeCatalogComparisonKey(symbol)))
            .OrderBy(static symbol => symbol, StringComparer.Ordinal)
            .Select(symbol =>
            {
                var matchedMethods = bySymbol[NormalizeCatalogComparisonKey(symbol)];
                var classifications = matchedMethods
                    .Select(static method => method.PurityClassification)
                    .Where(static classification => classification != null)
                    .Cast<MethodPurityClassification>()
                    .ToArray();
                var classification = AggregateCatalogClassification(classifications);
                var note = catalogName == "known_fresh_owned_array"
                    ? GetFreshArrayNote(classification)
                    : null;
                return new CatalogComparisonRow(
                    Symbol: symbol,
                    Catalog: catalogName,
                    Classification: classification?.Classification ?? "unclassified",
                    Categories: classification?.Categories ?? Array.Empty<string>(),
                    FirstBlockingCallChain: classification?.FirstBlockingCallChain ?? Array.Empty<string>(),
                    EffectVisibilityClassification: classification?.EffectVisibilityClassification ?? "unknown",
                    Note: note,
                    MatchedExactSymbolKeys: matchedMethods
                        .Select(static method => method.ExactSymbolKey)
                        .OrderBy(static key => key, StringComparer.Ordinal)
                        .ToArray());
            })
            .ToArray();
    }

    private static GeneratedPurityCatalogDocument BuildGeneratedPurityCatalog(IReadOnlyList<AssemblyEffectReport> assemblies)
    {
        return new GeneratedPurityCatalogDocument(
            SchemaVersion: 2,
            Entries: assemblies
                .SelectMany(assembly => assembly.Methods.Select(method => CreateGeneratedPurityEntry(assembly, method)))
                .OrderBy(static entry => entry.ExactSymbolKey, StringComparer.Ordinal)
                .ToArray());
    }

    private static GeneratedPurityCatalogEntry CreateGeneratedPurityEntry(
        AssemblyEffectReport assembly,
        MethodEffectSummary method)
    {
        var classification = method.PurityClassification ?? CreateUnknown(
            categories: new[] { "missing_classification" },
            callChain: Array.Empty<string>(),
            summary: method);

        return new GeneratedPurityCatalogEntry(
            Symbol: method.Symbol,
            ExactSymbolKey: method.ExactSymbolKey,
            CacheKey: method.CacheKey,
            AssemblyName: assembly.AssemblyName,
            AssemblyPath: assembly.AssemblyPath,
            AssemblySha256: assembly.AssemblySha256,
            ModuleVersionId: assembly.ModuleVersionId,
            MetadataToken: method.MetadataToken,
            MethodBodySha256: method.MethodBodySha256,
            Classification: classification.Classification,
            PrimaryCategory: classification.Categories.FirstOrDefault() ?? "generated_purity_summary",
            Categories: classification.Categories,
            FirstBlockingCallChain: classification.FirstBlockingCallChain,
            HasFreshArrayAllocationEvidence: classification.HasFreshArrayAllocationEvidence,
            HasFreshObjectAllocationEvidence: classification.HasFreshObjectAllocationEvidence,
            HasUnsupportedEffects: classification.HasUnsupportedEffects,
            FreshnessClassification: classification.FreshnessClassification,
            EffectVisibilityClassification: classification.EffectVisibilityClassification);
    }

    private static MethodPurityClassification? AggregateCatalogClassification(IReadOnlyList<MethodPurityClassification> classifications)
    {
        if (classifications.Count == 0)
        {
            return null;
        }

        if (classifications.Count == 1)
        {
            return classifications[0];
        }

        var distinctKinds = classifications
            .Select(static classification => classification.Classification)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var classification = distinctKinds.Length == 1
            ? distinctKinds[0]
            : "mixed";
        var categories = classifications
            .SelectMany(static item => item.Categories)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToArray();
        var blockingCallChain = classifications
            .Select(static item => item.FirstBlockingCallChain)
            .Where(static chain => chain.Length > 0)
            .OrderBy(static chain => string.Join(">", chain), StringComparer.Ordinal)
            .FirstOrDefault() ?? Array.Empty<string>();

        return new MethodPurityClassification(
            Classification: classification,
            Categories: categories,
            FirstBlockingCallChain: blockingCallChain,
            HasFreshArrayAllocationEvidence: classifications.Any(static item => item.HasFreshArrayAllocationEvidence),
            HasFreshObjectAllocationEvidence: classifications.Any(static item => item.HasFreshObjectAllocationEvidence),
            HasUnsupportedEffects: classifications.Any(static item => item.HasUnsupportedEffects),
            FreshnessClassification: AggregateFreshnessClassification(classifications),
            EffectVisibilityClassification: AggregateEffectVisibilityClassification(classifications));
    }

    private static string AggregateFreshnessClassification(IReadOnlyList<MethodPurityClassification> classifications)
    {
        var values = classifications
            .Select(static classification => classification.FreshnessClassification)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return values.Length == 1 ? values[0] : "multiple_exact_matches";
    }

    private static string NormalizeCatalogSymbol(string symbol)
    {
        var normalized = symbol.Trim();
        normalized = NormalizePropertyAccessorSymbol(normalized);
        normalized = NormalizeMethodSymbol(normalized);
        foreach (var pair in SpecialTypeAliases)
        {
            normalized = normalized.Replace(pair.Key, pair.Value, StringComparison.Ordinal);
        }

        return normalized;
    }

    private static string NormalizeCatalogComparisonKey(string symbol)
    {
        if (TryNormalizeAccessorComparisonKey(symbol, out var comparisonKey))
        {
            return comparisonKey;
        }

        return NormalizeCatalogSymbol(symbol);
    }

    private static bool TryNormalizeAccessorComparisonKey(string symbol, out string comparisonKey)
    {
        if (TryNormalizeCatalogAccessorComparisonKey(symbol, out comparisonKey))
        {
            return true;
        }

        if (TryNormalizeRuntimeAccessorComparisonKey(symbol, out comparisonKey))
        {
            return true;
        }

        comparisonKey = string.Empty;
        return false;
    }

    private static string NormalizePropertyAccessorSymbol(string symbol)
    {
        var suffix = symbol.EndsWith(".get", StringComparison.Ordinal)
            ? ".get"
            : symbol.EndsWith(".set", StringComparison.Ordinal)
                ? ".set"
                : null;
        if (suffix == null)
        {
            return symbol;
        }

        var memberSeparator = FindLastTopLevelDot(symbol, symbol.Length - suffix.Length);
        if (memberSeparator < 0)
        {
            return symbol;
        }

        var containingType = symbol.Substring(0, memberSeparator);
        var propertyName = symbol.Substring(
            memberSeparator + 1,
            symbol.Length - memberSeparator - suffix.Length - 1);
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return symbol;
        }

        var normalizedContainingType = NormalizeContainingTypeDefinition(containingType, out _);
        var accessorPrefix = string.Equals(suffix, ".get", StringComparison.Ordinal)
            ? "get_"
            : "set_";
        return normalizedContainingType + "." + accessorPrefix + propertyName + "()";
    }

    private static bool TryNormalizeCatalogAccessorComparisonKey(string symbol, out string comparisonKey)
    {
        var suffix = symbol.EndsWith(".get", StringComparison.Ordinal)
            ? ".get"
            : symbol.EndsWith(".set", StringComparison.Ordinal)
                ? ".set"
                : null;
        if (suffix == null)
        {
            comparisonKey = string.Empty;
            return false;
        }

        var memberSeparator = FindLastTopLevelDot(symbol, symbol.Length - suffix.Length);
        if (memberSeparator < 0)
        {
            comparisonKey = string.Empty;
            return false;
        }

        var containingType = symbol.Substring(0, memberSeparator);
        var propertyName = symbol.Substring(
            memberSeparator + 1,
            symbol.Length - memberSeparator - suffix.Length - 1);
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            comparisonKey = string.Empty;
            return false;
        }

        var normalizedContainingType = NormalizeContainingTypeDefinition(containingType, out var typeParameterOrdinals);
        var normalizedPropertyName = propertyName;
        var normalizedIndexParameterList = string.Empty;
        if (TryParseCatalogIndexer(propertyName, out var indexParameterList))
        {
            normalizedPropertyName = "Item";
            normalizedIndexParameterList = NormalizeParameterList(
                indexParameterList,
                typeParameterOrdinals,
                EmptyTypeParameterOrdinals);
        }

        comparisonKey = BuildAccessorComparisonKey(
            normalizedContainingType,
            string.Equals(suffix, ".get", StringComparison.Ordinal) ? "get" : "set",
            normalizedPropertyName,
            normalizedIndexParameterList);
        return true;
    }

    private static bool TryNormalizeRuntimeAccessorComparisonKey(string symbol, out string comparisonKey)
    {
        var openParen = symbol.IndexOf('(');
        if (openParen < 0 || !symbol.EndsWith(")", StringComparison.Ordinal))
        {
            comparisonKey = string.Empty;
            return false;
        }

        var memberSeparator = FindLastTopLevelDot(symbol, openParen);
        if (memberSeparator < 0)
        {
            comparisonKey = string.Empty;
            return false;
        }

        var containingType = symbol.Substring(0, memberSeparator);
        var memberName = symbol.Substring(memberSeparator + 1, openParen - memberSeparator - 1);
        var accessorKind = memberName.StartsWith("get_", StringComparison.Ordinal)
            ? "get"
            : memberName.StartsWith("set_", StringComparison.Ordinal)
                ? "set"
                : null;
        if (accessorKind == null)
        {
            comparisonKey = string.Empty;
            return false;
        }

        var normalizedContainingType = NormalizeContainingTypeDefinition(containingType, out var typeParameterOrdinals);
        var parameterList = symbol.Substring(openParen + 1, symbol.Length - openParen - 2);
        var normalizedParameterList = NormalizeParameterList(
            parameterList,
            typeParameterOrdinals,
            EmptyTypeParameterOrdinals);
        comparisonKey = BuildAccessorComparisonKey(
            normalizedContainingType,
            accessorKind,
            memberName.Substring(4),
            string.Equals(accessorKind, "set", StringComparison.Ordinal)
                ? TrimTrailingParameter(normalizedParameterList)
                : normalizedParameterList);
        return true;
    }

    private static bool TryParseCatalogIndexer(string propertyName, out string indexParameterList)
    {
        if (propertyName.StartsWith("this[", StringComparison.Ordinal) &&
            propertyName.EndsWith("]", StringComparison.Ordinal) &&
            propertyName.Length > "this[]".Length)
        {
            indexParameterList = propertyName.Substring(5, propertyName.Length - 6);
            return true;
        }

        indexParameterList = string.Empty;
        return false;
    }

    private static string BuildAccessorComparisonKey(
        string containingType,
        string accessorKind,
        string propertyName,
        string parameterList)
    {
        return containingType + "|" + accessorKind + "|" + propertyName + "|" + parameterList;
    }

    private static string NormalizeMethodSymbol(string symbol)
    {
        var openParen = symbol.IndexOf('(');
        if (openParen < 0 || !symbol.EndsWith(")", StringComparison.Ordinal))
        {
            return symbol;
        }

        var memberSeparator = FindLastTopLevelDot(symbol, openParen);
        if (memberSeparator < 0)
        {
            return symbol;
        }

        var containingType = symbol.Substring(0, memberSeparator);
        var memberName = symbol.Substring(memberSeparator + 1, openParen - memberSeparator - 1);
        var parameterList = symbol.Substring(openParen + 1, symbol.Length - openParen - 2);
        var normalizedContainingType = NormalizeContainingTypeDefinition(containingType, out var typeParameterOrdinals);
        var normalizedMethodName = NormalizeMethodDefinition(memberName, out var methodParameterOrdinals);
        var simpleContainingTypeName = GetSimpleTypeName(containingType);
        var normalizedMemberName = string.Equals(normalizedMethodName, simpleContainingTypeName, StringComparison.Ordinal)
            ? ".ctor"
            : normalizedMethodName;
        var normalizedParameterList = NormalizeParameterList(
            parameterList,
            typeParameterOrdinals,
            methodParameterOrdinals);
        return normalizedContainingType + "." + normalizedMemberName + "(" + normalizedParameterList + ")";
    }

    private static string NormalizeMethodDefinition(
        string memberName,
        out IReadOnlyDictionary<string, string> methodParameterOrdinals)
    {
        methodParameterOrdinals = EmptyTypeParameterOrdinals;
        if (!TryParseGenericType(memberName, out var baseName, out var genericArguments))
        {
            return memberName;
        }

        var ordinals = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < genericArguments.Length; i++)
        {
            var genericArgument = genericArguments[i];
            if (IsSimpleIdentifier(genericArgument))
            {
                ordinals[genericArgument] = "!!" + i;
            }
        }

        if (ordinals.Count > 0)
        {
            methodParameterOrdinals = ordinals;
        }

        return baseName;
    }

    private static string NormalizeContainingTypeDefinition(
        string containingType,
        out IReadOnlyDictionary<string, string> typeParameterOrdinals)
    {
        typeParameterOrdinals = EmptyTypeParameterOrdinals;
        var lastTypeSeparator = containingType.LastIndexOfAny(new[] { '.', '+' });
        var prefix = lastTypeSeparator >= 0 ? containingType.Substring(0, lastTypeSeparator + 1) : string.Empty;
        var simpleTypeName = lastTypeSeparator >= 0 ? containingType.Substring(lastTypeSeparator + 1) : containingType;
        if (!TryParseGenericType(simpleTypeName, out var baseName, out var genericArguments))
        {
            return containingType;
        }

        var ordinals = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < genericArguments.Length; i++)
        {
            var genericArgument = genericArguments[i];
            if (IsSimpleIdentifier(genericArgument))
            {
                ordinals[genericArgument] = "!" + i;
            }
        }

        if (ordinals.Count > 0)
        {
            typeParameterOrdinals = ordinals;
        }

        return prefix + NormalizeGenericTypeBaseName(baseName, genericArguments.Length);
    }

    private static string NormalizeGenericTypeBaseName(string baseName, int arity)
    {
        var existingAritySeparator = baseName.LastIndexOf('`');
        if (existingAritySeparator >= 0 &&
            existingAritySeparator + 1 < baseName.Length &&
            int.TryParse(baseName.Substring(existingAritySeparator + 1), out var existingArity))
        {
            return existingArity == arity
                ? baseName
                : baseName.Substring(0, existingAritySeparator) + "`" + arity;
        }

        return baseName + "`" + arity;
    }

    private static bool TryParseGenericType(string typeName, out string baseName, out string[] genericArguments)
    {
        baseName = typeName;
        genericArguments = Array.Empty<string>();
        var genericStart = typeName.IndexOf('<');
        if (genericStart < 0 || !typeName.EndsWith(">", StringComparison.Ordinal))
        {
            return false;
        }

        baseName = typeName.Substring(0, genericStart).Trim();
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = typeName;
            return false;
        }

        genericArguments = SplitTopLevelArguments(typeName.Substring(genericStart + 1, typeName.Length - genericStart - 2));
        return genericArguments.Length > 0;
    }

    private static string[] SplitTopLevelArguments(string text)
    {
        var arguments = new List<string>();
        var start = 0;
        var depth = 0;
        for (var i = 0; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '<':
                    depth++;
                    break;
                case '>':
                    depth = Math.Max(0, depth - 1);
                    break;
                case ',' when depth == 0:
                    arguments.Add(text.Substring(start, i - start).Trim());
                    start = i + 1;
                    break;
            }
        }

        arguments.Add(text.Substring(start).Trim());
        return arguments
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
    }

    private static string NormalizeParameterList(
        string text,
        IReadOnlyDictionary<string, string> typeParameterOrdinals,
        IReadOnlyDictionary<string, string> methodParameterOrdinals)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var parameters = SplitTopLevelArguments(text);
        if (parameters.Length == 0)
        {
            return string.Empty;
        }

        return string.Join(
            ", ",
            parameters.Select(parameter => NormalizeTypeExpression(
                parameter,
                typeParameterOrdinals,
                methodParameterOrdinals)));
    }

    private static string NormalizeTypeExpression(
        string text,
        IReadOnlyDictionary<string, string> typeParameterOrdinals,
        IReadOnlyDictionary<string, string> methodParameterOrdinals)
    {
        var trimmed = text.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return trimmed;
        }

        foreach (var modifier in new[]
                 {
                     "scoped ref ",
                     "ref readonly ",
                     "scoped in ",
                     "params ",
                     "scoped ",
                     "ref ",
                     "out ",
                     "in ",
                 })
        {
            if (trimmed.StartsWith(modifier, StringComparison.Ordinal))
            {
                return modifier + NormalizeTypeExpression(
                    trimmed.Substring(modifier.Length),
                    typeParameterOrdinals,
                    methodParameterOrdinals);
            }
        }

        var suffix = string.Empty;
        while (true)
        {
            if (trimmed.EndsWith("[]", StringComparison.Ordinal))
            {
                suffix = "[]" + suffix;
                trimmed = trimmed.Substring(0, trimmed.Length - 2).TrimEnd();
                continue;
            }

            if (trimmed.EndsWith("?", StringComparison.Ordinal) ||
                trimmed.EndsWith("*", StringComparison.Ordinal))
            {
                suffix = trimmed[^1] + suffix;
                trimmed = trimmed.Substring(0, trimmed.Length - 1).TrimEnd();
                continue;
            }

            break;
        }

        if (TryParseGenericType(trimmed, out _, out var genericArguments))
        {
            var normalizedBase = NormalizeContainingTypeDefinition(trimmed, out _);
            var normalizedArguments = genericArguments
                .Select(argument => NormalizeTypeExpression(
                    argument,
                    typeParameterOrdinals,
                    methodParameterOrdinals));
            return normalizedBase + "<" + string.Join(", ", normalizedArguments) + ">" + suffix;
        }

        return ReplaceTypeParameterTokens(trimmed, typeParameterOrdinals, methodParameterOrdinals) + suffix;
    }

    private static string TrimTrailingParameter(string parameterList)
    {
        var parameters = SplitTopLevelArguments(parameterList);
        return parameters.Length <= 1
            ? string.Empty
            : string.Join(", ", parameters.Take(parameters.Length - 1));
    }

    private static string ReplaceTypeParameterTokens(
        string text,
        IReadOnlyDictionary<string, string> typeParameterOrdinals,
        IReadOnlyDictionary<string, string> methodParameterOrdinals)
    {
        if (string.IsNullOrEmpty(text) ||
            (typeParameterOrdinals.Count == 0 && methodParameterOrdinals.Count == 0))
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length;)
        {
            if (!IsIdentifierStart(text[i]))
            {
                builder.Append(text[i]);
                i++;
                continue;
            }

            var start = i;
            i++;
            while (i < text.Length && IsIdentifierPart(text[i]))
            {
                i++;
            }

            var token = text.Substring(start, i - start);
            var hasTypeReplacement = typeParameterOrdinals.TryGetValue(token, out var typeReplacement);
            var hasMethodReplacement = methodParameterOrdinals.TryGetValue(token, out var methodReplacement);
            if (hasTypeReplacement && hasMethodReplacement && !string.Equals(typeReplacement, methodReplacement, StringComparison.Ordinal))
            {
                builder.Append(token);
                continue;
            }

            builder.Append(hasMethodReplacement
                ? methodReplacement
                : hasTypeReplacement
                    ? typeReplacement
                    : token);
        }

        return builder.ToString();
    }

    private static int FindLastTopLevelDot(string text, int exclusiveUpperBound)
    {
        var depth = 0;
        var lastDot = -1;
        for (var i = 0; i < exclusiveUpperBound; i++)
        {
            switch (text[i])
            {
                case '<':
                    depth++;
                    break;
                case '>':
                    depth = Math.Max(0, depth - 1);
                    break;
                case '.' when depth == 0:
                    lastDot = i;
                    break;
            }
        }

        return lastDot;
    }

    private static string GetSimpleTypeName(string containingType)
    {
        var lastTypeSeparator = containingType.LastIndexOfAny(new[] { '.', '+' });
        var simpleTypeName = lastTypeSeparator >= 0 ? containingType.Substring(lastTypeSeparator + 1) : containingType;
        var genericStart = simpleTypeName.IndexOf('<');
        return genericStart >= 0 ? simpleTypeName.Substring(0, genericStart) : simpleTypeName;
    }

    private static bool IsSimpleIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !IsIdentifierStart(value[0]))
        {
            return false;
        }

        for (var i = 1; i < value.Length; i++)
        {
            if (!IsIdentifierPart(value[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsIdentifierStart(char value) => char.IsLetter(value) || value == '_';

    private static bool IsIdentifierPart(char value) => char.IsLetterOrDigit(value) || value == '_';

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
        if (summary == null)
        {
            return "none";
        }

        if (summary.RootCandidates.Contains("fresh_owned_object_write", StringComparer.Ordinal))
        {
            return string.Equals(classification, "pure", StringComparison.Ordinal)
                ? "fresh_owned_object_write"
                : "fresh_object_candidate_requires_non_pure_resolution";
        }

        if (!HasFreshArrayAllocationEvidence(summary))
        {
            return "none";
        }

        if (!string.Equals(classification, "pure", StringComparison.Ordinal))
        {
            return "fresh_array_candidate_requires_non_pure_resolution";
        }

        if (summary.RootCandidates.Contains("fresh_owned_memory_write", StringComparer.Ordinal))
        {
            return "fresh_owned_array_write";
        }

        if (HasFreshOwnedArrayWritePattern(summary))
        {
            return "fresh_owned_array_write";
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

    private static bool HasFreshOwnedArrayWritePattern(MethodEffectSummary? summary)
    {
        if (summary == null)
        {
            return false;
        }

        if (summary.Effects.Contains("allocates_array", StringComparer.Ordinal) &&
            summary.Effects.Contains("writes_indirect_memory", StringComparer.Ordinal))
        {
            if (summary.Effects.Contains("writes_static_field", StringComparer.Ordinal) ||
                summary.Effects.Contains("writes_instance_field", StringComparer.Ordinal) ||
                summary.Effects.Contains("reads_instance_field", StringComparer.Ordinal) ||
                summary.Effects.Contains("indirect_call", StringComparer.Ordinal) ||
                summary.Effects.Contains("virtual_call", StringComparer.Ordinal) ||
                summary.Effects.Contains("block_memory_write", StringComparer.Ordinal))
            {
                return false;
            }

            if (!HasOnlySafeStaticReads(summary))
            {
                return false;
            }

            return true;
        }

        return HasAllocateUninitializedArrayWrapperPattern(summary);
    }

    private static bool HasFreshOwnedStringWritePattern(MethodEffectSummary? summary)
    {
        if (summary == null ||
            !summary.Effects.Contains("allocates_object", StringComparer.Ordinal) ||
            !summary.Effects.Contains("writes_indirect_memory", StringComparer.Ordinal))
        {
            return false;
        }

        if (!summary.Calls.Any(static call =>
                string.Equals(call, "string.FastAllocateString(int)->string", StringComparison.Ordinal)))
        {
            return false;
        }

        if (summary.Effects.Contains("allocates_array", StringComparer.Ordinal) ||
            summary.Effects.Contains("writes_static_field", StringComparer.Ordinal) ||
            summary.Effects.Contains("writes_instance_field", StringComparer.Ordinal) ||
            summary.Effects.Contains("indirect_call", StringComparer.Ordinal) ||
            summary.Effects.Contains("virtual_call", StringComparer.Ordinal) ||
            summary.Effects.Contains("block_memory_write", StringComparer.Ordinal))
        {
            return false;
        }

        if (!HasOnlySafeStaticReads(summary))
        {
            return false;
        }

        foreach (var field in summary.Fields)
        {
            if (string.Equals(field, "System.String.Empty", StringComparison.Ordinal) ||
                string.Equals(field, "System.String._firstChar", StringComparison.Ordinal))
            {
                continue;
            }

            return false;
        }

        return true;
    }

    private static bool HasLocalScratchMemoryWritePattern(MethodEffectSummary? summary)
    {
        if (summary == null ||
            !summary.Effects.Contains("writes_indirect_memory", StringComparer.Ordinal))
        {
            return false;
        }

        if (summary.Effects.Contains("allocates_array", StringComparer.Ordinal) ||
            summary.Effects.Contains("writes_static_field", StringComparer.Ordinal) ||
            summary.Effects.Contains("writes_instance_field", StringComparer.Ordinal) ||
            summary.Effects.Contains("reads_instance_field", StringComparer.Ordinal) ||
            summary.Effects.Contains("reads_static_field", StringComparer.Ordinal) ||
            summary.Effects.Contains("indirect_call", StringComparer.Ordinal) ||
            summary.Effects.Contains("virtual_call", StringComparer.Ordinal) ||
            summary.Effects.Contains("block_memory_write", StringComparer.Ordinal))
        {
            return false;
        }

        return summary.Calls.Any(static call =>
            call.StartsWith("System.Collections.Generic.ValueListBuilder`1<", StringComparison.Ordinal) ||
            call.StartsWith("System.Text.ValueStringBuilder.", StringComparison.Ordinal));
    }

    private static bool HasReturnValueInitializationPattern(MethodEffectSummary? summary)
    {
        if (summary == null ||
            !summary.Effects.Contains("writes_indirect_memory", StringComparer.Ordinal))
        {
            return false;
        }

        foreach (var effect in summary.Effects)
        {
            if (!string.Equals(effect, "writes_indirect_memory", StringComparison.Ordinal))
            {
                return false;
            }
        }

        if (summary.Calls.Length != 0 || summary.Fields.Length != 0)
        {
            return false;
        }

        return HasParameterlessNonVoidReturn(summary.ExactSymbolKey);
    }

    private static bool HasByRefLikeViewConstructionPattern(MethodEffectSummary? summary)
    {
        if (summary == null ||
            !summary.Effects.Contains("writes_indirect_memory", StringComparer.Ordinal))
        {
            return false;
        }

        var allowsTupleOffsetReads = HasOnlyByRefLikeViewHelperFieldReads(summary);
        foreach (var effect in summary.Effects)
        {
            if (string.Equals(effect, "allocates_object", StringComparison.Ordinal) ||
                string.Equals(effect, "calls_method", StringComparison.Ordinal) ||
                string.Equals(effect, "writes_indirect_memory", StringComparison.Ordinal) ||
                string.Equals(effect, "reads_instance_field", StringComparison.Ordinal) && allowsTupleOffsetReads)
            {
                continue;
            }

            return false;
        }

        var sawByRefLikeConstructor = false;
        foreach (var call in summary.Calls)
        {
            if (IsByRefLikeViewConstructionCall(call))
            {
                sawByRefLikeConstructor = true;
                continue;
            }

            if (IsByRefLikeViewConstructionHelperCall(call))
            {
                continue;
            }

            if (IsPurityNeutralIntrinsicHelperCall(call))
            {
                continue;
            }

            return false;
        }

        return sawByRefLikeConstructor;
    }

    private static bool HasOnlyByRefLikeViewHelperFieldReads(MethodEffectSummary summary)
    {
        if (!summary.Effects.Contains("reads_instance_field", StringComparer.Ordinal))
        {
            return true;
        }

        return summary.Fields.All(static field =>
            field.StartsWith("System.ValueTuple", StringComparison.Ordinal) &&
            (field.EndsWith(".Item1", StringComparison.Ordinal) ||
             field.EndsWith(".Item2", StringComparison.Ordinal)));
    }

    private static bool HasOnlySafeStaticReads(MethodEffectSummary summary)
    {
        if (!summary.Effects.Contains("reads_static_field", StringComparer.Ordinal))
        {
            return true;
        }

        return summary.Fields.All(static field =>
            string.Equals(field, "System.String.Empty", StringComparison.Ordinal) ||
            string.Equals(field, "System.String._firstChar", StringComparison.Ordinal) ||
            string.Equals(field, "System.UriHelper.Unreserved", StringComparison.Ordinal) ||
            string.Equals(field, "System.Globalization.TextInfo.Invariant", StringComparison.Ordinal) ||
            string.Equals(field, "System.Globalization.CompareInfo.Invariant", StringComparison.Ordinal) ||
            string.Equals(field, "System.BitConverter.IsLittleEndian", StringComparison.Ordinal) ||
            string.Equals(field, "IsLittleEndian", StringComparison.Ordinal) ||
            field.StartsWith("System.Linq.EmptyPartition`1", StringComparison.Ordinal) &&
            field.EndsWith(".Instance", StringComparison.Ordinal) ||
            field.StartsWith("System.Array+EmptyArray`1", StringComparison.Ordinal) &&
            field.EndsWith(".Value", StringComparison.Ordinal));
    }

    private static bool HasOnlyResolvedVirtualCallTargets(
        MethodEffectSummary summary,
        IReadOnlyDictionary<string, MethodEffectSummary> bySymbol)
    {
        if (!summary.Effects.Contains("virtual_call", StringComparer.Ordinal) ||
            summary.Calls.Length == 0 ||
            summary.Effects.Contains("indirect_call", StringComparer.Ordinal) ||
            summary.Effects.Contains("abstract", StringComparer.Ordinal) ||
            summary.Effects.Contains("no_il_body", StringComparer.Ordinal) ||
            summary.RootCandidates.Contains("metadata_only_or_external", StringComparer.Ordinal) ||
            summary.RootCandidates.Contains("pinvoke", StringComparer.Ordinal) ||
            summary.RootCandidates.Contains("runtime_native_or_internal", StringComparer.Ordinal))
        {
            return false;
        }

        var sawResolvedCall = false;
        foreach (var call in summary.Calls)
        {
            if (IsPurityNeutralIntrinsicHelperCall(call))
            {
                continue;
            }

            if (!TryResolveCallSummary(call, bySymbol, out _, out _))
            {
                return false;
            }

            sawResolvedCall = true;
        }

        return sawResolvedCall;
    }

    private static bool TryResolveCallSummary(
        string call,
        IReadOnlyDictionary<string, MethodEffectSummary> bySymbol,
        out string resolvedCallKey,
        out MethodEffectSummary resolvedCallSummary)
    {
        if (bySymbol.TryGetValue(call, out resolvedCallSummary!))
        {
            resolvedCallKey = call;
            return true;
        }

        var normalizedCall = NormalizeConstructedReceiverType(call);
        if (!string.Equals(normalizedCall, call, StringComparison.Ordinal) &&
            bySymbol.TryGetValue(normalizedCall, out resolvedCallSummary!))
        {
            resolvedCallKey = normalizedCall;
            return true;
        }

        resolvedCallKey = string.Empty;
        resolvedCallSummary = default!;
        return false;
    }

    private static bool TryClassifyUnresolvedInteropBoundaryCall(string callSymbol, out string category)
    {
        if (callSymbol.StartsWith("Interop+", StringComparison.Ordinal) ||
            callSymbol.StartsWith("Internal.Win32.", StringComparison.Ordinal) ||
            callSymbol.StartsWith("System.Runtime.InteropServices.NativeLibrary.", StringComparison.Ordinal) ||
            callSymbol.StartsWith("System.Runtime.InteropServices.Marshal.GetLastPInvokeError(", StringComparison.Ordinal) ||
            callSymbol.StartsWith("System.Runtime.InteropServices.Marshal.GetLastSystemError(", StringComparison.Ordinal))
        {
            category = "global_state_read";
            return true;
        }

        category = string.Empty;
        return false;
    }

    private static string NormalizeConstructedReceiverType(string exactSymbolKey)
    {
        var signatureStart = exactSymbolKey.IndexOf('(');
        if (signatureStart <= 0)
        {
            return exactSymbolKey;
        }

        var methodSeparator = -1;
        var genericDepth = 0;
        for (var i = 0; i < signatureStart; i++)
        {
            var current = exactSymbolKey[i];
            if (current == '<')
            {
                genericDepth++;
                continue;
            }

            if (current == '>')
            {
                if (genericDepth > 0)
                {
                    genericDepth--;
                }

                continue;
            }

            if (current == '.' && genericDepth == 0)
            {
                methodSeparator = i;
            }
        }

        if (methodSeparator <= 0)
        {
            return exactSymbolKey;
        }

        var receiverType = exactSymbolKey[..methodSeparator];
        var normalizedReceiverType = StripGenericInstantiations(receiverType);
        if (string.Equals(receiverType, normalizedReceiverType, StringComparison.Ordinal))
        {
            return exactSymbolKey;
        }

        return normalizedReceiverType + exactSymbolKey[methodSeparator..];
    }

    private static string StripGenericInstantiations(string text)
    {
        if (text.IndexOf('<') < 0)
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        var genericDepth = 0;
        foreach (var current in text)
        {
            if (current == '<')
            {
                genericDepth++;
                continue;
            }

            if (current == '>')
            {
                if (genericDepth > 0)
                {
                    genericDepth--;
                }

                continue;
            }

            if (genericDepth == 0)
            {
                builder.Append(current);
            }
        }

        return builder.ToString();
    }

    private static bool HasFreshArrayAllocationEvidence(MethodEffectSummary? summary)
    {
        return summary != null &&
            (summary.Effects.Contains("allocates_array", StringComparer.Ordinal) ||
             HasAllocateUninitializedArrayWrapperPattern(summary));
    }

    private static bool HasAllocateUninitializedArrayWrapperPattern(MethodEffectSummary summary)
    {
        if (!summary.Calls.Any(static call =>
                call.StartsWith("System.GC.AllocateUninitializedArray(int, bool)", StringComparison.Ordinal)))
        {
            return false;
        }

        if (!summary.Calls.Any(static call =>
                call.StartsWith("System.MemoryExtensions.AsSpan(", StringComparison.Ordinal)))
        {
            return false;
        }

        var methodBaseSymbol = GetMethodBaseSymbol(summary.Symbol);
        return summary.Calls.Any(call =>
            call.StartsWith(methodBaseSymbol + "(", StringComparison.Ordinal));
    }

    private static bool IsFreshOwnedObjectInitializationCompatible(
        string symbol,
        IReadOnlyDictionary<string, MethodEffectSummary> bySymbol,
        Dictionary<string, MethodPurityClassification> memo,
        Dictionary<string, bool> freshOwnedInitializationMemo,
        Dictionary<string, bool> validationThrowHelperMemo,
        HashSet<string> purityVisiting)
    {
        if (freshOwnedInitializationMemo.TryGetValue(symbol, out var cached))
        {
            return cached;
        }

        var compatibilityVisiting = new HashSet<string>(StringComparer.Ordinal);
        var compatible = IsFreshOwnedObjectInitializationCompatibleCore(
            symbol,
            bySymbol,
            memo,
            freshOwnedInitializationMemo,
            validationThrowHelperMemo,
            purityVisiting,
            compatibilityVisiting);
        freshOwnedInitializationMemo[symbol] = compatible;
        return compatible;
    }

    private static bool IsFreshOwnedObjectInitializationCompatibleCore(
        string symbol,
        IReadOnlyDictionary<string, MethodEffectSummary> bySymbol,
        Dictionary<string, MethodPurityClassification> memo,
        Dictionary<string, bool> freshOwnedInitializationMemo,
        Dictionary<string, bool> validationThrowHelperMemo,
        HashSet<string> purityVisiting,
        HashSet<string> compatibilityVisiting)
    {
        if (freshOwnedInitializationMemo.TryGetValue(symbol, out var cached))
        {
            return cached;
        }

        if (!bySymbol.TryGetValue(symbol, out var summary))
        {
            return false;
        }

        if (!compatibilityVisiting.Add(symbol))
        {
            return false;
        }

        foreach (var root in summary.RootCandidates)
        {
            if (InternalOnlyRoots.Contains(root) ||
                string.Equals(root, "object_state_write", StringComparison.Ordinal))
            {
                continue;
            }

            compatibilityVisiting.Remove(symbol);
            return false;
        }

        foreach (var effect in summary.Effects)
        {
            if (string.Equals(effect, "writes_instance_field", StringComparison.Ordinal) ||
                SafeEffects.Contains(effect))
            {
                continue;
            }

            compatibilityVisiting.Remove(symbol);
            return false;
        }

        foreach (var callSite in EnumerateCallSites(summary))
        {
            var call = callSite.ExactSymbolKey;
            if (IsPurityNeutralIntrinsicHelperCall(call))
            {
                continue;
            }

            if (!TryResolveCallSummary(call, bySymbol, out var resolvedCallKey, out var resolvedCallSummary))
            {
                if (TryClassifyUnresolvedInteropBoundaryCall(call, out _))
                {
                    compatibilityVisiting.Remove(symbol);
                    return false;
                }

                continue;
            }

            var calleeClassification = ClassifyMethod(
                resolvedCallKey,
                bySymbol,
                memo,
                freshOwnedInitializationMemo,
                validationThrowHelperMemo,
                purityVisiting);
            if (string.Equals(calleeClassification.Classification, "pure", StringComparison.Ordinal))
            {
                continue;
            }

            if (ShouldTreatCallAsSemanticallyPure(summary, callSite, resolvedCallSummary, calleeClassification))
            {
                continue;
            }

            if (string.Equals(calleeClassification.Classification, "impure", StringComparison.Ordinal) &&
                IsFreshOwnedObjectInitializationCompatibleCore(
                    resolvedCallKey,
                    bySymbol,
                    memo,
                    freshOwnedInitializationMemo,
                    validationThrowHelperMemo,
                    purityVisiting,
                    compatibilityVisiting))
            {
                continue;
            }

            compatibilityVisiting.Remove(symbol);
            return false;
        }

        compatibilityVisiting.Remove(symbol);
        freshOwnedInitializationMemo[symbol] = true;
        return true;
    }

    private static bool IsValidationThrowHelperCompatible(
        string symbol,
        IReadOnlyDictionary<string, MethodEffectSummary> bySymbol,
        Dictionary<string, MethodPurityClassification> memo,
        Dictionary<string, bool> freshOwnedInitializationMemo,
        Dictionary<string, bool> validationThrowHelperMemo,
        HashSet<string> purityVisiting)
    {
        if (validationThrowHelperMemo.TryGetValue(symbol, out var cached))
        {
            return cached;
        }

        var compatibilityVisiting = new HashSet<string>(StringComparer.Ordinal);
        var compatible = IsValidationThrowHelperCompatibleCore(
            symbol,
            bySymbol,
            memo,
            freshOwnedInitializationMemo,
            validationThrowHelperMemo,
            purityVisiting,
            compatibilityVisiting);
        validationThrowHelperMemo[symbol] = compatible;
        return compatible;
    }

    private static bool IsValidationThrowHelperCompatibleCore(
        string symbol,
        IReadOnlyDictionary<string, MethodEffectSummary> bySymbol,
        Dictionary<string, MethodPurityClassification> memo,
        Dictionary<string, bool> freshOwnedInitializationMemo,
        Dictionary<string, bool> validationThrowHelperMemo,
        HashSet<string> purityVisiting,
        HashSet<string> compatibilityVisiting)
    {
        if (validationThrowHelperMemo.TryGetValue(symbol, out var cached))
        {
            return cached;
        }

        if (!bySymbol.TryGetValue(symbol, out var summary))
        {
            return false;
        }

        if (!compatibilityVisiting.Add(symbol))
        {
            return false;
        }

        foreach (var root in summary.RootCandidates)
        {
            if (string.Equals(root, "throw", StringComparison.Ordinal) ||
                InternalOnlyRoots.Contains(root))
            {
                continue;
            }

            compatibilityVisiting.Remove(symbol);
            return false;
        }

        foreach (var effect in summary.Effects)
        {
            if (string.Equals(effect, "throws", StringComparison.Ordinal) ||
                SafeEffects.Contains(effect))
            {
                continue;
            }

            compatibilityVisiting.Remove(symbol);
            return false;
        }

        foreach (var callSite in EnumerateCallSites(summary))
        {
            var call = callSite.ExactSymbolKey;
            if (IsPurityNeutralIntrinsicHelperCall(call))
            {
                continue;
            }

            if (!TryResolveCallSummary(call, bySymbol, out var resolvedCallKey, out var resolvedCallSummary))
            {
                if (TryClassifyUnresolvedInteropBoundaryCall(call, out _))
                {
                    compatibilityVisiting.Remove(symbol);
                    return false;
                }

                continue;
            }

            var calleeClassification = ClassifyMethod(
                resolvedCallKey,
                bySymbol,
                memo,
                freshOwnedInitializationMemo,
                validationThrowHelperMemo,
                purityVisiting);
            if (string.Equals(calleeClassification.Classification, "pure", StringComparison.Ordinal))
            {
                continue;
            }

            if (ShouldTreatCallAsSemanticallyPure(summary, callSite, resolvedCallSummary, calleeClassification))
            {
                continue;
            }

            if (string.Equals(calleeClassification.Classification, "impure", StringComparison.Ordinal) &&
                IsValidationThrowHelperCompatibleCore(
                    resolvedCallKey,
                    bySymbol,
                    memo,
                    freshOwnedInitializationMemo,
                    validationThrowHelperMemo,
                    purityVisiting,
                    compatibilityVisiting))
            {
                continue;
            }

            compatibilityVisiting.Remove(symbol);
            return false;
        }

        compatibilityVisiting.Remove(symbol);
        validationThrowHelperMemo[symbol] = true;
        return true;
    }

    private static bool IsFreshOwnedObjectConstructor(MethodEffectSummary summary)
    {
        if (string.IsNullOrWhiteSpace(summary.Symbol) ||
            !summary.Symbol.Contains("..ctor(", StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var effect in summary.Effects)
        {
            if (string.Equals(effect, "calls_method", StringComparison.Ordinal) ||
                string.Equals(effect, "writes_instance_field", StringComparison.Ordinal) ||
                SafeEffects.Contains(effect))
            {
                continue;
            }

            return false;
        }

        return true;
    }

    private static string GetMethodBaseSymbol(string symbol)
    {
        var openParenIndex = symbol.IndexOf('(');
        return openParenIndex >= 0 ? symbol.Substring(0, openParenIndex) : symbol;
    }

    private static string GetEffectVisibilityClassification(MethodEffectSummary? summary, string classification)
    {
        if (summary == null)
        {
            return "unknown";
        }

        if (string.Equals(classification, "conservative_unknown", StringComparison.Ordinal))
        {
            return "unknown";
        }

        if (string.Equals(classification, "impure", StringComparison.Ordinal))
        {
            return "caller_visible";
        }

        if (summary.RootCandidates.Contains("fresh_owned_memory_write", StringComparer.Ordinal) ||
            summary.RootCandidates.Contains("fresh_owned_array_write", StringComparer.Ordinal) ||
            summary.RootCandidates.Contains("fresh_owned_object_write", StringComparer.Ordinal) ||
            HasFreshOwnedArrayWritePattern(summary) ||
            HasFreshOwnedStringWritePattern(summary) ||
            HasLocalScratchMemoryWritePattern(summary) ||
            summary.RootCandidates.Contains("safe_static_cache_read", StringComparer.Ordinal) ||
            summary.RootCandidates.Contains("safe_static_constant_read", StringComparer.Ordinal))
        {
            return "internal_only";
        }

        if (HasFreshArrayAllocationEvidence(summary) && string.Equals(classification, "pure", StringComparison.Ordinal))
        {
            return "internal_only";
        }

        return "none";
    }

    private static string AggregateEffectVisibilityClassification(IReadOnlyList<MethodPurityClassification> classifications)
    {
        var values = classifications
            .Select(static classification => classification.EffectVisibilityClassification)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (values.Contains("caller_visible", StringComparer.Ordinal))
        {
            return "caller_visible";
        }

        if (values.Contains("unknown", StringComparer.Ordinal))
        {
            return "unknown";
        }

        if (values.Contains("internal_only", StringComparer.Ordinal))
        {
            return "internal_only";
        }

        return "none";
    }

    private static bool IsPurityNeutralIntrinsicHelperCall(string callSymbol)
    {
        return callSymbol.StartsWith("System.Runtime.CompilerServices.Unsafe.As(", StringComparison.Ordinal) ||
            callSymbol.StartsWith("System.Runtime.CompilerServices.Unsafe.Add(", StringComparison.Ordinal) ||
            callSymbol.StartsWith("System.Runtime.CompilerServices.Unsafe.BitCast(", StringComparison.Ordinal) ||
            callSymbol.StartsWith("System.Runtime.CompilerServices.Unsafe.ReadUnaligned(", StringComparison.Ordinal) ||
            callSymbol.StartsWith("System.Runtime.CompilerServices.Unsafe.WriteUnaligned(", StringComparison.Ordinal) ||
            callSymbol.StartsWith("string.GetRawStringData()", StringComparison.Ordinal) ||
            callSymbol.StartsWith("string.get_Length()", StringComparison.Ordinal) ||
            callSymbol.StartsWith("System.Span`1<", StringComparison.Ordinal) && callSymbol.Contains(".get_Length()", StringComparison.Ordinal) ||
            callSymbol.StartsWith("System.ReadOnlySpan`1<", StringComparison.Ordinal) && callSymbol.Contains(".get_Length()", StringComparison.Ordinal) ||
            callSymbol.StartsWith("System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences(", StringComparison.Ordinal);
    }

    private static bool IsByRefLikeViewConstructionHelperCall(string callSymbol)
    {
        return callSymbol.StartsWith("System.Runtime.InteropServices.MemoryMarshal.GetArrayDataReference(", StringComparison.Ordinal) ||
            callSymbol.StartsWith("System.Index.Equals(System.Index)", StringComparison.Ordinal) ||
            callSymbol.StartsWith("System.Index.GetOffset(int)", StringComparison.Ordinal) ||
            callSymbol.StartsWith("System.Index.get_Start()", StringComparison.Ordinal) ||
            callSymbol.StartsWith("System.Range.GetOffsetAndLength(int)", StringComparison.Ordinal) ||
            callSymbol.StartsWith("System.Range.get_End()", StringComparison.Ordinal) ||
            callSymbol.StartsWith("System.Range.get_Start()", StringComparison.Ordinal) ||
            callSymbol.StartsWith("System.ThrowHelper.ThrowArgumentNullException(", StringComparison.Ordinal) ||
            callSymbol.StartsWith("System.ThrowHelper.ThrowArgumentOutOfRangeException(", StringComparison.Ordinal) ||
            callSymbol.StartsWith("System.ThrowHelper.ThrowArrayTypeMismatchException()", StringComparison.Ordinal) ||
            callSymbol.StartsWith("System.Type.GetTypeFromHandle(System.RuntimeTypeHandle)", StringComparison.Ordinal) ||
            callSymbol.StartsWith("System.Type.get_IsValueType()", StringComparison.Ordinal) ||
            callSymbol.StartsWith("System.Type.op_Inequality(System.Type, System.Type)", StringComparison.Ordinal) ||
            callSymbol.StartsWith("object.GetType()", StringComparison.Ordinal);
    }

    private static bool IsByRefLikeViewConstructionCall(string callSymbol)
    {
        return callSymbol.StartsWith("System.Span`1<", StringComparison.Ordinal) && callSymbol.Contains("..ctor(ref ", StringComparison.Ordinal) ||
            callSymbol.StartsWith("System.ReadOnlySpan`1<", StringComparison.Ordinal) && callSymbol.Contains("..ctor(ref ", StringComparison.Ordinal);
    }

    private static bool HasParameterlessNonVoidReturn(string exactSymbolKey)
    {
        if (string.IsNullOrWhiteSpace(exactSymbolKey))
        {
            return false;
        }

        var openParenIndex = exactSymbolKey.IndexOf('(');
        var returnSeparatorIndex = exactSymbolKey.LastIndexOf(")->", StringComparison.Ordinal);
        if (openParenIndex < 0 || returnSeparatorIndex <= openParenIndex)
        {
            return false;
        }

        var parameters = exactSymbolKey.Substring(openParenIndex + 1, returnSeparatorIndex - openParenIndex - 1);
        if (!string.IsNullOrEmpty(parameters))
        {
            return false;
        }

        var returnType = exactSymbolKey[(returnSeparatorIndex + 3)..];
        return !string.IsNullOrWhiteSpace(returnType) &&
            !string.Equals(returnType, "void", StringComparison.Ordinal) &&
            !returnType.StartsWith("ref ", StringComparison.Ordinal);
    }

    private static bool IsPureArgumentGuardWrapper(string symbol)
    {
        var methodBaseSymbol = GetMethodBaseSymbol(symbol);
        if (!methodBaseSymbol.StartsWith("System.Argument", StringComparison.Ordinal) ||
            !methodBaseSymbol.Contains(".ThrowIf", StringComparison.Ordinal))
        {
            return false;
        }

        return !symbol.Contains('*', StringComparison.Ordinal) &&
            !symbol.Contains("nint", StringComparison.Ordinal);
    }

    private static bool IsArgumentGuardThrowHelper(string symbol)
    {
        var methodBaseSymbol = GetMethodBaseSymbol(symbol);
        if (!methodBaseSymbol.StartsWith("System.Argument", StringComparison.Ordinal))
        {
            return false;
        }

        return methodBaseSymbol.Contains(".Throw", StringComparison.Ordinal) &&
            !methodBaseSymbol.Contains(".ThrowIf", StringComparison.Ordinal);
    }

    private static bool IsSemanticallyNeutralValidationThrowHelper(string symbol)
    {
        if (IsArgumentGuardThrowHelper(symbol))
        {
            return true;
        }

        var methodBaseSymbol = GetMethodBaseSymbol(symbol);
        return methodBaseSymbol.StartsWith("System.ThrowHelper.Throw", StringComparison.Ordinal);
    }

    private static bool IsSemanticallyCheckedDelegateInvokingBclMethod(string symbol)
    {
        var methodBaseSymbol = GetMethodBaseSymbol(symbol);
        var lastDotIndex = methodBaseSymbol.LastIndexOf('.');
        if (lastDotIndex <= 0 || lastDotIndex == methodBaseSymbol.Length - 1)
        {
            return false;
        }

        var containingType = methodBaseSymbol[..lastDotIndex];
        var methodName = methodBaseSymbol[(lastDotIndex + 1)..];

        // These helpers already rely on analyzer-side semantic checking of the delegate target.
        // The runtime summaries should ignore delegate dispatch noise and validation throw helpers.
        return containingType switch
        {
            "System.Array" => methodName is
                "Exists" or
                "Find" or
                "FindIndex" or
                "FindLast" or
                "FindLastIndex" or
                "TrueForAll",
            "System.Collections.Generic.List`1" => methodName is
                "Exists" or
                "Find" or
                "FindIndex" or
                "FindLast" or
                "FindLastIndex" or
                "TrueForAll",
            _ => false
        };
    }
}

internal sealed record PurityClassificationOutput(
    AssemblyEffectReport[] Assemblies,
    PurityClassificationReport Report,
    GeneratedPurityCatalogDocument GeneratedPurityCatalog);

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
    string EffectVisibilityClassification,
    string? Note,
    string[] MatchedExactSymbolKeys);

internal sealed record GeneratedPurityCatalogDocument(
    int SchemaVersion,
    GeneratedPurityCatalogEntry[] Entries);

internal sealed record GeneratedPurityCatalogEntry(
    string Symbol,
    string ExactSymbolKey,
    string CacheKey,
    string AssemblyName,
    string AssemblyPath,
    string AssemblySha256,
    string ModuleVersionId,
    string MetadataToken,
    string? MethodBodySha256,
    string Classification,
    string PrimaryCategory,
    string[] Categories,
    string[] FirstBlockingCallChain,
    bool HasFreshArrayAllocationEvidence,
    bool HasFreshObjectAllocationEvidence,
    bool HasUnsupportedEffects,
    string FreshnessClassification,
    string EffectVisibilityClassification);

internal sealed record MethodPurityClassification(
    string Classification,
    string[] Categories,
    string[] FirstBlockingCallChain,
    bool HasFreshArrayAllocationEvidence,
    bool HasFreshObjectAllocationEvidence,
    [property: JsonPropertyName("HasUnsupportedEffects")]
    bool HasUnsupportedEffects,
    string FreshnessClassification,
    string EffectVisibilityClassification);

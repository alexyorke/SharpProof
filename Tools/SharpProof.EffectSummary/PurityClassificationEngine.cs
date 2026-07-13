using System.Collections.Immutable;
using System.Text;
using System.Text.Json.Serialization;
using SharpProof.Analyzer.Engine;
using SharpProof.Identity;
using SharpProof.Schema;

internal static class PurityClassificationEngine
{
    private const int MaxCrossAssemblyClassificationPasses = 8;

    internal static readonly IReadOnlyDictionary<string, string> EmptyTypeParameterOrdinals =
        new Dictionary<string, string>(StringComparer.Ordinal);

    internal static readonly IReadOnlyDictionary<string, string> SpecialTypeAliases =
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
            ["System.Void"] = "void"
        };

    internal static readonly ImmutableHashSet<string> SafeEffects = ImmutableHashSet.Create(
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

    internal static readonly ImmutableHashSet<string> InternalOnlyRoots = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "safe_static_cache_read",
        "safe_static_constant_read",
        "fresh_owned_memory_write",
        "fresh_owned_object_write");

    private static readonly IReadOnlyDictionary<string, GeneratedPurityCatalogEntry>
        EmptyExternalGeneratedPurityEntries =
            new Dictionary<string, GeneratedPurityCatalogEntry>(StringComparer.Ordinal);

    public static PurityClassificationOutput Classify(
        AssemblyEffectReport[] assemblies,
        bool includeCatalogComparison,
        IReadOnlyDictionary<string, GeneratedPurityCatalogEntry>? externalGeneratedPurityEntries = null)
    {
        var seedEntries = externalGeneratedPurityEntries ?? EmptyExternalGeneratedPurityEntries;
        var resolvedExternalEntries = seedEntries;
        Dictionary<string, GeneratedPurityCatalogEntry>? previousResolvedEntries = null;
        var classifiedAssemblies = assemblies;
        GeneratedPurityCatalogDocument? generatedPurityCatalog = null;

        for (var pass = 0; pass < MaxCrossAssemblyClassificationPasses; pass++)
        {
            classifiedAssemblies = assemblies
                .Select(assembly => ClassifyAssembly(assembly, resolvedExternalEntries, seedEntries))
                .ToArray();

            generatedPurityCatalog = BuildGeneratedPurityCatalog(classifiedAssemblies);
            var nextResolvedEntries = MergeGeneratedPurityEntries(
                seedEntries.Values.Concat(generatedPurityCatalog.Entries));
            if (previousResolvedEntries != null &&
                HaveSameGeneratedPurityEntryMap(previousResolvedEntries, nextResolvedEntries))
                break;

            previousResolvedEntries = nextResolvedEntries;
            resolvedExternalEntries = nextResolvedEntries;
        }

        var methods = classifiedAssemblies
            .SelectMany(assembly => assembly.Methods)
            .ToArray();
        var report = BuildReport(methods, includeCatalogComparison);
        return new PurityClassificationOutput(
            classifiedAssemblies,
            report,
            generatedPurityCatalog ?? BuildGeneratedPurityCatalog(classifiedAssemblies));
    }

    private static AssemblyEffectReport ClassifyAssembly(
        AssemblyEffectReport assembly,
        IReadOnlyDictionary<string, GeneratedPurityCatalogEntry> externalGeneratedPurityEntries,
        IReadOnlyDictionary<string, GeneratedPurityCatalogEntry> reviewedGeneratedPurityEntries)
    {
        var classificationMethods = assembly.ClassificationMethods.Length == 0
            ? assembly.Methods
            : assembly.ClassificationMethods;
        var bySymbol = classificationMethods
            .GroupBy(method => method.CanonicalKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var context = new PurityClassificationContext(
            assembly,
            bySymbol,
            externalGeneratedPurityEntries,
            reviewedGeneratedPurityEntries);

        return assembly with
        {
            Methods = assembly.Methods
                .Select(method => method with
                {
                    PurityClassification = ClassifyMethod(method.CanonicalKey, context)
                })
                .ToArray()
        };
    }

    internal static MethodPurityClassification ClassifyMethod(
        string symbol,
        PurityClassificationContext context)
    {
        var assembly = context.Assembly;
        var bySymbol = context.BySymbol;
        var externalGeneratedPurityEntries = context.ExternalGeneratedPurityEntries;
        var reviewedGeneratedPurityEntries = context.ReviewedGeneratedPurityEntries;
        var memo = context.Memo;
        var freshOwnedInitializationMemo = context.FreshOwnedInitializationMemo;
        var validationThrowHelperMemo = context.ValidationThrowHelperMemo;
        var visiting = context.Visiting;
        if (memo.TryGetValue(symbol, out var cached))
        {
            if (bySymbol.TryGetValue(symbol, out var cachedSummary) &&
                TryResolveReviewedUpgrade(assembly, symbol, cachedSummary, reviewedGeneratedPurityEntries,
                    out var reviewedUpgrade))
            {
                memo[symbol] = reviewedUpgrade;
                return reviewedUpgrade;
            }

            return cached;
        }

        if (!bySymbol.TryGetValue(symbol, out var summary))
            return CreateUnknown(
                new[] { "missing_summary" },
                Array.Empty<string>(),
                null);

        if (!visiting.Add(symbol))
            return CreateUnknown(
                new[] { "recursive_cycle" },
                new[] { summary.Symbol },
                summary);

        if (TryClassifyRuntimeIntrinsicStub(summary, out var intrinsicStubClassification))
        {
            visiting.Remove(symbol);
            memo[symbol] = intrinsicStubClassification;
            return intrinsicStubClassification;
        }

        if (TryClassifyKnownBclSummary(summary, out var knownBclClassification))
        {
            visiting.Remove(symbol);
            memo[symbol] = knownBclClassification;
            return knownBclClassification;
        }

        if (TryClassifySemanticPureWrapper(summary, out var semanticWrapperClassification))
        {
            visiting.Remove(symbol);
            memo[symbol] = semanticWrapperClassification;
            return semanticWrapperClassification;
        }

        var impureCategories = new SortedSet<string>(StringComparer.Ordinal);
        var conservativeCategories = new SortedSet<string>(StringComparer.Ordinal);

        var treatsObjectStateAsFreshOwned = IsFreshOwnedObjectConstructor(summary);
        var treatsConstructorReceiverWritesAsFreshOwned =
            treatsObjectStateAsFreshOwned &&
            !HasByRefParameter(summary.Identity);
        var treatsVirtualDispatchAsResolved = HasOnlyResolvedVirtualCallTargets(summary, bySymbol);
        var treatsDeterministicStringComparisonDispatchAsSemantic =
            HasOnlyDeterministicStringComparisonDispatch(summary);
        var treatsArgumentGuardThrowHelpersAsPure = IsPureArgumentGuardWrapper(summary.Symbol);
        var treatsDelegateDispatchAsSemantic = IsSemanticallyCheckedDelegateInvokingBclMethod(summary.Symbol);
        foreach (var root in summary.RootCandidates)
        {
            if ((treatsVirtualDispatchAsResolved ||
                 treatsDeterministicStringComparisonDispatchAsSemantic ||
                 treatsDelegateDispatchAsSemantic) &&
                string.Equals(root, "dynamic_dispatch", StringComparison.Ordinal))
                continue;

            if (string.Equals(root, "caller_visible_memory_write", StringComparison.Ordinal) &&
                (HasFreshOwnedArrayWritePattern(summary) ||
                 HasFreshOwnedStringWritePattern(summary) ||
                 HasReturnValueInitializationPattern(summary) ||
                 HasLocalScratchMemoryWritePattern(summary) ||
                 HasByRefLikeViewConstructionPattern(summary)))
                continue;

            if (treatsConstructorReceiverWritesAsFreshOwned &&
                string.Equals(root, "caller_visible_memory_write", StringComparison.Ordinal))
                continue;

            if (treatsObjectStateAsFreshOwned &&
                string.Equals(root, "object_state_write", StringComparison.Ordinal))
                continue;

            if (ImpureRoots.Contains(root))
            {
                impureCategories.Add(root);
            }
            else if (InternalOnlyRoots.Contains(root))
            {
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
                continue;

            if (string.Equals(effect, "writes_indirect_memory", StringComparison.Ordinal) &&
                (summary.RootCandidates.Contains("fresh_owned_memory_write", StringComparer.Ordinal) ||
                 HasFreshOwnedArrayWritePattern(summary) ||
                 HasFreshOwnedStringWritePattern(summary) ||
                 HasReturnValueInitializationPattern(summary) ||
                 HasLocalScratchMemoryWritePattern(summary) ||
                 HasByRefLikeViewConstructionPattern(summary)))
                continue;

            if (treatsConstructorReceiverWritesAsFreshOwned &&
                string.Equals(effect, "writes_indirect_memory", StringComparison.Ordinal))
                continue;

            if (string.Equals(effect, "writes_instance_field", StringComparison.Ordinal) &&
                (summary.RootCandidates.Contains("fresh_owned_object_write", StringComparer.Ordinal) ||
                 treatsObjectStateAsFreshOwned))
                continue;

            if (string.Equals(effect, "writes_instance_field", StringComparison.Ordinal) &&
                summary.RootCandidates.Contains("object_state_write", StringComparer.Ordinal))
                continue;

            if (string.Equals(effect, "reads_static_field", StringComparison.Ordinal) &&
                (summary.RootCandidates.Contains("safe_static_cache_read", StringComparer.Ordinal) ||
                 summary.RootCandidates.Contains("safe_static_constant_read", StringComparer.Ordinal)))
                continue;

            if (SafeEffects.Contains(effect)) continue;

            if (ConservativeEffects.Contains(effect) ||
                effect.StartsWith("unknown_opcode_at_", StringComparison.Ordinal))
            {
                conservativeCategories.Add(effect.StartsWith("unknown_opcode_at_", StringComparison.Ordinal)
                    ? "unknown_opcode"
                    : effect);
                continue;
            }

            conservativeCategories.Add("unsupported_effect:" + effect);
        }

        var blockingCallChain = Array.Empty<string>();
        var freshOwnedArrayCalleeSeen = false;
        var freshOwnedObjectCalleeSeen = false;
        foreach (var callSite in EnumerateCallSites(summary))
        {
            var call = callSite.DisplayName;
            var callKey = callSite.CanonicalKey;
            if (IsDeterministicStringComparisonDispatch(callSite)) continue;
            if (IsPurityNeutralIntrinsicHelperCall(call)) continue;

            if (callKey == null ||
                !TryResolveCallSummary(callKey, bySymbol, out var resolvedCallKey, out var resolvedCallSummary))
            {
                if (callKey != null && TryResolveExternalCallClassification(
                        callKey,
                        externalGeneratedPurityEntries,
                        out var externalCallKey,
                        out var externalEntry,
                        out var externalClassification))
                {
                    if (callSite.UsesDynamicDispatch &&
                        !treatsVirtualDispatchAsResolved &&
                        !string.Equals(externalClassification.Classification, "pure", StringComparison.Ordinal))
                    {
                        conservativeCategories.Add("dynamic_dispatch");
                        if (blockingCallChain.Length == 0)
                            blockingCallChain = JoinCallChain(externalEntry.Symbol,
                                externalClassification.FirstBlockingCallChain);
                        continue;
                    }

                    if (string.Equals(externalClassification.Classification, "impure", StringComparison.Ordinal))
                    {
                        if (IsPureArgumentGuardWrapper(externalEntry.Symbol) ||
                            (treatsArgumentGuardThrowHelpersAsPure &&
                             IsArgumentGuardThrowHelper(externalEntry.Symbol)) ||
                            (treatsDelegateDispatchAsSemantic &&
                             IsSemanticallyNeutralValidationThrowHelper(externalEntry.Symbol)) ||
                            IsValidationThrowHelperCompatible(externalCallKey, context) ||
                            ShouldTreatCallAsSemanticallyPure(summary, callSite, externalEntry.Symbol,
                                externalClassification))
                            continue;

                        AddImpureCalleeCategories(impureCategories, externalClassification);
                        if (blockingCallChain.Length == 0)
                            blockingCallChain = JoinCallChain(externalEntry.Symbol,
                                externalClassification.FirstBlockingCallChain);
                    }
                    else if (string.Equals(externalClassification.Classification, "conservative_unknown",
                                 StringComparison.Ordinal))
                    {
                        if (ShouldIgnoreUnknownCall(
                                summary,
                                callSite,
                                externalEntry.Symbol,
                                externalClassification,
                                externalCallKey,
                                context,
                                treatsArgumentGuardThrowHelpersAsPure,
                                treatsDelegateDispatchAsSemantic))
                            continue;

                        conservativeCategories.Add("unknown_callee");
                        if (blockingCallChain.Length == 0)
                            blockingCallChain = JoinCallChain(externalEntry.Symbol,
                                externalClassification.FirstBlockingCallChain);
                    }
                    else if (string.Equals(externalClassification.Classification, "pure", StringComparison.Ordinal))
                    {
                        if (string.Equals(externalClassification.FreshnessClassification, "fresh_owned_array_write",
                                StringComparison.Ordinal)) freshOwnedArrayCalleeSeen = true;

                        if (string.Equals(externalClassification.FreshnessClassification, "fresh_owned_object_write",
                                StringComparison.Ordinal)) freshOwnedObjectCalleeSeen = true;
                    }

                    continue;
                }

                if (TryClassifyKnownUnresolvedBclCall(call, out var knownCallIsPure,
                        out var knownCallCategories))
                {
                    if (!knownCallIsPure)
                    {
                        foreach (var category in knownCallCategories)
                            if (string.Equals(category, "global_state_read", StringComparison.Ordinal) ||
                                string.Equals(category, "global_state_write", StringComparison.Ordinal))
                                impureCategories.Add(category);

                        impureCategories.Add("impure_callee");
                        if (blockingCallChain.Length == 0)
                            blockingCallChain = new[] { RemoveReturnTypeSuffix(call) };
                    }

                    continue;
                }

                if (TryClassifyUnresolvedInteropBoundaryCall(summary, call, out var unresolvedInteropCategory))
                {
                    impureCategories.Add(unresolvedInteropCategory);
                    if (blockingCallChain.Length == 0) blockingCallChain = new[] { call };
                }
                else
                {
                    conservativeCategories.Add("unknown_callee");
                    if (blockingCallChain.Length == 0) blockingCallChain = new[] { call };
                }

                continue;
            }

            if (visiting.Contains(resolvedCallKey)) continue;

            var calleeClassification = ClassifyMethod(resolvedCallKey, context);
            var effectiveCalleeClassification = calleeClassification;
            if (!string.Equals(calleeClassification.Classification, "pure", StringComparison.Ordinal) &&
                TryResolveReviewedUpgrade(
                    assembly,
                    resolvedCallKey,
                    resolvedCallSummary,
                    externalGeneratedPurityEntries,
                    out var reviewedCalleeClassification))
                effectiveCalleeClassification = reviewedCalleeClassification;

            if (ShouldTreatCallAsSemanticallyPure(summary, callSite, resolvedCallSummary,
                    effectiveCalleeClassification)) continue;

            if (callSite.UsesDynamicDispatch &&
                !treatsVirtualDispatchAsResolved &&
                !string.Equals(effectiveCalleeClassification.Classification, "pure", StringComparison.Ordinal))
            {
                conservativeCategories.Add("dynamic_dispatch");
                if (blockingCallChain.Length == 0)
                    blockingCallChain = JoinCallChain(resolvedCallSummary.Symbol,
                        effectiveCalleeClassification.FirstBlockingCallChain);
                continue;
            }

            if (string.Equals(effectiveCalleeClassification.Classification, "impure", StringComparison.Ordinal))
            {
                if (IsPureArgumentGuardWrapper(resolvedCallSummary.Symbol) ||
                    (treatsArgumentGuardThrowHelpersAsPure &&
                     IsArgumentGuardThrowHelper(resolvedCallSummary.Symbol)) ||
                    (treatsDelegateDispatchAsSemantic &&
                     IsSemanticallyNeutralValidationThrowHelper(resolvedCallSummary.Symbol)) ||
                    IsValidationThrowHelperCompatible(resolvedCallKey, context) ||
                    (treatsObjectStateAsFreshOwned &&
                     IsFreshOwnedObjectInitializationCompatible(resolvedCallKey, context)))
                    continue;

                AddImpureCalleeCategories(impureCategories, effectiveCalleeClassification);
                if (blockingCallChain.Length == 0)
                    blockingCallChain = JoinCallChain(resolvedCallSummary.Symbol,
                        effectiveCalleeClassification.FirstBlockingCallChain);
            }
            else if (string.Equals(effectiveCalleeClassification.Classification, "conservative_unknown",
                         StringComparison.Ordinal))
            {
                if (ShouldIgnoreUnknownCall(
                        summary,
                        callSite,
                        resolvedCallSummary.Symbol,
                        effectiveCalleeClassification,
                        resolvedCallKey,
                        context,
                        treatsArgumentGuardThrowHelpersAsPure,
                        treatsDelegateDispatchAsSemantic))
                    continue;

                conservativeCategories.Add("unknown_callee");
                if (blockingCallChain.Length == 0)
                    blockingCallChain = JoinCallChain(resolvedCallSummary.Symbol,
                        effectiveCalleeClassification.FirstBlockingCallChain);
            }
            else if (string.Equals(effectiveCalleeClassification.Classification, "pure", StringComparison.Ordinal))
            {
                if (string.Equals(effectiveCalleeClassification.FreshnessClassification, "fresh_owned_array_write",
                        StringComparison.Ordinal)) freshOwnedArrayCalleeSeen = true;

                if (string.Equals(effectiveCalleeClassification.FreshnessClassification, "fresh_owned_object_write",
                        StringComparison.Ordinal)) freshOwnedObjectCalleeSeen = true;
            }
        }

        visiting.Remove(symbol);
        MethodPurityClassification result;

        if (HasAllocateUninitializedArrayWrapperPattern(summary))
        {
            result = new MethodPurityClassification(
                "pure",
                Array.Empty<string>(),
                Array.Empty<string>(),
                true,
                summary.Effects.Contains("allocates_object", StringComparer.Ordinal),
                false,
                "fresh_owned_array_write",
                "internal_only");
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
                "impure",
                impureCategories.ToArray(),
                blockingCallChain,
                summary.Effects.Contains("allocates_array", StringComparer.Ordinal),
                hasFreshObjectEvidence,
                conservativeCategories.Count > 0,
                GetFreshnessClassification(summary, "impure"),
                GetEffectVisibilityClassification(summary, "impure"));
        }
        else if (conservativeCategories.Count > 0)
        {
            result = new MethodPurityClassification(
                "conservative_unknown",
                conservativeCategories.ToArray(),
                blockingCallChain,
                summary.Effects.Contains("allocates_array", StringComparer.Ordinal),
                hasFreshObjectEvidence,
                true,
                GetFreshnessClassification(summary, "conservative_unknown"),
                GetEffectVisibilityClassification(summary, "conservative_unknown"));
        }
        else
        {
            var treatsByRefLikeViewAsPure =
                HasByRefLikeViewConstructionPattern(summary) ||
                HasPureArrayBackedByRefLikeViewWrapperPattern(summary);
            var freshnessClassification = GetFreshnessClassification(summary, "pure");
            if (string.Equals(freshnessClassification, "none", StringComparison.Ordinal))
            {
                if (freshOwnedArrayCalleeSeen)
                    freshnessClassification = "fresh_owned_array_write";
                else if (freshOwnedObjectCalleeSeen || treatsObjectStateAsFreshOwned)
                    freshnessClassification = "fresh_owned_object_write";
            }

            var effectVisibilityClassification = GetEffectVisibilityClassification(summary, "pure");
            if (treatsByRefLikeViewAsPure)
            {
                freshnessClassification = "none";
                effectVisibilityClassification = "none";
            }

            if (string.Equals(effectVisibilityClassification, "none", StringComparison.Ordinal) &&
                !string.Equals(freshnessClassification, "none", StringComparison.Ordinal))
                effectVisibilityClassification = "internal_only";

            result = new MethodPurityClassification(
                "pure",
                Array.Empty<string>(),
                Array.Empty<string>(),
                HasFreshArrayAllocationEvidence(summary) || freshOwnedArrayCalleeSeen,
                treatsByRefLikeViewAsPure ? false : hasFreshObjectEvidence,
                false,
                freshnessClassification,
                effectVisibilityClassification);
        }

        if (TryResolveReviewedUpgrade(
                assembly,
                symbol,
                summary,
                reviewedGeneratedPurityEntries,
                out var reviewedClassification))
            result = reviewedClassification;

        memo[symbol] = result;
        return result;
    }

    private static string[] JoinCallChain(string callee, IReadOnlyList<string> nested)
    {
        if (nested.Count == 0) return new[] { callee };

        var chain = new string[nested.Count + 1];
        chain[0] = callee;
        for (var i = 0; i < nested.Count; i++) chain[i + 1] = nested[i];

        return chain;
    }

    internal static MethodPurityClassification CreateUnknown(
        IEnumerable<string> categories,
        string[] callChain,
        MethodEffectSummary? summary)
    {
        return new MethodPurityClassification(
            "conservative_unknown",
            categories.ToArray(),
            callChain,
            summary?.Effects.Contains("allocates_array", StringComparer.Ordinal) == true,
            summary?.Effects.Contains("allocates_object", StringComparer.Ordinal) == true,
            true,
            GetFreshnessClassification(summary, "conservative_unknown"),
            GetEffectVisibilityClassification(summary, "conservative_unknown"));
    }

}

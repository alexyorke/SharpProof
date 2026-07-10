using System.Collections.Immutable;
using System.Text;
using System.Text.Json.Serialization;
using SharpProof.Analyzer.Engine;

internal static class PurityClassificationEngine
{
    private const int MaxCrossAssemblyClassificationPasses = 8;

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
            ["System.Void"] = "void"
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
                        assembly,
                        method.ExactSymbolKey,
                        bySymbol,
                        externalGeneratedPurityEntries,
                        reviewedGeneratedPurityEntries,
                        memo,
                        freshOwnedInitializationMemo,
                        validationThrowHelperMemo,
                        visiting)
                })
                .ToArray()
        };
    }

    private static MethodPurityClassification ClassifyMethod(
        AssemblyEffectReport assembly,
        string symbol,
        IReadOnlyDictionary<string, MethodEffectSummary> bySymbol,
        IReadOnlyDictionary<string, GeneratedPurityCatalogEntry> externalGeneratedPurityEntries,
        IReadOnlyDictionary<string, GeneratedPurityCatalogEntry> reviewedGeneratedPurityEntries,
        Dictionary<string, MethodPurityClassification> memo,
        Dictionary<string, bool> freshOwnedInitializationMemo,
        Dictionary<string, bool> validationThrowHelperMemo,
        HashSet<string> visiting)
    {
        if (memo.TryGetValue(symbol, out var cached))
        {
            if (bySymbol.TryGetValue(symbol, out var cachedSummary) &&
                TryResolveReviewedUpgrade(assembly, symbol, cachedSummary, reviewedGeneratedPurityEntries,
                    out var reviewedUpgrade) &&
                ShouldPreferReviewedUpgrade(cached, reviewedUpgrade))
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
                new[] { symbol },
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
            !HasByRefParameter(summary.ExactSymbolKey);
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
            var call = callSite.ExactSymbolKey;
            if (IsPurityNeutralIntrinsicHelperCall(call)) continue;

            if (!TryResolveCallSummary(call, bySymbol, out var resolvedCallKey, out var resolvedCallSummary))
            {
                if (TryResolveExternalCallClassification(
                        call,
                        externalGeneratedPurityEntries,
                        out var externalCallKey,
                        out var externalEntry,
                        out var externalClassification))
                {
                    if (string.Equals(externalClassification.Classification, "impure", StringComparison.Ordinal))
                    {
                        if (IsPureArgumentGuardWrapper(externalEntry.Symbol) ||
                            (treatsArgumentGuardThrowHelpersAsPure &&
                             IsArgumentGuardThrowHelper(externalEntry.Symbol)) ||
                            (treatsDelegateDispatchAsSemantic &&
                             IsSemanticallyNeutralValidationThrowHelper(externalEntry.Symbol)) ||
                            IsValidationThrowHelperCompatible(
                                assembly,
                                externalCallKey,
                                bySymbol,
                                externalGeneratedPurityEntries,
                                reviewedGeneratedPurityEntries,
                                memo,
                                freshOwnedInitializationMemo,
                                validationThrowHelperMemo,
                                visiting) ||
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
                                assembly,
                                externalCallKey,
                                bySymbol,
                                externalGeneratedPurityEntries,
                                reviewedGeneratedPurityEntries,
                                memo,
                                freshOwnedInitializationMemo,
                                validationThrowHelperMemo,
                                visiting,
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

                if (TryClassifyUnresolvedInteropBoundaryCall(summary, call, out var unresolvedInteropCategory))
                {
                    impureCategories.Add(unresolvedInteropCategory);
                    if (blockingCallChain.Length == 0) blockingCallChain = new[] { call };
                }

                continue;
            }

            if (visiting.Contains(resolvedCallKey)) continue;

            var calleeClassification = ClassifyMethod(
                assembly,
                resolvedCallKey,
                bySymbol,
                externalGeneratedPurityEntries,
                reviewedGeneratedPurityEntries,
                memo,
                freshOwnedInitializationMemo,
                validationThrowHelperMemo,
                visiting);
            var effectiveCalleeClassification = calleeClassification;
            if (!string.Equals(calleeClassification.Classification, "pure", StringComparison.Ordinal) &&
                TryResolveReviewedUpgrade(
                    assembly,
                    resolvedCallKey,
                    resolvedCallSummary,
                    externalGeneratedPurityEntries,
                    out var reviewedCalleeClassification) &&
                ShouldPreferReviewedUpgrade(calleeClassification, reviewedCalleeClassification))
                effectiveCalleeClassification = reviewedCalleeClassification;

            if (ShouldTreatCallAsSemanticallyPure(summary, callSite, resolvedCallSummary,
                    effectiveCalleeClassification)) continue;

            if (string.Equals(effectiveCalleeClassification.Classification, "impure", StringComparison.Ordinal))
            {
                if (IsPureArgumentGuardWrapper(resolvedCallSummary.Symbol) ||
                    (treatsArgumentGuardThrowHelpersAsPure &&
                     IsArgumentGuardThrowHelper(resolvedCallSummary.Symbol)) ||
                    (treatsDelegateDispatchAsSemantic &&
                     IsSemanticallyNeutralValidationThrowHelper(resolvedCallSummary.Symbol)) ||
                    IsValidationThrowHelperCompatible(
                        assembly,
                        resolvedCallKey,
                        bySymbol,
                        externalGeneratedPurityEntries,
                        reviewedGeneratedPurityEntries,
                        memo,
                        freshOwnedInitializationMemo,
                        validationThrowHelperMemo,
                        visiting) ||
                    (treatsObjectStateAsFreshOwned &&
                     IsFreshOwnedObjectInitializationCompatible(
                         assembly,
                         resolvedCallKey,
                         bySymbol,
                         externalGeneratedPurityEntries,
                         reviewedGeneratedPurityEntries,
                         memo,
                         freshOwnedInitializationMemo,
                         validationThrowHelperMemo,
                         visiting)))
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
                        assembly,
                        resolvedCallKey,
                        bySymbol,
                        externalGeneratedPurityEntries,
                        reviewedGeneratedPurityEntries,
                        memo,
                        freshOwnedInitializationMemo,
                        validationThrowHelperMemo,
                        visiting,
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
                out var reviewedClassification) &&
            ShouldPreferReviewedUpgrade(result, reviewedClassification))
            result = reviewedClassification;

        memo[symbol] = result;
        return result;
    }

    private static IEnumerable<CallSiteSummary> EnumerateCallSites(MethodEffectSummary summary)
    {
        if (summary.CallSites.Length != 0) return summary.CallSites;

        return summary.Calls.Select(static call => new CallSiteSummary(call));
    }

    private static bool ShouldTreatCallAsSemanticallyPure(
        MethodEffectSummary callerSummary,
        CallSiteSummary callSite,
        MethodEffectSummary resolvedCallSummary,
        MethodPurityClassification calleeClassification)
    {
        return ShouldTreatCallAsSemanticallyPure(
            callerSummary,
            callSite,
            resolvedCallSummary.Symbol,
            calleeClassification);
    }

    private static bool ShouldTreatCallAsSemanticallyPure(
        MethodEffectSummary callerSummary,
        CallSiteSummary callSite,
        string calleeSymbol,
        MethodPurityClassification calleeClassification)
    {
        return IsInteropLastErrorBookkeepingCall(callerSummary, calleeSymbol) ||
               (!string.Equals(calleeClassification.Classification, "pure", StringComparison.Ordinal) &&
                IsFreshArrayInitializationHelperCall(callerSummary, calleeSymbol, calleeClassification)) ||
               (!string.Equals(calleeClassification.Classification, "pure", StringComparison.Ordinal) &&
                IsFreshArrayTemporaryInitializationHelperCall(callerSummary, calleeSymbol, calleeClassification)) ||
               (!string.Equals(calleeClassification.Classification, "pure", StringComparison.Ordinal) &&
                HasDeterministicStringComparisonEvidence(callSite) &&
                IsContextSensitiveStringComparisonMethod(calleeSymbol)) ||
               (!string.Equals(calleeClassification.Classification, "pure", StringComparison.Ordinal) &&
                IsFreshStringInitializationHelperCall(callerSummary, calleeSymbol, calleeClassification)) ||
               (!string.Equals(calleeClassification.Classification, "pure", StringComparison.Ordinal) &&
                IsCharSpanToStringWrapperCall(callerSummary, callSite, calleeSymbol, calleeClassification)) ||
               (!string.Equals(calleeClassification.Classification, "pure", StringComparison.Ordinal) &&
                IsSemanticallyPureCharSpanSearchHelperCall(callerSummary, calleeSymbol, calleeClassification)) ||
               (!string.Equals(calleeClassification.Classification, "pure", StringComparison.Ordinal) &&
                IsDateTimeArithmeticHelperCall(callerSummary.Symbol, calleeSymbol)) ||
               (!string.Equals(calleeClassification.Classification, "pure", StringComparison.Ordinal) &&
                IsDateTimeOffsetArithmeticHelperCall(callerSummary.Symbol, calleeSymbol)) ||
               (!string.Equals(calleeClassification.Classification, "pure", StringComparison.Ordinal) &&
                IsDateTimeToBinaryHelperCall(callerSummary.Symbol, calleeSymbol)) ||
               (!string.Equals(calleeClassification.Classification, "pure", StringComparison.Ordinal) &&
                IsDateTimeConstructorHelperCall(callerSummary.Symbol, calleeSymbol));
    }

    private static bool IsDateTimeArithmeticHelperCall(string callerSymbol, string calleeSymbol)
    {
        return string.Equals(callerSymbol, "System.DateTime.AddUnits(double, long, long)", StringComparison.Ordinal) &&
               (string.Equals(calleeSymbol, "System.Math.Abs(double)", StringComparison.Ordinal) ||
                string.Equals(calleeSymbol, "System.Math.Truncate(double)", StringComparison.Ordinal));
    }

    private static bool IsDateTimeToBinaryHelperCall(string callerSymbol, string calleeSymbol)
    {
        return string.Equals(callerSymbol, "System.DateTime.ToBinary()", StringComparison.Ordinal) &&
               string.Equals(calleeSymbol,
                   "System.TimeZoneInfo.GetLocalUtcOffset(System.DateTime, System.TimeZoneInfoOptions)",
                   StringComparison.Ordinal);
    }

    private static bool IsDateTimeConstructorHelperCall(string callerSymbol, string calleeSymbol)
    {
        return string.Equals(callerSymbol, "System.DateTime..ctor(int, int, int)", StringComparison.Ordinal) &&
               string.Equals(calleeSymbol, "System.DateTime.DateToTicks(int, int, int)", StringComparison.Ordinal);
    }

    private static bool IsDateTimeOffsetArithmeticHelperCall(string callerSymbol, string calleeSymbol)
    {
        return IsDateTimeOffsetArithmeticWrapper(callerSymbol) &&
               (IsDateTimeArithmeticCall(calleeSymbol) ||
                string.Equals(calleeSymbol, "System.DateTimeOffset..ctor(System.DateTime, System.TimeSpan)",
                    StringComparison.Ordinal) ||
                string.Equals(calleeSymbol, "System.DateTimeOffset.get_ClockDateTime()", StringComparison.Ordinal) ||
                string.Equals(calleeSymbol, "System.DateTimeOffset.get_Offset()", StringComparison.Ordinal));
    }

    private static bool IsDateTimeOffsetArithmeticWrapper(string symbol)
    {
        return symbol is
            "System.DateTimeOffset.Add(System.TimeSpan)" or
            "System.DateTimeOffset.AddDays(double)" or
            "System.DateTimeOffset.AddHours(double)" or
            "System.DateTimeOffset.AddMilliseconds(double)" or
            "System.DateTimeOffset.AddMinutes(double)" or
            "System.DateTimeOffset.AddMonths(int)" or
            "System.DateTimeOffset.AddSeconds(double)" or
            "System.DateTimeOffset.AddTicks(long)" or
            "System.DateTimeOffset.AddYears(int)";
    }

    private static bool IsDateTimeArithmeticCall(string symbol)
    {
        return symbol is
            "System.DateTime.Add(System.TimeSpan)" or
            "System.DateTime.AddDays(double)" or
            "System.DateTime.AddHours(double)" or
            "System.DateTime.AddMilliseconds(double)" or
            "System.DateTime.AddMinutes(double)" or
            "System.DateTime.AddMonths(int)" or
            "System.DateTime.AddSeconds(double)" or
            "System.DateTime.AddTicks(long)" or
            "System.DateTime.AddYears(int)";
    }

    private static bool IsFreshArrayInitializationHelperCall(
        MethodEffectSummary callerSummary,
        string calleeSymbol,
        MethodPurityClassification calleeClassification)
    {
        if (!IsFreshArrayInitializationContext(callerSummary)) return false;

        return (IsFreshArrayCopyHelperCall(calleeSymbol) &&
                HasFreshArrayCopyBlockingChain(calleeClassification.FirstBlockingCallChain)) ||
               (IsFreshArraySpanWriteHelperCall(calleeSymbol) &&
                HasFreshArraySpanWriteValidationBlockingChain(calleeClassification.FirstBlockingCallChain));
    }

    private static bool IsFreshArrayInitializationContext(MethodEffectSummary summary)
    {
        if (!summary.Effects.Contains("allocates_array", StringComparer.Ordinal) ||
            summary.Effects.Contains("allocates_object", StringComparer.Ordinal) ||
            summary.Effects.Contains("writes_static_field", StringComparer.Ordinal) ||
            summary.Effects.Contains("writes_instance_field", StringComparer.Ordinal) ||
            summary.Effects.Contains("writes_indirect_memory", StringComparer.Ordinal) ||
            summary.Effects.Contains("indirect_call", StringComparer.Ordinal) ||
            summary.Effects.Contains("virtual_call", StringComparer.Ordinal))
            return false;

        return HasOnlySafeStaticReads(summary);
    }

    private static bool IsFreshStringInitializationHelperCall(
        MethodEffectSummary callerSummary,
        string calleeSymbol,
        MethodPurityClassification calleeClassification)
    {
        if (!IsFreshStringInitializationContext(callerSummary)) return false;

        return IsFreshStringCopyHelperCall(calleeSymbol) &&
               HasFreshStringCopyBlockingChain(calleeClassification.FirstBlockingCallChain);
    }

    private static bool IsFreshStringInitializationContext(MethodEffectSummary summary)
    {
        if (!summary.Effects.Contains("allocates_object", StringComparer.Ordinal) ||
            summary.Effects.Contains("allocates_array", StringComparer.Ordinal) ||
            summary.Effects.Contains("writes_static_field", StringComparer.Ordinal) ||
            summary.Effects.Contains("writes_instance_field", StringComparer.Ordinal) ||
            summary.Effects.Contains("writes_indirect_memory", StringComparer.Ordinal) ||
            summary.Effects.Contains("indirect_call", StringComparer.Ordinal) ||
            summary.Effects.Contains("virtual_call", StringComparer.Ordinal))
            return false;

        if (!summary.Calls.Any(static call =>
                string.Equals(call, "string.FastAllocateString(int)->string", StringComparison.Ordinal)))
            return false;

        return HasOnlySafeStaticReads(summary);
    }

    private static bool IsFreshStringCopyHelperCall(string calleeSymbol)
    {
        return calleeSymbol.StartsWith("System.ReadOnlySpan`1", StringComparison.Ordinal) &&
               calleeSymbol.Contains(".CopyTo(System.Span`1<!0>)", StringComparison.Ordinal);
    }

    private static bool IsFreshArrayCopyHelperCall(string calleeSymbol)
    {
        return IsBufferMemmoveCall(calleeSymbol) ||
               IsBufferMemmoveHelper(calleeSymbol);
    }

    private static bool IsFreshArraySpanWriteHelperCall(string calleeSymbol)
    {
        return calleeSymbol.StartsWith("System.Runtime.InteropServices.MemoryMarshal.TryWrite(",
            StringComparison.Ordinal);
    }

    private static bool IsFreshArrayTemporaryInitializationHelperCall(
        MethodEffectSummary callerSummary,
        string calleeSymbol,
        MethodPurityClassification calleeClassification)
    {
        return IsFreshArrayInitializationContext(callerSummary) &&
               calleeSymbol.Contains("..ctor(", StringComparison.Ordinal) &&
               calleeClassification.Categories.All(IsTemporaryInitializationCategory) &&
               HasValidationOnlyBlockingChain(calleeClassification.FirstBlockingCallChain);
    }

    private static bool HasFreshStringCopyBlockingChain(string[] blockingCallChain)
    {
        if (blockingCallChain.Length == 0) return false;

        if (blockingCallChain.All(IsBufferMemmoveHelper)) return true;

        return string.Equals(blockingCallChain[0], "System.ReadOnlySpan`1.CopyTo(System.Span`1<!0>)",
                   StringComparison.Ordinal) &&
               blockingCallChain.Skip(1).All(IsBufferMemmoveHelper);
    }

    private static bool HasFreshArrayCopyBlockingChain(string[] blockingCallChain)
    {
        return blockingCallChain.Length != 0 &&
               blockingCallChain.All(IsBufferMemmoveHelper);
    }

    private static bool HasFreshArraySpanWriteValidationBlockingChain(string[] blockingCallChain)
    {
        return blockingCallChain.Length >= 1 &&
               string.Equals(
                   blockingCallChain[0],
                   "System.ThrowHelper.ThrowInvalidTypeWithPointersNotSupported(System.Type)",
                   StringComparison.Ordinal);
    }

    private static bool HasValidationOnlyBlockingChain(string[] blockingCallChain)
    {
        if (blockingCallChain.Length == 0) return false;

        var first = blockingCallChain[0];
        return first.Contains(".Throw", StringComparison.Ordinal) &&
               (first.Contains("Argument", StringComparison.Ordinal) ||
                first.StartsWith("System.ThrowHelper.Throw", StringComparison.Ordinal));
    }

    private static bool IsTemporaryInitializationCategory(string category)
    {
        return string.Equals(category, "caller_visible_memory_write", StringComparison.Ordinal) ||
               string.Equals(category, "global_state_read", StringComparison.Ordinal) ||
               string.Equals(category, "global_state_write", StringComparison.Ordinal) ||
               string.Equals(category, "impure_callee", StringComparison.Ordinal) ||
               string.Equals(category, "object_state_write", StringComparison.Ordinal);
    }

    private static bool IsBufferMemmoveHelper(string symbol)
    {
        return string.Equals(symbol, "System.Buffer.Memmove(ref !!0, ref !!0, nuint)", StringComparison.Ordinal) ||
               string.Equals(symbol, "System.Buffer.Memmove(ref byte, ref byte, nuint)", StringComparison.Ordinal) ||
               string.Equals(symbol, "System.Buffer._Memmove(ref byte, ref byte, nuint)", StringComparison.Ordinal) ||
               string.Equals(symbol, "System.Buffer.__Memmove(byte*, byte*, nuint)", StringComparison.Ordinal) ||
               string.Equals(symbol, "System.Buffer.BulkMoveWithWriteBarrier(ref byte, ref byte, nuint)",
                   StringComparison.Ordinal) ||
               string.Equals(symbol, "System.Buffer._BulkMoveWithWriteBarrier(ref byte, ref byte, nuint)",
                   StringComparison.Ordinal) ||
               string.Equals(symbol, "System.Buffer.__BulkMoveWithWriteBarrier(ref byte, ref byte, nuint)",
                   StringComparison.Ordinal);
    }

    private static bool IsSemanticallyPureCharSpanSearchHelperCall(
        MethodEffectSummary callerSummary,
        string calleeSymbol,
        MethodPurityClassification calleeClassification)
    {
        return HasCharSpanSearchContext(callerSummary) &&
               IsEqualityBasedSpanSearchHelper(calleeSymbol) &&
               HasEqualityBasedSpanSearchBlockingChain(calleeClassification.FirstBlockingCallChain);
    }

    private static bool IsCharSpanToStringWrapperCall(
        MethodEffectSummary callerSummary,
        CallSiteSummary callSite,
        string calleeSymbol,
        MethodPurityClassification calleeClassification)
    {
        if (callSite.UsesDynamicDispatch ||
            !HasCharSpanToStringWrapperContext(callerSummary) ||
            !IsObjectToStringCall(calleeSymbol))
            return false;

        return HasObjectToStringBlockingChain(calleeClassification.FirstBlockingCallChain);
    }

    private static bool HasCharSpanToStringWrapperContext(MethodEffectSummary summary)
    {
        if (!summary.ExactSymbolKey.EndsWith(")->string", StringComparison.Ordinal)) return false;

        return summary.Calls.Any(IsCharSpanReturningCall);
    }

    private static bool IsCharSpanReturningCall(string callSymbol)
    {
        return callSymbol.EndsWith(")->System.ReadOnlySpan`1<char>", StringComparison.Ordinal) ||
               callSymbol.EndsWith(")->System.Span`1<char>", StringComparison.Ordinal);
    }

    private static bool IsObjectToStringCall(string calleeSymbol)
    {
        return string.Equals(calleeSymbol, "object.ToString()", StringComparison.Ordinal) ||
               string.Equals(calleeSymbol, "System.Object.ToString()", StringComparison.Ordinal);
    }

    private static bool HasObjectToStringBlockingChain(string[] blockingCallChain)
    {
        return (blockingCallChain.Length == 1 &&
                string.Equals(blockingCallChain[0], "System.Object.GetType()", StringComparison.Ordinal)) ||
               (blockingCallChain.Length == 2 &&
                string.Equals(blockingCallChain[0], "System.Object.ToString()", StringComparison.Ordinal) &&
                string.Equals(blockingCallChain[1], "System.Object.GetType()", StringComparison.Ordinal));
    }

    private static bool HasCharSpanSearchContext(MethodEffectSummary summary)
    {
        return summary.Symbol.Contains("System.ReadOnlySpan`1<char>", StringComparison.Ordinal) ||
               summary.Symbol.Contains("System.Span`1<char>", StringComparison.Ordinal);
    }

    private static bool IsEqualityBasedSpanSearchHelper(string calleeSymbol)
    {
        var methodBaseSymbol = GetMethodBaseSymbol(calleeSymbol);
        return string.Equals(methodBaseSymbol, "System.MemoryExtensions.Contains", StringComparison.Ordinal) ||
               string.Equals(methodBaseSymbol, "System.MemoryExtensions.IndexOf", StringComparison.Ordinal) ||
               string.Equals(methodBaseSymbol, "System.MemoryExtensions.IndexOfAny", StringComparison.Ordinal) ||
               string.Equals(methodBaseSymbol, "System.MemoryExtensions.LastIndexOf", StringComparison.Ordinal) ||
               string.Equals(methodBaseSymbol, "System.MemoryExtensions.LastIndexOfAny", StringComparison.Ordinal);
    }

    private static bool HasEqualityBasedSpanSearchBlockingChain(string[] blockingCallChain)
    {
        return blockingCallChain.Length >= 2 &&
               (blockingCallChain[0].StartsWith("System.SpanHelpers.Contains(", StringComparison.Ordinal) ||
                blockingCallChain[0].StartsWith("System.SpanHelpers.IndexOf(", StringComparison.Ordinal) ||
                blockingCallChain[0].StartsWith("System.SpanHelpers.IndexOfAny(", StringComparison.Ordinal) ||
                blockingCallChain[0].StartsWith("System.SpanHelpers.LastIndexOf(", StringComparison.Ordinal) ||
                blockingCallChain[0].StartsWith("System.SpanHelpers.LastIndexOfAny(", StringComparison.Ordinal)) &&
               string.Equals(blockingCallChain[1], "System.IEquatable`1.Equals(!0)", StringComparison.Ordinal);
    }

    private static bool ShouldIgnoreUnknownCall(
        MethodEffectSummary callerSummary,
        CallSiteSummary callSite,
        string calleeSymbol,
        MethodPurityClassification calleeClassification,
        AssemblyEffectReport assembly,
        string calleeKey,
        IReadOnlyDictionary<string, MethodEffectSummary> bySymbol,
        IReadOnlyDictionary<string, GeneratedPurityCatalogEntry> externalGeneratedPurityEntries,
        IReadOnlyDictionary<string, GeneratedPurityCatalogEntry> reviewedGeneratedPurityEntries,
        Dictionary<string, MethodPurityClassification> memo,
        Dictionary<string, bool> freshOwnedInitializationMemo,
        Dictionary<string, bool> validationThrowHelperMemo,
        HashSet<string> visiting,
        bool treatsArgumentGuardThrowHelpersAsPure,
        bool treatsDelegateDispatchAsSemantic)
    {
        return IsPureArgumentGuardWrapper(calleeSymbol) ||
               (treatsArgumentGuardThrowHelpersAsPure &&
                IsArgumentGuardThrowHelper(calleeSymbol)) ||
               (treatsDelegateDispatchAsSemantic &&
                IsSemanticallyNeutralValidationThrowHelper(calleeSymbol)) ||
               IsValidationThrowHelperCompatible(
                   assembly,
                   calleeKey,
                   bySymbol,
                   externalGeneratedPurityEntries,
                   reviewedGeneratedPurityEntries,
                   memo,
                   freshOwnedInitializationMemo,
                   validationThrowHelperMemo,
                   visiting) ||
               ShouldTreatCallAsSemanticallyPure(callerSummary, callSite, calleeSymbol, calleeClassification);
    }

    private static void AddImpureCalleeCategories(
        SortedSet<string> impureCategories,
        MethodPurityClassification calleeClassification)
    {
        foreach (var category in calleeClassification.Categories)
            if (string.Equals(category, "global_state_read", StringComparison.Ordinal) ||
                string.Equals(category, "global_state_write", StringComparison.Ordinal))
                impureCategories.Add(category);

        impureCategories.Add("impure_callee");
    }

    private static bool TryClassifyRuntimeIntrinsicStub(
        MethodEffectSummary summary,
        out MethodPurityClassification classification)
    {
        classification = default!;
        if (string.IsNullOrWhiteSpace(summary.Symbol)) return false;

        if (IsPureRuntimeIntrinsicStub(summary.Symbol))
        {
            var freshnessClassification = IsFastAllocateString(summary.Symbol)
                ? "fresh_owned_object_write"
                : "none";
            classification = new MethodPurityClassification(
                "pure",
                Array.Empty<string>(),
                Array.Empty<string>(),
                false,
                string.Equals(freshnessClassification, "fresh_owned_object_write", StringComparison.Ordinal),
                false,
                freshnessClassification,
                string.Equals(freshnessClassification, "fresh_owned_object_write", StringComparison.Ordinal)
                    ? "internal_only"
                    : "none");
            return true;
        }

        if (summary.Symbol.StartsWith("System.Runtime.CompilerServices.Unsafe.WriteUnaligned(",
                StringComparison.Ordinal))
        {
            classification = new MethodPurityClassification(
                "impure",
                new[] { "caller_visible_memory_write" },
                Array.Empty<string>(),
                false,
                false,
                false,
                "none",
                "caller_visible");
            return true;
        }

        return false;
    }

    private static bool TryClassifyKnownBclSummary(
        MethodEffectSummary summary,
        out MethodPurityClassification classification)
    {
        classification = default!;
        var symbol = summary.Symbol;
        if (string.IsNullOrWhiteSpace(symbol)) return false;

        if (TryGetKnownGeneratedPureVisibility(symbol, out var pureVisibility))
        {
            classification = CreateGeneratedPureClassification(summary, pureVisibility);
            return true;
        }

        if (TryGetKnownGeneratedImpureCategories(symbol, out var impureCategories))
        {
            classification = CreateGeneratedImpureClassification(summary, impureCategories);
            return true;
        }

        return false;
    }

    private static MethodPurityClassification CreateGeneratedPureClassification(
        MethodEffectSummary summary,
        string effectVisibilityClassification)
    {
        var freshnessClassification = GetFreshnessClassification(summary, "pure");
        if (string.Equals(effectVisibilityClassification, "none", StringComparison.Ordinal) &&
            !string.Equals(freshnessClassification, "none", StringComparison.Ordinal))
            effectVisibilityClassification = "internal_only";

        return new MethodPurityClassification(
            "pure",
            Array.Empty<string>(),
            Array.Empty<string>(),
            summary.Effects.Contains("allocates_array", StringComparer.Ordinal),
            summary.Effects.Contains("allocates_object", StringComparer.Ordinal),
            false,
            freshnessClassification,
            effectVisibilityClassification);
    }

    private static MethodPurityClassification CreateGeneratedImpureClassification(
        MethodEffectSummary summary,
        string[] categories)
    {
        return new MethodPurityClassification(
            "impure",
            categories,
            Array.Empty<string>(),
            summary.Effects.Contains("allocates_array", StringComparer.Ordinal),
            summary.Effects.Contains("allocates_object", StringComparer.Ordinal),
            false,
            "none",
            "caller_visible");
    }

    private static bool TryGetKnownGeneratedPureVisibility(string symbol, out string effectVisibilityClassification)
    {
        effectVisibilityClassification = "none";

        if (symbol is
            "System.Diagnostics.StackFrame.GetMethod()" or
            "System.Object.GetType()" or
            "System.HashCode.ToHashCode()" or
            "System.Index.get_End()" or
            "System.Index.get_Start()" or
            "System.Uri.IsWellFormedUriString(string, System.UriKind)" or
            "System.Uri.UnescapeDataString(string)" or
            "System.Decimal.Negate(decimal)" or
            "System.Decimal.op_UnaryNegation(decimal)" or
            "System.Decimal.Compare(decimal, decimal)" or
            "System.Decimal.ToDouble(decimal)" or
            "System.Buffers.ReadOnlySequence`1.Slice(long)")
            return true;

        if (symbol is
            "System.Diagnostics.Debug.Assert(bool)" or
            "System.ComponentModel.BrowsableAttribute..ctor(bool)" or
            "System.ComponentModel.DescriptionAttribute..ctor(string)" or
            "System.ComponentModel.DataAnnotations.EmailAddressAttribute..ctor()" or
            "System.Diagnostics.ConditionalAttribute..ctor(string)" or
            "System.Uri.EscapeDataString(string)")
        {
            effectVisibilityClassification = "internal_only";
            return true;
        }

        if (IsPureGeneratedStringMember(symbol) ||
            IsPureGeneratedPathHelper(symbol) ||
            IsPureGeneratedExpressionFactory(symbol) ||
            IsPureGeneratedInterpolatedStringHandlerMember(symbol) ||
            IsPureGeneratedImmutableArrayMember(symbol) ||
            IsPureGeneratedValueArrayProjection(symbol) ||
            IsPureGeneratedDeterministicValueFormatting(symbol) ||
            IsPureGeneratedDeterministicNumericHelper(symbol) ||
            IsPureGeneratedStableNetworkValue(symbol) ||
            IsPureGeneratedArrayPredicate(symbol) ||
            IsPureGeneratedListPredicate(symbol) ||
            IsPureGeneratedArrayRead(symbol) ||
            IsPureGeneratedArgumentGuard(symbol) ||
            IsPureGeneratedContractGuard(symbol) ||
            IsPureGeneratedConstructor(symbol) ||
            IsPureGeneratedTypeMetadata(symbol) ||
            IsPureGeneratedImmutableMember(symbol) ||
            IsPureGeneratedFileSystemMetadataGetter(symbol) ||
            IsPureGeneratedEnvironmentStableGetter(symbol) ||
            IsPureGeneratedCharHelper(symbol) ||
            IsPureGeneratedQueueFreshArray(symbol) ||
            IsPureGeneratedCultureCompare(symbol))
        {
            if (IsPureGeneratedStringMember(symbol) ||
                IsPureGeneratedPathHelper(symbol) ||
                IsPureGeneratedExpressionFactory(symbol) ||
                IsPureGeneratedInterpolatedStringHandlerMember(symbol) ||
                IsPureGeneratedImmutableArrayMember(symbol) ||
                IsPureGeneratedValueArrayProjection(symbol) ||
                IsPureGeneratedDeterministicValueFormatting(symbol) ||
                IsPureGeneratedStableNetworkValue(symbol))
                effectVisibilityClassification = "internal_only";

            return true;
        }

        return false;
    }

    private static bool TryGetKnownGeneratedImpureCategories(string symbol, out string[] categories)
    {
        categories = new[] { "impure_callee" };

        if (symbol is
            "System.Guid.NewGuid()" or
            "System.Decimal.ToInt32(decimal)")
        {
            categories = new[] { "throw" };
            return true;
        }

        if (symbol.StartsWith("System.Char.ConvertToUtf32(", StringComparison.Ordinal) ||
            symbol.StartsWith("System.Char.ConvertFromUtf32(", StringComparison.Ordinal))
        {
            categories = new[] { "throw" };
            return true;
        }

        if (symbol.StartsWith("System.IO.Path.GetFullPath(", StringComparison.Ordinal) ||
            string.Equals(symbol, "System.TimeZoneInfo.FindSystemTimeZoneById(string)", StringComparison.Ordinal))
        {
            categories = new[] { "throw" };
            return true;
        }

        if (string.Equals(symbol, "System.IO.FileSystemInfo.get_Extension()", StringComparison.Ordinal))
        {
            categories = new[] { "impure_callee" };
            return true;
        }

        if (symbol.StartsWith("System.Console.Beep", StringComparison.Ordinal) ||
            symbol.StartsWith("System.Array.BinarySearch(", StringComparison.Ordinal))
        {
            categories = new[] { "impure_callee" };
            return true;
        }

        if (symbol.StartsWith("System.Console.Read", StringComparison.Ordinal) ||
            symbol.StartsWith("System.Console.Write", StringComparison.Ordinal))
        {
            categories = new[] { "catalog_hit" };
            return true;
        }

        if (symbol.StartsWith("System.Console.get_", StringComparison.Ordinal))
        {
            categories = new[] { "global_state_read" };
            return true;
        }

        if (symbol is
            "System.Diagnostics.Stopwatch.GetTimestamp()" or
            "System.Diagnostics.Stopwatch.get_ElapsedTicks()" or
            "System.Diagnostics.Stopwatch.Start()" or
            "System.Environment.get_StackTrace()")
        {
            categories = new[] { "impure_callee" };
            return true;
        }

        if (symbol.StartsWith("System.Diagnostics.Process.Start(", StringComparison.Ordinal))
        {
            categories = new[] { "global_state_write" };
            return true;
        }

        if (symbol.StartsWith("System.Diagnostics.Process.GetCurrentProcess(", StringComparison.Ordinal) ||
            symbol.StartsWith("System.Diagnostics.Process.GetProcesses", StringComparison.Ordinal) ||
            symbol.StartsWith("System.Diagnostics.Process.get_", StringComparison.Ordinal))
        {
            categories = new[] { "global_state_read" };
            return true;
        }

        if (symbol.StartsWith("System.Text.StringBuilder.Append(", StringComparison.Ordinal) ||
            symbol.StartsWith("System.Text.StringBuilder.AppendLine(", StringComparison.Ordinal) ||
            symbol.StartsWith("System.Text.StringBuilder.Clear(", StringComparison.Ordinal) ||
            symbol.StartsWith("System.Text.StringBuilder.Insert(", StringComparison.Ordinal) ||
            symbol.StartsWith("System.Text.StringBuilder.Remove(", StringComparison.Ordinal) ||
            symbol.StartsWith("System.Text.StringBuilder.Replace(", StringComparison.Ordinal))
        {
            categories = new[] { "catalog_hit" };
            return true;
        }

        if (symbol.StartsWith("System.Threading.Tasks.Task.Run(", StringComparison.Ordinal))
        {
            categories = new[] { "caller_visible_memory_write" };
            return true;
        }

        if (symbol.StartsWith("System.Activator.CreateInstance", StringComparison.Ordinal) ||
            symbol.StartsWith("System.Activator.CreateInstanceFrom", StringComparison.Ordinal) ||
            string.Equals(symbol, "System.AppContext.get_TargetFrameworkName()", StringComparison.Ordinal) ||
            string.Equals(symbol, "System.Environment.set_CurrentDirectory(string)", StringComparison.Ordinal) ||
            string.Equals(symbol, "System.IO.Directory.SetCurrentDirectory(string)", StringComparison.Ordinal) ||
            symbol.StartsWith("System.IO.Path.GetTempPath", StringComparison.Ordinal) ||
            symbol.StartsWith("System.Threading.Tasks.Task.Delay(", StringComparison.Ordinal) ||
            string.Equals(symbol, "System.Threading.Thread.get_CurrentThread()", StringComparison.Ordinal))
        {
            categories = new[] { "global_state_write" };
            return true;
        }

        if (string.Equals(symbol, "System.AppDomain.get_BaseDirectory()", StringComparison.Ordinal) ||
            string.Equals(symbol, "System.TimeZoneInfo.ConvertTime(System.DateTimeOffset, System.TimeZoneInfo)",
                StringComparison.Ordinal) ||
            string.Equals(symbol, "System.Configuration.ConfigurationManager.get_AppSettings()",
                StringComparison.Ordinal) ||
            string.Equals(symbol, "System.Configuration.ConfigurationManager.get_ConnectionStrings()",
                StringComparison.Ordinal))
        {
            categories = new[] { "global_state_read" };
            return true;
        }

        if (symbol is
            "System.Environment.get_TickCount()" or
            "System.Environment.get_TickCount64()" or
            "System.Environment.get_CurrentManagedThreadId()" or
            "System.Environment.get_ExitCode()")
        {
            categories = new[] { "metadata_only_or_external" };
            return true;
        }

        if (string.Equals(symbol, "System.Environment.Exit(int)", StringComparison.Ordinal))
        {
            categories = new[] { "unknown_callee" };
            return true;
        }

        if (symbol.StartsWith("System.Array.ConvertAll(", StringComparison.Ordinal) ||
            string.Equals(symbol, "System.Collections.Generic.List`1.ForEach(System.Action`1<!0>)",
                StringComparison.Ordinal))
        {
            categories = new[] { "caller_visible_memory_write" };
            return true;
        }

        if (IsGeneratedArrayComparerSort(symbol))
        {
            categories = new[] { "global_state_read", "impure_callee" };
            return true;
        }

        if (string.Equals(symbol, "System.Security.Claims.ClaimsPrincipal.IsInRole(string)", StringComparison.Ordinal))
        {
            categories = new[] { "global_state_read" };
            return true;
        }

        return false;
    }

    private static bool IsPureGeneratedArrayRead(string symbol)
    {
        return symbol.StartsWith("System.Array.IndexOf(", StringComparison.Ordinal) ||
               string.Equals(symbol, "System.Array.get_Length()", StringComparison.Ordinal);
    }

    private static bool IsPureGeneratedArgumentGuard(string symbol)
    {
        return symbol.StartsWith("System.ArgumentException.ThrowIfNullOrEmpty(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.ArgumentException.ThrowIfNullOrWhiteSpace(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.ArgumentNullException.ThrowIfNull(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.ArgumentOutOfRangeException.ThrowIf", StringComparison.Ordinal);
    }

    private static bool IsPureGeneratedContractGuard(string symbol)
    {
        return symbol.StartsWith("System.Diagnostics.Contracts.Contract.Requires(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Diagnostics.Contracts.Contract.Ensures(", StringComparison.Ordinal);
    }

    private static bool IsPureGeneratedConstructor(string symbol)
    {
        return symbol.StartsWith("System.ArgumentException..ctor(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.ArgumentNullException..ctor(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.BadImageFormatException..ctor(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.DivideByZeroException..ctor(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.IO.EndOfStreamException..ctor(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.FlagsAttribute..ctor(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.FormatException..ctor(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Index..ctor(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.InvalidOperationException..ctor(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.IO.FileNotFoundException..ctor(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.ComponentModel.AddingNewEventArgs..ctor(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.ComponentModel.DataAnnotations.ValidationResult..ctor(",
                   StringComparison.Ordinal) ||
               symbol.StartsWith("System.NotImplementedException..ctor(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.NotSupportedException..ctor(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.ObjectDisposedException..ctor(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.ObsoleteAttribute..ctor(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.OverflowException..ctor(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.PlatformNotSupportedException..ctor(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Range..ctor(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Runtime.CompilerServices.CallerArgumentExpressionAttribute..ctor(",
                   StringComparison.Ordinal) ||
               symbol.StartsWith("System.Runtime.CompilerServices.MethodImplAttribute..ctor(",
                   StringComparison.Ordinal) ||
               symbol.StartsWith("System.SerializableAttribute..ctor(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.UIntPtr..ctor(", StringComparison.Ordinal);
    }

    private static bool IsPureGeneratedTypeMetadata(string symbol)
    {
        return symbol.StartsWith("System.Type.get_", StringComparison.Ordinal) ||
               symbol.StartsWith("System.RuntimeType.get_", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Reflection.MemberInfo.get_", StringComparison.Ordinal) ||
               symbol.StartsWith("System.RuntimeTypeHandle.", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Runtime.CompilerServices.TypeHandle.", StringComparison.Ordinal);
    }

    private static bool IsPureGeneratedStringMember(string symbol)
    {
        return symbol.StartsWith("System.String.Contains(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.String..ctor(char", StringComparison.Ordinal) ||
               symbol.StartsWith("System.String.Split(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.String.CompareTo(", StringComparison.Ordinal) ||
               IsPureGeneratedStringJoin(symbol) ||
               string.Equals(symbol, "System.String.Clone()", StringComparison.Ordinal) ||
               symbol.StartsWith("System.String.IndexOf(char", StringComparison.Ordinal);
    }

    private static bool IsPureGeneratedStringJoin(string symbol)
    {
        return symbol.StartsWith("System.String.Join(char, string[]", StringComparison.Ordinal) ||
               symbol.StartsWith("System.String.Join(string, string[]", StringComparison.Ordinal) ||
               symbol.StartsWith("System.String.Join(string, System.Collections.Generic.IEnumerable`1<string>",
                   StringComparison.Ordinal);
    }

    private static bool IsPureGeneratedPathHelper(string symbol)
    {
        return symbol.StartsWith("System.IO.Path.GetExtension(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.IO.Path.HasExtension(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.IO.Path.GetFileName(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.IO.Path.GetFileNameWithoutExtension(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.IO.Path.GetDirectoryName(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.IO.Path.ChangeExtension(", StringComparison.Ordinal);
    }

    private static bool IsPureGeneratedExpressionFactory(string symbol)
    {
        return symbol.StartsWith("System.Linq.Expressions.Expression.Parameter(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Linq.Expressions.Expression.Constant(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Linq.Expressions.Expression.Lambda(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Linq.Expressions.Expression.Call(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Linq.Expressions.Expression.Equal(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Linq.Expressions.Expression.NotEqual(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Linq.Expressions.Expression.Add(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Linq.Expressions.Expression.AddChecked(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Linq.Expressions.Expression.Subtract(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Linq.Expressions.Expression.SubtractChecked(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Linq.Expressions.Expression.Multiply(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Linq.Expressions.Expression.MultiplyChecked(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Linq.Expressions.Expression.Divide(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Linq.Expressions.Expression.Modulo(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Linq.Expressions.Expression.AndAlso(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Linq.Expressions.Expression.OrElse(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Linq.Expressions.Expression.GreaterThan(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Linq.Expressions.Expression.GreaterThanOrEqual(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Linq.Expressions.Expression.LessThan(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Linq.Expressions.Expression.LessThanOrEqual(", StringComparison.Ordinal);
    }

    private static bool IsPureGeneratedInterpolatedStringHandlerMember(string symbol)
    {
        return string.Equals(symbol, "System.Runtime.CompilerServices.DefaultInterpolatedStringHandler..ctor(int, int)",
                   StringComparison.Ordinal) ||
               string.Equals(symbol,
                   "System.Runtime.CompilerServices.DefaultInterpolatedStringHandler.AppendFormatted(!!0)",
                   StringComparison.Ordinal) ||
               string.Equals(symbol,
                   "System.Runtime.CompilerServices.DefaultInterpolatedStringHandler.AppendFormatted(string)",
                   StringComparison.Ordinal) ||
               string.Equals(symbol,
                   "System.Runtime.CompilerServices.DefaultInterpolatedStringHandler.AppendLiteral(string)",
                   StringComparison.Ordinal) ||
               string.Equals(symbol,
                   "System.Runtime.CompilerServices.DefaultInterpolatedStringHandler.ToStringAndClear()",
                   StringComparison.Ordinal);
    }

    private static bool IsPureGeneratedImmutableMember(string symbol)
    {
        return symbol.StartsWith("System.Collections.Immutable.ImmutableList.Create", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Collections.Immutable.ImmutableList`1.Add(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Collections.Immutable.ImmutableList`1.AddRange(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Collections.Immutable.ImmutableList`1.Insert(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Collections.Immutable.ImmutableList`1.InsertRange(",
                   StringComparison.Ordinal) ||
               symbol.StartsWith("System.Collections.Immutable.ImmutableList`1.Remove(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Collections.Immutable.ImmutableList`1.RemoveAt(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Collections.Immutable.ImmutableList`1.RemoveRange(",
                   StringComparison.Ordinal) ||
               symbol.StartsWith("System.Collections.Immutable.ImmutableList`1.Replace(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Collections.Immutable.ImmutableList`1.SetItem(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Collections.Immutable.ImmutableHashSet.Create", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Collections.Immutable.ImmutableDictionary.Create", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Collections.Immutable.ImmutableHashSet`1.get_Count(",
                   StringComparison.Ordinal) ||
               symbol.StartsWith("System.Collections.Immutable.ImmutableHashSet`1.get_IsEmpty(",
                   StringComparison.Ordinal) ||
               symbol.StartsWith("System.Collections.Immutable.ImmutableHashSet`1.get_KeyComparer(",
                   StringComparison.Ordinal) ||
               string.Equals(symbol, "System.Collections.Immutable.ImmutableQueue`1.Clear()",
                   StringComparison.Ordinal) ||
               string.Equals(symbol, "System.Collections.Immutable.ImmutableStack`1.Clear()",
                   StringComparison.Ordinal) ||
               string.Equals(symbol, "System.Collections.Immutable.ImmutableStack`1.get_IsEmpty()",
                   StringComparison.Ordinal) ||
               symbol.StartsWith("System.Collections.Immutable.ImmutableStack`1.Push(", StringComparison.Ordinal);
    }

    private static bool IsPureGeneratedImmutableArrayMember(string symbol)
    {
        return symbol.StartsWith("System.Collections.Immutable.ImmutableArray.Create", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Collections.Immutable.ImmutableArray.CreateRange", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Collections.Immutable.ImmutableArray.ToImmutableArray",
                   StringComparison.Ordinal) ||
               symbol.StartsWith("System.Collections.Immutable.ImmutableArray`1.Slice(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Collections.Immutable.ImmutableArray`1.AddRange(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Collections.Immutable.ImmutableArray`1.InsertRange(",
                   StringComparison.Ordinal) ||
               symbol.StartsWith("System.Collections.Immutable.ImmutableArray`1.RemoveRange(",
                   StringComparison.Ordinal);
    }

    private static bool IsPureGeneratedValueArrayProjection(string symbol)
    {
        return symbol.StartsWith("System.Guid.ToByteArray(", StringComparison.Ordinal);
    }

    private static bool IsPureGeneratedDeterministicValueFormatting(string symbol)
    {
        return symbol.StartsWith("System.Guid.ToString(", StringComparison.Ordinal);
    }

    private static bool IsPureGeneratedDeterministicNumericHelper(string symbol)
    {
        return symbol.StartsWith("System.Numerics.BitOperations.", StringComparison.Ordinal) ||
               symbol.StartsWith("System.BitConverter.To", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(",
                   StringComparison.Ordinal);
    }

    private static bool IsPureGeneratedFileSystemMetadataGetter(string symbol)
    {
        return string.Equals(symbol, "System.IO.DirectoryInfo.get_Parent()", StringComparison.Ordinal) ||
               string.Equals(symbol, "System.IO.FileInfo.get_DirectoryName()", StringComparison.Ordinal);
    }

    private static bool IsPureGeneratedEnvironmentStableGetter(string symbol)
    {
        return symbol is
            "System.Environment.get_Is64BitOperatingSystem()" or
            "System.Environment.get_Is64BitProcess()" or
            "System.Environment.get_NewLine()" or
            "System.Environment.get_HasShutdownStarted()";
    }

    private static bool IsPureGeneratedCharHelper(string symbol)
    {
        return symbol is
                   "System.Boolean.CompareTo(bool)" or
                   "System.Char.GetNumericValue(char)" or
                   "System.Char.ToLowerInvariant(char)" or
                   "System.Char.ToUpperInvariant(char)" ||
               symbol.StartsWith("System.Char.Is", StringComparison.Ordinal);
    }

    private static bool IsPureGeneratedQueueFreshArray(string symbol)
    {
        return string.Equals(symbol, "System.Collections.Generic.Queue`1.ToArray()", StringComparison.Ordinal);
    }

    private static bool IsPureGeneratedCultureCompare(string symbol)
    {
        return symbol.StartsWith("System.Globalization.CompareInfo.Compare(", StringComparison.Ordinal);
    }

    private static bool IsPureGeneratedStableNetworkValue(string symbol)
    {
        return symbol.StartsWith("System.Net.IPAddress.get_", StringComparison.Ordinal);
    }

    private static bool IsPureGeneratedArrayPredicate(string symbol)
    {
        return symbol.StartsWith("System.Array.Exists(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Array.FindIndex(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Array.TrueForAll(", StringComparison.Ordinal);
    }

    private static bool IsPureGeneratedListPredicate(string symbol)
    {
        return symbol.StartsWith("System.Collections.Generic.List`1.Exists(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Collections.Generic.List`1.FindIndex(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Collections.Generic.List`1.TrueForAll(", StringComparison.Ordinal);
    }

    private static bool IsGeneratedArrayComparerSort(string symbol)
    {
        return symbol.StartsWith("System.Array.Sort(!!0[], System.Collections.Generic.IComparer`1<!!0>)",
                   StringComparison.Ordinal) ||
               symbol.StartsWith("System.Array.Sort(!!0[], int, int, System.Collections.Generic.IComparer`1<!!0>)",
                   StringComparison.Ordinal);
    }

    private static bool TryClassifySemanticPureWrapper(
        MethodEffectSummary summary,
        out MethodPurityClassification classification)
    {
        classification = default!;

        string effectVisibilityClassification;
        var treatsByRefLikeViewWrapperAsPure = false;
        if (HasPureReadOnlyCharSpanSearchWrapperPattern(summary))
        {
            effectVisibilityClassification = "none";
        }
        else if (HasPureArrayBackedByRefLikeViewWrapperPattern(summary))
        {
            effectVisibilityClassification = "none";
            treatsByRefLikeViewWrapperAsPure = true;
        }
        else if (HasPureSpanBackedByRefLikeViewWrapperPattern(summary))
        {
            effectVisibilityClassification = "none";
            treatsByRefLikeViewWrapperAsPure = true;
        }
        else if (HasPureStringFromReadOnlyCharSpanWrapperPattern(summary))
        {
            effectVisibilityClassification = "none";
        }
        else if (HasPureStringSliceNormalizationWrapperPattern(summary))
        {
            effectVisibilityClassification = "none";
        }
        else if (HasPureInvariantTextInfoStringWrapperPattern(summary))
        {
            effectVisibilityClassification = "internal_only";
        }
        else if (HasPureTypeMetadataBooleanWrapperPattern(summary))
        {
            effectVisibilityClassification = "none";
        }
        else if (HasPureTypeMetadataValueWrapperPattern(summary))
        {
            effectVisibilityClassification = "none";
        }
        else if (HasPureRuntimeTypeMetadataWrapperPattern(summary))
        {
            effectVisibilityClassification = "none";
        }
        else if (HasPureTypeIdentityWrapperPattern(summary))
        {
            effectVisibilityClassification = "none";
        }
        else if (HasPureCharScalarProjectionWrapperPattern(summary))
        {
            effectVisibilityClassification = "none";
        }
        else if (HasPureGuardedStringCharScanWrapperPattern(summary))
        {
            effectVisibilityClassification = "none";
        }
        else if (HasPureStringHashWrapperPattern(summary))
        {
            effectVisibilityClassification = "none";
        }
        else if (HasPureCharReplaceStringWrapperPattern(summary))
        {
            effectVisibilityClassification = "internal_only";
        }
        else if (HasPureStringSubstringWrapperPattern(summary) ||
                 HasPureFreshAllocatedStringCopyCorePattern(summary) ||
                 HasPureStringLengthCheckedConcatWrapperPattern(summary) ||
                 HasPureStringArrayConcatWrapperPattern(summary))
        {
            effectVisibilityClassification = "internal_only";
        }
        else if (HasPureGuardedImmutableStringRewriteWrapperPattern(summary))
        {
            effectVisibilityClassification =
                summary.RootCandidates.Contains("safe_static_cache_read", StringComparer.Ordinal) ||
                summary.RootCandidates.Contains("safe_static_constant_read", StringComparer.Ordinal)
                    ? "internal_only"
                    : "none";
        }
        else if (HasPureIndexedStringReplaceWrapperPattern(summary))
        {
            effectVisibilityClassification = "internal_only";
        }
        else if (HasPureStackLocalCharBuilderStringWrapperPattern(summary) ||
                 HasPureImmutableStringRewriteWrapperPattern(summary))
        {
            effectVisibilityClassification = "internal_only";
        }
        else
        {
            return false;
        }

        classification = new MethodPurityClassification(
            "pure",
            Array.Empty<string>(),
            Array.Empty<string>(),
            false,
            treatsByRefLikeViewWrapperAsPure
                ? false
                : summary.Effects.Contains("allocates_object", StringComparer.Ordinal),
            false,
            "none",
            effectVisibilityClassification);
        return true;
    }

    private static bool IsPureRuntimeIntrinsicStub(string symbol)
    {
        return symbol.StartsWith("System.Runtime.CompilerServices.Unsafe.As(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Runtime.CompilerServices.Unsafe.AsPointer(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Runtime.CompilerServices.Unsafe.AsRef(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Runtime.CompilerServices.Unsafe.ReadUnaligned(", StringComparison.Ordinal) ||
               string.Equals(symbol, "System.Runtime.CompilerServices.Unsafe.SizeOf()", StringComparison.Ordinal) ||
               IsFastAllocateString(symbol);
    }

    private static bool IsFastAllocateString(string symbol)
    {
        return string.Equals(symbol, "string.FastAllocateString(int)", StringComparison.Ordinal) ||
               string.Equals(symbol, "System.String.FastAllocateString(int)", StringComparison.Ordinal);
    }

    private static bool HasPureReadOnlyCharSpanSearchWrapperPattern(MethodEffectSummary summary)
    {
        return CallsOnly(summary, "calls_method") &&
               RootsAreSemanticallyPureWrapperCompatible(summary) &&
               summary.Calls.Any(static call =>
                   call.Contains("System.ReadOnlySpan`1<char>", StringComparison.Ordinal)) &&
               summary.Calls.Any(IsReadOnlyCharSpanSearchHelperCall) &&
               summary.Calls.All(IsReadOnlyCharSpanSearchHelperCall);
    }

    private static bool HasPureArrayBackedByRefLikeViewWrapperPattern(MethodEffectSummary summary)
    {
        return CallsOnly(summary, "allocates_object", "calls_method", "writes_indirect_memory") &&
               RootsAreArrayBackedByRefLikeViewWrapperCompatible(summary) &&
               IsByRefLikeViewReturn(summary.ExactSymbolKey) &&
               summary.Calls.Any(IsArrayBackedByRefLikeViewConstructionCall) &&
               summary.Calls.All(IsArrayBackedByRefLikeViewWrapperCall);
    }

    private static bool HasPureSpanBackedByRefLikeViewWrapperPattern(MethodEffectSummary summary)
    {
        return CallsOnly(summary, "allocates_object", "calls_method", "reads_instance_field") &&
               RootsAreSemanticallyPureWrapperCompatible(summary) &&
               IsByRefLikeViewReturn(summary.ExactSymbolKey) &&
               HasOnlyByRefLikeViewProjectionFieldReads(summary) &&
               summary.Calls.Any(IsByRefLikeViewConstructionCall) &&
               summary.Calls.All(IsSpanBackedByRefLikeViewWrapperCall);
    }

    private static bool HasPureStringFromReadOnlyCharSpanWrapperPattern(MethodEffectSummary summary)
    {
        return CallsOnly(summary, "calls_method") &&
               RootsAreSemanticallyPureWrapperCompatible(summary) &&
               summary.Calls.Any(IsStringToReadOnlyCharSpanWrapperCall) &&
               summary.Calls.Any(static call =>
                   string.Equals(call, "object.ToString()->string", StringComparison.Ordinal)) &&
               summary.Calls.All(IsStringFromReadOnlyCharSpanWrapperCall);
    }

    private static bool HasPureStringSliceNormalizationWrapperPattern(MethodEffectSummary summary)
    {
        return CallsOnly(summary, "calls_method") &&
               RootsAreSemanticallyPureWrapperCompatible(summary) &&
               summary.Calls.Any(IsStringToReadOnlyCharSpanWrapperCall) &&
               summary.Calls.Any(static call =>
                   string.Equals(call, "string.Substring(int, int)->string", StringComparison.Ordinal)) &&
               summary.Calls.Any(static call =>
                   call.StartsWith("System.IO.PathInternal.NormalizeDirectorySeparators(string)",
                       StringComparison.Ordinal)) &&
               summary.Calls.All(IsStringSliceNormalizationWrapperCall);
    }

    private static bool HasPureInvariantTextInfoStringWrapperPattern(MethodEffectSummary summary)
    {
        return CallsOnly(summary, "calls_method", "reads_static_field") &&
               RootsAreSemanticallyPureWrapperCompatible(summary) &&
               summary.Fields.Length == 1 &&
               string.Equals(summary.Fields[0], "System.Globalization.TextInfo.Invariant", StringComparison.Ordinal) &&
               summary.Calls.Any(IsInvariantTextInfoStringWrapperCall) &&
               summary.Calls.All(IsInvariantTextInfoStringWrapperCall);
    }

    private static bool HasPureTypeMetadataBooleanWrapperPattern(MethodEffectSummary summary)
    {
        if (summary.Fields.Length != 0 ||
            !CallsOnly(summary, "calls_method", "virtual_call") ||
            !summary.RootCandidates.All(static root =>
                string.Equals(root, "dynamic_dispatch", StringComparison.Ordinal)))
            return false;

        var callSites = EnumerateCallSites(summary).ToArray();
        if (IsPureTypeAttributeFlagsWrapperMethod(summary.Symbol))
            return CallSitesMatch(
                callSites,
                ("System.Type.GetAttributeFlagsImpl()->System.Reflection.TypeAttributes", true));

        if (TryGetPureTypeSingleImplWrapperCall(summary.Symbol, out var implCall))
            return CallSitesMatch(
                callSites,
                (implCall, true));

        return summary.Symbol switch
        {
            "System.Type.get_IsClass()" => CallSitesMatch(
                callSites,
                ("System.Type.GetAttributeFlagsImpl()->System.Reflection.TypeAttributes", true),
                ("System.Type.get_IsValueType()->bool", false)),
            "System.Type.get_IsNested()" => CallSitesMatch(
                callSites,
                ("System.Reflection.MemberInfo.get_DeclaringType()->System.Type", true),
                ("System.Type.op_Inequality(System.Type, System.Type)->bool", false)),
            "System.Type.get_IsInterface()" => CallSitesMatch(
                callSites,
                ("System.RuntimeTypeHandle.IsInterface(System.RuntimeType)->bool", false),
                ("System.Type.GetAttributeFlagsImpl()->System.Reflection.TypeAttributes", true)),
            _ => false
        };
    }

    private static bool HasPureTypeMetadataValueWrapperPattern(MethodEffectSummary summary)
    {
        if (summary.Fields.Length != 0 ||
            !CallsOnly(summary, "calls_method", "virtual_call") ||
            !summary.RootCandidates.All(static root =>
                string.Equals(root, "dynamic_dispatch", StringComparison.Ordinal)))
            return false;

        var callSites = EnumerateCallSites(summary).ToArray();
        return summary.Symbol switch
        {
            "System.Type.get_Attributes()" => CallSitesMatch(
                callSites,
                ("System.Type.GetAttributeFlagsImpl()->System.Reflection.TypeAttributes", true)),
            _ => false
        };
    }

    private static bool HasPureRuntimeTypeMetadataWrapperPattern(MethodEffectSummary summary)
    {
        var callSites = EnumerateCallSites(summary).ToArray();
        return summary.Symbol switch
        {
            "System.RuntimeType.get_ContainsGenericParameters()" =>
                summary.Fields.Length == 0 &&
                CallsOnly(summary, "calls_method", "virtual_call") &&
                summary.RootCandidates.All(static root =>
                    string.Equals(root, "dynamic_dispatch", StringComparison.Ordinal)) &&
                CallSitesMatch(
                    callSites,
                    ("System.RuntimeTypeHandle.ContainsGenericVariables()->bool", false),
                    ("System.Type.GetRootElementType()->System.Type", false),
                    ("System.Type.get_TypeHandle()->System.RuntimeTypeHandle", true)),
            "System.RuntimeType.get_IsEnum()" =>
                CallsOnly(summary, "calls_method", "reads_instance_field", "virtual_call") &&
                summary.RootCandidates.All(static root =>
                    string.Equals(root, "dynamic_dispatch", StringComparison.Ordinal)) &&
                summary.Fields.Length == 1 &&
                string.Equals(summary.Fields[0], "System.Runtime.CompilerServices.MethodTable.ParentMethodTable",
                    StringComparison.Ordinal) &&
                CallSitesMatch(
                    callSites,
                    ("System.GC.KeepAlive(object)->void", false),
                    ("System.Runtime.CompilerServices.TypeHandle.AsMethodTable()->System.Runtime.CompilerServices.MethodTable*",
                        false),
                    ("System.Runtime.CompilerServices.TypeHandle.TypeHandleOf()->System.Runtime.CompilerServices.TypeHandle",
                        false),
                    ("System.Runtime.CompilerServices.TypeHandle.get_IsTypeDesc()->bool", false),
                    ("System.RuntimeType.GetNativeTypeHandle()->System.Runtime.CompilerServices.TypeHandle", false),
                    ("System.Type.GetTypeFromHandle(System.RuntimeTypeHandle)->System.Type", false),
                    ("System.Type.IsSubclassOf(System.Type)->bool", true)),
            _ => false
        };
    }

    private static bool HasPureTypeIdentityWrapperPattern(MethodEffectSummary summary)
    {
        return summary.Fields.Length == 0 &&
               CallsOnly(summary, "calls_method", "virtual_call") &&
               summary.RootCandidates.All(static root =>
                   string.Equals(root, "dynamic_dispatch", StringComparison.Ordinal)) &&
               IsTypeIdentityWrapperMethod(summary.Symbol) &&
               summary.Calls.Any(IsTypeIdentityWrapperAnchorCall) &&
               summary.Calls.All(IsTypeIdentityWrapperCall);
    }

    private static bool HasPureStringHashWrapperPattern(MethodEffectSummary summary)
    {
        return summary.ExactSymbolKey.EndsWith(")->int", StringComparison.Ordinal) &&
               CallsOnly(summary, "calls_method", "reads_instance_field") &&
               summary.RootCandidates.Length == 0 &&
               summary.Calls.Any(static call =>
                   string.Equals(call, "System.Marvin.ComputeHash32(ref byte, uint, uint, uint)->int",
                       StringComparison.Ordinal)) &&
               summary.Calls.Any(static call =>
                   string.Equals(call, "System.Marvin.get_DefaultSeed()->ulong", StringComparison.Ordinal)) &&
               summary.Calls.All(IsStringHashWrapperCall) &&
               summary.Fields.All(static field =>
                   string.Equals(field, "System.String._firstChar", StringComparison.Ordinal) ||
                   string.Equals(field, "System.String._stringLength", StringComparison.Ordinal));
    }

    private static bool HasPureStackLocalCharBuilderStringWrapperPattern(MethodEffectSummary summary)
    {
        return CallsOnly(summary, "allocates_object", "calls_method") &&
               RootsAreSemanticallyPureWrapperCompatible(summary) &&
               summary.Calls.Any(static call =>
                   call.StartsWith("System.Text.ValueStringBuilder..ctor(System.Span`1<char>)",
                       StringComparison.Ordinal)) &&
               summary.Calls.Any(static call =>
                   call.StartsWith("System.Text.ValueStringBuilder.Append(char)", StringComparison.Ordinal)) &&
               summary.Calls.Any(static call =>
                   string.Equals(call, "object.ToString()->string", StringComparison.Ordinal)) &&
               summary.Calls.All(IsStackLocalCharBuilderStringWrapperCall);
    }

    private static bool HasPureImmutableStringRewriteWrapperPattern(MethodEffectSummary summary)
    {
        return CallsOnly(summary, "calls_method", "reads_static_field") &&
               RootsAreSemanticallyPureWrapperCompatible(summary) &&
               summary.Calls.Any(static call =>
                   call.StartsWith("string.Concat(System.ReadOnlySpan`1<char>", StringComparison.Ordinal)) &&
               summary.Calls.All(IsImmutableStringRewriteWrapperCall);
    }

    private static bool HasPureStringSubstringWrapperPattern(MethodEffectSummary summary)
    {
        return CallsOnly(summary, "calls_method", "reads_static_field") &&
               RootsAreSemanticallyPureWrapperCompatible(summary) &&
               summary.Calls.Any(static call =>
                   string.Equals(call, "string.InternalSubString(int, int)->string", StringComparison.Ordinal)) &&
               summary.Calls.Any(static call => string.Equals(call,
                   "string.ThrowSubstringArgumentOutOfRange(int, int)->void", StringComparison.Ordinal)) &&
               summary.Calls.All(IsStringSubstringWrapperCall);
    }

    private static bool HasPureCharReplaceStringWrapperPattern(MethodEffectSummary summary)
    {
        return string.Equals(summary.Symbol, "System.String.Replace(char, char)", StringComparison.Ordinal) &&
               CallsOnly(summary, "calls_method", "reads_instance_field") &&
               summary.RootCandidates.Length == 0 &&
               summary.Calls.Any(IsFastAllocateStringCall) &&
               summary.Calls.Any(IsBufferMemmoveCall) &&
               summary.Calls.Any(static call =>
                   string.Equals(call, "System.SpanHelpers.ReplaceValueType(ref !!0, ref !!0, !!0, !!0, nuint)->void",
                       StringComparison.Ordinal)) &&
               summary.Calls.All(IsCharReplaceStringWrapperCall) &&
               summary.Fields.All(static field =>
                   string.Equals(field, "System.String._firstChar", StringComparison.Ordinal));
    }

    private static bool HasPureFreshAllocatedStringCopyCorePattern(MethodEffectSummary summary)
    {
        return CallsOnly(summary, "calls_method", "reads_instance_field") &&
               RootsAreSemanticallyPureWrapperCompatible(summary) &&
               summary.Calls.Any(IsFastAllocateStringCall) &&
               summary.Calls.Any(IsBufferMemmoveCall) &&
               summary.Calls.All(IsFreshAllocatedStringCopyCoreCall) &&
               summary.Fields.All(static field =>
                   string.Equals(field, "System.String._firstChar", StringComparison.Ordinal));
    }

    private static bool HasPureStringLengthCheckedConcatWrapperPattern(MethodEffectSummary summary)
    {
        return CallsOnly(summary, "calls_method", "reads_static_field") &&
               RootsAreSemanticallyPureWrapperCompatible(summary) &&
               summary.Calls.Any(IsFastAllocateStringCall) &&
               summary.Calls.Any(static call => string.Equals(call,
                   "string.CopyStringContent(string, int, string)->void", StringComparison.Ordinal)) &&
               summary.Calls.All(IsStringLengthCheckedConcatWrapperCall);
    }

    private static bool HasPureStringArrayConcatWrapperPattern(MethodEffectSummary summary)
    {
        return CallsOnly(summary, "calls_method", "reads_static_field") &&
               RootsAreSemanticallyPureWrapperCompatible(summary) &&
               summary.Calls.Any(IsFastAllocateStringCall) &&
               summary.Calls.Any(static call =>
                   string.Equals(call, "System.Array.Clone()->object", StringComparison.Ordinal)) &&
               summary.Calls.All(IsStringArrayConcatWrapperCall);
    }

    private static bool HasPureCharScalarProjectionWrapperPattern(MethodEffectSummary summary)
    {
        if (!IsCharScalarProjectionSymbol(summary.ExactSymbolKey) ||
            summary.Fields.Length != 0 ||
            !CallsOnly(summary, "calls_method") ||
            !RootsAreSemanticallyPureWrapperCompatible(summary))
            return false;

        var callSites = EnumerateCallSites(summary).ToArray();
        return callSites.Length != 0 &&
               callSites.All(static callSite =>
                   !callSite.UsesDynamicDispatch &&
                   IsCharScalarProjectionCall(callSite.ExactSymbolKey));
    }

    private static bool HasPureGuardedStringCharScanWrapperPattern(MethodEffectSummary summary)
    {
        if (!summary.ExactSymbolKey.EndsWith(")->bool", StringComparison.Ordinal) ||
            summary.Fields.Length != 0 ||
            !CallsOnly(summary, "calls_method") ||
            !RootsAreSemanticallyPureWrapperCompatible(summary))
            return false;

        var callSites = EnumerateCallSites(summary).ToArray();
        return callSites.Any(static callSite => IsStringLengthCall(callSite.ExactSymbolKey)) &&
               callSites.Any(static callSite => IsStringGetCharsCall(callSite.ExactSymbolKey)) &&
               callSites.Any(static callSite => IsCharScalarProjectionCall(callSite.ExactSymbolKey)) &&
               callSites.All(static callSite =>
                   !callSite.UsesDynamicDispatch &&
                   (IsStringLengthCall(callSite.ExactSymbolKey) ||
                    IsStringGetCharsCall(callSite.ExactSymbolKey) ||
                    IsCharScalarProjectionCall(callSite.ExactSymbolKey)));
    }

    private static bool HasPureGuardedImmutableStringRewriteWrapperPattern(MethodEffectSummary summary)
    {
        return summary.ExactSymbolKey.EndsWith(")->string", StringComparison.Ordinal) &&
               CallsOnly(summary, "allocates_object", "calls_method", "reads_instance_field", "reads_static_field") &&
               RootsAreSemanticallyPureWrapperCompatible(summary) &&
               summary.Calls.Any(IsFastAllocateStringCall) &&
               summary.Calls.Any(static call =>
                   IsBufferMemmoveCall(call) ||
                   call.StartsWith("System.Span`1<char>.Fill(", StringComparison.Ordinal)) &&
               summary.Calls.All(IsGuardedImmutableStringRewriteWrapperCall) &&
               summary.Fields.All(static field =>
                   string.Equals(field, "System.String._firstChar", StringComparison.Ordinal) ||
                   string.Equals(field, "System.String.Empty", StringComparison.Ordinal));
    }

    private static bool HasPureIndexedStringReplaceWrapperPattern(MethodEffectSummary summary)
    {
        return CallsOnly(summary, "allocates_object", "calls_method", "reads_instance_field", "reads_static_field") &&
               RootsAreSemanticallyPureWrapperCompatible(summary) &&
               summary.Calls.Any(static call =>
                   call.StartsWith("string.ReplaceHelper(int, string, System.ReadOnlySpan`1<int>)",
                       StringComparison.Ordinal)) &&
               summary.Calls.Any(IsLocalScratchIndexBuilderCall) &&
               summary.Calls.All(IsIndexedStringReplaceWrapperCall) &&
               summary.Fields.All(static field =>
                   string.Equals(field, "System.String.Empty", StringComparison.Ordinal) ||
                   string.Equals(field, "System.String._firstChar", StringComparison.Ordinal));
    }

    private static bool RootsAreSemanticallyPureWrapperCompatible(MethodEffectSummary summary)
    {
        return summary.RootCandidates.All(static root =>
            string.Equals(root, "safe_static_cache_read", StringComparison.Ordinal) ||
            string.Equals(root, "safe_static_constant_read", StringComparison.Ordinal));
    }

    private static bool IsCharScalarProjectionCall(string exactSymbolKey)
    {
        return IsCharScalarProjectionSymbol(exactSymbolKey) ||
               IsCharScalarTableProjectionCall(exactSymbolKey) ||
               IsScalarValueHelperCall(exactSymbolKey, "System.Globalization.CharUnicodeInfo") ||
               IsScalarValueHelperCall(exactSymbolKey, "System.Globalization.TextInfo");
    }

    private static bool IsCharScalarTableProjectionCall(string exactSymbolKey)
    {
        return string.Equals(
                   exactSymbolKey,
                   "System.ReadOnlySpan`1<byte>.get_Item(int)->ref !0",
                   StringComparison.Ordinal) ||
               ((exactSymbolKey.StartsWith("char.get_", StringComparison.Ordinal) ||
                 exactSymbolKey.StartsWith("System.Char.get_", StringComparison.Ordinal)) &&
                exactSymbolKey.EndsWith(")->System.ReadOnlySpan`1<byte>", StringComparison.Ordinal));
    }

    private static bool IsStringLengthCall(string exactSymbolKey)
    {
        return string.Equals(exactSymbolKey, "string.get_Length()->int", StringComparison.Ordinal) ||
               string.Equals(exactSymbolKey, "System.String.get_Length()->int", StringComparison.Ordinal);
    }

    private static bool IsStringGetCharsCall(string exactSymbolKey)
    {
        return string.Equals(exactSymbolKey, "string.get_Chars(int)->char", StringComparison.Ordinal) ||
               string.Equals(exactSymbolKey, "System.String.get_Chars(int)->char", StringComparison.Ordinal);
    }

    private static bool IsCharScalarProjectionSymbol(string exactSymbolKey)
    {
        return (IsScalarValueHelperCall(exactSymbolKey, "char") ||
                IsScalarValueHelperCall(exactSymbolKey, "System.Char")) &&
               HasOnlyCharScalarArguments(exactSymbolKey);
    }

    private static bool IsScalarValueHelperCall(string exactSymbolKey, string declaringType)
    {
        var openParenIndex = exactSymbolKey.IndexOf('(');
        if (openParenIndex <= declaringType.Length ||
            !exactSymbolKey.StartsWith(declaringType + ".", StringComparison.Ordinal))
            return false;

        var returnSeparatorIndex = exactSymbolKey.LastIndexOf(")->", StringComparison.Ordinal);
        return returnSeparatorIndex >= 0 &&
               IsScalarValueReturnType(exactSymbolKey.Substring(returnSeparatorIndex + 3));
    }

    private static bool IsScalarValueReturnType(string returnType)
    {
        return string.Equals(returnType, "bool", StringComparison.Ordinal) ||
               string.Equals(returnType, "byte", StringComparison.Ordinal) ||
               string.Equals(returnType, "char", StringComparison.Ordinal) ||
               string.Equals(returnType, "double", StringComparison.Ordinal) ||
               string.Equals(returnType, "int", StringComparison.Ordinal) ||
               string.Equals(returnType, "uint", StringComparison.Ordinal) ||
               string.Equals(returnType, "System.Globalization.UnicodeCategory", StringComparison.Ordinal);
    }

    private static bool HasOnlyCharScalarArguments(string exactSymbolKey)
    {
        var openParenIndex = exactSymbolKey.IndexOf('(');
        var returnSeparatorIndex = exactSymbolKey.LastIndexOf(")->", StringComparison.Ordinal);
        if (openParenIndex < 0 || returnSeparatorIndex < openParenIndex) return false;

        var argumentList = exactSymbolKey.Substring(openParenIndex + 1, returnSeparatorIndex - openParenIndex - 1);
        if (argumentList.Length == 0) return true;

        foreach (var argument in argumentList.Split(','))
        {
            var trimmedArgument = argument.Trim();
            if (!string.Equals(trimmedArgument, "char", StringComparison.Ordinal) &&
                !string.Equals(trimmedArgument, "int", StringComparison.Ordinal) &&
                !string.Equals(trimmedArgument, "uint", StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private static bool RootsAreArrayBackedByRefLikeViewWrapperCompatible(MethodEffectSummary summary)
    {
        return summary.RootCandidates.All(static root =>
            string.Equals(root, "caller_visible_memory_write", StringComparison.Ordinal) ||
            string.Equals(root, "safe_static_cache_read", StringComparison.Ordinal) ||
            string.Equals(root, "safe_static_constant_read", StringComparison.Ordinal));
    }

    private static bool CallsOnly(MethodEffectSummary summary, params string[] allowedEffects)
    {
        return summary.Effects.All(effect => allowedEffects.Contains(effect, StringComparer.Ordinal));
    }

    private static bool IsByRefLikeViewReturn(string exactSymbolKey)
    {
        return exactSymbolKey.EndsWith(")->System.Span`1<!0>", StringComparison.Ordinal) ||
               exactSymbolKey.EndsWith(")->System.ReadOnlySpan`1<!0>", StringComparison.Ordinal) ||
               exactSymbolKey.EndsWith(")->System.Span`1<!!0>", StringComparison.Ordinal) ||
               exactSymbolKey.EndsWith(")->System.ReadOnlySpan`1<!!0>", StringComparison.Ordinal) ||
               exactSymbolKey.EndsWith(")->System.Span`1<byte>", StringComparison.Ordinal) ||
               exactSymbolKey.EndsWith(")->System.ReadOnlySpan`1<byte>", StringComparison.Ordinal) ||
               exactSymbolKey.EndsWith(")->System.Span`1<char>", StringComparison.Ordinal) ||
               exactSymbolKey.EndsWith(")->System.ReadOnlySpan`1<char>", StringComparison.Ordinal);
    }

    private static bool IsArrayBackedByRefLikeViewConstructionCall(string callSymbol)
    {
        return (callSymbol.StartsWith("System.Span`1<", StringComparison.Ordinal) &&
                (callSymbol.Contains("..ctor(!0[])", StringComparison.Ordinal) ||
                 callSymbol.Contains("..ctor(ref !0, int)", StringComparison.Ordinal))) ||
               (callSymbol.StartsWith("System.ReadOnlySpan`1<", StringComparison.Ordinal) &&
                (callSymbol.Contains("..ctor(!0[])", StringComparison.Ordinal) ||
                 callSymbol.Contains("..ctor(ref !0, int)", StringComparison.Ordinal)));
    }

    private static bool IsArrayBackedByRefLikeViewWrapperCall(string callSymbol)
    {
        return IsArrayBackedByRefLikeViewConstructionCall(callSymbol) ||
               callSymbol.StartsWith("System.Runtime.CompilerServices.Unsafe.Add(ref ", StringComparison.Ordinal) ||
               callSymbol.StartsWith("System.Runtime.InteropServices.MemoryMarshal.GetArrayDataReference(",
                   StringComparison.Ordinal) ||
               callSymbol.StartsWith("System.ThrowHelper.ThrowArgumentOutOfRangeException()",
                   StringComparison.Ordinal) ||
               callSymbol.StartsWith("System.ThrowHelper.ThrowArrayTypeMismatchException()",
                   StringComparison.Ordinal) ||
               callSymbol.StartsWith("System.Type.GetTypeFromHandle(System.RuntimeTypeHandle)",
                   StringComparison.Ordinal) ||
               callSymbol.StartsWith("System.Type.get_IsValueType()", StringComparison.Ordinal) ||
               callSymbol.StartsWith("System.Type.op_Inequality(System.Type, System.Type)", StringComparison.Ordinal) ||
               callSymbol.StartsWith("object.GetType()", StringComparison.Ordinal) ||
               callSymbol.StartsWith("string.get_Length()", StringComparison.Ordinal);
    }

    private static bool IsSpanBackedByRefLikeViewWrapperCall(string callSymbol)
    {
        return IsByRefLikeViewConstructionCall(callSymbol) ||
               IsPurityNeutralIntrinsicHelperCall(callSymbol) ||
               callSymbol.StartsWith("System.Runtime.InteropServices.MemoryMarshal.GetReference(System.Span`1<",
                   StringComparison.Ordinal) ||
               callSymbol.StartsWith("System.Runtime.InteropServices.MemoryMarshal.GetReference(System.ReadOnlySpan`1<",
                   StringComparison.Ordinal) ||
               callSymbol.StartsWith("System.Type.GetTypeFromHandle(System.RuntimeTypeHandle)",
                   StringComparison.Ordinal) ||
               IsSemanticallyNeutralValidationThrowHelper(callSymbol);
    }

    private static bool HasOnlyByRefLikeViewProjectionFieldReads(MethodEffectSummary summary)
    {
        if (!summary.Effects.Contains("reads_instance_field", StringComparer.Ordinal)) return true;

        return summary.Fields.All(static field =>
            (field.StartsWith("System.ValueTuple", StringComparison.Ordinal) &&
             (field.EndsWith(".Item1", StringComparison.Ordinal) ||
              field.EndsWith(".Item2", StringComparison.Ordinal))) ||
            string.Equals(field, "System.ReadOnlySpan`1._length", StringComparison.Ordinal) ||
            string.Equals(field, "System.ReadOnlySpan`1._reference", StringComparison.Ordinal) ||
            string.Equals(field, "System.Span`1._length", StringComparison.Ordinal) ||
            string.Equals(field, "System.Span`1._reference", StringComparison.Ordinal));
    }

    private static bool IsReadOnlyCharSpanSearchHelperCall(string callSymbol)
    {
        return callSymbol.StartsWith("System.IO.Path.GetDirectoryNameOffset(System.ReadOnlySpan`1<char>)",
                   StringComparison.Ordinal) ||
               callSymbol.StartsWith("System.IO.Path.GetExtension(System.ReadOnlySpan`1<char>)",
                   StringComparison.Ordinal) ||
               callSymbol.StartsWith("System.IO.Path.GetFileName(System.ReadOnlySpan`1<char>)",
                   StringComparison.Ordinal) ||
               callSymbol.StartsWith("System.IO.Path.GetFileNameWithoutExtension(System.ReadOnlySpan`1<char>)",
                   StringComparison.Ordinal) ||
               callSymbol.StartsWith("System.IO.Path.GetPathRoot(System.ReadOnlySpan`1<char>)",
                   StringComparison.Ordinal) ||
               callSymbol.StartsWith("System.IO.PathInternal.GetRootLength(System.ReadOnlySpan`1<char>)",
                   StringComparison.Ordinal) ||
               callSymbol.StartsWith("System.IO.PathInternal.IsDirectorySeparator(char)", StringComparison.Ordinal) ||
               callSymbol.StartsWith("System.IO.PathInternal.IsEffectivelyEmpty(System.ReadOnlySpan`1<char>)",
                   StringComparison.Ordinal) ||
               callSymbol.StartsWith("System.MemoryExtensions.IndexOf(System.ReadOnlySpan`1<",
                   StringComparison.Ordinal) ||
               callSymbol.StartsWith("System.MemoryExtensions.IndexOfAny(System.ReadOnlySpan`1<",
                   StringComparison.Ordinal) ||
               callSymbol.StartsWith("System.MemoryExtensions.LastIndexOf(System.ReadOnlySpan`1<",
                   StringComparison.Ordinal) ||
               callSymbol.StartsWith("System.MemoryExtensions.LastIndexOfAny(System.ReadOnlySpan`1<",
                   StringComparison.Ordinal) ||
               callSymbol.StartsWith("System.ReadOnlySpan`1<char>.Slice(", StringComparison.Ordinal) ||
               callSymbol.StartsWith("System.ReadOnlySpan`1<char>.get_Empty()", StringComparison.Ordinal) ||
               callSymbol.StartsWith("System.ReadOnlySpan`1<char>.get_Item(int)", StringComparison.Ordinal) ||
               callSymbol.StartsWith("System.ReadOnlySpan`1<char>.get_Length()", StringComparison.Ordinal);
    }

    private static bool IsStringToReadOnlyCharSpanWrapperCall(string callSymbol)
    {
        return callSymbol.StartsWith("System.MemoryExtensions.AsSpan(string", StringComparison.Ordinal) ||
               callSymbol.StartsWith("string.op_Implicit(string)->System.ReadOnlySpan`1<char>",
                   StringComparison.Ordinal);
    }

    private static bool IsStringFromReadOnlyCharSpanWrapperCall(string callSymbol)
    {
        return IsStringToReadOnlyCharSpanWrapperCall(callSymbol) ||
               IsReadOnlyCharSpanSearchHelperCall(callSymbol) ||
               string.Equals(callSymbol, "object.ToString()->string", StringComparison.Ordinal) ||
               string.Equals(callSymbol, "string.get_Length()->int", StringComparison.Ordinal);
    }

    private static bool IsStringSliceNormalizationWrapperCall(string callSymbol)
    {
        return IsStringToReadOnlyCharSpanWrapperCall(callSymbol) ||
               IsReadOnlyCharSpanSearchHelperCall(callSymbol) ||
               callSymbol.StartsWith("System.IO.PathInternal.NormalizeDirectorySeparators(string)",
                   StringComparison.Ordinal) ||
               string.Equals(callSymbol, "string.Substring(int, int)->string", StringComparison.Ordinal);
    }

    private static bool IsStackLocalCharBuilderStringWrapperCall(string callSymbol)
    {
        return callSymbol.StartsWith("System.IO.PathInternal.IsDirectorySeparator(char)", StringComparison.Ordinal) ||
               callSymbol.StartsWith("System.Span`1<char>..ctor(", StringComparison.Ordinal) ||
               callSymbol.StartsWith("System.Text.ValueStringBuilder..ctor(System.Span`1<char>)",
                   StringComparison.Ordinal) ||
               callSymbol.StartsWith("System.Text.ValueStringBuilder.Append(char)", StringComparison.Ordinal) ||
               string.Equals(callSymbol, "object.ToString()->string", StringComparison.Ordinal) ||
               string.Equals(callSymbol, "string.IsNullOrEmpty(string)->bool", StringComparison.Ordinal) ||
               string.Equals(callSymbol, "string.get_Chars(int)->char", StringComparison.Ordinal) ||
               string.Equals(callSymbol, "string.get_Length()->int", StringComparison.Ordinal);
    }

    private static bool IsImmutableStringRewriteWrapperCall(string callSymbol)
    {
        return callSymbol.StartsWith("System.IO.PathInternal.IsDirectorySeparator(char)", StringComparison.Ordinal) ||
               callSymbol.StartsWith("System.MemoryExtensions.AsSpan(string", StringComparison.Ordinal) ||
               callSymbol.StartsWith("string.Concat(System.ReadOnlySpan`1<char>", StringComparison.Ordinal) ||
               callSymbol.StartsWith("string.StartsWith(char)->bool", StringComparison.Ordinal) ||
               callSymbol.StartsWith("string.Substring(int, int)->string", StringComparison.Ordinal) ||
               string.Equals(callSymbol, "string.get_Chars(int)->char", StringComparison.Ordinal) ||
               string.Equals(callSymbol, "string.get_Length()->int", StringComparison.Ordinal) ||
               string.Equals(callSymbol, "string.op_Implicit(string)->System.ReadOnlySpan`1<char>",
                   StringComparison.Ordinal);
    }

    private static bool IsInvariantTextInfoStringWrapperCall(string callSymbol)
    {
        return string.Equals(callSymbol, "System.Globalization.TextInfo.ToLower(string)->string",
                   StringComparison.Ordinal) ||
               string.Equals(callSymbol, "System.Globalization.TextInfo.ToUpper(string)->string",
                   StringComparison.Ordinal);
    }

    private static bool CallSitesMatch(
        IReadOnlyList<CallSiteSummary> actual,
        params (string ExactSymbolKey, bool UsesDynamicDispatch)[] expected)
    {
        if (actual.Count != expected.Length) return false;

        foreach (var expectedCallSite in expected)
            if (actual.Count(callSite =>
                    callSite.UsesDynamicDispatch == expectedCallSite.UsesDynamicDispatch &&
                    string.Equals(callSite.ExactSymbolKey, expectedCallSite.ExactSymbolKey,
                        StringComparison.Ordinal)) != 1)
                return false;

        return true;
    }

    private static bool IsPureTypeAttributeFlagsWrapperMethod(string symbol)
    {
        return string.Equals(symbol, "System.Type.get_IsAbstract()", StringComparison.Ordinal) ||
               string.Equals(symbol, "System.Type.get_IsAnsiClass()", StringComparison.Ordinal) ||
               string.Equals(symbol, "System.Type.get_IsAutoClass()", StringComparison.Ordinal) ||
               string.Equals(symbol, "System.Type.get_IsAutoLayout()", StringComparison.Ordinal) ||
               string.Equals(symbol, "System.Type.get_IsExplicitLayout()", StringComparison.Ordinal) ||
               string.Equals(symbol, "System.Type.get_IsImport()", StringComparison.Ordinal) ||
               string.Equals(symbol, "System.Type.get_IsLayoutSequential()", StringComparison.Ordinal) ||
               string.Equals(symbol, "System.Type.get_IsNestedAssembly()", StringComparison.Ordinal) ||
               string.Equals(symbol, "System.Type.get_IsNestedFamANDAssem()", StringComparison.Ordinal) ||
               string.Equals(symbol, "System.Type.get_IsNestedFamily()", StringComparison.Ordinal) ||
               string.Equals(symbol, "System.Type.get_IsNestedFamORAssem()", StringComparison.Ordinal) ||
               string.Equals(symbol, "System.Type.get_IsNestedPrivate()", StringComparison.Ordinal) ||
               string.Equals(symbol, "System.Type.get_IsNestedPublic()", StringComparison.Ordinal) ||
               string.Equals(symbol, "System.Type.get_IsNotPublic()", StringComparison.Ordinal) ||
               string.Equals(symbol, "System.Type.get_IsPublic()", StringComparison.Ordinal) ||
               string.Equals(symbol, "System.Type.get_IsSealed()", StringComparison.Ordinal) ||
               string.Equals(symbol, "System.Type.get_IsSpecialName()", StringComparison.Ordinal) ||
               string.Equals(symbol, "System.Type.get_IsUnicodeClass()", StringComparison.Ordinal);
    }

    private static bool TryGetPureTypeSingleImplWrapperCall(string symbol, out string implCall)
    {
        implCall = symbol switch
        {
            "System.Type.get_IsArray()" => "System.Type.IsArrayImpl()->bool",
            "System.Type.get_IsByRef()" => "System.Type.IsByRefImpl()->bool",
            "System.Type.get_IsCOMObject()" => "System.Type.IsCOMObjectImpl()->bool",
            "System.Type.get_IsPointer()" => "System.Type.IsPointerImpl()->bool",
            "System.Type.get_IsPrimitive()" => "System.Type.IsPrimitiveImpl()->bool",
            "System.Type.get_IsValueType()" => "System.Type.IsValueTypeImpl()->bool",
            _ => string.Empty
        };

        return implCall.Length != 0;
    }

    private static bool IsTypeIdentityWrapperMethod(string symbol)
    {
        return string.Equals(symbol, "System.Type.Equals(System.Type)", StringComparison.Ordinal) ||
               string.Equals(symbol, "System.Type.Equals(object)", StringComparison.Ordinal) ||
               string.Equals(symbol, "System.Type.GetHashCode()", StringComparison.Ordinal);
    }

    private static bool IsTypeIdentityWrapperAnchorCall(string callSymbol)
    {
        return string.Equals(callSymbol, "System.Type.Equals(System.Type)->bool", StringComparison.Ordinal) ||
               string.Equals(callSymbol, "System.Type.get_UnderlyingSystemType()->System.Type",
                   StringComparison.Ordinal) ||
               string.Equals(callSymbol, "System.Reflection.MemberInfo.GetHashCode()->int", StringComparison.Ordinal) ||
               string.Equals(callSymbol, "object.GetHashCode()->int", StringComparison.Ordinal);
    }

    private static bool IsTypeIdentityWrapperCall(string callSymbol)
    {
        return IsTypeIdentityWrapperAnchorCall(callSymbol) ||
               string.Equals(callSymbol, "System.Type.op_Equality(System.Type, System.Type)->bool",
                   StringComparison.Ordinal);
    }

    private static bool IsStringHashWrapperCall(string callSymbol)
    {
        return string.Equals(callSymbol, "System.Marvin.ComputeHash32(ref byte, uint, uint, uint)->int",
                   StringComparison.Ordinal) ||
               string.Equals(callSymbol, "System.Marvin.get_DefaultSeed()->ulong", StringComparison.Ordinal) ||
               string.Equals(callSymbol, "System.Runtime.CompilerServices.Unsafe.As(ref !!0)->ref !!1",
                   StringComparison.Ordinal);
    }

    private static bool IsStringSubstringWrapperCall(string callSymbol)
    {
        return string.Equals(callSymbol, "string.InternalSubString(int, int)->string", StringComparison.Ordinal) ||
               string.Equals(callSymbol, "string.ThrowSubstringArgumentOutOfRange(int, int)->void",
                   StringComparison.Ordinal) ||
               string.Equals(callSymbol, "string.get_Length()->int", StringComparison.Ordinal);
    }

    private static bool IsFreshAllocatedStringCopyCoreCall(string callSymbol)
    {
        return IsBufferMemmoveCall(callSymbol) ||
               IsFastAllocateStringCall(callSymbol) ||
               string.Equals(callSymbol, "System.Runtime.CompilerServices.Unsafe.Add(ref !!0, nint)->ref !!0",
                   StringComparison.Ordinal);
    }

    private static bool IsCharReplaceStringWrapperCall(string callSymbol)
    {
        return IsBufferMemmoveCall(callSymbol) ||
               IsFastAllocateStringCall(callSymbol) ||
               string.Equals(callSymbol, "System.Runtime.CompilerServices.Unsafe.Add(ref !!0, nuint)->ref !!0",
                   StringComparison.Ordinal) ||
               string.Equals(callSymbol, "System.Runtime.CompilerServices.Unsafe.Subtract(ref !!0, nuint)->ref !!0",
                   StringComparison.Ordinal) ||
               string.Equals(callSymbol, "System.Runtime.Intrinsics.Vector128.get_IsHardwareAccelerated()->bool",
                   StringComparison.Ordinal) ||
               string.Equals(callSymbol, "System.Runtime.Intrinsics.Vector128`1<ushort>.get_Count()->int",
                   StringComparison.Ordinal) ||
               string.Equals(callSymbol, "System.SpanHelpers.ReplaceValueType(ref !!0, ref !!0, !!0, !!0, nuint)->void",
                   StringComparison.Ordinal) ||
               string.Equals(callSymbol, "string.GetRawStringDataAsUInt16()->ref ushort", StringComparison.Ordinal) ||
               string.Equals(callSymbol, "string.IndexOf(char)->int", StringComparison.Ordinal) ||
               string.Equals(callSymbol, "string.get_Length()->int", StringComparison.Ordinal);
    }

    private static bool IsStringLengthCheckedConcatWrapperCall(string callSymbol)
    {
        return IsFastAllocateStringCall(callSymbol) ||
               string.Equals(callSymbol, "System.ThrowHelper.ThrowOutOfMemoryException_StringTooLong()->void",
                   StringComparison.Ordinal) ||
               string.Equals(callSymbol, "string.CopyStringContent(string, int, string)->void",
                   StringComparison.Ordinal) ||
               string.Equals(callSymbol, "string.IsNullOrEmpty(string)->bool", StringComparison.Ordinal) ||
               string.Equals(callSymbol, "string.get_Length()->int", StringComparison.Ordinal);
    }

    private static bool IsStringArrayConcatWrapperCall(string callSymbol)
    {
        return IsStringLengthCheckedConcatWrapperCall(callSymbol) ||
               string.Equals(callSymbol, "System.ArgumentNullException.ThrowIfNull(object, string)->void",
                   StringComparison.Ordinal) ||
               string.Equals(callSymbol, "System.Array.Clone()->object", StringComparison.Ordinal) ||
               string.Equals(callSymbol, "string.Concat(string[])->string", StringComparison.Ordinal);
    }

    private static bool IsGuardedImmutableStringRewriteWrapperCall(string callSymbol)
    {
        return IsFastAllocateStringCall(callSymbol) ||
               IsBufferMemmoveCall(callSymbol) ||
               IsPureArgumentGuardWrapper(callSymbol) ||
               string.Equals(callSymbol, "System.Runtime.CompilerServices.Unsafe.Add(ref !!0, int)->ref !!0",
                   StringComparison.Ordinal) ||
               string.Equals(callSymbol, "System.Span`1<char>..ctor(ref !0, int)->void", StringComparison.Ordinal) ||
               callSymbol.StartsWith("System.Span`1<char>.Fill(", StringComparison.Ordinal) ||
               string.Equals(callSymbol, "string.CopyStringContent(string, int, string)->void",
                   StringComparison.Ordinal) ||
               string.Equals(callSymbol, "string.get_Chars(int)->char", StringComparison.Ordinal) ||
               string.Equals(callSymbol, "string.get_Length()->int", StringComparison.Ordinal);
    }

    private static bool IsLocalScratchIndexBuilderCall(string callSymbol)
    {
        return callSymbol.StartsWith("System.Collections.Generic.ValueListBuilder`1<", StringComparison.Ordinal) &&
               (callSymbol.Contains("..ctor(System.Span`1<!0>)", StringComparison.Ordinal) ||
                callSymbol.Contains(".Append(!0)", StringComparison.Ordinal) ||
                callSymbol.Contains(".AsSpan()", StringComparison.Ordinal) ||
                callSymbol.Contains(".Dispose()", StringComparison.Ordinal) ||
                callSymbol.Contains(".get_Length()", StringComparison.Ordinal));
    }

    private static bool IsIndexedStringReplaceWrapperCall(string callSymbol)
    {
        return IsLocalScratchIndexBuilderCall(callSymbol) ||
               IsPureArgumentGuardWrapper(callSymbol) ||
               callSymbol.StartsWith("System.PackedSpanHelpers.CanUsePackedIndexOf(", StringComparison.Ordinal) ||
               callSymbol.StartsWith("System.PackedSpanHelpers.IndexOf(", StringComparison.Ordinal) ||
               callSymbol.StartsWith("System.PackedSpanHelpers.get_PackedIndexOfIsSupported()",
                   StringComparison.Ordinal) ||
               callSymbol.StartsWith("System.SpanHelpers.IndexOf(", StringComparison.Ordinal) ||
               callSymbol.StartsWith("System.SpanHelpers.NonPackedIndexOfChar(", StringComparison.Ordinal) ||
               string.Equals(callSymbol, "System.Runtime.CompilerServices.Unsafe.Add(ref !!0, int)->ref !!0",
                   StringComparison.Ordinal) ||
               string.Equals(callSymbol, "System.Span`1<int>..ctor(void*, int)->void", StringComparison.Ordinal) ||
               string.Equals(callSymbol, "string.Replace(char, char)->string", StringComparison.Ordinal) ||
               string.Equals(callSymbol, "string.ReplaceHelper(int, string, System.ReadOnlySpan`1<int>)->string",
                   StringComparison.Ordinal) ||
               string.Equals(callSymbol, "string.get_Chars(int)->char", StringComparison.Ordinal) ||
               string.Equals(callSymbol, "string.get_Length()->int", StringComparison.Ordinal);
    }

    private static bool IsFastAllocateStringCall(string callSymbol)
    {
        return string.Equals(callSymbol, "string.FastAllocateString(int)->string", StringComparison.Ordinal) ||
               string.Equals(callSymbol, "System.String.FastAllocateString(int)->string", StringComparison.Ordinal);
    }

    private static bool IsBufferMemmoveCall(string callSymbol)
    {
        return string.Equals(callSymbol, "System.Buffer.Memmove(ref !!0, ref !!0, nuint)->void",
                   StringComparison.Ordinal) ||
               string.Equals(callSymbol, "System.Buffer.Memmove(ref byte, ref byte, nuint)->void",
                   StringComparison.Ordinal);
    }

    private static bool HasOnlyDeterministicStringComparisonDispatch(MethodEffectSummary summary)
    {
        if (!summary.Effects.Contains("virtual_call", StringComparer.Ordinal)) return false;

        var dynamicDispatchCallSites = EnumerateCallSites(summary)
            .Where(static callSite => callSite.UsesDynamicDispatch)
            .ToArray();
        if (dynamicDispatchCallSites.Length == 0) return false;

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
                return true;

            if (string.Equals(argumentEvidence.Type, "System.StringComparer", StringComparison.Ordinal) &&
                IsDeterministicStringComparerValue(argumentEvidence.Value))
                return true;
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
        if (lastDotIndex <= 0 || lastDotIndex == methodBaseSymbol.Length - 1) return false;

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
        if (nested.Count == 0) return new[] { callee };

        var chain = new string[nested.Count + 1];
        chain[0] = callee;
        for (var i = 0; i < nested.Count; i++) chain[i + 1] = nested[i];

        return chain;
    }

    private static MethodPurityClassification CreateUnknown(
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
            3,
            methods.Count,
            pureCount,
            impureCount,
            unknownCount,
            includeCatalogComparison
                ? BuildCatalogComparison(methods)
                : null);
    }

    private static CatalogComparisonReport BuildCatalogComparison(
        IReadOnlyList<MethodEffectSummary> methods)
    {
        var bySymbol = methods
            .GroupBy(method => NormalizeCatalogComparisonKey(method.Symbol), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        var knownPureSymbols = Constants.KnownPureBCLMembers;

        return new CatalogComparisonReport(
            BuildRows(knownPureSymbols, bySymbol, "known_pure"),
            BuildRows(Constants.KnownImpureMethods, bySymbol, "known_impure"),
            BuildRows(Constants.KnownFreshOwnedArrayReturningMembers, bySymbol, "known_fresh_owned_array"));
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
                    symbol,
                    catalogName,
                    classification?.Classification ?? "unclassified",
                    classification?.Categories ?? Array.Empty<string>(),
                    classification?.FirstBlockingCallChain ?? Array.Empty<string>(),
                    classification?.EffectVisibilityClassification ?? "unknown",
                    note,
                    matchedMethods
                        .Select(static method => method.ExactSymbolKey)
                        .OrderBy(static key => key, StringComparer.Ordinal)
                        .ToArray());
            })
            .ToArray();
    }

    private static GeneratedPurityCatalogDocument BuildGeneratedPurityCatalog(
        IReadOnlyList<AssemblyEffectReport> assemblies)
    {
        return new GeneratedPurityCatalogDocument(
            2,
            assemblies
                .SelectMany(assembly => assembly.Methods.Select(method => CreateGeneratedPurityEntry(assembly, method)))
                .OrderBy(static entry => entry.ExactSymbolKey, StringComparer.Ordinal)
                .ToArray());
    }

    private static Dictionary<string, GeneratedPurityCatalogEntry> MergeGeneratedPurityEntries(
        IEnumerable<GeneratedPurityCatalogEntry> entries)
    {
        var candidatesByKey = new Dictionary<string, List<GeneratedPurityCatalogEntry>>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (!candidatesByKey.TryGetValue(entry.ExactSymbolKey, out var candidates))
            {
                candidates = new List<GeneratedPurityCatalogEntry>();
                candidatesByKey.Add(entry.ExactSymbolKey, candidates);
            }

            candidates.Add(entry);
        }

        var resolvedEntries = new Dictionary<string, GeneratedPurityCatalogEntry>(StringComparer.Ordinal);
        foreach (var pair in candidatesByKey)
        {
            var resolvedEntry = ResolveGeneratedPurityEntryCandidates(pair.Value);
            if (resolvedEntry != null) resolvedEntries[pair.Key] = resolvedEntry;
        }

        return resolvedEntries;
    }

    private static GeneratedPurityCatalogEntry? ResolveGeneratedPurityEntryCandidates(
        IReadOnlyList<GeneratedPurityCatalogEntry> candidates)
    {
        GeneratedPurityCatalogEntry? bestEntry = null;
        foreach (var implementationGroup in candidates
                     .GroupBy(CreateGeneratedPurityImplementationKey, StringComparer.Ordinal))
        {
            var resolvedEntry = ResolveSameImplementationGeneratedPurityEntries(
                implementationGroup.ToArray());
            if (resolvedEntry == null) continue;

            if (bestEntry == null)
            {
                bestEntry = resolvedEntry;
                continue;
            }

            if (GeneratedPurityCatalogEntryRelations.AreEquivalent(bestEntry, resolvedEntry)) continue;

            var bestDominatesResolved = GeneratedPurityCatalogEntryRelations.DoesDominate(bestEntry, resolvedEntry);
            var resolvedDominatesBest = GeneratedPurityCatalogEntryRelations.DoesDominate(resolvedEntry, bestEntry);
            if (bestDominatesResolved == resolvedDominatesBest) return null;

            if (resolvedDominatesBest) bestEntry = resolvedEntry;
        }

        return bestEntry;
    }

    private static GeneratedPurityCatalogEntry? ResolveSameImplementationGeneratedPurityEntries(
        IReadOnlyList<GeneratedPurityCatalogEntry> candidates)
    {
        if (candidates.Count == 0) return null;

        var bestEntry = candidates[0];
        for (var i = 1; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            if (GeneratedPurityCatalogEntryRelations.AreEquivalent(bestEntry, candidate)) continue;

            var bestDominatesCandidate = GeneratedPurityCatalogEntryRelations.DoesDominate(bestEntry, candidate);
            var candidateDominatesBest = GeneratedPurityCatalogEntryRelations.DoesDominate(candidate, bestEntry);
            if (bestDominatesCandidate == candidateDominatesBest) return null;

            if (candidateDominatesBest) bestEntry = candidate;
        }

        return bestEntry;
    }

    private static bool HaveSameGeneratedPurityEntryMap(
        IReadOnlyDictionary<string, GeneratedPurityCatalogEntry> left,
        IReadOnlyDictionary<string, GeneratedPurityCatalogEntry> right)
    {
        if (left.Count != right.Count) return false;

        foreach (var pair in left)
            if (!right.TryGetValue(pair.Key, out var rightEntry) ||
                !GeneratedPurityCatalogEntryRelations.AreEquivalent(pair.Value, rightEntry))
                return false;

        return true;
    }

    private static string CreateGeneratedPurityImplementationKey(GeneratedPurityCatalogEntry entry)
    {
        return string.Join(
            "|",
            entry.AssemblyName,
            entry.AssemblySha256,
            entry.ModuleVersionId,
            entry.MetadataToken,
            entry.MethodBodySha256 ?? string.Empty);
    }

    private static GeneratedPurityCatalogEntry CreateGeneratedPurityEntry(
        AssemblyEffectReport assembly,
        MethodEffectSummary method)
    {
        var classification = method.PurityClassification ?? CreateUnknown(
            new[] { "missing_classification" },
            Array.Empty<string>(),
            method);

        return new GeneratedPurityCatalogEntry(
            method.Symbol,
            method.ExactSymbolKey,
            method.CacheKey,
            assembly.AssemblyName,
            assembly.AssemblyPath,
            assembly.ArtifactSource,
            assembly.AssemblySha256,
            assembly.ModuleVersionId,
            method.MetadataToken,
            method.MethodBodySha256,
            classification.Classification,
            GetPrimaryCategory(classification.Categories),
            classification.Categories,
            classification.FirstBlockingCallChain,
            classification.HasFreshArrayAllocationEvidence,
            classification.HasFreshObjectAllocationEvidence,
            classification.HasUnsupportedEffects,
            classification.FreshnessClassification,
            classification.EffectVisibilityClassification);
    }

    private static string GetPrimaryCategory(IReadOnlyList<string> categories)
    {
        if (categories.Contains("global_state_write", StringComparer.Ordinal)) return "global_state_write";

        return categories.FirstOrDefault() ?? "generated_purity_summary";
    }

    private static MethodPurityClassification? AggregateCatalogClassification(
        IReadOnlyList<MethodPurityClassification> classifications)
    {
        if (classifications.Count == 0) return null;

        if (classifications.Count == 1) return classifications[0];

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
            classification,
            categories,
            blockingCallChain,
            classifications.Any(static item => item.HasFreshArrayAllocationEvidence),
            classifications.Any(static item => item.HasFreshObjectAllocationEvidence),
            classifications.Any(static item => item.HasUnsupportedEffects),
            AggregateFreshnessClassification(classifications),
            AggregateEffectVisibilityClassification(classifications));
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
            normalized = normalized.Replace(pair.Key, pair.Value, StringComparison.Ordinal);

        return normalized;
    }

    private static string NormalizeCatalogComparisonKey(string symbol)
    {
        if (TryNormalizeAccessorComparisonKey(symbol, out var comparisonKey)) return comparisonKey;

        return NormalizeCatalogSymbol(symbol);
    }

    private static bool TryNormalizeAccessorComparisonKey(string symbol, out string comparisonKey)
    {
        if (TryNormalizeCatalogAccessorComparisonKey(symbol, out comparisonKey)) return true;

        if (TryNormalizeRuntimeAccessorComparisonKey(symbol, out comparisonKey)) return true;

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
        if (suffix == null) return symbol;

        var memberSeparator = FindLastTopLevelDot(symbol, symbol.Length - suffix.Length);
        if (memberSeparator < 0) return symbol;

        var containingType = symbol.Substring(0, memberSeparator);
        var propertyName = symbol.Substring(
            memberSeparator + 1,
            symbol.Length - memberSeparator - suffix.Length - 1);
        if (string.IsNullOrWhiteSpace(propertyName)) return symbol;

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
        if (openParen < 0 || !symbol.EndsWith(")", StringComparison.Ordinal)) return symbol;

        var memberSeparator = FindLastTopLevelDot(symbol, openParen);
        if (memberSeparator < 0) return symbol;

        var containingType = symbol.Substring(0, memberSeparator);
        var memberName = symbol.Substring(memberSeparator + 1, openParen - memberSeparator - 1);
        var parameterList = symbol.Substring(openParen + 1, symbol.Length - openParen - 2);
        var normalizedContainingType = NormalizeContainingTypeDefinition(containingType, out var typeParameterOrdinals);
        var normalizedMethodName = NormalizeMethodDefinition(memberName, out var methodParameterOrdinals);
        var simpleContainingTypeName = GetSimpleTypeName(containingType);
        var normalizedMemberName =
            string.Equals(normalizedMethodName, simpleContainingTypeName, StringComparison.Ordinal)
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
        if (!TryParseGenericType(memberName, out var baseName, out var genericArguments)) return memberName;

        var ordinals = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < genericArguments.Length; i++)
        {
            var genericArgument = genericArguments[i];
            if (IsSimpleIdentifier(genericArgument)) ordinals[genericArgument] = "!!" + i;
        }

        if (ordinals.Count > 0) methodParameterOrdinals = ordinals;

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
        if (!TryParseGenericType(simpleTypeName, out var baseName, out var genericArguments)) return containingType;

        var ordinals = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < genericArguments.Length; i++)
        {
            var genericArgument = genericArguments[i];
            if (IsSimpleIdentifier(genericArgument)) ordinals[genericArgument] = "!" + i;
        }

        if (ordinals.Count > 0) typeParameterOrdinals = ordinals;

        return prefix + NormalizeGenericTypeBaseName(baseName, genericArguments.Length);
    }

    private static string NormalizeGenericTypeBaseName(string baseName, int arity)
    {
        var existingAritySeparator = baseName.LastIndexOf('`');
        if (existingAritySeparator >= 0 &&
            existingAritySeparator + 1 < baseName.Length &&
            int.TryParse(baseName.Substring(existingAritySeparator + 1), out var existingArity))
            return existingArity == arity
                ? baseName
                : baseName.Substring(0, existingAritySeparator) + "`" + arity;

        return baseName + "`" + arity;
    }

    private static bool TryParseGenericType(string typeName, out string baseName, out string[] genericArguments)
    {
        baseName = typeName;
        genericArguments = Array.Empty<string>();
        var genericStart = typeName.IndexOf('<');
        if (genericStart < 0 || !typeName.EndsWith(">", StringComparison.Ordinal)) return false;

        baseName = typeName.Substring(0, genericStart).Trim();
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = typeName;
            return false;
        }

        genericArguments =
            SplitTopLevelArguments(typeName.Substring(genericStart + 1, typeName.Length - genericStart - 2));
        return genericArguments.Length > 0;
    }

    private static string[] SplitTopLevelArguments(string text)
    {
        var arguments = new List<string>();
        var start = 0;
        var depth = 0;
        for (var i = 0; i < text.Length; i++)
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
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var parameters = SplitTopLevelArguments(text);
        if (parameters.Length == 0) return string.Empty;

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
        if (string.IsNullOrEmpty(trimmed)) return trimmed;

        foreach (var modifier in new[]
                 {
                     "scoped ref ",
                     "ref readonly ",
                     "scoped in ",
                     "params ",
                     "scoped ",
                     "ref ",
                     "out ",
                     "in "
                 })
            if (trimmed.StartsWith(modifier, StringComparison.Ordinal))
                return modifier + NormalizeTypeExpression(
                    trimmed.Substring(modifier.Length),
                    typeParameterOrdinals,
                    methodParameterOrdinals);

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
            return text;

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
            while (i < text.Length && IsIdentifierPart(text[i])) i++;

            var token = text.Substring(start, i - start);
            var hasTypeReplacement = typeParameterOrdinals.TryGetValue(token, out var typeReplacement);
            var hasMethodReplacement = methodParameterOrdinals.TryGetValue(token, out var methodReplacement);
            if (hasTypeReplacement && hasMethodReplacement &&
                !string.Equals(typeReplacement, methodReplacement, StringComparison.Ordinal))
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
        if (string.IsNullOrWhiteSpace(value) || !IsIdentifierStart(value[0])) return false;

        for (var i = 1; i < value.Length; i++)
            if (!IsIdentifierPart(value[i]))
                return false;

        return true;
    }

    private static bool IsIdentifierStart(char value)
    {
        return char.IsLetter(value) || value == '_';
    }

    private static bool IsIdentifierPart(char value)
    {
        return char.IsLetterOrDigit(value) || value == '_';
    }

    private static string? GetFreshArrayNote(MethodPurityClassification? classification)
    {
        if (classification == null) return "unclassified";

        if (!string.IsNullOrWhiteSpace(classification.FreshnessClassification) &&
            !string.Equals(classification.FreshnessClassification, "none", StringComparison.Ordinal))
            return classification.FreshnessClassification;

        if (!classification.HasFreshArrayAllocationEvidence) return "no_fresh_array_allocation_evidence";

        return classification.Classification == "pure"
            ? "fresh_array_allocation_evidence_present"
            : "fresh_array_allocation_evidence_present_but_not_proven_pure";
    }

    private static string GetFreshnessClassification(MethodEffectSummary? summary, string classification)
    {
        if (summary == null) return "none";

        if (summary.RootCandidates.Contains("fresh_owned_object_write", StringComparer.Ordinal))
            return string.Equals(classification, "pure", StringComparison.Ordinal)
                ? "fresh_owned_object_write"
                : "fresh_object_candidate_requires_non_pure_resolution";

        if (!HasFreshArrayAllocationEvidence(summary)) return "none";

        if (!string.Equals(classification, "pure", StringComparison.Ordinal))
            return "fresh_array_candidate_requires_non_pure_resolution";

        if (summary.RootCandidates.Contains("fresh_owned_memory_write", StringComparer.Ordinal))
            return "fresh_owned_array_write";

        if (HasFreshOwnedArrayWritePattern(summary)) return "fresh_owned_array_write";

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

        if (!hasDispatchOrOpaqueCall && !hasDirectMethodCall && !hasWrites) return "direct_fresh_array_allocation";

        if (!hasDispatchOrOpaqueCall && !hasWrites) return "fresh_array_candidate_via_local_helpers";

        return "fresh_array_candidate_with_unknown_escape_risk";
    }

    private static bool HasFreshOwnedArrayWritePattern(MethodEffectSummary? summary)
    {
        if (summary == null) return false;

        if (HasByRefParameter(summary.ExactSymbolKey)) return false;

        if (summary.Effects.Contains("allocates_array", StringComparer.Ordinal) &&
            summary.Effects.Contains("writes_indirect_memory", StringComparer.Ordinal))
        {
            if (summary.Effects.Contains("writes_static_field", StringComparer.Ordinal) ||
                summary.Effects.Contains("writes_instance_field", StringComparer.Ordinal) ||
                summary.Effects.Contains("reads_instance_field", StringComparer.Ordinal) ||
                summary.Effects.Contains("indirect_call", StringComparer.Ordinal) ||
                summary.Effects.Contains("virtual_call", StringComparer.Ordinal) ||
                summary.Effects.Contains("block_memory_write", StringComparer.Ordinal))
                return false;

            if (!HasOnlySafeStaticReads(summary)) return false;

            return true;
        }

        return HasAllocateUninitializedArrayWrapperPattern(summary);
    }

    private static bool HasFreshOwnedStringWritePattern(MethodEffectSummary? summary)
    {
        if (summary == null ||
            !summary.Effects.Contains("allocates_object", StringComparer.Ordinal) ||
            !summary.Effects.Contains("writes_indirect_memory", StringComparer.Ordinal))
            return false;

        if (!summary.Calls.Any(static call =>
                string.Equals(call, "string.FastAllocateString(int)->string", StringComparison.Ordinal)))
            return false;

        if (summary.Effects.Contains("allocates_array", StringComparer.Ordinal) ||
            summary.Effects.Contains("writes_static_field", StringComparer.Ordinal) ||
            summary.Effects.Contains("writes_instance_field", StringComparer.Ordinal) ||
            summary.Effects.Contains("indirect_call", StringComparer.Ordinal) ||
            summary.Effects.Contains("virtual_call", StringComparer.Ordinal) ||
            summary.Effects.Contains("block_memory_write", StringComparer.Ordinal))
            return false;

        if (!HasOnlySafeStaticReads(summary)) return false;

        return true;
    }

    private static bool HasLocalScratchMemoryWritePattern(MethodEffectSummary? summary)
    {
        if (summary == null ||
            !summary.Effects.Contains("writes_indirect_memory", StringComparer.Ordinal))
            return false;

        if (summary.Effects.Contains("allocates_array", StringComparer.Ordinal) ||
            summary.Effects.Contains("writes_static_field", StringComparer.Ordinal) ||
            summary.Effects.Contains("writes_instance_field", StringComparer.Ordinal) ||
            summary.Effects.Contains("reads_instance_field", StringComparer.Ordinal) ||
            summary.Effects.Contains("reads_static_field", StringComparer.Ordinal) ||
            summary.Effects.Contains("indirect_call", StringComparer.Ordinal) ||
            summary.Effects.Contains("virtual_call", StringComparer.Ordinal) ||
            summary.Effects.Contains("block_memory_write", StringComparer.Ordinal))
            return false;

        return summary.Calls.Any(static call =>
            call.StartsWith("System.Collections.Generic.ValueListBuilder`1<", StringComparison.Ordinal) ||
            call.StartsWith("System.Text.ValueStringBuilder.", StringComparison.Ordinal));
    }

    private static bool HasReturnValueInitializationPattern(MethodEffectSummary? summary)
    {
        if (summary == null ||
            !summary.Effects.Contains("writes_indirect_memory", StringComparer.Ordinal))
            return false;

        foreach (var effect in summary.Effects)
            if (!string.Equals(effect, "writes_indirect_memory", StringComparison.Ordinal))
                return false;

        if (summary.Calls.Length != 0 || summary.Fields.Length != 0) return false;

        return HasParameterlessNonVoidReturn(summary.ExactSymbolKey);
    }

    private static bool HasByRefLikeViewConstructionPattern(MethodEffectSummary? summary)
    {
        if (summary == null ||
            !summary.Effects.Contains("writes_indirect_memory", StringComparer.Ordinal))
            return false;

        var allowsTupleOffsetReads = HasOnlyByRefLikeViewHelperFieldReads(summary);
        foreach (var effect in summary.Effects)
        {
            if (string.Equals(effect, "allocates_object", StringComparison.Ordinal) ||
                string.Equals(effect, "calls_method", StringComparison.Ordinal) ||
                string.Equals(effect, "writes_indirect_memory", StringComparison.Ordinal) ||
                (string.Equals(effect, "reads_instance_field", StringComparison.Ordinal) && allowsTupleOffsetReads))
                continue;

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

            if (IsByRefLikeViewConstructionHelperCall(call)) continue;

            if (IsPurityNeutralIntrinsicHelperCall(call)) continue;

            return false;
        }

        return sawByRefLikeConstructor;
    }

    private static bool HasOnlyByRefLikeViewHelperFieldReads(MethodEffectSummary summary)
    {
        if (!summary.Effects.Contains("reads_instance_field", StringComparer.Ordinal)) return true;

        return summary.Fields.All(static field =>
            field.StartsWith("System.ValueTuple", StringComparison.Ordinal) &&
            (field.EndsWith(".Item1", StringComparison.Ordinal) ||
             field.EndsWith(".Item2", StringComparison.Ordinal)));
    }

    private static bool HasOnlySafeStaticReads(MethodEffectSummary summary)
    {
        if (!summary.Effects.Contains("reads_static_field", StringComparer.Ordinal)) return true;

        return summary.RootCandidates.Contains("safe_static_cache_read", StringComparer.Ordinal) ||
               summary.RootCandidates.Contains("safe_static_constant_read", StringComparer.Ordinal);
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
            return false;

        var sawResolvedCall = false;
        foreach (var call in summary.Calls)
        {
            if (IsPurityNeutralIntrinsicHelperCall(call)) continue;

            if (!TryResolveCallSummary(call, bySymbol, out _, out _)) return false;

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

        var normalizedCall = EffectSummaryExactSymbolKeyNormalizer.NormalizeConstructedReceiverType(call);
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

    private static bool TryResolveExternalCallClassification(
        string call,
        IReadOnlyDictionary<string, GeneratedPurityCatalogEntry> externalGeneratedPurityEntries,
        out string resolvedCallKey,
        out GeneratedPurityCatalogEntry resolvedEntry,
        out MethodPurityClassification classification)
    {
        if (TryGetExternalEntry(call, externalGeneratedPurityEntries, out resolvedCallKey, out resolvedEntry))
        {
            classification = CreateClassification(resolvedEntry);
            return true;
        }

        resolvedCallKey = string.Empty;
        resolvedEntry = default!;
        classification = default!;
        return false;
    }

    private static bool TryResolveReviewedImplementationClassification(
        AssemblyEffectReport assembly,
        string symbol,
        MethodEffectSummary summary,
        IReadOnlyDictionary<string, GeneratedPurityCatalogEntry> externalGeneratedPurityEntries,
        out MethodPurityClassification classification)
    {
        classification = default!;
        if (!TryGetExternalEntry(symbol, externalGeneratedPurityEntries, out _, out var entry) ||
            !IsSameReviewedMethodImplementation(assembly, summary, entry))
            return false;

        classification = CreateClassification(entry);
        return !string.Equals(classification.Classification, "conservative_unknown", StringComparison.Ordinal);
    }

    private static bool TryResolveReviewedUpgrade(
        AssemblyEffectReport assembly,
        string symbol,
        MethodEffectSummary summary,
        IReadOnlyDictionary<string, GeneratedPurityCatalogEntry> externalGeneratedPurityEntries,
        out MethodPurityClassification classification)
    {
        return TryResolveReviewedImplementationClassification(
                   assembly,
                   symbol,
                   summary,
                   externalGeneratedPurityEntries,
                   out classification) &&
               !string.Equals(classification.Classification, "conservative_unknown", StringComparison.Ordinal);
    }

    private static bool ShouldPreferReviewedUpgrade(
        MethodPurityClassification currentClassification,
        MethodPurityClassification reviewedClassification)
    {
        if (!string.Equals(currentClassification.Classification, "impure", StringComparison.Ordinal) ||
            !string.Equals(reviewedClassification.Classification, "impure", StringComparison.Ordinal))
            return true;

        foreach (var category in currentClassification.Categories)
            if (!reviewedClassification.Categories.Contains(category, StringComparer.Ordinal))
                return false;

        foreach (var category in reviewedClassification.Categories)
            if (!currentClassification.Categories.Contains(category, StringComparer.Ordinal))
                return false;

        return true;
    }

    private static bool TryGetExternalEntry(
        string call,
        IReadOnlyDictionary<string, GeneratedPurityCatalogEntry> externalGeneratedPurityEntries,
        out string resolvedCallKey,
        out GeneratedPurityCatalogEntry resolvedEntry)
    {
        if (externalGeneratedPurityEntries.TryGetValue(call, out resolvedEntry!))
        {
            resolvedCallKey = call;
            return true;
        }

        var normalizedCall = EffectSummaryExactSymbolKeyNormalizer.NormalizeConstructedReceiverType(call);
        if (!string.Equals(normalizedCall, call, StringComparison.Ordinal) &&
            externalGeneratedPurityEntries.TryGetValue(normalizedCall, out resolvedEntry!))
        {
            resolvedCallKey = normalizedCall;
            return true;
        }

        resolvedCallKey = string.Empty;
        resolvedEntry = default!;
        return false;
    }

    private static bool IsSameReviewedMethodImplementation(
        AssemblyEffectReport assembly,
        MethodEffectSummary summary,
        GeneratedPurityCatalogEntry entry)
    {
        if (!string.Equals(assembly.AssemblyName, entry.AssemblyName, StringComparison.Ordinal) ||
            !string.Equals(assembly.AssemblySha256, entry.AssemblySha256, StringComparison.Ordinal) ||
            !string.Equals(assembly.ModuleVersionId, entry.ModuleVersionId, StringComparison.Ordinal) ||
            !string.Equals(summary.MetadataToken, entry.MetadataToken, StringComparison.Ordinal))
            return false;

        var summaryBodyHash = string.IsNullOrWhiteSpace(summary.MethodBodySha256)
            ? null
            : summary.MethodBodySha256;
        var entryBodyHash = string.IsNullOrWhiteSpace(entry.MethodBodySha256)
            ? null
            : entry.MethodBodySha256;

        if (summaryBodyHash == null || entryBodyHash == null) return summaryBodyHash == null && entryBodyHash == null;

        return string.Equals(summaryBodyHash, entryBodyHash, StringComparison.Ordinal);
    }

    private static MethodPurityClassification CreateClassification(GeneratedPurityCatalogEntry entry)
    {
        return new MethodPurityClassification(
            entry.Classification,
            entry.Categories,
            entry.FirstBlockingCallChain,
            entry.HasFreshArrayAllocationEvidence,
            entry.HasFreshObjectAllocationEvidence,
            entry.HasUnsupportedEffects,
            entry.FreshnessClassification,
            entry.EffectVisibilityClassification);
    }

    private static bool TryClassifyUnresolvedInteropBoundaryCall(
        MethodEffectSummary callerSummary,
        string callSymbol,
        out string category)
    {
        if (IsInteropLastErrorBookkeepingCall(callerSummary, callSymbol))
        {
            category = string.Empty;
            return false;
        }

        if (callSymbol.StartsWith("Interop+", StringComparison.Ordinal) ||
            callSymbol.StartsWith("Internal.Win32.", StringComparison.Ordinal) ||
            callSymbol.StartsWith("System.Runtime.InteropServices.NativeLibrary.", StringComparison.Ordinal) ||
            callSymbol.StartsWith("System.Runtime.InteropServices.Marshal.SetLastPInvokeError(",
                StringComparison.Ordinal) ||
            callSymbol.StartsWith("System.Runtime.InteropServices.Marshal.SetLastSystemError(",
                StringComparison.Ordinal) ||
            callSymbol.StartsWith("System.Runtime.InteropServices.Marshal.GetLastPInvokeError(",
                StringComparison.Ordinal) ||
            callSymbol.StartsWith("System.Runtime.InteropServices.Marshal.GetLastSystemError(",
                StringComparison.Ordinal))
        {
            category = IsSetterLikeUnresolvedInteropBoundaryCall(callSymbol)
                ? "global_state_write"
                : "global_state_read";
            return true;
        }

        category = string.Empty;
        return false;
    }

    private static bool IsInteropLastErrorBookkeepingCall(
        MethodEffectSummary callerSummary,
        string calleeSymbol)
    {
        return (IsInteropBoundaryWrapper(callerSummary.Symbol) || UsesWin32ErrorTranslation(callerSummary)) &&
               (calleeSymbol.StartsWith("System.Runtime.InteropServices.Marshal.GetLastPInvokeError(",
                    StringComparison.Ordinal) ||
                calleeSymbol.StartsWith("System.Runtime.InteropServices.Marshal.GetLastSystemError(",
                    StringComparison.Ordinal) ||
                calleeSymbol.StartsWith("System.Runtime.InteropServices.Marshal.SetLastPInvokeError(",
                    StringComparison.Ordinal) ||
                calleeSymbol.StartsWith("System.Runtime.InteropServices.Marshal.SetLastSystemError(",
                    StringComparison.Ordinal));
    }

    private static bool IsInteropBoundaryWrapper(string symbol)
    {
        return symbol.StartsWith("Interop+", StringComparison.Ordinal) ||
               symbol.StartsWith("Internal.Win32.", StringComparison.Ordinal);
    }

    private static bool UsesWin32ErrorTranslation(MethodEffectSummary summary)
    {
        return summary.Calls.Any(call =>
            call.StartsWith("System.IO.Win32Marshal.GetExceptionForWin32Error(", StringComparison.Ordinal));
    }

    private static bool IsSetterLikeUnresolvedInteropBoundaryCall(string callSymbol)
    {
        return callSymbol.Contains(".set_", StringComparison.Ordinal) ||
               callSymbol.Contains(".Set", StringComparison.Ordinal) ||
               callSymbol.Contains("<Set", StringComparison.Ordinal);
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
            return false;

        if (!summary.Calls.Any(static call =>
                call.StartsWith("System.MemoryExtensions.AsSpan(", StringComparison.Ordinal)))
            return false;

        var methodBaseSymbol = GetMethodBaseSymbol(summary.Symbol);
        return summary.Calls.Any(call =>
            call.StartsWith(methodBaseSymbol + "(", StringComparison.Ordinal));
    }

    private static bool IsFreshOwnedObjectInitializationCompatible(
        AssemblyEffectReport assembly,
        string symbol,
        IReadOnlyDictionary<string, MethodEffectSummary> bySymbol,
        IReadOnlyDictionary<string, GeneratedPurityCatalogEntry> externalGeneratedPurityEntries,
        IReadOnlyDictionary<string, GeneratedPurityCatalogEntry> reviewedGeneratedPurityEntries,
        Dictionary<string, MethodPurityClassification> memo,
        Dictionary<string, bool> freshOwnedInitializationMemo,
        Dictionary<string, bool> validationThrowHelperMemo,
        HashSet<string> purityVisiting)
    {
        if (freshOwnedInitializationMemo.TryGetValue(symbol, out var cached)) return cached;

        var compatibilityVisiting = new HashSet<string>(StringComparer.Ordinal);
        var compatible = IsFreshOwnedObjectInitializationCompatibleCore(
            assembly,
            symbol,
            bySymbol,
            externalGeneratedPurityEntries,
            reviewedGeneratedPurityEntries,
            memo,
            freshOwnedInitializationMemo,
            validationThrowHelperMemo,
            purityVisiting,
            compatibilityVisiting);
        freshOwnedInitializationMemo[symbol] = compatible;
        return compatible;
    }

    private static bool IsFreshOwnedObjectInitializationCompatibleCore(
        AssemblyEffectReport assembly,
        string symbol,
        IReadOnlyDictionary<string, MethodEffectSummary> bySymbol,
        IReadOnlyDictionary<string, GeneratedPurityCatalogEntry> externalGeneratedPurityEntries,
        IReadOnlyDictionary<string, GeneratedPurityCatalogEntry> reviewedGeneratedPurityEntries,
        Dictionary<string, MethodPurityClassification> memo,
        Dictionary<string, bool> freshOwnedInitializationMemo,
        Dictionary<string, bool> validationThrowHelperMemo,
        HashSet<string> purityVisiting,
        HashSet<string> compatibilityVisiting)
    {
        if (freshOwnedInitializationMemo.TryGetValue(symbol, out var cached)) return cached;

        if (!bySymbol.TryGetValue(symbol, out var summary)) return false;

        if (!compatibilityVisiting.Add(symbol)) return false;

        foreach (var root in summary.RootCandidates)
        {
            if (InternalOnlyRoots.Contains(root) ||
                string.Equals(root, "object_state_write", StringComparison.Ordinal))
                continue;

            compatibilityVisiting.Remove(symbol);
            return false;
        }

        foreach (var effect in summary.Effects)
        {
            if (string.Equals(effect, "writes_instance_field", StringComparison.Ordinal) ||
                SafeEffects.Contains(effect))
                continue;

            compatibilityVisiting.Remove(symbol);
            return false;
        }

        foreach (var callSite in EnumerateCallSites(summary))
        {
            var call = callSite.ExactSymbolKey;
            if (IsPurityNeutralIntrinsicHelperCall(call)) continue;

            if (IsValidationThrowHelperSupportCall(call)) continue;

            if (!TryResolveCallSummary(call, bySymbol, out var resolvedCallKey, out var resolvedCallSummary))
            {
                if (TryClassifyUnresolvedInteropBoundaryCall(summary, call, out _))
                {
                    compatibilityVisiting.Remove(symbol);
                    return false;
                }

                continue;
            }

            var calleeClassification = ClassifyMethod(
                assembly,
                resolvedCallKey,
                bySymbol,
                externalGeneratedPurityEntries,
                reviewedGeneratedPurityEntries,
                memo,
                freshOwnedInitializationMemo,
                validationThrowHelperMemo,
                purityVisiting);
            if (string.Equals(calleeClassification.Classification, "pure", StringComparison.Ordinal)) continue;

            if (ShouldTreatCallAsSemanticallyPure(summary, callSite, resolvedCallSummary, calleeClassification))
                continue;

            if (string.Equals(calleeClassification.Classification, "impure", StringComparison.Ordinal) &&
                IsFreshOwnedObjectInitializationCompatibleCore(
                    assembly,
                    resolvedCallKey,
                    bySymbol,
                    externalGeneratedPurityEntries,
                    reviewedGeneratedPurityEntries,
                    memo,
                    freshOwnedInitializationMemo,
                    validationThrowHelperMemo,
                    purityVisiting,
                    compatibilityVisiting))
                continue;

            compatibilityVisiting.Remove(symbol);
            return false;
        }

        compatibilityVisiting.Remove(symbol);
        freshOwnedInitializationMemo[symbol] = true;
        return true;
    }

    private static bool IsValidationThrowHelperCompatible(
        AssemblyEffectReport assembly,
        string symbol,
        IReadOnlyDictionary<string, MethodEffectSummary> bySymbol,
        IReadOnlyDictionary<string, GeneratedPurityCatalogEntry> externalGeneratedPurityEntries,
        IReadOnlyDictionary<string, GeneratedPurityCatalogEntry> reviewedGeneratedPurityEntries,
        Dictionary<string, MethodPurityClassification> memo,
        Dictionary<string, bool> freshOwnedInitializationMemo,
        Dictionary<string, bool> validationThrowHelperMemo,
        HashSet<string> purityVisiting)
    {
        if (validationThrowHelperMemo.TryGetValue(symbol, out var cached)) return cached;

        var compatibilityVisiting = new HashSet<string>(StringComparer.Ordinal);
        var compatible = IsValidationThrowHelperCompatibleCore(
            assembly,
            symbol,
            bySymbol,
            externalGeneratedPurityEntries,
            reviewedGeneratedPurityEntries,
            memo,
            freshOwnedInitializationMemo,
            validationThrowHelperMemo,
            purityVisiting,
            compatibilityVisiting);
        validationThrowHelperMemo[symbol] = compatible;
        return compatible;
    }

    private static bool IsValidationThrowHelperCompatibleCore(
        AssemblyEffectReport assembly,
        string symbol,
        IReadOnlyDictionary<string, MethodEffectSummary> bySymbol,
        IReadOnlyDictionary<string, GeneratedPurityCatalogEntry> externalGeneratedPurityEntries,
        IReadOnlyDictionary<string, GeneratedPurityCatalogEntry> reviewedGeneratedPurityEntries,
        Dictionary<string, MethodPurityClassification> memo,
        Dictionary<string, bool> freshOwnedInitializationMemo,
        Dictionary<string, bool> validationThrowHelperMemo,
        HashSet<string> purityVisiting,
        HashSet<string> compatibilityVisiting)
    {
        if (validationThrowHelperMemo.TryGetValue(symbol, out var cached)) return cached;

        if (!bySymbol.TryGetValue(symbol, out var summary)) return false;

        if (!compatibilityVisiting.Add(symbol)) return false;

        foreach (var root in summary.RootCandidates)
        {
            if (string.Equals(root, "throw", StringComparison.Ordinal) ||
                InternalOnlyRoots.Contains(root))
                continue;

            compatibilityVisiting.Remove(symbol);
            return false;
        }

        foreach (var effect in summary.Effects)
        {
            if (string.Equals(effect, "throws", StringComparison.Ordinal) ||
                SafeEffects.Contains(effect))
                continue;

            compatibilityVisiting.Remove(symbol);
            return false;
        }

        foreach (var callSite in EnumerateCallSites(summary))
        {
            var call = callSite.ExactSymbolKey;
            if (IsPurityNeutralIntrinsicHelperCall(call)) continue;

            if (IsValidationThrowHelperSupportCall(call)) continue;

            if (!TryResolveCallSummary(call, bySymbol, out var resolvedCallKey, out var resolvedCallSummary))
            {
                if (TryClassifyUnresolvedInteropBoundaryCall(summary, call, out _))
                {
                    compatibilityVisiting.Remove(symbol);
                    return false;
                }

                continue;
            }

            var calleeClassification = ClassifyMethod(
                assembly,
                resolvedCallKey,
                bySymbol,
                externalGeneratedPurityEntries,
                reviewedGeneratedPurityEntries,
                memo,
                freshOwnedInitializationMemo,
                validationThrowHelperMemo,
                purityVisiting);
            if (string.Equals(calleeClassification.Classification, "pure", StringComparison.Ordinal)) continue;

            if (ShouldTreatCallAsSemanticallyPure(summary, callSite, resolvedCallSummary, calleeClassification))
                continue;

            if (string.Equals(calleeClassification.Classification, "impure", StringComparison.Ordinal) &&
                IsValidationThrowHelperCompatibleCore(
                    assembly,
                    resolvedCallKey,
                    bySymbol,
                    externalGeneratedPurityEntries,
                    reviewedGeneratedPurityEntries,
                    memo,
                    freshOwnedInitializationMemo,
                    validationThrowHelperMemo,
                    purityVisiting,
                    compatibilityVisiting))
                continue;

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
            return false;

        foreach (var effect in summary.Effects)
        {
            if (string.Equals(effect, "calls_method", StringComparison.Ordinal) ||
                string.Equals(effect, "writes_instance_field", StringComparison.Ordinal) ||
                SafeEffects.Contains(effect))
                continue;

            return false;
        }

        return true;
    }

    private static string GetMethodBaseSymbol(string symbol)
    {
        var openParenIndex = symbol.IndexOf('(');
        return openParenIndex >= 0 ? symbol.Substring(0, openParenIndex) : symbol;
    }

    private static bool IsValidationThrowHelperSupportCall(string symbol)
    {
        var methodBaseSymbol = GetMethodBaseSymbol(symbol);
        return IsExceptionConstructor(methodBaseSymbol) ||
               IsResourceStringLookup(methodBaseSymbol);
    }

    private static bool IsExceptionConstructor(string methodBaseSymbol)
    {
        return methodBaseSymbol.EndsWith("Exception..ctor", StringComparison.Ordinal);
    }

    private static bool IsResourceStringLookup(string methodBaseSymbol)
    {
        return methodBaseSymbol.StartsWith("System.SR.get_", StringComparison.Ordinal) ||
               string.Equals(methodBaseSymbol, "System.SR.GetResourceString", StringComparison.Ordinal) ||
               string.Equals(methodBaseSymbol, "System.SR.Format", StringComparison.Ordinal);
    }

    private static string GetEffectVisibilityClassification(MethodEffectSummary? summary, string classification)
    {
        if (summary == null) return "unknown";

        if (string.Equals(classification, "conservative_unknown", StringComparison.Ordinal)) return "unknown";

        if (string.Equals(classification, "impure", StringComparison.Ordinal)) return "caller_visible";

        if (summary.RootCandidates.Contains("fresh_owned_memory_write", StringComparer.Ordinal) ||
            summary.RootCandidates.Contains("fresh_owned_array_write", StringComparer.Ordinal) ||
            summary.RootCandidates.Contains("fresh_owned_object_write", StringComparer.Ordinal) ||
            HasFreshOwnedArrayWritePattern(summary) ||
            HasFreshOwnedStringWritePattern(summary) ||
            HasLocalScratchMemoryWritePattern(summary) ||
            summary.RootCandidates.Contains("safe_static_cache_read", StringComparer.Ordinal) ||
            summary.RootCandidates.Contains("safe_static_constant_read", StringComparer.Ordinal))
            return "internal_only";

        if (HasFreshArrayAllocationEvidence(summary) && string.Equals(classification, "pure", StringComparison.Ordinal))
            return "internal_only";

        return "none";
    }

    private static string AggregateEffectVisibilityClassification(
        IReadOnlyList<MethodPurityClassification> classifications)
    {
        var values = classifications
            .Select(static classification => classification.EffectVisibilityClassification)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (values.Contains("caller_visible", StringComparer.Ordinal)) return "caller_visible";

        if (values.Contains("unknown", StringComparer.Ordinal)) return "unknown";

        if (values.Contains("internal_only", StringComparer.Ordinal)) return "internal_only";

        return "none";
    }

    internal static bool IsPurityNeutralIntrinsicHelperCall(string callSymbol)
    {
        return callSymbol.StartsWith("System.Runtime.CompilerServices.Unsafe.As(", StringComparison.Ordinal) ||
               callSymbol.StartsWith("System.Runtime.CompilerServices.Unsafe.AsPointer(", StringComparison.Ordinal) ||
               callSymbol.StartsWith("System.Runtime.CompilerServices.Unsafe.AsRef(", StringComparison.Ordinal) ||
               callSymbol.StartsWith("System.Runtime.CompilerServices.Unsafe.Add(", StringComparison.Ordinal) ||
               callSymbol.StartsWith("System.Runtime.CompilerServices.Unsafe.BitCast(", StringComparison.Ordinal) ||
               callSymbol.StartsWith("System.Runtime.CompilerServices.Unsafe.ReadUnaligned(",
                   StringComparison.Ordinal) ||
               callSymbol.StartsWith("System.Runtime.CompilerServices.Unsafe.WriteUnaligned(",
                   StringComparison.Ordinal) ||
               callSymbol.StartsWith("string.GetRawStringData()", StringComparison.Ordinal) ||
               callSymbol.StartsWith("string.get_Length()", StringComparison.Ordinal) ||
               (callSymbol.StartsWith("System.Span`1<", StringComparison.Ordinal) &&
                callSymbol.Contains("..ctor(!0[])", StringComparison.Ordinal)) ||
               (callSymbol.StartsWith("System.ReadOnlySpan`1<", StringComparison.Ordinal) &&
                callSymbol.Contains("..ctor(!0[])", StringComparison.Ordinal)) ||
               (callSymbol.StartsWith("System.Span`1<", StringComparison.Ordinal) &&
                callSymbol.Contains(".op_Implicit(!0[])", StringComparison.Ordinal)) ||
               (callSymbol.StartsWith("System.ReadOnlySpan`1<", StringComparison.Ordinal) &&
                callSymbol.Contains(".op_Implicit(!0[])", StringComparison.Ordinal)) ||
               (callSymbol.StartsWith("System.Span`1<", StringComparison.Ordinal) &&
                callSymbol.Contains("..ctor(void*, int)", StringComparison.Ordinal)) ||
               (callSymbol.StartsWith("System.ReadOnlySpan`1<", StringComparison.Ordinal) &&
                callSymbol.Contains("..ctor(void*, int)", StringComparison.Ordinal)) ||
               (callSymbol.StartsWith("System.Span`1<", StringComparison.Ordinal) &&
                callSymbol.Contains(".get_Length()", StringComparison.Ordinal)) ||
               (callSymbol.StartsWith("System.ReadOnlySpan`1<", StringComparison.Ordinal) &&
                callSymbol.Contains(".get_Length()", StringComparison.Ordinal)) ||
               callSymbol.StartsWith("System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences(",
                   StringComparison.Ordinal);
    }

    private static bool IsByRefLikeViewConstructionHelperCall(string callSymbol)
    {
        return callSymbol.StartsWith("System.Runtime.InteropServices.MemoryMarshal.GetArrayDataReference(",
                   StringComparison.Ordinal) ||
               callSymbol.StartsWith("System.Index.Equals(System.Index)", StringComparison.Ordinal) ||
               callSymbol.StartsWith("System.Index.GetOffset(int)", StringComparison.Ordinal) ||
               callSymbol.StartsWith("System.Index.get_Start()", StringComparison.Ordinal) ||
               callSymbol.StartsWith("System.Range.GetOffsetAndLength(int)", StringComparison.Ordinal) ||
               callSymbol.StartsWith("System.Range.get_End()", StringComparison.Ordinal) ||
               callSymbol.StartsWith("System.Range.get_Start()", StringComparison.Ordinal) ||
               callSymbol.StartsWith("System.ThrowHelper.ThrowArgumentNullException(", StringComparison.Ordinal) ||
               callSymbol.StartsWith("System.ThrowHelper.ThrowArgumentOutOfRangeException(",
                   StringComparison.Ordinal) ||
               callSymbol.StartsWith("System.ThrowHelper.ThrowArrayTypeMismatchException()",
                   StringComparison.Ordinal) ||
               callSymbol.StartsWith("System.Type.GetTypeFromHandle(System.RuntimeTypeHandle)",
                   StringComparison.Ordinal) ||
               callSymbol.StartsWith("System.Type.get_IsValueType()", StringComparison.Ordinal) ||
               callSymbol.StartsWith("System.Type.op_Inequality(System.Type, System.Type)", StringComparison.Ordinal) ||
               callSymbol.StartsWith("object.GetType()", StringComparison.Ordinal);
    }

    private static bool IsByRefLikeViewConstructionCall(string callSymbol)
    {
        return (callSymbol.StartsWith("System.Span`1<", StringComparison.Ordinal) &&
                callSymbol.Contains("..ctor(ref ", StringComparison.Ordinal)) ||
               (callSymbol.StartsWith("System.ReadOnlySpan`1<", StringComparison.Ordinal) &&
                callSymbol.Contains("..ctor(ref ", StringComparison.Ordinal));
    }

    private static bool HasParameterlessNonVoidReturn(string exactSymbolKey)
    {
        if (string.IsNullOrWhiteSpace(exactSymbolKey)) return false;

        var openParenIndex = exactSymbolKey.IndexOf('(');
        var returnSeparatorIndex = exactSymbolKey.LastIndexOf(")->", StringComparison.Ordinal);
        if (openParenIndex < 0 || returnSeparatorIndex <= openParenIndex) return false;

        var parameters = exactSymbolKey.Substring(openParenIndex + 1, returnSeparatorIndex - openParenIndex - 1);
        if (!string.IsNullOrEmpty(parameters)) return false;

        var returnType = exactSymbolKey[(returnSeparatorIndex + 3)..];
        return !string.IsNullOrWhiteSpace(returnType) &&
               !string.Equals(returnType, "void", StringComparison.Ordinal) &&
               !returnType.StartsWith("ref ", StringComparison.Ordinal);
    }

    private static bool HasByRefParameter(string exactSymbolKey)
    {
        if (string.IsNullOrWhiteSpace(exactSymbolKey)) return false;

        var openParenIndex = exactSymbolKey.IndexOf('(');
        var returnSeparatorIndex = exactSymbolKey.LastIndexOf(")->", StringComparison.Ordinal);
        if (openParenIndex < 0 || returnSeparatorIndex <= openParenIndex) return false;

        var parameters = exactSymbolKey.Substring(openParenIndex + 1, returnSeparatorIndex - openParenIndex - 1);
        return parameters.Contains("ref ", StringComparison.Ordinal);
    }

    private static bool IsPureArgumentGuardWrapper(string symbol)
    {
        var methodBaseSymbol = GetMethodBaseSymbol(symbol);
        if (!methodBaseSymbol.StartsWith("System.Argument", StringComparison.Ordinal) ||
            !methodBaseSymbol.Contains(".ThrowIf", StringComparison.Ordinal))
            return false;

        return !symbol.Contains('*', StringComparison.Ordinal) &&
               !symbol.Contains("nint", StringComparison.Ordinal);
    }

    private static bool IsArgumentGuardThrowHelper(string symbol)
    {
        var methodBaseSymbol = GetMethodBaseSymbol(symbol);
        if (!methodBaseSymbol.StartsWith("System.Argument", StringComparison.Ordinal)) return false;

        return methodBaseSymbol.Contains(".Throw", StringComparison.Ordinal) &&
               !methodBaseSymbol.Contains(".ThrowIf", StringComparison.Ordinal);
    }

    private static bool IsSemanticallyNeutralValidationThrowHelper(string symbol)
    {
        if (IsArgumentGuardThrowHelper(symbol)) return true;

        var methodBaseSymbol = GetMethodBaseSymbol(symbol);
        return methodBaseSymbol.StartsWith("System.ThrowHelper.Throw", StringComparison.Ordinal);
    }

    private static bool IsSemanticallyCheckedDelegateInvokingBclMethod(string symbol)
    {
        var methodBaseSymbol = GetMethodBaseSymbol(symbol);
        var lastDotIndex = methodBaseSymbol.LastIndexOf('.');
        if (lastDotIndex <= 0 || lastDotIndex == methodBaseSymbol.Length - 1) return false;

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
    EffectSummaryArtifactSource? ArtifactSource,
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

internal static class EffectSummaryClassificationEvidenceRules
{
    private static readonly string[] CommonFreshResultDisqualifyingEffects =
        ["writes_static_field", "writes_instance_field", "indirect_call", "virtual_call", "block_memory_write"];

    internal static string? GetFreshArrayNote(MethodPurityClassification? classification)
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

    internal static string GetFreshnessClassification(MethodEffectSummary? summary, string classification)
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

    internal static bool HasFreshOwnedArrayWritePattern(MethodEffectSummary? summary)
    {
        if (summary == null) return false;

        if (HasByRefParameter(summary.Identity)) return false;

        if (summary.Effects.Contains("allocates_array", StringComparer.Ordinal) &&
            summary.Effects.Contains("writes_indirect_memory", StringComparer.Ordinal))
        {
            if (HasFreshResultDisqualifyingEffect(summary, "reads_instance_field"))
                return false;

            if (!HasOnlySafeStaticReads(summary)) return false;

            return true;
        }

        return HasAllocateUninitializedArrayWrapperPattern(summary);
    }

    internal static bool HasFreshOwnedStringWritePattern(MethodEffectSummary? summary)
    {
        if (summary == null ||
            !summary.Effects.Contains("allocates_object", StringComparer.Ordinal) ||
            !summary.Effects.Contains("writes_indirect_memory", StringComparer.Ordinal))
            return false;

        if (!summary.Calls.Any(static call =>
                string.Equals(call, "string.FastAllocateString(int)->string", StringComparison.Ordinal)))
            return false;

        if (HasFreshResultDisqualifyingEffect(summary, "allocates_array"))
            return false;

        if (!HasOnlySafeStaticReads(summary)) return false;

        return true;
    }

    internal static bool HasLocalScratchMemoryWritePattern(MethodEffectSummary? summary)
    {
        if (summary == null ||
            !summary.Effects.Contains("writes_indirect_memory", StringComparer.Ordinal))
            return false;

        if (HasFreshResultDisqualifyingEffect(summary,
                "allocates_array", "reads_instance_field", "reads_static_field"))
            return false;

        return summary.Calls.Any(static call =>
            call.StartsWith("System.Collections.Generic.ValueListBuilder`1<", StringComparison.Ordinal) ||
            call.StartsWith("System.Text.ValueStringBuilder.", StringComparison.Ordinal));
    }

    private static bool HasFreshResultDisqualifyingEffect(
        MethodEffectSummary summary,
        params string[] additionalEffects) =>
        summary.Effects.Any(effect =>
            CommonFreshResultDisqualifyingEffects.Contains(effect, StringComparer.Ordinal) ||
            additionalEffects.Contains(effect, StringComparer.Ordinal));

    internal static bool HasReturnValueInitializationPattern(MethodEffectSummary? summary)
    {
        if (summary == null ||
            !summary.Effects.Contains("writes_indirect_memory", StringComparer.Ordinal))
            return false;

        foreach (var effect in summary.Effects)
            if (!string.Equals(effect, "writes_indirect_memory", StringComparison.Ordinal))
                return false;

        if (summary.Calls.Length != 0 || summary.Fields.Length != 0) return false;

        return HasParameterlessNonVoidReturn(summary.Identity);
    }

    internal static bool HasByRefLikeViewConstructionPattern(MethodEffectSummary? summary)
    {
        if (summary == null) return false;

        var hasIndirectWrite = summary.Effects.Contains("writes_indirect_memory", StringComparer.Ordinal);
        var isReadOnlyProjection = !hasIndirectWrite &&
                                   IsByRefLikeViewReturn(summary.Identity) &&
                                   RootsAreSemanticallyPureWrapperCompatible(summary);
        if (!hasIndirectWrite && !isReadOnlyProjection) return false;

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

    private static bool IsByRefLikeViewReturn(StructuralMethodIdentity identity) =>
        identity.ReturnType.StartsWith("named:System.Span`1[", StringComparison.Ordinal) ||
        identity.ReturnType.StartsWith("named:System.ReadOnlySpan`1[", StringComparison.Ordinal);

    internal static bool HasOnlyByRefLikeViewHelperFieldReads(MethodEffectSummary summary)
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

    internal static bool HasOnlySafeStaticReads(MethodEffectSummary summary)
    {
        if (!summary.Effects.Contains("reads_static_field", StringComparer.Ordinal)) return true;

        return summary.RootCandidates.Contains("safe_static_cache_read", StringComparer.Ordinal) ||
               summary.RootCandidates.Contains("safe_static_constant_read", StringComparer.Ordinal);
    }

    internal static bool HasOnlyResolvedVirtualCallTargets(
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
        foreach (var callSite in EnumerateCallSites(summary))
        {
            if (IsPurityNeutralIntrinsicHelperCall(callSite.DisplayName)) continue;

            if (callSite.CanonicalKey == null ||
                !TryResolveCallSummary(callSite.CanonicalKey, bySymbol, out _, out var resolvedCallSummary))
                return false;

            if (callSite.UsesDynamicDispatch &&
                (resolvedCallSummary.Effects.Contains("abstract", StringComparer.Ordinal) ||
                 resolvedCallSummary.Effects.Contains("no_il_body", StringComparer.Ordinal) ||
                 resolvedCallSummary.RootCandidates.Contains("metadata_only_or_external", StringComparer.Ordinal)))
                return false;

            sawResolvedCall = true;
        }

        return sawResolvedCall;
    }

    internal static bool TryResolveCallSummary(
        string canonicalKey,
        IReadOnlyDictionary<string, MethodEffectSummary> bySymbol,
        out string resolvedCallKey,
        out MethodEffectSummary resolvedCallSummary)
    {
        if (bySymbol.TryGetValue(canonicalKey, out resolvedCallSummary!))
        {
            resolvedCallKey = canonicalKey;
            return true;
        }

        resolvedCallKey = string.Empty;
        resolvedCallSummary = default!;
        return false;
    }

    internal static bool TryResolveExternalCallClassification(
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

    internal static bool TryResolveReviewedImplementationClassification(
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

    internal static bool TryResolveReviewedUpgrade(
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

    internal static bool TryGetExternalEntry(
        string canonicalKey,
        IReadOnlyDictionary<string, GeneratedPurityCatalogEntry> externalGeneratedPurityEntries,
        out string resolvedCallKey,
        out GeneratedPurityCatalogEntry resolvedEntry)
    {
        if (externalGeneratedPurityEntries.TryGetValue(canonicalKey, out resolvedEntry!))
        {
            resolvedCallKey = canonicalKey;
            return true;
        }

        resolvedCallKey = string.Empty;
        resolvedEntry = default!;
        return false;
    }

    internal static bool IsSameReviewedMethodImplementation(
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

    internal static MethodPurityClassification CreateClassification(GeneratedPurityCatalogEntry entry)
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

    internal static bool TryClassifyUnresolvedInteropBoundaryCall(
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

    internal static bool IsInteropLastErrorBookkeepingCall(
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

    internal static bool IsInteropBoundaryWrapper(string symbol)
    {
        return symbol.StartsWith("Interop+", StringComparison.Ordinal) ||
               symbol.StartsWith("Internal.Win32.", StringComparison.Ordinal);
    }

    internal static bool UsesWin32ErrorTranslation(MethodEffectSummary summary)
    {
        return summary.Calls.Any(call =>
            call.StartsWith("System.IO.Win32Marshal.GetExceptionForWin32Error(", StringComparison.Ordinal));
    }

    internal static bool IsSetterLikeUnresolvedInteropBoundaryCall(string callSymbol)
    {
        return callSymbol.Contains(".set_", StringComparison.Ordinal) ||
               callSymbol.Contains(".Set", StringComparison.Ordinal) ||
               callSymbol.Contains("<Set", StringComparison.Ordinal);
    }

    internal static bool HasFreshArrayAllocationEvidence(MethodEffectSummary? summary)
    {
        return summary != null &&
               (summary.Effects.Contains("allocates_array", StringComparer.Ordinal) ||
                HasAllocateUninitializedArrayWrapperPattern(summary));
    }

    internal static bool HasAllocateUninitializedArrayWrapperPattern(MethodEffectSummary summary)
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

    internal static bool IsFreshOwnedObjectInitializationCompatible(
        string symbol,
        PurityClassificationContext context)
    {
        return IsClassificationCompatible(
            symbol,
            context,
            context.FreshOwnedInitializationMemo,
            IsFreshOwnedInitializationRoot,
            IsFreshOwnedInitializationEffect);
    }

    internal static bool IsValidationThrowHelperCompatible(
        string symbol,
        PurityClassificationContext context)
    {
        return IsClassificationCompatible(
            symbol,
            context,
            context.ValidationThrowHelperMemo,
            IsValidationThrowHelperRoot,
            IsValidationThrowHelperEffect);
    }

    private static bool IsClassificationCompatible(
        string symbol,
        PurityClassificationContext context,
        Dictionary<string, bool> memo,
        Func<string, bool> isAllowedRoot,
        Func<string, bool> isAllowedEffect)
    {
        if (memo.TryGetValue(symbol, out var cached)) return cached;

        var compatibilityVisiting = new HashSet<string>(StringComparer.Ordinal);
        var compatible = IsClassificationCompatibleCore(
            symbol,
            context,
            memo,
            compatibilityVisiting,
            isAllowedRoot,
            isAllowedEffect);
        memo[symbol] = compatible;
        return compatible;
    }

    private static bool IsClassificationCompatibleCore(
        string symbol,
        PurityClassificationContext context,
        Dictionary<string, bool> memo,
        HashSet<string> compatibilityVisiting,
        Func<string, bool> isAllowedRoot,
        Func<string, bool> isAllowedEffect)
    {
        if (memo.TryGetValue(symbol, out var cached)) return cached;
        if (!context.BySymbol.TryGetValue(symbol, out var summary)) return false;
        if (!compatibilityVisiting.Add(symbol)) return false;

        if (summary.RootCandidates.Any(root => !isAllowedRoot(root)) ||
            summary.Effects.Any(effect => !isAllowedEffect(effect)))
        {
            compatibilityVisiting.Remove(symbol);
            return false;
        }

        foreach (var callSite in EnumerateCallSites(summary))
        {
            var call = callSite.DisplayName;
            if (IsPurityNeutralIntrinsicHelperCall(call) ||
                IsValidationThrowHelperSupportCall(call))
                continue;

            if (callSite.CanonicalKey == null ||
                !TryResolveCallSummary(
                    callSite.CanonicalKey,
                    context.BySymbol,
                    out var resolvedCallKey,
                    out var resolvedCallSummary))
            {
                if (TryClassifyUnresolvedInteropBoundaryCall(summary, call, out _))
                {
                    compatibilityVisiting.Remove(symbol);
                    return false;
                }

                continue;
            }

            var calleeClassification = ClassifyMethod(resolvedCallKey, context);
            if (string.Equals(calleeClassification.Classification, "pure", StringComparison.Ordinal) ||
                ShouldTreatCallAsSemanticallyPure(summary, callSite, resolvedCallSummary, calleeClassification))
                continue;

            if (string.Equals(calleeClassification.Classification, "impure", StringComparison.Ordinal) &&
                IsClassificationCompatibleCore(
                    resolvedCallKey,
                    context,
                    memo,
                    compatibilityVisiting,
                    isAllowedRoot,
                    isAllowedEffect))
                continue;

            compatibilityVisiting.Remove(symbol);
            return false;
        }

        compatibilityVisiting.Remove(symbol);
        memo[symbol] = true;
        return true;
    }

    private static bool IsFreshOwnedInitializationRoot(string root)
    {
        return InternalOnlyRoots.Contains(root) ||
               string.Equals(root, "object_state_write", StringComparison.Ordinal);
    }

    private static bool IsFreshOwnedInitializationEffect(string effect)
    {
        return SafeEffects.Contains(effect) ||
               string.Equals(effect, "writes_instance_field", StringComparison.Ordinal);
    }

    private static bool IsValidationThrowHelperRoot(string root)
    {
        return InternalOnlyRoots.Contains(root) ||
               string.Equals(root, "throw", StringComparison.Ordinal);
    }

    private static bool IsValidationThrowHelperEffect(string effect)
    {
        return SafeEffects.Contains(effect) ||
               string.Equals(effect, "throws", StringComparison.Ordinal);
    }
    internal static bool IsFreshOwnedObjectConstructor(MethodEffectSummary summary)
    {
        if (string.IsNullOrWhiteSpace(summary.Symbol) ||
            !summary.Symbol.Contains("..ctor(", StringComparison.Ordinal))
            return false;

        foreach (var effect in summary.Effects)
        {
            if (string.Equals(effect, "calls_method", StringComparison.Ordinal) ||
                string.Equals(effect, "writes_instance_field", StringComparison.Ordinal) ||
                string.Equals(effect, "writes_indirect_memory", StringComparison.Ordinal) ||
                SafeEffects.Contains(effect))
                continue;

            return false;
        }

        return true;
    }

    internal static string GetMethodBaseSymbol(string symbol)
    {
        var openParenIndex = symbol.IndexOf('(');
        return openParenIndex >= 0 ? symbol.Substring(0, openParenIndex) : symbol;
    }

    internal static bool IsValidationThrowHelperSupportCall(string symbol)
    {
        var methodBaseSymbol = GetMethodBaseSymbol(symbol);
        return IsExceptionConstructor(methodBaseSymbol) ||
               IsResourceStringLookup(methodBaseSymbol);
    }

    internal static bool IsExceptionConstructor(string methodBaseSymbol)
    {
        return methodBaseSymbol.EndsWith("Exception..ctor", StringComparison.Ordinal);
    }

    internal static bool IsResourceStringLookup(string methodBaseSymbol)
    {
        return methodBaseSymbol.StartsWith("System.SR.get_", StringComparison.Ordinal) ||
               string.Equals(methodBaseSymbol, "System.SR.GetResourceString", StringComparison.Ordinal) ||
               string.Equals(methodBaseSymbol, "System.SR.Format", StringComparison.Ordinal);
    }

    internal static string GetEffectVisibilityClassification(MethodEffectSummary? summary, string classification)
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

    internal static string AggregateEffectVisibilityClassification(
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
        return callSymbol.StartsWith("System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(",
                   StringComparison.Ordinal) ||
               callSymbol.StartsWith("System.Runtime.CompilerServices.Unsafe.As(", StringComparison.Ordinal) ||
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

    internal static bool IsByRefLikeViewConstructionHelperCall(string callSymbol)
    {
        return EffectSummaryKnownFrameworkCalls.IsArrayDataReference(callSymbol) ||
               callSymbol.StartsWith("System.Runtime.InteropServices.MemoryMarshal.GetReference(System.Span`1<",
                   StringComparison.Ordinal) ||
               callSymbol.StartsWith("System.Runtime.InteropServices.MemoryMarshal.GetReference(System.ReadOnlySpan`1<",
                   StringComparison.Ordinal) ||
               callSymbol.StartsWith("System.Index.Equals(System.Index)", StringComparison.Ordinal) ||
               callSymbol.StartsWith("System.Index.GetOffset(int)", StringComparison.Ordinal) ||
               callSymbol.StartsWith("System.Index.get_Start()", StringComparison.Ordinal) ||
               callSymbol.StartsWith("System.Range.GetOffsetAndLength(int)", StringComparison.Ordinal) ||
               callSymbol.StartsWith("System.Range.get_End()", StringComparison.Ordinal) ||
               callSymbol.StartsWith("System.Range.get_Start()", StringComparison.Ordinal) ||
               EffectSummaryKnownFrameworkCalls.IsByRefLikeRuntimeTypeHelper(callSymbol) ||
               IsSemanticallyNeutralValidationThrowHelper(callSymbol);
    }

    internal static bool IsByRefLikeViewConstructionCall(string callSymbol)
    {
        return (callSymbol.StartsWith("System.Span`1<", StringComparison.Ordinal) &&
                (callSymbol.Contains("..ctor(ref ", StringComparison.Ordinal) ||
                 callSymbol.Contains("..ctor(!0[])", StringComparison.Ordinal))) ||
               (callSymbol.StartsWith("System.ReadOnlySpan`1<", StringComparison.Ordinal) &&
                (callSymbol.Contains("..ctor(ref ", StringComparison.Ordinal) ||
                 callSymbol.Contains("..ctor(!0[])", StringComparison.Ordinal)));
    }

    internal static bool HasParameterlessNonVoidReturn(StructuralMethodIdentity identity)
    {
        return identity.Parameters.IsDefaultOrEmpty &&
               !string.Equals(identity.ReturnType, "named:System.Void", StringComparison.Ordinal) &&
               string.Equals(identity.ReturnRefKind, "none", StringComparison.Ordinal);
    }

    internal static bool HasByRefParameter(StructuralMethodIdentity identity)
    {
        return identity.Parameters.Any(static parameter =>
            !string.Equals(parameter.RefKind, "none", StringComparison.Ordinal));
    }

    internal static bool HasReturnType(StructuralMethodIdentity identity, string returnType)
    {
        return string.Equals(identity.ReturnType, returnType, StringComparison.Ordinal) &&
               string.Equals(identity.ReturnRefKind, "none", StringComparison.Ordinal);
    }

    internal static bool IsPureArgumentGuardWrapper(string symbol)
    {
        var methodBaseSymbol = GetMethodBaseSymbol(symbol);
        if (!methodBaseSymbol.StartsWith("System.Argument", StringComparison.Ordinal) ||
            !methodBaseSymbol.Contains(".ThrowIf", StringComparison.Ordinal))
            return false;

        return !symbol.Contains('*', StringComparison.Ordinal) &&
               !symbol.Contains("nint", StringComparison.Ordinal);
    }

    internal static bool IsArgumentGuardThrowHelper(string symbol)
    {
        var methodBaseSymbol = GetMethodBaseSymbol(symbol);
        if (!methodBaseSymbol.StartsWith("System.Argument", StringComparison.Ordinal)) return false;

        return methodBaseSymbol.Contains(".Throw", StringComparison.Ordinal) &&
               !methodBaseSymbol.Contains(".ThrowIf", StringComparison.Ordinal);
    }

    internal static bool IsSemanticallyNeutralValidationThrowHelper(string symbol)
    {
        if (IsArgumentGuardThrowHelper(symbol)) return true;

        var methodBaseSymbol = GetMethodBaseSymbol(symbol);
        return methodBaseSymbol.StartsWith("System.ThrowHelper.Throw", StringComparison.Ordinal);
    }

    internal static bool IsSemanticallyCheckedDelegateInvokingBclMethod(string symbol)
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

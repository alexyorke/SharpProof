internal static class EffectSummaryCallPurityRules {
    internal static IEnumerable<CallSiteSummary> EnumerateCallSites(MethodEffectSummary summary) {
        if (summary.CallSites.Length != 0) return summary.CallSites;

        return summary.CallIdentities.Select(static identity => new CallSiteSummary(identity.ToCanonicalKey()) {
            Identity = identity
        });
    }

    internal static bool ShouldTreatCallAsSemanticallyPure(
        MethodEffectSummary callerSummary,
        CallSiteSummary callSite,
        string calleeSymbol,
        MethodPurityClassification calleeClassification) {
        if (IsInteropLastErrorBookkeepingCall(callerSummary, calleeSymbol)) return true;
        if (string.Equals(calleeClassification.Classification, "pure", StringComparison.Ordinal)) return false;

        return IsFreshArrayInitializationHelperCall(callerSummary, calleeSymbol, calleeClassification) ||
               IsFreshArrayTemporaryInitializationHelperCall(callerSummary, calleeSymbol, calleeClassification) ||
               (HasDeterministicStringComparisonEvidence(callSite) &&
                IsContextSensitiveStringComparisonMethod(calleeSymbol)) ||
               IsFreshStringInitializationHelperCall(callerSummary, calleeSymbol, calleeClassification) ||
               IsCharSpanToStringWrapperCall(callerSummary, callSite, calleeSymbol, calleeClassification) ||
               IsSemanticallyPureCharSpanSearchHelperCall(callerSummary, calleeSymbol, calleeClassification) ||
               IsDateTimeArithmeticHelperCall(callerSummary.Symbol, calleeSymbol) ||
               IsDateTimeOffsetArithmeticHelperCall(callerSummary.Symbol, calleeSymbol) ||
               IsDateTimeToBinaryHelperCall(callerSummary.Symbol, calleeSymbol) ||
               IsDateTimeConstructorHelperCall(callerSummary.Symbol, calleeSymbol);
    }

    internal static bool IsDateTimeArithmeticHelperCall(string callerSymbol, string calleeSymbol) => string.Equals(callerSymbol, "System.DateTime.AddUnits(double, long, long)", StringComparison.Ordinal) &&
               (string.Equals(calleeSymbol, "System.Math.Abs(double)", StringComparison.Ordinal) ||
                string.Equals(calleeSymbol, "System.Math.Truncate(double)", StringComparison.Ordinal));

    internal static bool IsDateTimeToBinaryHelperCall(string callerSymbol, string calleeSymbol) => string.Equals(callerSymbol, "System.DateTime.ToBinary()", StringComparison.Ordinal) &&
               string.Equals(calleeSymbol,
                   "System.TimeZoneInfo.GetLocalUtcOffset(System.DateTime, System.TimeZoneInfoOptions)",
                   StringComparison.Ordinal);

    internal static bool IsDateTimeConstructorHelperCall(string callerSymbol, string calleeSymbol) => string.Equals(callerSymbol, "System.DateTime..ctor(int, int, int)", StringComparison.Ordinal) &&
               string.Equals(calleeSymbol, "System.DateTime.DateToTicks(int, int, int)", StringComparison.Ordinal);

    internal static bool IsDateTimeOffsetArithmeticHelperCall(string callerSymbol, string calleeSymbol) => IsDateTimeArithmeticMember(callerSymbol, "System.DateTimeOffset") &&
               (IsDateTimeArithmeticMember(calleeSymbol, "System.DateTime") ||
                string.Equals(calleeSymbol, "System.DateTimeOffset..ctor(System.DateTime, System.TimeSpan)",
                    StringComparison.Ordinal) ||
                string.Equals(calleeSymbol, "System.DateTimeOffset.get_ClockDateTime()", StringComparison.Ordinal) ||
                string.Equals(calleeSymbol, "System.DateTimeOffset.get_Offset()", StringComparison.Ordinal));

    internal static bool IsDateTimeArithmeticMember(string symbol, string containingType) {
        var prefix = containingType + ".";
        if (!symbol.StartsWith(prefix, StringComparison.Ordinal)) return false;

        return symbol.Substring(prefix.Length) is
            "Add(System.TimeSpan)" or
            "AddDays(double)" or
            "AddHours(double)" or
            "AddMilliseconds(double)" or
            "AddMinutes(double)" or
            "AddMonths(int)" or
            "AddSeconds(double)" or
            "AddTicks(long)" or
            "AddYears(int)";
    }

    internal static bool IsFreshArrayInitializationHelperCall(
        MethodEffectSummary callerSummary,
        string calleeSymbol,
        MethodPurityClassification calleeClassification) {
        if (!IsFreshArrayInitializationContext(callerSummary)) return false;

        return (IsFreshArrayCopyHelperCall(calleeSymbol) &&
                HasFreshArrayCopyBlockingChain(calleeClassification.FirstBlockingCallChain)) ||
               (IsFreshArraySpanWriteHelperCall(calleeSymbol) &&
                HasFreshArraySpanWriteValidationBlockingChain(calleeClassification.FirstBlockingCallChain));
    }

    internal static bool IsFreshArrayInitializationContext(MethodEffectSummary summary) => IsFreshAllocationInitializationContext(
            summary,
            "allocates_array",
            "allocates_object");

    internal static bool IsFreshStringInitializationHelperCall(
        MethodEffectSummary callerSummary,
        string calleeSymbol,
        MethodPurityClassification calleeClassification) {
        if (!IsFreshStringInitializationContext(callerSummary)) return false;

        return IsFreshStringCopyHelperCall(calleeSymbol) &&
               HasFreshStringCopyBlockingChain(calleeClassification.FirstBlockingCallChain);
    }

    internal static bool IsFreshStringInitializationContext(MethodEffectSummary summary) {
        if (!IsFreshAllocationInitializationContext(
                summary,
                "allocates_object",
                "allocates_array") ||
            !summary.Calls.Any(static call =>
                string.Equals(call, "string.FastAllocateString(int)->string", StringComparison.Ordinal)))
            return false;

        return true;
    }

    internal static bool IsFreshAllocationInitializationContext(
        MethodEffectSummary summary,
        string requiredAllocationEffect,
        string excludedAllocationEffect) {
        if (!summary.Effects.Contains(requiredAllocationEffect, StringComparer.Ordinal) ||
            summary.Effects.Contains(excludedAllocationEffect, StringComparer.Ordinal) ||
            summary.Effects.Contains("writes_static_field", StringComparer.Ordinal) ||
            summary.Effects.Contains("writes_instance_field", StringComparer.Ordinal) ||
            summary.Effects.Contains("writes_indirect_memory", StringComparer.Ordinal) ||
            summary.Effects.Contains("indirect_call", StringComparer.Ordinal) ||
            summary.Effects.Contains("virtual_call", StringComparer.Ordinal))
            return false;

        return HasOnlySafeStaticReads(summary);
    }

    internal static bool IsFreshStringCopyHelperCall(string calleeSymbol) => calleeSymbol.StartsWith("System.ReadOnlySpan`1", StringComparison.Ordinal) &&
               calleeSymbol.Contains(".CopyTo(System.Span`1<!0>)", StringComparison.Ordinal);

    internal static bool IsFreshArrayCopyHelperCall(string calleeSymbol) => IsBufferMemmoveCall(calleeSymbol) ||
               IsBufferMemmoveHelper(calleeSymbol);

    internal static bool IsFreshArraySpanWriteHelperCall(string calleeSymbol) => calleeSymbol.StartsWith("System.Runtime.InteropServices.MemoryMarshal.TryWrite(",
            StringComparison.Ordinal);

    internal static bool IsFreshArrayTemporaryInitializationHelperCall(
        MethodEffectSummary callerSummary,
        string calleeSymbol,
        MethodPurityClassification calleeClassification) => IsFreshArrayInitializationContext(callerSummary) &&
               calleeSymbol.Contains("..ctor(", StringComparison.Ordinal) &&
               calleeClassification.Categories.All(IsTemporaryInitializationCategory) &&
               HasValidationOnlyBlockingChain(calleeClassification.FirstBlockingCallChain);

    internal static bool HasFreshStringCopyBlockingChain(string[] blockingCallChain) {
        if (blockingCallChain.Length == 0) return false;

        if (blockingCallChain.All(IsBufferMemmoveHelper)) return true;

        return string.Equals(blockingCallChain[0], "System.ReadOnlySpan`1.CopyTo(System.Span`1<!0>)",
                   StringComparison.Ordinal) &&
               blockingCallChain.Skip(1).All(IsBufferMemmoveHelper);
    }

    internal static bool HasFreshArrayCopyBlockingChain(string[] blockingCallChain) => blockingCallChain.Length != 0 &&
               blockingCallChain.All(IsBufferMemmoveHelper);

    internal static bool HasFreshArraySpanWriteValidationBlockingChain(string[] blockingCallChain) => blockingCallChain.Length >= 1 &&
               string.Equals(
                   blockingCallChain[0],
                   "System.ThrowHelper.ThrowInvalidTypeWithPointersNotSupported(System.Type)",
                   StringComparison.Ordinal);

    internal static bool HasValidationOnlyBlockingChain(string[] blockingCallChain) {
        if (blockingCallChain.Length == 0) return false;

        var first = blockingCallChain[0];
        return first.Contains(".Throw", StringComparison.Ordinal) &&
               (first.Contains("Argument", StringComparison.Ordinal) ||
                first.StartsWith("System.ThrowHelper.Throw", StringComparison.Ordinal));
    }

    internal static bool IsTemporaryInitializationCategory(string category) => string.Equals(category, "caller_visible_memory_write", StringComparison.Ordinal) ||
               string.Equals(category, "global_state_read", StringComparison.Ordinal) ||
               string.Equals(category, "global_state_write", StringComparison.Ordinal) ||
               string.Equals(category, "impure_callee", StringComparison.Ordinal) ||
               string.Equals(category, "object_state_write", StringComparison.Ordinal);

    internal static bool IsBufferMemmoveHelper(string symbol) => string.Equals(symbol, "System.Buffer.Memmove(ref !!0, ref !!0, nuint)", StringComparison.Ordinal) ||
               string.Equals(symbol, "System.Buffer.Memmove(ref byte, ref byte, nuint)", StringComparison.Ordinal) ||
               string.Equals(symbol, "System.Buffer._Memmove(ref byte, ref byte, nuint)", StringComparison.Ordinal) ||
               string.Equals(symbol, "System.Buffer.__Memmove(byte*, byte*, nuint)", StringComparison.Ordinal) ||
               string.Equals(symbol, "System.Buffer.BulkMoveWithWriteBarrier(ref byte, ref byte, nuint)",
                   StringComparison.Ordinal) ||
               string.Equals(symbol, "System.Buffer._BulkMoveWithWriteBarrier(ref byte, ref byte, nuint)",
                   StringComparison.Ordinal) ||
               string.Equals(symbol, "System.Buffer.__BulkMoveWithWriteBarrier(ref byte, ref byte, nuint)",
                   StringComparison.Ordinal);

    internal static bool IsSemanticallyPureCharSpanSearchHelperCall(
        MethodEffectSummary callerSummary,
        string calleeSymbol,
        MethodPurityClassification calleeClassification) => HasCharSpanSearchContext(callerSummary) &&
               IsEqualityBasedSpanSearchHelper(calleeSymbol) &&
               HasEqualityBasedSpanSearchBlockingChain(calleeClassification.FirstBlockingCallChain);

    internal static bool IsCharSpanToStringWrapperCall(
        MethodEffectSummary callerSummary,
        CallSiteSummary callSite,
        string calleeSymbol,
        MethodPurityClassification calleeClassification) {
        if (callSite.UsesDynamicDispatch ||
            !HasCharSpanToStringWrapperContext(callerSummary) ||
            !IsObjectToStringCall(calleeSymbol))
            return false;

        return HasObjectToStringBlockingChain(calleeClassification.FirstBlockingCallChain);
    }

    internal static bool HasCharSpanToStringWrapperContext(MethodEffectSummary summary) {
        if (!HasReturnType(summary.Identity, "named:System.String")) return false;

        return summary.Calls.Any(IsCharSpanReturningCall);
    }

    internal static bool IsCharSpanReturningCall(string callSymbol) => callSymbol.EndsWith(")->System.ReadOnlySpan`1<char>", StringComparison.Ordinal) ||
               callSymbol.EndsWith(")->System.Span`1<char>", StringComparison.Ordinal);

    internal static bool IsObjectToStringCall(string calleeSymbol) => string.Equals(calleeSymbol, "object.ToString()", StringComparison.Ordinal) ||
               string.Equals(calleeSymbol, "System.Object.ToString()", StringComparison.Ordinal);

    internal static bool HasObjectToStringBlockingChain(string[] blockingCallChain) => (blockingCallChain.Length == 1 &&
                string.Equals(blockingCallChain[0], "System.Object.GetType()", StringComparison.Ordinal)) ||
               (blockingCallChain.Length == 2 &&
                string.Equals(blockingCallChain[0], "System.Object.ToString()", StringComparison.Ordinal) &&
                string.Equals(blockingCallChain[1], "System.Object.GetType()", StringComparison.Ordinal));

    internal static bool HasCharSpanSearchContext(MethodEffectSummary summary) => summary.Symbol.Contains("System.ReadOnlySpan`1<char>", StringComparison.Ordinal) ||
               summary.Symbol.Contains("System.Span`1<char>", StringComparison.Ordinal);

    internal static bool IsEqualityBasedSpanSearchHelper(string calleeSymbol) {
        var methodBaseSymbol = GetMethodBaseSymbol(calleeSymbol);
        return string.Equals(methodBaseSymbol, "System.MemoryExtensions.Contains", StringComparison.Ordinal) ||
               string.Equals(methodBaseSymbol, "System.MemoryExtensions.IndexOf", StringComparison.Ordinal) ||
               string.Equals(methodBaseSymbol, "System.MemoryExtensions.IndexOfAny", StringComparison.Ordinal) ||
               string.Equals(methodBaseSymbol, "System.MemoryExtensions.LastIndexOf", StringComparison.Ordinal) ||
               string.Equals(methodBaseSymbol, "System.MemoryExtensions.LastIndexOfAny", StringComparison.Ordinal);
    }

    internal static bool HasEqualityBasedSpanSearchBlockingChain(string[] blockingCallChain) => blockingCallChain.Length >= 2 &&
               (blockingCallChain[0].StartsWith("System.SpanHelpers.Contains(", StringComparison.Ordinal) ||
                blockingCallChain[0].StartsWith("System.SpanHelpers.IndexOf(", StringComparison.Ordinal) ||
                blockingCallChain[0].StartsWith("System.SpanHelpers.IndexOfAny(", StringComparison.Ordinal) ||
                blockingCallChain[0].StartsWith("System.SpanHelpers.LastIndexOf(", StringComparison.Ordinal) ||
                blockingCallChain[0].StartsWith("System.SpanHelpers.LastIndexOfAny(", StringComparison.Ordinal)) &&
               string.Equals(blockingCallChain[1], "System.IEquatable`1.Equals(!0)", StringComparison.Ordinal);

    internal static bool ShouldIgnoreUnknownCall(
        MethodEffectSummary callerSummary,
        CallSiteSummary callSite,
        string calleeSymbol,
        MethodPurityClassification calleeClassification,
        string calleeKey,
        PurityClassificationContext context,
        bool treatsArgumentGuardThrowHelpersAsPure,
        bool treatsDelegateDispatchAsSemantic) => IsPureArgumentGuardWrapper(calleeSymbol) ||
               (treatsArgumentGuardThrowHelpersAsPure &&
                IsArgumentGuardThrowHelper(calleeSymbol)) ||
               (treatsDelegateDispatchAsSemantic &&
                IsSemanticallyNeutralValidationThrowHelper(calleeSymbol)) ||
               IsValidationThrowHelperCompatible(calleeKey, context) ||
               ShouldTreatCallAsSemanticallyPure(callerSummary, callSite, calleeSymbol, calleeClassification);

    internal static void AddImpureCalleeCategories(
        SortedSet<string> impureCategories,
        MethodPurityClassification calleeClassification) {
        foreach (var category in calleeClassification.Categories)
            if (string.Equals(category, "global_state_read", StringComparison.Ordinal) ||
                string.Equals(category, "global_state_write", StringComparison.Ordinal))
                impureCategories.Add(category);

        impureCategories.Add("impure_callee");
    }

    internal static bool TryClassifyRuntimeIntrinsicStub(
        MethodEffectSummary summary,
        out MethodPurityClassification classification) {
        classification = default!;
        if (string.IsNullOrWhiteSpace(summary.Symbol)) return false;

        if (IsPureRuntimeIntrinsicStub(summary.Symbol)) {
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
                StringComparison.Ordinal)) {
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

    internal static bool TryClassifyKnownBclSummary(
        MethodEffectSummary summary,
        out MethodPurityClassification classification) {
        classification = default!;
        var symbol = summary.Symbol;
        if (string.IsNullOrWhiteSpace(symbol)) return false;

        if (string.Equals(
                symbol,
                "System.Collections.ObjectModel.KeyedCollection`2.Contains(!0)",
                StringComparison.Ordinal)) {
            classification = new MethodPurityClassification(
                "conservative_unknown",
                new[] { "dynamic_dispatch" },
                new[] { "System.Collections.ObjectModel.KeyedCollection`2.GetKeyForItem(!1)" },
                summary.Effects.Contains("allocates_array", StringComparer.Ordinal),
                summary.Effects.Contains("allocates_object", StringComparer.Ordinal),
                true,
                "none",
                "unknown");
            return true;
        }

        if (TryGetKnownGeneratedPureVisibility(symbol, out var pureVisibility)) {
            classification = CreateGeneratedPureClassification(summary, pureVisibility);
            return true;
        }

        if (TryGetKnownGeneratedImpureCategories(symbol, out var impureCategories)) {
            classification = CreateGeneratedImpureClassification(summary, impureCategories);
            return true;
        }

        return false;
    }

    internal static bool TryClassifyKnownUnresolvedBclCall(
        string exactSymbol,
        out bool isPure,
        out string[] categories) {
        var displaySymbol = RemoveReturnTypeSuffix(exactSymbol);
        if (IsPureRuntimeIntrinsicStub(displaySymbol) ||
            TryGetKnownGeneratedPureVisibility(displaySymbol, out _)) {
            isPure = true;
            categories = Array.Empty<string>();
            return true;
        }

        if (TryGetKnownGeneratedImpureCategories(displaySymbol, out categories)) {
            isPure = false;
            return true;
        }

        isPure = false;
        categories = Array.Empty<string>();
        return false;
    }

    internal static string RemoveReturnTypeSuffix(string exactSymbol) {
        var returnSeparator = exactSymbol.IndexOf("->", StringComparison.Ordinal);
        return returnSeparator < 0 ? exactSymbol : exactSymbol.Substring(0, returnSeparator);
    }

    internal static MethodPurityClassification CreateGeneratedPureClassification(
        MethodEffectSummary summary,
        string effectVisibilityClassification) {
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

    internal static MethodPurityClassification CreateGeneratedImpureClassification(
        MethodEffectSummary summary,
        string[] categories) => new MethodPurityClassification(
            "impure",
            categories,
            Array.Empty<string>(),
            summary.Effects.Contains("allocates_array", StringComparer.Ordinal),
            summary.Effects.Contains("allocates_object", StringComparer.Ordinal),
            false,
            "none",
            "caller_visible");
}

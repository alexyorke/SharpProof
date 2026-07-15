internal static class EffectSummarySemanticWrapperRules
{
    private static readonly SemanticPureWrapperRule[] SemanticPureWrapperRules =
    [
        new(HasPureReadOnlyCharSpanSearchWrapperPattern, "none"),
        new(HasPureArrayBackedByRefLikeViewWrapperPattern, "none", TreatsByRefLikeViewWrapperAsPure: true),
        new(HasPureSpanBackedByRefLikeViewWrapperPattern, "none", TreatsByRefLikeViewWrapperAsPure: true),
        new(HasPureStringFromReadOnlyCharSpanWrapperPattern, "none"),
        new(HasPureStringSliceNormalizationWrapperPattern, "none"),
        new(HasPureInvariantTextInfoStringWrapperPattern, "internal_only"),
        new(HasPureTypeMetadataWrapperPattern, "none"),
        new(HasPureRuntimeTypeMetadataWrapperPattern, "none"),
        new(HasPureTypeIdentityWrapperPattern, "none"),
        new(HasPureCharScalarProjectionWrapperPattern, "none"),
        new(HasPureGuardedStringCharScanWrapperPattern, "none"),
        new(HasPureStringHashWrapperPattern, "none"),
        new(HasPureCharReplaceStringWrapperPattern, "internal_only"),
        new(HasPureStringSubstringWrapperPattern, "internal_only"),
        new(HasPureFreshAllocatedStringCopyCorePattern, "internal_only"),
        new(HasPureStringLengthCheckedConcatWrapperPattern, "internal_only"),
        new(HasPureStringArrayConcatWrapperPattern, "internal_only"),
        new(HasPureGuardedImmutableStringRewriteWrapperPattern, "none", GetGuardedRewriteVisibility),
        new(HasPureIndexedStringReplaceWrapperPattern, "internal_only"),
        new(HasPureStackLocalCharBuilderStringWrapperPattern, "internal_only"),
        new(HasPureImmutableStringRewriteWrapperPattern, "internal_only")
    ];

    internal static bool TryClassifySemanticPureWrapper(
        MethodEffectSummary summary,
        out MethodPurityClassification classification)
    {
        classification = default!;

        var rule = SemanticPureWrapperRules.FirstOrDefault(candidate => candidate.Predicate(summary));
        if (rule == null) return false;

        var effectVisibilityClassification = rule.VisibilitySelector?.Invoke(summary) ?? rule.Visibility;

        classification = new MethodPurityClassification(
            "pure",
            Array.Empty<string>(),
            Array.Empty<string>(),
            false,
            rule.TreatsByRefLikeViewWrapperAsPure
                ? false
                : summary.Effects.Contains("allocates_object", StringComparer.Ordinal),
            false,
            "none",
            effectVisibilityClassification);
        return true;
    }

    private static string GetGuardedRewriteVisibility(MethodEffectSummary summary)
    {
        return summary.RootCandidates.Contains("safe_static_cache_read", StringComparer.Ordinal) ||
               summary.RootCandidates.Contains("safe_static_constant_read", StringComparer.Ordinal)
            ? "internal_only"
            : "none";
    }

    private sealed record SemanticPureWrapperRule(
        Func<MethodEffectSummary, bool> Predicate,
        string Visibility,
        Func<MethodEffectSummary, string>? VisibilitySelector = null,
        bool TreatsByRefLikeViewWrapperAsPure = false);

    internal static bool IsPureRuntimeIntrinsicStub(string symbol)
    {
        return string.Equals(symbol, "object..ctor()", StringComparison.Ordinal) ||
               string.Equals(symbol, "System.Object..ctor()", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Runtime.CompilerServices.Unsafe.As(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Runtime.CompilerServices.Unsafe.AsPointer(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Runtime.CompilerServices.Unsafe.AsRef(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Runtime.CompilerServices.Unsafe.ReadUnaligned(", StringComparison.Ordinal) ||
               string.Equals(symbol, "System.Runtime.CompilerServices.Unsafe.SizeOf()", StringComparison.Ordinal) ||
               IsFastAllocateString(symbol);
    }

    internal static bool IsFastAllocateString(string symbol)
    {
        return string.Equals(symbol, "string.FastAllocateString(int)", StringComparison.Ordinal) ||
               string.Equals(symbol, "System.String.FastAllocateString(int)", StringComparison.Ordinal);
    }

    internal static bool HasPureReadOnlyCharSpanSearchWrapperPattern(MethodEffectSummary summary)
    {
        return CallsOnly(summary, "calls_method") &&
               RootsAreSemanticallyPureWrapperCompatible(summary) &&
               summary.Calls.Any(static call =>
                   call.Contains("System.ReadOnlySpan`1<char>", StringComparison.Ordinal)) &&
               summary.Calls.Any(IsReadOnlyCharSpanSearchHelperCall) &&
               summary.Calls.All(IsReadOnlyCharSpanSearchHelperCall);
    }

    internal static bool HasPureArrayBackedByRefLikeViewWrapperPattern(MethodEffectSummary summary)
    {
        return CallsOnly(summary, "allocates_object", "calls_method", "writes_indirect_memory") &&
               RootsAreArrayBackedByRefLikeViewWrapperCompatible(summary) &&
               IsByRefLikeViewReturn(summary.Identity) &&
               summary.Calls.Any(IsArrayBackedByRefLikeViewConstructionCall) &&
               summary.Calls.All(IsArrayBackedByRefLikeViewWrapperCall);
    }

    internal static bool HasPureSpanBackedByRefLikeViewWrapperPattern(MethodEffectSummary summary)
    {
        return CallsOnly(summary, "allocates_object", "calls_method", "reads_instance_field") &&
               RootsAreSemanticallyPureWrapperCompatible(summary) &&
               IsByRefLikeViewReturn(summary.Identity) &&
               HasOnlyByRefLikeViewProjectionFieldReads(summary) &&
               summary.Calls.Any(IsByRefLikeViewConstructionCall) &&
               summary.Calls.All(IsSpanBackedByRefLikeViewWrapperCall);
    }

    internal static bool HasPureStringFromReadOnlyCharSpanWrapperPattern(MethodEffectSummary summary)
    {
        return CallsOnly(summary, "calls_method") &&
               RootsAreSemanticallyPureWrapperCompatible(summary) &&
               summary.Calls.Any(IsStringToReadOnlyCharSpanWrapperCall) &&
               summary.Calls.Any(static call =>
                   string.Equals(call, "object.ToString()->string", StringComparison.Ordinal)) &&
               summary.Calls.All(IsStringFromReadOnlyCharSpanWrapperCall);
    }

    internal static bool HasPureStringSliceNormalizationWrapperPattern(MethodEffectSummary summary)
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

    internal static bool HasPureInvariantTextInfoStringWrapperPattern(MethodEffectSummary summary)
    {
        return CallsOnly(summary, "calls_method", "reads_static_field") &&
               RootsAreSemanticallyPureWrapperCompatible(summary) &&
               summary.Fields.Length == 1 &&
               string.Equals(summary.Fields[0], "System.Globalization.TextInfo.Invariant", StringComparison.Ordinal) &&
               summary.Calls.Any(IsInvariantTextInfoStringWrapperCall) &&
               summary.Calls.All(IsInvariantTextInfoStringWrapperCall);
    }

    internal static bool HasPureTypeMetadataWrapperPattern(MethodEffectSummary summary)
    {
        if (!HasFieldlessDynamicDispatchWrapperShape(summary) ||
            !IsParameterlessProperty(summary.Identity, "System.Type"))
            return false;

        var callSites = EnumerateCallSites(summary).ToArray();
        if (summary.Identity.Name == "Attributes")
            return CallSitesMatch(
                callSites,
                ("System.Type.GetAttributeFlagsImpl()->System.Reflection.TypeAttributes", true));

        if (!string.Equals(summary.Identity.ReturnType, "named:System.Boolean", StringComparison.Ordinal))
            return false;

        if (IsPureTypeAttributeFlagsWrapperMethod(summary.Identity.Name))
            return CallSitesMatch(
                callSites,
                ("System.Type.GetAttributeFlagsImpl()->System.Reflection.TypeAttributes", true));

        if (TryGetPureTypeSingleImplWrapperCall(summary.Identity.Name, out var implCall))
            return CallSitesMatch(callSites, (implCall, true));

        return summary.Identity.Name switch
        {
            "IsClass" => CallSitesMatch(
                callSites,
                ("System.Type.GetAttributeFlagsImpl()->System.Reflection.TypeAttributes", true),
                ("System.Type.get_IsValueType()->bool", false)),
            "IsNested" => CallSitesMatch(
                callSites,
                ("System.Reflection.MemberInfo.get_DeclaringType()->System.Type", true),
                ("System.Type.op_Inequality(System.Type, System.Type)->bool", false)),
            "IsInterface" => CallSitesMatch(
                callSites,
                ("System.RuntimeTypeHandle.IsInterface(System.RuntimeType)->bool", false),
                ("System.Type.GetAttributeFlagsImpl()->System.Reflection.TypeAttributes", true)),
            _ => false
        };
    }

    internal static bool HasPureRuntimeTypeMetadataWrapperPattern(MethodEffectSummary summary)
    {
        if (!IsParameterlessProperty(summary.Identity, "System.RuntimeType")) return false;

        var callSites = EnumerateCallSites(summary).ToArray();
        return summary.Identity.Name switch
        {
            "ContainsGenericParameters" =>
                HasFieldlessDynamicDispatchWrapperShape(summary) &&
                CallSitesMatch(
                    callSites,
                    ("System.RuntimeTypeHandle.ContainsGenericVariables()->bool", false),
                    ("System.Type.GetRootElementType()->System.Type", false),
                    ("System.Type.get_TypeHandle()->System.RuntimeTypeHandle", true)),
            "IsEnum" =>
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

    private static bool IsParameterlessProperty(StructuralMethodIdentity identity, string containingType)
    {
        return string.Equals(identity.ContainingMetadataType, containingType, StringComparison.Ordinal) &&
               string.Equals(identity.MethodKind, "property-get", StringComparison.Ordinal) &&
               identity.GenericArity == 0 &&
               identity.Parameters.Length == 0;
    }

    internal static bool HasPureTypeIdentityWrapperPattern(MethodEffectSummary summary)
    {
        return HasFieldlessDynamicDispatchWrapperShape(summary) &&
               IsTypeIdentityWrapperMethod(summary.Symbol) &&
               summary.Calls.Any(IsTypeIdentityWrapperAnchorCall) &&
               summary.Calls.All(IsTypeIdentityWrapperCall);
    }

    internal static bool HasPureStringHashWrapperPattern(MethodEffectSummary summary)
    {
        return HasReturnType(summary.Identity, "named:System.Int32") &&
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

    internal static bool HasPureStackLocalCharBuilderStringWrapperPattern(MethodEffectSummary summary)
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

    internal static bool HasPureImmutableStringRewriteWrapperPattern(MethodEffectSummary summary)
    {
        return CallsOnly(summary, "calls_method", "reads_static_field") &&
               RootsAreSemanticallyPureWrapperCompatible(summary) &&
               summary.Calls.Any(static call =>
                   call.StartsWith("string.Concat(System.ReadOnlySpan`1<char>", StringComparison.Ordinal)) &&
               summary.Calls.All(IsImmutableStringRewriteWrapperCall);
    }

    internal static bool HasPureStringSubstringWrapperPattern(MethodEffectSummary summary)
    {
        return CallsOnly(summary, "calls_method", "reads_static_field") &&
               RootsAreSemanticallyPureWrapperCompatible(summary) &&
               summary.Calls.Any(static call =>
                   string.Equals(call, "string.InternalSubString(int, int)->string", StringComparison.Ordinal)) &&
               summary.Calls.Any(static call => string.Equals(call,
                   "string.ThrowSubstringArgumentOutOfRange(int, int)->void", StringComparison.Ordinal)) &&
               summary.Calls.All(IsStringSubstringWrapperCall);
    }

    internal static bool HasPureCharReplaceStringWrapperPattern(MethodEffectSummary summary)
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

    internal static bool HasPureFreshAllocatedStringCopyCorePattern(MethodEffectSummary summary)
    {
        return CallsOnly(summary, "calls_method", "reads_instance_field") &&
               RootsAreSemanticallyPureWrapperCompatible(summary) &&
               summary.Calls.Any(IsFastAllocateStringCall) &&
               summary.Calls.Any(IsBufferMemmoveCall) &&
               summary.Calls.All(IsFreshAllocatedStringCopyCoreCall) &&
               summary.Fields.All(static field =>
                   string.Equals(field, "System.String._firstChar", StringComparison.Ordinal));
    }

    internal static bool HasPureStringLengthCheckedConcatWrapperPattern(MethodEffectSummary summary)
    {
        return CallsOnly(summary, "calls_method", "reads_static_field") &&
               RootsAreSemanticallyPureWrapperCompatible(summary) &&
               summary.Calls.Any(IsFastAllocateStringCall) &&
               summary.Calls.Any(static call => string.Equals(call,
                   "string.CopyStringContent(string, int, string)->void", StringComparison.Ordinal)) &&
               summary.Calls.All(IsStringLengthCheckedConcatWrapperCall);
    }

    internal static bool HasPureStringArrayConcatWrapperPattern(MethodEffectSummary summary)
    {
        return CallsOnly(summary, "calls_method", "reads_static_field") &&
               RootsAreSemanticallyPureWrapperCompatible(summary) &&
               summary.Calls.Any(IsFastAllocateStringCall) &&
               summary.Calls.Any(static call =>
                   string.Equals(call, "System.Array.Clone()->object", StringComparison.Ordinal)) &&
               summary.Calls.All(IsStringArrayConcatWrapperCall);
    }

    internal static bool HasPureCharScalarProjectionWrapperPattern(MethodEffectSummary summary)
    {
        if (!IsCharScalarProjectionIdentity(summary.Identity) ||
            summary.Fields.Length != 0 ||
            !CallsOnly(summary, "calls_method") ||
            !RootsAreSemanticallyPureWrapperCompatible(summary))
            return false;

        var callSites = EnumerateCallSites(summary).ToArray();
        return callSites.Length != 0 &&
               callSites.All(static callSite =>
                   !callSite.UsesDynamicDispatch &&
                   IsCharScalarProjectionCall(callSite.DisplayName));
    }

    internal static bool HasPureGuardedStringCharScanWrapperPattern(MethodEffectSummary summary)
    {
        if (!HasReturnType(summary.Identity, "named:System.Boolean") ||
            summary.Fields.Length != 0 ||
            !CallsOnly(summary, "calls_method") ||
            !RootsAreSemanticallyPureWrapperCompatible(summary))
            return false;

        var callSites = EnumerateCallSites(summary).ToArray();
        return callSites.Any(static callSite => IsStringLengthCall(callSite.DisplayName)) &&
               callSites.Any(static callSite => IsStringGetCharsCall(callSite.DisplayName)) &&
               callSites.Any(static callSite => IsCharScalarProjectionCall(callSite.DisplayName)) &&
               callSites.All(static callSite =>
                   !callSite.UsesDynamicDispatch &&
                   (IsStringLengthCall(callSite.DisplayName) ||
                    IsStringGetCharsCall(callSite.DisplayName) ||
                    IsCharScalarProjectionCall(callSite.DisplayName)));
    }

    internal static bool HasPureGuardedImmutableStringRewriteWrapperPattern(MethodEffectSummary summary)
    {
        return HasReturnType(summary.Identity, "named:System.String") &&
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

    internal static bool HasPureIndexedStringReplaceWrapperPattern(MethodEffectSummary summary)
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

    internal static bool RootsAreSemanticallyPureWrapperCompatible(MethodEffectSummary summary)
    {
        return summary.RootCandidates.All(static root =>
            string.Equals(root, "safe_static_cache_read", StringComparison.Ordinal) ||
            string.Equals(root, "safe_static_constant_read", StringComparison.Ordinal));
    }

    internal static bool IsCharScalarProjectionCall(string displayName)
    {
        return IsCharScalarProjectionSymbol(displayName) ||
               IsCharScalarTableProjectionCall(displayName) ||
               IsScalarValueHelperCall(displayName, "System.Globalization.CharUnicodeInfo") ||
               IsScalarValueHelperCall(displayName, "System.Globalization.TextInfo");
    }

    internal static bool IsCharScalarTableProjectionCall(string displayName)
    {
        return string.Equals(
                   displayName,
                   "System.ReadOnlySpan`1<byte>.get_Item(int)->ref !0",
                   StringComparison.Ordinal) ||
               ((displayName.StartsWith("char.get_", StringComparison.Ordinal) ||
                 displayName.StartsWith("System.Char.get_", StringComparison.Ordinal)) &&
                displayName.EndsWith(")->System.ReadOnlySpan`1<byte>", StringComparison.Ordinal));
    }

    internal static bool IsStringLengthCall(string displayName)
    {
        return string.Equals(displayName, "string.get_Length()->int", StringComparison.Ordinal) ||
               string.Equals(displayName, "System.String.get_Length()->int", StringComparison.Ordinal);
    }

    internal static bool IsStringGetCharsCall(string displayName)
    {
        return string.Equals(displayName, "string.get_Chars(int)->char", StringComparison.Ordinal) ||
               string.Equals(displayName, "System.String.get_Chars(int)->char", StringComparison.Ordinal);
    }

    internal static bool IsCharScalarProjectionSymbol(string displayName)
    {
        return (IsScalarValueHelperCall(displayName, "char") ||
                IsScalarValueHelperCall(displayName, "System.Char")) &&
               HasOnlyCharScalarArguments(displayName);
    }

    internal static bool IsCharScalarProjectionIdentity(StructuralMethodIdentity identity)
    {
        if (!string.Equals(identity.ContainingMetadataType, "System.Char", StringComparison.Ordinal) ||
            !IsScalarStructuralReturnType(identity.ReturnType))
            return false;

        return identity.Parameters.All(static parameter =>
            parameter.Type is "named:System.Char" or "named:System.Int32" or "named:System.UInt32");
    }

    internal static bool IsScalarStructuralReturnType(string returnType)
    {
        return returnType is
            "named:System.Boolean" or
            "named:System.Byte" or
            "named:System.Char" or
            "named:System.Double" or
            "named:System.Int32" or
            "named:System.UInt32" or
            "named:System.Globalization.UnicodeCategory";
    }

    internal static bool IsScalarValueHelperCall(string displayName, string declaringType)
    {
        var openParenIndex = displayName.IndexOf('(');
        if (openParenIndex <= declaringType.Length ||
            !displayName.StartsWith(declaringType + ".", StringComparison.Ordinal))
            return false;

        var returnSeparatorIndex = displayName.LastIndexOf(")->", StringComparison.Ordinal);
        return returnSeparatorIndex >= 0 &&
               IsScalarValueReturnType(displayName.Substring(returnSeparatorIndex + 3));
    }

    internal static bool IsScalarValueReturnType(string returnType)
    {
        return string.Equals(returnType, "bool", StringComparison.Ordinal) ||
               string.Equals(returnType, "byte", StringComparison.Ordinal) ||
               string.Equals(returnType, "char", StringComparison.Ordinal) ||
               string.Equals(returnType, "double", StringComparison.Ordinal) ||
               string.Equals(returnType, "int", StringComparison.Ordinal) ||
               string.Equals(returnType, "uint", StringComparison.Ordinal) ||
               string.Equals(returnType, "System.Globalization.UnicodeCategory", StringComparison.Ordinal);
    }

    internal static bool HasOnlyCharScalarArguments(string displayName)
    {
        var openParenIndex = displayName.IndexOf('(');
        var returnSeparatorIndex = displayName.LastIndexOf(")->", StringComparison.Ordinal);
        if (openParenIndex < 0 || returnSeparatorIndex < openParenIndex) return false;

        var argumentList = displayName.Substring(openParenIndex + 1, returnSeparatorIndex - openParenIndex - 1);
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

    internal static bool RootsAreArrayBackedByRefLikeViewWrapperCompatible(MethodEffectSummary summary)
    {
        return summary.RootCandidates.All(static root =>
            string.Equals(root, "caller_visible_memory_write", StringComparison.Ordinal) ||
            string.Equals(root, "safe_static_cache_read", StringComparison.Ordinal) ||
            string.Equals(root, "safe_static_constant_read", StringComparison.Ordinal));
    }

    internal static bool CallsOnly(MethodEffectSummary summary, params string[] allowedEffects)
    {
        return summary.Effects.All(effect => allowedEffects.Contains(effect, StringComparer.Ordinal));
    }

    private static bool HasFieldlessDynamicDispatchWrapperShape(MethodEffectSummary summary)
    {
        return summary.Fields.Length == 0 &&
               CallsOnly(summary, "calls_method", "virtual_call") &&
               summary.RootCandidates.All(static root =>
                   string.Equals(root, "dynamic_dispatch", StringComparison.Ordinal));
    }

    internal static bool IsByRefLikeViewReturn(StructuralMethodIdentity identity)
    {
        return identity.ReturnType.StartsWith("named:System.Span`1[", StringComparison.Ordinal) ||
               identity.ReturnType.StartsWith("named:System.ReadOnlySpan`1[", StringComparison.Ordinal);
    }

    internal static bool IsArrayBackedByRefLikeViewConstructionCall(string callSymbol)
    {
        return (callSymbol.StartsWith("System.Span`1<", StringComparison.Ordinal) &&
                (callSymbol.Contains("..ctor(!0[])", StringComparison.Ordinal) ||
                 callSymbol.Contains("..ctor(ref !0, int)", StringComparison.Ordinal))) ||
               (callSymbol.StartsWith("System.ReadOnlySpan`1<", StringComparison.Ordinal) &&
                (callSymbol.Contains("..ctor(!0[])", StringComparison.Ordinal) ||
                 callSymbol.Contains("..ctor(ref !0, int)", StringComparison.Ordinal)));
    }

    internal static bool IsArrayBackedByRefLikeViewWrapperCall(string callSymbol)
    {
        return IsArrayBackedByRefLikeViewConstructionCall(callSymbol) ||
               callSymbol.StartsWith("System.Runtime.CompilerServices.Unsafe.Add(ref ", StringComparison.Ordinal) ||
               EffectSummaryKnownFrameworkCalls.IsArrayDataReference(callSymbol) ||
               callSymbol.StartsWith("System.ThrowHelper.ThrowArgumentOutOfRangeException()",
                   StringComparison.Ordinal) ||
               EffectSummaryKnownFrameworkCalls.IsByRefLikeRuntimeTypeHelper(callSymbol) ||
               callSymbol.StartsWith("string.get_Length()", StringComparison.Ordinal);
    }

    internal static bool IsSpanBackedByRefLikeViewWrapperCall(string callSymbol)
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

    internal static bool HasOnlyByRefLikeViewProjectionFieldReads(MethodEffectSummary summary)
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

    private static readonly SemanticCallRule[] SemanticCallRules =
    [
        Prefix(SemanticCallFamily.ReadOnlyCharSpanSearch, "System.IO.Path.GetDirectoryNameOffset(System.ReadOnlySpan`1<char>)"),
        Prefix(SemanticCallFamily.ReadOnlyCharSpanSearch, "System.IO.Path.GetExtension(System.ReadOnlySpan`1<char>)"),
        Prefix(SemanticCallFamily.ReadOnlyCharSpanSearch, "System.IO.Path.GetFileName(System.ReadOnlySpan`1<char>)"),
        Prefix(SemanticCallFamily.ReadOnlyCharSpanSearch, "System.IO.Path.GetFileNameWithoutExtension(System.ReadOnlySpan`1<char>)"),
        Prefix(SemanticCallFamily.ReadOnlyCharSpanSearch, "System.IO.Path.GetPathRoot(System.ReadOnlySpan`1<char>)"),
        Prefix(SemanticCallFamily.ReadOnlyCharSpanSearch, "System.IO.PathInternal.GetRootLength(System.ReadOnlySpan`1<char>)"),
        Prefix(SemanticCallFamily.ReadOnlyCharSpanSearch, "System.IO.PathInternal.IsDirectorySeparator(char)"),
        Prefix(SemanticCallFamily.ReadOnlyCharSpanSearch, "System.IO.PathInternal.IsEffectivelyEmpty(System.ReadOnlySpan`1<char>)"),
        Prefix(SemanticCallFamily.ReadOnlyCharSpanSearch, "System.MemoryExtensions.IndexOf(System.ReadOnlySpan`1<"),
        Prefix(SemanticCallFamily.ReadOnlyCharSpanSearch, "System.MemoryExtensions.IndexOfAny(System.ReadOnlySpan`1<"),
        Prefix(SemanticCallFamily.ReadOnlyCharSpanSearch, "System.MemoryExtensions.LastIndexOf(System.ReadOnlySpan`1<"),
        Prefix(SemanticCallFamily.ReadOnlyCharSpanSearch, "System.MemoryExtensions.LastIndexOfAny(System.ReadOnlySpan`1<"),
        Prefix(SemanticCallFamily.ReadOnlyCharSpanSearch, "System.ReadOnlySpan`1<char>.Slice("),
        Prefix(SemanticCallFamily.ReadOnlyCharSpanSearch, "System.ReadOnlySpan`1<char>.get_Empty()"),
        Prefix(SemanticCallFamily.ReadOnlyCharSpanSearch, "System.ReadOnlySpan`1<char>.get_Item(int)"),
        Prefix(SemanticCallFamily.ReadOnlyCharSpanSearch, "System.ReadOnlySpan`1<char>.get_Length()"),
        Prefix(SemanticCallFamily.StringToReadOnlyCharSpan, "System.MemoryExtensions.AsSpan(string"),
        Prefix(SemanticCallFamily.StringToReadOnlyCharSpan, "string.op_Implicit(string)->System.ReadOnlySpan`1<char>"),
        Exact(SemanticCallFamily.StringFromReadOnlyCharSpan, "object.ToString()->string"),
        Exact(SemanticCallFamily.StringFromReadOnlyCharSpan, "string.get_Length()->int"),
        Prefix(SemanticCallFamily.StringSliceNormalization, "System.IO.PathInternal.NormalizeDirectorySeparators(string)"),
        Exact(SemanticCallFamily.StringSliceNormalization, "string.Substring(int, int)->string"),
        Prefix(SemanticCallFamily.StackLocalCharBuilder, "System.IO.PathInternal.IsDirectorySeparator(char)"),
        Prefix(SemanticCallFamily.StackLocalCharBuilder, "System.Span`1<char>..ctor("),
        Prefix(SemanticCallFamily.StackLocalCharBuilder, "System.Text.ValueStringBuilder..ctor(System.Span`1<char>)"),
        Prefix(SemanticCallFamily.StackLocalCharBuilder, "System.Text.ValueStringBuilder.Append(char)"),
        Exact(SemanticCallFamily.StackLocalCharBuilder, "object.ToString()->string"),
        Exact(SemanticCallFamily.StackLocalCharBuilder, "string.IsNullOrEmpty(string)->bool"),
        Exact(SemanticCallFamily.StackLocalCharBuilder, "string.get_Chars(int)->char"),
        Exact(SemanticCallFamily.StackLocalCharBuilder, "string.get_Length()->int"),
        Prefix(SemanticCallFamily.ImmutableStringRewrite, "System.IO.PathInternal.IsDirectorySeparator(char)"),
        Prefix(SemanticCallFamily.ImmutableStringRewrite, "System.MemoryExtensions.AsSpan(string"),
        Prefix(SemanticCallFamily.ImmutableStringRewrite, "string.Concat(System.ReadOnlySpan`1<char>"),
        Prefix(SemanticCallFamily.ImmutableStringRewrite, "string.StartsWith(char)->bool"),
        Prefix(SemanticCallFamily.ImmutableStringRewrite, "string.Substring(int, int)->string"),
        Exact(SemanticCallFamily.ImmutableStringRewrite, "string.get_Chars(int)->char"),
        Exact(SemanticCallFamily.ImmutableStringRewrite, "string.get_Length()->int"),
        Exact(SemanticCallFamily.ImmutableStringRewrite, "string.op_Implicit(string)->System.ReadOnlySpan`1<char>"),
        Exact(SemanticCallFamily.InvariantTextInfo, "System.Globalization.TextInfo.ToLower(string)->string"),
        Exact(SemanticCallFamily.InvariantTextInfo, "System.Globalization.TextInfo.ToUpper(string)->string"),
        Exact(SemanticCallFamily.TypeIdentity | SemanticCallFamily.TypeIdentityAnchor, "System.Type.Equals(System.Type)->bool"),
        Exact(SemanticCallFamily.TypeIdentity | SemanticCallFamily.TypeIdentityAnchor, "System.Type.get_UnderlyingSystemType()->System.Type"),
        Exact(SemanticCallFamily.TypeIdentity | SemanticCallFamily.TypeIdentityAnchor, "System.Reflection.MemberInfo.GetHashCode()->int"),
        Exact(SemanticCallFamily.TypeIdentity | SemanticCallFamily.TypeIdentityAnchor, "object.GetHashCode()->int"),
        Exact(SemanticCallFamily.TypeIdentity, "System.Type.op_Equality(System.Type, System.Type)->bool"),
        Exact(SemanticCallFamily.StringHash, "System.Marvin.ComputeHash32(ref byte, uint, uint, uint)->int"),
        Exact(SemanticCallFamily.StringHash, "System.Marvin.get_DefaultSeed()->ulong"),
        Exact(SemanticCallFamily.StringHash, "System.Runtime.CompilerServices.Unsafe.As(ref !!0)->ref !!1"),
        Exact(SemanticCallFamily.StringSubstring, "string.InternalSubString(int, int)->string"),
        Exact(SemanticCallFamily.StringSubstring, "string.ThrowSubstringArgumentOutOfRange(int, int)->void"),
        Exact(SemanticCallFamily.StringSubstring, "string.get_Length()->int"),
        Exact(SemanticCallFamily.FreshAllocatedStringCopy, "System.Runtime.CompilerServices.Unsafe.Add(ref !!0, nint)->ref !!0"),
        Exact(SemanticCallFamily.CharReplaceString, "System.Runtime.CompilerServices.Unsafe.Add(ref !!0, nuint)->ref !!0"),
        Exact(SemanticCallFamily.CharReplaceString, "System.Runtime.CompilerServices.Unsafe.Subtract(ref !!0, nuint)->ref !!0"),
        Exact(SemanticCallFamily.CharReplaceString, "System.Runtime.Intrinsics.Vector128.get_IsHardwareAccelerated()->bool"),
        Exact(SemanticCallFamily.CharReplaceString, "System.Runtime.Intrinsics.Vector128`1<ushort>.get_Count()->int"),
        Exact(SemanticCallFamily.CharReplaceString, "System.SpanHelpers.ReplaceValueType(ref !!0, ref !!0, !!0, !!0, nuint)->void"),
        Exact(SemanticCallFamily.CharReplaceString, "string.GetRawStringDataAsUInt16()->ref ushort"),
        Exact(SemanticCallFamily.CharReplaceString, "string.IndexOf(char)->int"),
        Exact(SemanticCallFamily.CharReplaceString, "string.get_Length()->int"),
        Exact(SemanticCallFamily.StringLengthCheckedConcat, "System.ThrowHelper.ThrowOutOfMemoryException_StringTooLong()->void"),
        Exact(SemanticCallFamily.StringLengthCheckedConcat, "string.CopyStringContent(string, int, string)->void"),
        Exact(SemanticCallFamily.StringLengthCheckedConcat, "string.IsNullOrEmpty(string)->bool"),
        Exact(SemanticCallFamily.StringLengthCheckedConcat, "string.get_Length()->int"),
        Exact(SemanticCallFamily.StringArrayConcat, "System.ArgumentNullException.ThrowIfNull(object, string)->void"),
        Exact(SemanticCallFamily.StringArrayConcat, "System.Array.Clone()->object"),
        Exact(SemanticCallFamily.StringArrayConcat, "string.Concat(string[])->string"),
        Prefix(SemanticCallFamily.GuardedImmutableStringRewrite, "System.Span`1<char>.Fill("),
        Exact(SemanticCallFamily.GuardedImmutableStringRewrite, "System.Runtime.CompilerServices.Unsafe.Add(ref !!0, int)->ref !!0"),
        Exact(SemanticCallFamily.GuardedImmutableStringRewrite, "System.Span`1<char>..ctor(ref !0, int)->void"),
        Exact(SemanticCallFamily.GuardedImmutableStringRewrite, "string.CopyStringContent(string, int, string)->void"),
        Exact(SemanticCallFamily.GuardedImmutableStringRewrite, "string.get_Chars(int)->char"),
        Exact(SemanticCallFamily.GuardedImmutableStringRewrite, "string.get_Length()->int"),
        Prefix(SemanticCallFamily.IndexedStringReplace, "System.PackedSpanHelpers.CanUsePackedIndexOf("),
        Prefix(SemanticCallFamily.IndexedStringReplace, "System.PackedSpanHelpers.IndexOf("),
        Prefix(SemanticCallFamily.IndexedStringReplace, "System.PackedSpanHelpers.get_PackedIndexOfIsSupported()"),
        Prefix(SemanticCallFamily.IndexedStringReplace, "System.SpanHelpers.IndexOf("),
        Prefix(SemanticCallFamily.IndexedStringReplace, "System.SpanHelpers.NonPackedIndexOfChar("),
        Exact(SemanticCallFamily.IndexedStringReplace, "System.Runtime.CompilerServices.Unsafe.Add(ref !!0, int)->ref !!0"),
        Exact(SemanticCallFamily.IndexedStringReplace, "System.Span`1<int>..ctor(void*, int)->void"),
        Exact(SemanticCallFamily.IndexedStringReplace, "string.Replace(char, char)->string"),
        Exact(SemanticCallFamily.IndexedStringReplace, "string.ReplaceHelper(int, string, System.ReadOnlySpan`1<int>)->string"),
        Exact(SemanticCallFamily.IndexedStringReplace, "string.get_Chars(int)->char"),
        Exact(SemanticCallFamily.IndexedStringReplace, "string.get_Length()->int"),
        Exact(SemanticCallFamily.FastAllocateString, "string.FastAllocateString(int)->string"),
        Exact(SemanticCallFamily.FastAllocateString, "System.String.FastAllocateString(int)->string"),
        Exact(SemanticCallFamily.BufferMemmove, "System.Buffer.Memmove(ref !!0, ref !!0, nuint)->void"),
        Exact(SemanticCallFamily.BufferMemmove, "System.Buffer.Memmove(ref byte, ref byte, nuint)->void")
    ];

    [Flags]
    private enum SemanticCallFamily
    {
        ReadOnlyCharSpanSearch = 1 << 0,
        StringToReadOnlyCharSpan = 1 << 1,
        StringFromReadOnlyCharSpan = 1 << 2,
        StringSliceNormalization = 1 << 3,
        StackLocalCharBuilder = 1 << 4,
        ImmutableStringRewrite = 1 << 5,
        InvariantTextInfo = 1 << 6,
        TypeIdentity = 1 << 7,
        TypeIdentityAnchor = 1 << 8,
        StringHash = 1 << 9,
        StringSubstring = 1 << 10,
        FreshAllocatedStringCopy = 1 << 11,
        CharReplaceString = 1 << 12,
        StringLengthCheckedConcat = 1 << 13,
        StringArrayConcat = 1 << 14,
        GuardedImmutableStringRewrite = 1 << 15,
        IndexedStringReplace = 1 << 16,
        FastAllocateString = 1 << 17,
        BufferMemmove = 1 << 18
    }

    private readonly record struct SemanticCallRule(
        SemanticCallFamily Families,
        string Pattern,
        bool IsPrefix)
    {
        internal bool Matches(string call) => IsPrefix
            ? call.StartsWith(Pattern, StringComparison.Ordinal)
            : string.Equals(call, Pattern, StringComparison.Ordinal);
    }

    private static SemanticCallRule Exact(SemanticCallFamily family, string pattern) =>
        new(family, pattern, false);

    private static SemanticCallRule Prefix(SemanticCallFamily family, string pattern) =>
        new(family, pattern, true);

    private static bool MatchesSemanticCall(string call, SemanticCallFamily family) =>
        SemanticCallRules.Any(rule => (rule.Families & family) != 0 && rule.Matches(call));

    internal static bool IsReadOnlyCharSpanSearchHelperCall(string callSymbol) =>
        MatchesSemanticCall(callSymbol, SemanticCallFamily.ReadOnlyCharSpanSearch);

    internal static bool IsStringToReadOnlyCharSpanWrapperCall(string callSymbol) =>
        MatchesSemanticCall(callSymbol, SemanticCallFamily.StringToReadOnlyCharSpan);

    internal static bool IsStringFromReadOnlyCharSpanWrapperCall(string callSymbol) =>
        IsStringToReadOnlyCharSpanWrapperCall(callSymbol) ||
        IsReadOnlyCharSpanSearchHelperCall(callSymbol) ||
        MatchesSemanticCall(callSymbol, SemanticCallFamily.StringFromReadOnlyCharSpan);

    internal static bool IsStringSliceNormalizationWrapperCall(string callSymbol) =>
        IsStringToReadOnlyCharSpanWrapperCall(callSymbol) ||
        IsReadOnlyCharSpanSearchHelperCall(callSymbol) ||
        MatchesSemanticCall(callSymbol, SemanticCallFamily.StringSliceNormalization);

    internal static bool IsStackLocalCharBuilderStringWrapperCall(string callSymbol) =>
        MatchesSemanticCall(callSymbol, SemanticCallFamily.StackLocalCharBuilder);

    internal static bool IsImmutableStringRewriteWrapperCall(string callSymbol) =>
        MatchesSemanticCall(callSymbol, SemanticCallFamily.ImmutableStringRewrite);

    internal static bool IsInvariantTextInfoStringWrapperCall(string callSymbol) =>
        MatchesSemanticCall(callSymbol, SemanticCallFamily.InvariantTextInfo);

    internal static bool CallSitesMatch(
        IReadOnlyList<CallSiteSummary> actual,
        params (string DisplayName, bool UsesDynamicDispatch)[] expected)
    {
        if (actual.Count != expected.Length) return false;

        foreach (var expectedCallSite in expected)
            if (actual.Count(callSite =>
                    callSite.UsesDynamicDispatch == expectedCallSite.UsesDynamicDispatch &&
                    string.Equals(callSite.DisplayName, expectedCallSite.DisplayName,
                        StringComparison.Ordinal)) != 1)
                return false;

        return true;
    }

    internal static bool IsPureTypeAttributeFlagsWrapperMethod(string propertyName)
    {
        return propertyName is
            "IsAbstract" or
            "IsAnsiClass" or
            "IsAutoClass" or
            "IsAutoLayout" or
            "IsExplicitLayout" or
            "IsImport" or
            "IsLayoutSequential" or
            "IsNestedAssembly" or
            "IsNestedFamANDAssem" or
            "IsNestedFamily" or
            "IsNestedFamORAssem" or
            "IsNestedPrivate" or
            "IsNestedPublic" or
            "IsNotPublic" or
            "IsPublic" or
            "IsSealed" or
            "IsSpecialName" or
            "IsUnicodeClass";
    }

    internal static bool TryGetPureTypeSingleImplWrapperCall(string propertyName, out string implCall)
    {
        implCall = propertyName switch
        {
            "IsArray" => "System.Type.IsArrayImpl()->bool",
            "IsByRef" => "System.Type.IsByRefImpl()->bool",
            "IsCOMObject" => "System.Type.IsCOMObjectImpl()->bool",
            "IsPointer" => "System.Type.IsPointerImpl()->bool",
            "IsPrimitive" => "System.Type.IsPrimitiveImpl()->bool",
            "IsValueType" => "System.Type.IsValueTypeImpl()->bool",
            _ => string.Empty
        };

        return implCall.Length != 0;
    }

    internal static bool IsTypeIdentityWrapperMethod(string symbol)
    {
        return symbol is
            "System.Type.Equals(System.Type)" or
            "System.Type.Equals(object)" or
            "System.Type.GetHashCode()";
    }

    internal static bool IsTypeIdentityWrapperAnchorCall(string callSymbol) =>
        MatchesSemanticCall(callSymbol, SemanticCallFamily.TypeIdentityAnchor);

    internal static bool IsTypeIdentityWrapperCall(string callSymbol) =>
        MatchesSemanticCall(callSymbol, SemanticCallFamily.TypeIdentity);

    internal static bool IsStringHashWrapperCall(string callSymbol) =>
        MatchesSemanticCall(callSymbol, SemanticCallFamily.StringHash);

    internal static bool IsStringSubstringWrapperCall(string callSymbol) =>
        MatchesSemanticCall(callSymbol, SemanticCallFamily.StringSubstring);

    internal static bool IsFreshAllocatedStringCopyCoreCall(string callSymbol) =>
        IsBufferMemmoveCall(callSymbol) ||
        IsFastAllocateStringCall(callSymbol) ||
        MatchesSemanticCall(callSymbol, SemanticCallFamily.FreshAllocatedStringCopy);

    internal static bool IsCharReplaceStringWrapperCall(string callSymbol) =>
        IsBufferMemmoveCall(callSymbol) ||
        IsFastAllocateStringCall(callSymbol) ||
        MatchesSemanticCall(callSymbol, SemanticCallFamily.CharReplaceString);

    internal static bool IsStringLengthCheckedConcatWrapperCall(string callSymbol) =>
        IsFastAllocateStringCall(callSymbol) ||
        MatchesSemanticCall(callSymbol, SemanticCallFamily.StringLengthCheckedConcat);

    internal static bool IsStringArrayConcatWrapperCall(string callSymbol) =>
        IsStringLengthCheckedConcatWrapperCall(callSymbol) ||
        MatchesSemanticCall(callSymbol, SemanticCallFamily.StringArrayConcat);

    internal static bool IsGuardedImmutableStringRewriteWrapperCall(string callSymbol) =>
        IsFastAllocateStringCall(callSymbol) ||
        IsBufferMemmoveCall(callSymbol) ||
        IsPureArgumentGuardWrapper(callSymbol) ||
        MatchesSemanticCall(callSymbol, SemanticCallFamily.GuardedImmutableStringRewrite);

    internal static bool IsLocalScratchIndexBuilderCall(string callSymbol)
    {
        return callSymbol.StartsWith("System.Collections.Generic.ValueListBuilder`1<", StringComparison.Ordinal) &&
               (callSymbol.Contains("..ctor(System.Span`1<!0>)", StringComparison.Ordinal) ||
                callSymbol.Contains(".Append(!0)", StringComparison.Ordinal) ||
                callSymbol.Contains(".AsSpan()", StringComparison.Ordinal) ||
                callSymbol.Contains(".Dispose()", StringComparison.Ordinal) ||
                callSymbol.Contains(".get_Length()", StringComparison.Ordinal));
    }

    internal static bool IsIndexedStringReplaceWrapperCall(string callSymbol) =>
        IsLocalScratchIndexBuilderCall(callSymbol) ||
        IsPureArgumentGuardWrapper(callSymbol) ||
        MatchesSemanticCall(callSymbol, SemanticCallFamily.IndexedStringReplace);

    internal static bool IsFastAllocateStringCall(string callSymbol) =>
        MatchesSemanticCall(callSymbol, SemanticCallFamily.FastAllocateString);

    internal static bool IsBufferMemmoveCall(string callSymbol) =>
        MatchesSemanticCall(callSymbol, SemanticCallFamily.BufferMemmove);

    internal static bool HasOnlyDeterministicStringComparisonDispatch(MethodEffectSummary summary)
    {
        if (!summary.Effects.Contains("virtual_call", StringComparer.Ordinal)) return false;

        var dynamicDispatchCallSites = EnumerateCallSites(summary)
            .Where(static callSite => callSite.UsesDynamicDispatch)
            .ToArray();
        if (dynamicDispatchCallSites.Length == 0) return false;

        return dynamicDispatchCallSites.All(IsDeterministicStringComparisonDispatch);
    }

    internal static bool IsDeterministicStringComparisonDispatch(CallSiteSummary callSite)
    {
        return callSite.UsesDynamicDispatch &&
               HasDeterministicStringComparisonEvidence(callSite) &&
               IsContextSensitiveStringComparisonMethod(callSite);
    }

    internal static bool IsContextSensitiveStringComparisonMethod(CallSiteSummary callSite)
    {
        return callSite.Identity is { } identity
            ? IsContextSensitiveStringComparisonMethod(identity.ContainingMetadataType, identity.Name)
            : IsContextSensitiveStringComparisonMethod(callSite.DisplayName);
    }

    internal static bool HasDeterministicStringComparisonEvidence(CallSiteSummary callSite)
    {
        foreach (var argumentEvidence in callSite.ArgumentEvidence)
        {
            if (EffectSummaryKnownFrameworkCalls.IsDeterministicStringComparison(
                    argumentEvidence.Type,
                    argumentEvidence.Value))
                return true;
        }

        return false;
    }

    internal static bool IsContextSensitiveStringComparisonMethod(string displayName)
    {
        var methodBaseSymbol = GetMethodBaseSymbol(displayName);
        var lastDotIndex = methodBaseSymbol.LastIndexOf('.');
        if (lastDotIndex <= 0 || lastDotIndex == methodBaseSymbol.Length - 1) return false;

        var containingType = methodBaseSymbol[..lastDotIndex];
        var methodName = methodBaseSymbol[(lastDotIndex + 1)..];
        return IsContextSensitiveStringComparisonMethod(containingType, methodName);
    }

    private static bool IsContextSensitiveStringComparisonMethod(
        string containingType,
        string methodName)
    {
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
}

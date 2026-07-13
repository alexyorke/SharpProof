internal static class EffectSummarySemanticWrapperRules
{
    internal static bool TryClassifySemanticPureWrapper(
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

    internal static bool HasPureTypeMetadataBooleanWrapperPattern(MethodEffectSummary summary)
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

    internal static bool HasPureTypeMetadataValueWrapperPattern(MethodEffectSummary summary)
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

    internal static bool HasPureRuntimeTypeMetadataWrapperPattern(MethodEffectSummary summary)
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

    internal static bool HasPureTypeIdentityWrapperPattern(MethodEffectSummary summary)
    {
        return summary.Fields.Length == 0 &&
               CallsOnly(summary, "calls_method", "virtual_call") &&
               summary.RootCandidates.All(static root =>
                   string.Equals(root, "dynamic_dispatch", StringComparison.Ordinal)) &&
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

    internal static bool IsReadOnlyCharSpanSearchHelperCall(string callSymbol)
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

    internal static bool IsStringToReadOnlyCharSpanWrapperCall(string callSymbol)
    {
        return callSymbol.StartsWith("System.MemoryExtensions.AsSpan(string", StringComparison.Ordinal) ||
               callSymbol.StartsWith("string.op_Implicit(string)->System.ReadOnlySpan`1<char>",
                   StringComparison.Ordinal);
    }

    internal static bool IsStringFromReadOnlyCharSpanWrapperCall(string callSymbol)
    {
        return IsStringToReadOnlyCharSpanWrapperCall(callSymbol) ||
               IsReadOnlyCharSpanSearchHelperCall(callSymbol) ||
               string.Equals(callSymbol, "object.ToString()->string", StringComparison.Ordinal) ||
               string.Equals(callSymbol, "string.get_Length()->int", StringComparison.Ordinal);
    }

    internal static bool IsStringSliceNormalizationWrapperCall(string callSymbol)
    {
        return IsStringToReadOnlyCharSpanWrapperCall(callSymbol) ||
               IsReadOnlyCharSpanSearchHelperCall(callSymbol) ||
               callSymbol.StartsWith("System.IO.PathInternal.NormalizeDirectorySeparators(string)",
                   StringComparison.Ordinal) ||
               string.Equals(callSymbol, "string.Substring(int, int)->string", StringComparison.Ordinal);
    }

    internal static bool IsStackLocalCharBuilderStringWrapperCall(string callSymbol)
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

    internal static bool IsImmutableStringRewriteWrapperCall(string callSymbol)
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

    internal static bool IsInvariantTextInfoStringWrapperCall(string callSymbol)
    {
        return string.Equals(callSymbol, "System.Globalization.TextInfo.ToLower(string)->string",
                   StringComparison.Ordinal) ||
               string.Equals(callSymbol, "System.Globalization.TextInfo.ToUpper(string)->string",
                   StringComparison.Ordinal);
    }

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

    internal static bool IsPureTypeAttributeFlagsWrapperMethod(string symbol)
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

    internal static bool TryGetPureTypeSingleImplWrapperCall(string symbol, out string implCall)
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

    internal static bool IsTypeIdentityWrapperMethod(string symbol)
    {
        return string.Equals(symbol, "System.Type.Equals(System.Type)", StringComparison.Ordinal) ||
               string.Equals(symbol, "System.Type.Equals(object)", StringComparison.Ordinal) ||
               string.Equals(symbol, "System.Type.GetHashCode()", StringComparison.Ordinal);
    }

    internal static bool IsTypeIdentityWrapperAnchorCall(string callSymbol)
    {
        return string.Equals(callSymbol, "System.Type.Equals(System.Type)->bool", StringComparison.Ordinal) ||
               string.Equals(callSymbol, "System.Type.get_UnderlyingSystemType()->System.Type",
                   StringComparison.Ordinal) ||
               string.Equals(callSymbol, "System.Reflection.MemberInfo.GetHashCode()->int", StringComparison.Ordinal) ||
               string.Equals(callSymbol, "object.GetHashCode()->int", StringComparison.Ordinal);
    }

    internal static bool IsTypeIdentityWrapperCall(string callSymbol)
    {
        return IsTypeIdentityWrapperAnchorCall(callSymbol) ||
               string.Equals(callSymbol, "System.Type.op_Equality(System.Type, System.Type)->bool",
                   StringComparison.Ordinal);
    }

    internal static bool IsStringHashWrapperCall(string callSymbol)
    {
        return string.Equals(callSymbol, "System.Marvin.ComputeHash32(ref byte, uint, uint, uint)->int",
                   StringComparison.Ordinal) ||
               string.Equals(callSymbol, "System.Marvin.get_DefaultSeed()->ulong", StringComparison.Ordinal) ||
               string.Equals(callSymbol, "System.Runtime.CompilerServices.Unsafe.As(ref !!0)->ref !!1",
                   StringComparison.Ordinal);
    }

    internal static bool IsStringSubstringWrapperCall(string callSymbol)
    {
        return string.Equals(callSymbol, "string.InternalSubString(int, int)->string", StringComparison.Ordinal) ||
               string.Equals(callSymbol, "string.ThrowSubstringArgumentOutOfRange(int, int)->void",
                   StringComparison.Ordinal) ||
               string.Equals(callSymbol, "string.get_Length()->int", StringComparison.Ordinal);
    }

    internal static bool IsFreshAllocatedStringCopyCoreCall(string callSymbol)
    {
        return IsBufferMemmoveCall(callSymbol) ||
               IsFastAllocateStringCall(callSymbol) ||
               string.Equals(callSymbol, "System.Runtime.CompilerServices.Unsafe.Add(ref !!0, nint)->ref !!0",
                   StringComparison.Ordinal);
    }

    internal static bool IsCharReplaceStringWrapperCall(string callSymbol)
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

    internal static bool IsStringLengthCheckedConcatWrapperCall(string callSymbol)
    {
        return IsFastAllocateStringCall(callSymbol) ||
               string.Equals(callSymbol, "System.ThrowHelper.ThrowOutOfMemoryException_StringTooLong()->void",
                   StringComparison.Ordinal) ||
               string.Equals(callSymbol, "string.CopyStringContent(string, int, string)->void",
                   StringComparison.Ordinal) ||
               string.Equals(callSymbol, "string.IsNullOrEmpty(string)->bool", StringComparison.Ordinal) ||
               string.Equals(callSymbol, "string.get_Length()->int", StringComparison.Ordinal);
    }

    internal static bool IsStringArrayConcatWrapperCall(string callSymbol)
    {
        return IsStringLengthCheckedConcatWrapperCall(callSymbol) ||
               string.Equals(callSymbol, "System.ArgumentNullException.ThrowIfNull(object, string)->void",
                   StringComparison.Ordinal) ||
               string.Equals(callSymbol, "System.Array.Clone()->object", StringComparison.Ordinal) ||
               string.Equals(callSymbol, "string.Concat(string[])->string", StringComparison.Ordinal);
    }

    internal static bool IsGuardedImmutableStringRewriteWrapperCall(string callSymbol)
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

    internal static bool IsLocalScratchIndexBuilderCall(string callSymbol)
    {
        return callSymbol.StartsWith("System.Collections.Generic.ValueListBuilder`1<", StringComparison.Ordinal) &&
               (callSymbol.Contains("..ctor(System.Span`1<!0>)", StringComparison.Ordinal) ||
                callSymbol.Contains(".Append(!0)", StringComparison.Ordinal) ||
                callSymbol.Contains(".AsSpan()", StringComparison.Ordinal) ||
                callSymbol.Contains(".Dispose()", StringComparison.Ordinal) ||
                callSymbol.Contains(".get_Length()", StringComparison.Ordinal));
    }

    internal static bool IsIndexedStringReplaceWrapperCall(string callSymbol)
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

    internal static bool IsFastAllocateStringCall(string callSymbol)
    {
        return string.Equals(callSymbol, "string.FastAllocateString(int)->string", StringComparison.Ordinal) ||
               string.Equals(callSymbol, "System.String.FastAllocateString(int)->string", StringComparison.Ordinal);
    }

    internal static bool IsBufferMemmoveCall(string callSymbol)
    {
        return string.Equals(callSymbol, "System.Buffer.Memmove(ref !!0, ref !!0, nuint)->void",
                   StringComparison.Ordinal) ||
               string.Equals(callSymbol, "System.Buffer.Memmove(ref byte, ref byte, nuint)->void",
                   StringComparison.Ordinal);
    }

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
            if (string.Equals(argumentEvidence.Type, "System.StringComparison", StringComparison.Ordinal) &&
                IsDeterministicStringComparisonValue(argumentEvidence.Value))
                return true;

            if (string.Equals(argumentEvidence.Type, "System.StringComparer", StringComparison.Ordinal) &&
                IsDeterministicStringComparerValue(argumentEvidence.Value))
                return true;
        }

        return false;
    }

    internal static bool IsDeterministicStringComparisonValue(string value)
    {
        return string.Equals(value, "System.StringComparison.InvariantCulture", StringComparison.Ordinal) ||
               string.Equals(value, "System.StringComparison.InvariantCultureIgnoreCase", StringComparison.Ordinal) ||
               string.Equals(value, "System.StringComparison.Ordinal", StringComparison.Ordinal) ||
               string.Equals(value, "System.StringComparison.OrdinalIgnoreCase", StringComparison.Ordinal);
    }

    internal static bool IsDeterministicStringComparerValue(string value)
    {
        return string.Equals(value, "System.StringComparer.Ordinal", StringComparison.Ordinal) ||
               string.Equals(value, "System.StringComparer.OrdinalIgnoreCase", StringComparison.Ordinal) ||
               string.Equals(value, "System.StringComparer.InvariantCulture", StringComparison.Ordinal) ||
               string.Equals(value, "System.StringComparer.InvariantCultureIgnoreCase", StringComparison.Ordinal);
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

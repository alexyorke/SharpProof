internal static class EffectSummaryIlAnalyzer
{
    internal static readonly IReadOnlyDictionary<int, StaticFieldFact> EmptyStaticFieldFacts =
        new Dictionary<int, StaticFieldFact>();

    private static readonly IReadOnlyDictionary<short, OpCode> OpCodesByValue =
        typeof(OpCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(OpCode))
            .Select(field => (OpCode)field.GetValue(null)!)
            .ToDictionary(opCode => opCode.Value);

    internal static void AnalyzeIl(
        EffectSummaryIlAnalysisContext context,
        byte[] il,
        ImmutableArray<ExceptionRegion> exceptionRegions,
        SortedSet<string> effects,
        SortedSet<string> calls,
        Dictionary<string, StructuralMethodIdentity> callIdentities,
        List<CallSiteSummary> callSites,
        SortedSet<string> fields,
        SortedSet<string> staticReadFields,
        SortedSet<int> sameAssemblyStaticReadFieldTokens,
        SortedSet<string> thrownExceptionTypes,
        List<ExceptionPropagationSite> exceptionPropagationSites)
    {
        var peReader = context.PeReader;
        var reader = context.Reader;
        var knownThrownExceptionSites = new List<KnownThrownExceptionSite>();
        var trackedLocals = new Dictionary<int, TrackedStackValue>();
        var trackedStack = new List<TrackedStackValue>();
        var suppressDynamicDispatchForNextCallvirt = false;
        foreach (var instruction in EnumerateInstructions(il))
        {
            var instructionOffset = instruction.Offset;
            var opCode = instruction.OpCode;
            var operandOffset = instruction.OperandOffset;
            var operandToken = instruction.MetadataToken;

            if (opCode == OpCodes.Constrained)
            {
                suppressDynamicDispatchForNextCallvirt = true;
                continue;
            }

            if (opCode == OpCodes.Call || opCode == OpCodes.Callvirt || opCode == OpCodes.Newobj)
            {
                string? calledSymbol;
                if (opCode == OpCodes.Newobj)
                    effects.Add("allocates_object");
                else
                    effects.Add("calls_method");

                var usesDynamicDispatch = opCode == OpCodes.Callvirt &&
                                          !suppressDynamicDispatchForNextCallvirt &&
                                          operandToken is not null &&
                                          ShouldTreatCallvirtAsDynamicDispatch(reader, operandToken.Value);
                if (usesDynamicDispatch) effects.Add("virtual_call");

                if (operandToken is not null)
                {
                    calledSymbol = ResolveMethodExactKey(reader, operandToken.Value);
                    calls.Add(calledSymbol);
                    var calledIdentity = TryResolveStructuralMethodIdentity(
                        reader,
                        operandToken.Value,
                        context.MethodsByExactKey);
                    if (calledIdentity != null) callIdentities[calledSymbol] = calledIdentity;
                    exceptionPropagationSites.Add(CreateExceptionPropagationSite(
                        il,
                        reader,
                        exceptionRegions,
                        instructionOffset,
                        calledIdentity));
                    if (TryGetCallTargetSignature(reader, operandToken.Value, opCode == OpCodes.Newobj,
                            out var signature))
                    {
                        var argumentValues = PopTrackedStackValues(trackedStack, signature.ParameterTypes.Length);
                        var receiverValue = signature.HasReceiver
                            ? PopTrackedStackValue(trackedStack)
                            : TrackedStackValue.Unknown;
                        callSites.Add(CreateCallSiteSummary(
                            calledSymbol,
                            calledIdentity,
                            usesDynamicDispatch,
                            signature,
                            receiverValue,
                            argumentValues));
                        PushCallReturnValue(
                            context,
                            operandToken,
                            trackedStack,
                            calledSymbol,
                            signature,
                            argumentValues,
                            opCode == OpCodes.Newobj);
                    }
                    else
                    {
                        callSites.Add(new CallSiteSummary(calledSymbol)
                        {
                            Identity = calledIdentity,
                            UsesDynamicDispatch = usesDynamicDispatch
                        });
                        trackedStack.Clear();
                        trackedLocals.Clear();
                        if (opCode == OpCodes.Newobj) trackedStack.Add(TrackedStackValue.Unknown);
                    }
                }
            }
            else if (opCode == OpCodes.Calli)
            {
                effects.Add("indirect_call");
            }
            else if (opCode == OpCodes.Newarr)
            {
                effects.Add("allocates_array");
            }
            else if (opCode == OpCodes.Box)
            {
                effects.Add("allocates_box");
            }
            else if (opCode == OpCodes.Ldfld || opCode == OpCodes.Ldflda)
            {
                effects.Add("reads_instance_field");
                AddField(reader, operandToken, context.FieldsBySymbol, context.FieldsByExactKey,
                    fields);
            }
            else if (opCode == OpCodes.Ldsfld || opCode == OpCodes.Ldsflda)
            {
                effects.Add("reads_static_field");
                AddField(reader, operandToken, context.FieldsBySymbol, context.FieldsByExactKey,
                    fields);
                AddField(reader, operandToken, context.FieldsBySymbol, context.FieldsByExactKey,
                    staticReadFields);
                AddSameAssemblyStaticFieldToken(
                    reader,
                    operandToken,
                    context.FieldsBySymbol,
                    context.FieldsByExactKey,
                    sameAssemblyStaticReadFieldTokens);
            }
            else if (opCode == OpCodes.Stfld)
            {
                effects.Add("writes_instance_field");
                AddField(reader, operandToken, context.FieldsBySymbol, context.FieldsByExactKey,
                    fields);
            }
            else if (opCode == OpCodes.Stsfld)
            {
                effects.Add("writes_static_field");
                AddField(reader, operandToken, context.FieldsBySymbol, context.FieldsByExactKey,
                    fields);
            }
            else if (opCode == OpCodes.Throw || opCode == OpCodes.Rethrow)
            {
                effects.Add("throws");
                var thrownExceptionType = opCode == OpCodes.Rethrow
                    ? TryResolveRethrowExceptionType(reader, exceptionRegions, instructionOffset,
                        knownThrownExceptionSites)
                    : PeekTrackedExceptionType(trackedStack);
                if (opCode == OpCodes.Throw && thrownExceptionType != null)
                    knownThrownExceptionSites.Add(new KnownThrownExceptionSite(instructionOffset, thrownExceptionType));

                if (thrownExceptionType != null &&
                    IsEscapingThrow(il, reader, exceptionRegions, instructionOffset, thrownExceptionType))
                    thrownExceptionTypes.Add(thrownExceptionType);
            }
            else if (IsIndirectWrite(opCode))
            {
                effects.Add("writes_indirect_memory");
            }
            else if (opCode == OpCodes.Cpblk || opCode == OpCodes.Initblk)
            {
                effects.Add("writes_indirect_memory");
                effects.Add("block_memory_write");
            }
            else if (opCode == OpCodes.Ldftn || opCode == OpCodes.Ldvirtftn)
            {
                effects.Add("loads_method_pointer");
                if (operandToken is not null)
                {
                    var calledSymbol = ResolveMethodExactKey(reader, operandToken.Value);
                    calls.Add(calledSymbol);
                    var calledIdentity = TryResolveStructuralMethodIdentity(
                        reader,
                        operandToken.Value,
                        context.MethodsByExactKey);
                    if (calledIdentity != null) callIdentities[calledSymbol] = calledIdentity;
                }
            }
            else if (opCode.Size == 0)
            {
                effects.Add($"unknown_opcode_at_{instructionOffset}");
                trackedStack.Clear();
                trackedLocals.Clear();
                break;
            }

            if (opCode != OpCodes.Call && opCode != OpCodes.Callvirt && opCode != OpCodes.Newobj)
                ApplyTrackedStackTransition(
                    context,
                    il,
                    opCode,
                    operandOffset,
                    operandToken,
                    trackedStack,
                    trackedLocals);

            suppressDynamicDispatchForNextCallvirt = false;
        }
    }

    internal static string GetCallSiteDeduplicationKey(CallSiteSummary callSite)
    {
        var argumentEvidenceKey = string.Join(
            ";",
            callSite.ArgumentEvidence.Select(static evidence =>
                $"{evidence.Target}:{evidence.ParameterIndex?.ToString() ?? string.Empty}:{evidence.Type}:{evidence.Value}"));
        return $"{callSite.CanonicalKey}|dynamic:{callSite.UsesDynamicDispatch}|evidence:{argumentEvidenceKey}";
    }

    internal static CallSiteSummary CreateCallSiteSummary(
        string calledSymbol,
        StructuralMethodIdentity? calledIdentity,
        bool usesDynamicDispatch,
        CallTargetSignature signature,
        TrackedStackValue receiverValue,
        IReadOnlyList<TrackedStackValue> argumentValues)
    {
        var argumentEvidence = new List<CallSiteArgumentEvidence>();
        if (signature.HasReceiver &&
            receiverValue.KnownStringComparer is { Length: > 0 } knownReceiverComparer)
            argumentEvidence.Add(new CallSiteArgumentEvidence(
                "receiver",
                null,
                "System.StringComparer",
                knownReceiverComparer));

        for (var parameterIndex = 0; parameterIndex < signature.ParameterTypes.Length; parameterIndex++)
        {
            var argumentValue = parameterIndex < argumentValues.Count
                ? argumentValues[parameterIndex]
                : TrackedStackValue.Unknown;
            if (argumentValue.KnownStringComparer is { Length: > 0 } knownArgumentComparer)
                argumentEvidence.Add(new CallSiteArgumentEvidence(
                    "argument",
                    parameterIndex,
                    "System.StringComparer",
                    knownArgumentComparer));

            if (string.Equals(signature.ParameterTypes[parameterIndex], "System.StringComparison",
                    StringComparison.Ordinal) &&
                argumentValue.Int32Constant is int comparisonValue &&
                TryGetStringComparisonValueName(comparisonValue, out var stringComparisonValueName))
                argumentEvidence.Add(new CallSiteArgumentEvidence(
                    "argument",
                    parameterIndex,
                    "System.StringComparison",
                    stringComparisonValueName));
        }

        return new CallSiteSummary(calledSymbol)
        {
            Identity = calledIdentity,
            UsesDynamicDispatch = usesDynamicDispatch,
            ArgumentEvidence = argumentEvidence.ToArray()
        };
    }

    internal static void PushCallReturnValue(
        EffectSummaryIlAnalysisContext context,
        int? operandToken,
        List<TrackedStackValue> trackedStack,
        string calledSymbol,
        CallTargetSignature signature,
        IReadOnlyList<TrackedStackValue> argumentValues,
        bool isObjectConstruction)
    {
        if (isObjectConstruction)
        {
            var exceptionType = TryGetConstructedExceptionType(calledSymbol);
            trackedStack.Add(exceptionType == null
                ? TrackedStackValue.Unknown
                : TrackedStackValue.FromKnownExceptionType(exceptionType));
            return;
        }

        if (string.Equals(signature.ReturnType, "void", StringComparison.Ordinal)) return;

        trackedStack.Add(TryGetKnownCallReturnValue(
            context,
            operandToken,
            calledSymbol,
            argumentValues,
            out var returnValue)
            ? returnValue
            : TrackedStackValue.Unknown);
    }

    internal static void ApplyTrackedStackTransition(
        EffectSummaryIlAnalysisContext context,
        byte[] il,
        OpCode opCode,
        int operandOffset,
        int? operandToken,
        List<TrackedStackValue> trackedStack,
        Dictionary<int, TrackedStackValue> trackedLocals)
    {
        if (TryGetPushedInt32Constant(opCode, il, operandOffset, out var pushedInt32Constant))
        {
            trackedStack.Add(TrackedStackValue.FromInt32(pushedInt32Constant));
            return;
        }

        if (TryGetStoreLocalIndex(opCode, il, operandOffset, out var storeLocalIndex))
        {
            trackedLocals[storeLocalIndex] = PopTrackedStackValue(trackedStack);
            return;
        }

        if (TryGetLoadLocalIndex(opCode, il, operandOffset, out var loadLocalIndex))
        {
            trackedStack.Add(trackedLocals.TryGetValue(loadLocalIndex, out var trackedLocalValue)
                ? trackedLocalValue
                : TrackedStackValue.Unknown);
            return;
        }

        if (opCode == OpCodes.Dup)
        {
            trackedStack.Add(trackedStack.Count == 0 ? TrackedStackValue.Unknown : trackedStack[^1]);
            return;
        }

        if (opCode == OpCodes.Ldsfld)
        {
            trackedStack.Add(TryGetKnownTrackedStaticFieldValue(
                context,
                operandToken,
                out var trackedFieldValue)
                ? trackedFieldValue
                : TrackedStackValue.Unknown);
            return;
        }

        if (opCode == OpCodes.Ldfld || opCode == OpCodes.Ldflda)
        {
            PopTrackedStackValue(trackedStack);
            trackedStack.Add(TrackedStackValue.Unknown);
            return;
        }

        if (opCode == OpCodes.Stfld)
        {
            PopTrackedStackValue(trackedStack);
            PopTrackedStackValue(trackedStack);
            return;
        }

        if (opCode == OpCodes.Stsfld)
        {
            PopTrackedStackValue(trackedStack);
            return;
        }

        if (!TryGetStackPopCount(opCode.StackBehaviourPop, out var popCount) ||
            !TryGetStackPushCount(opCode.StackBehaviourPush, out var pushCount))
        {
            trackedStack.Clear();
            trackedLocals.Clear();
            return;
        }

        PopTrackedStackValues(trackedStack, popCount);
        for (var i = 0; i < pushCount; i++) trackedStack.Add(TrackedStackValue.Unknown);

        if (ShouldResetTrackedState(opCode))
        {
            trackedStack.Clear();
            trackedLocals.Clear();
        }
    }

    internal static bool TryGetKnownTrackedStaticFieldValue(
        EffectSummaryIlAnalysisContext context,
        int? operandToken,
        out TrackedStackValue trackedValue)
    {
        trackedValue = TrackedStackValue.Unknown;
        if (operandToken is null) return false;

        if (TryResolveSameAssemblyFieldDefinitionHandle(
                context.Reader,
                operandToken.Value,
                context.FieldsBySymbol,
                context.FieldsByExactKey,
                out var fieldHandle) &&
            context.StaticFields.TryGetValue(MetadataTokens.GetToken(fieldHandle), out var staticFieldFact) &&
            !staticFieldFact.TrackedValue.IsUnknown)
        {
            trackedValue = staticFieldFact.TrackedValue;
            return true;
        }

        return TryGetKnownStringComparerIdentity(
            ResolveFieldToken(context.Reader, operandToken.Value),
            out trackedValue);
    }

    internal static bool TryGetKnownCallReturnValue(
        EffectSummaryIlAnalysisContext context,
        int? operandToken,
        string calledSymbol,
        IReadOnlyList<TrackedStackValue> argumentValues,
        out TrackedStackValue trackedValue)
    {
        if (TryGetKnownStringComparerIdentity(calledSymbol, out trackedValue)) return true;

        if (string.Equals(
                calledSymbol,
                "System.StringComparer.FromComparison(System.StringComparison)->System.StringComparer",
                StringComparison.Ordinal) &&
            argumentValues.Count == 1 &&
            argumentValues[0].Int32Constant is int comparisonValue)
            return TryGetStringComparerIdentityFromComparison(comparisonValue, out trackedValue);

        if (operandToken is not null &&
            TryResolveSameAssemblyMethodDefinitionHandle(
                context.Reader,
                operandToken.Value,
                context.MethodsByExactKey,
                out var methodDefinitionHandle) &&
            TryGetKnownMethodReturnValue(
                context,
                methodDefinitionHandle,
                out trackedValue))
            return true;

        trackedValue = TrackedStackValue.Unknown;
        return false;
    }

    internal static bool TryGetKnownMethodReturnValue(
        EffectSummaryIlAnalysisContext context,
        MethodDefinitionHandle handle,
        out TrackedStackValue trackedValue)
    {
        var metadataToken = MetadataTokens.GetToken(handle);
        if (context.KnownMethodReturns.TryGetValue(metadataToken, out trackedValue)) return !trackedValue.IsUnknown;

        if (!context.ReturnValueVisiting.Add(metadataToken))
        {
            trackedValue = TrackedStackValue.Unknown;
            return false;
        }

        try
        {
            trackedValue = AnalyzeKnownMethodReturnValue(
                context,
                handle);
            context.KnownMethodReturns[metadataToken] = trackedValue;
            return !trackedValue.IsUnknown;
        }
        finally
        {
            context.ReturnValueVisiting.Remove(metadataToken);
        }
    }

    internal static TrackedStackValue AnalyzeKnownMethodReturnValue(
        EffectSummaryIlAnalysisContext context,
        MethodDefinitionHandle handle)
    {
        var peReader = context.PeReader;
        var reader = context.Reader;
        var definition = reader.GetMethodDefinition(handle);
        if (definition.RelativeVirtualAddress == 0 ||
            (definition.Attributes & MethodAttributes.Abstract) != 0)
            return TrackedStackValue.Unknown;

        CallTargetSignature signature;
        try
        {
            signature = GetMethodDefinitionCallTargetSignature(reader, handle, false);
        }
        catch (BadImageFormatException)
        {
            return TrackedStackValue.Unknown;
        }
        catch (InvalidOperationException)
        {
            return TrackedStackValue.Unknown;
        }

        if (string.Equals(signature.ReturnType, "void", StringComparison.Ordinal)) return TrackedStackValue.Unknown;

        var body = peReader.GetMethodBody(definition.RelativeVirtualAddress);
        var il = body.GetILBytes();
        if (il is null) return TrackedStackValue.Unknown;

        var trackedLocals = new Dictionary<int, TrackedStackValue>();
        var trackedStack = new List<TrackedStackValue>();
        var pendingBranchStates = new Dictionary<int, BranchTrackedState>();
        TrackedStackValue? knownReturnValue = null;
        foreach (var instruction in EnumerateInstructions(il))
        {
            var instructionOffset = instruction.Offset;
            if (pendingBranchStates.TryGetValue(instructionOffset, out var pendingBranchState))
            {
                if ((trackedStack.Count != 0 || trackedLocals.Count != 0) &&
                    !TrackedStatesEqual(trackedStack, trackedLocals, pendingBranchState))
                    return TrackedStackValue.Unknown;

                RestoreTrackedState(trackedStack, trackedLocals, pendingBranchState);
            }

            var opCode = instruction.OpCode;
            var operandOffset = instruction.OperandOffset;
            var operandToken = instruction.MetadataToken;

            if (opCode == OpCodes.Constrained) continue;

            if (opCode == OpCodes.Ret)
            {
                var returnValue = PopTrackedStackValue(trackedStack);
                if (returnValue.IsUnknown) return TrackedStackValue.Unknown;

                if (knownReturnValue is null)
                    knownReturnValue = returnValue;
                else if (knownReturnValue.Value != returnValue) return TrackedStackValue.Unknown;

                trackedStack.Clear();
                trackedLocals.Clear();
                continue;
            }

            if (opCode == OpCodes.Call || opCode == OpCodes.Callvirt || opCode == OpCodes.Newobj)
            {
                if (operandToken is not null &&
                    TryGetCallTargetSignature(reader, operandToken.Value, opCode == OpCodes.Newobj,
                        out var calledSignature))
                {
                    var argumentValues = PopTrackedStackValues(trackedStack, calledSignature.ParameterTypes.Length);
                    if (calledSignature.HasReceiver) PopTrackedStackValue(trackedStack);

                    PushCallReturnValue(
                        context,
                        operandToken,
                        trackedStack,
                        ResolveMethodExactKey(reader, operandToken.Value),
                        calledSignature,
                        argumentValues,
                        opCode == OpCodes.Newobj);
                }
                else
                {
                    trackedStack.Clear();
                    trackedLocals.Clear();
                    if (opCode == OpCodes.Newobj) trackedStack.Add(TrackedStackValue.Unknown);
                }

                continue;
            }

            if (opCode.FlowControl == FlowControl.Branch &&
                TryGetBranchTargetOffset(opCode, il, operandOffset, instructionOffset, out var branchTargetOffset))
            {
                var branchState = CaptureTrackedState(trackedStack, trackedLocals);
                if (pendingBranchStates.TryGetValue(branchTargetOffset, out var existingBranchState) &&
                    !TrackedStatesEqual(branchState.Stack, branchState.Locals, existingBranchState))
                    return TrackedStackValue.Unknown;

                pendingBranchStates[branchTargetOffset] = branchState;
            }

            ApplyTrackedStackTransition(
                context,
                il,
                opCode,
                operandOffset,
                operandToken,
                trackedStack,
                trackedLocals);
        }

        return knownReturnValue ?? TrackedStackValue.Unknown;
    }

    internal static bool TryGetKnownStringComparerIdentity(string symbol, out TrackedStackValue trackedValue)
    {
        trackedValue = symbol switch
        {
            "System.StringComparer.get_CurrentCulture()->System.StringComparer" => TrackedStackValue
                .FromKnownStringComparer("System.StringComparer.CurrentCulture"),
            "System.StringComparer.get_CurrentCultureIgnoreCase()->System.StringComparer" => TrackedStackValue
                .FromKnownStringComparer("System.StringComparer.CurrentCultureIgnoreCase"),
            "System.StringComparer.get_InvariantCulture()->System.StringComparer" => TrackedStackValue
                .FromKnownStringComparer("System.StringComparer.InvariantCulture"),
            "System.StringComparer.get_InvariantCultureIgnoreCase()->System.StringComparer" => TrackedStackValue
                .FromKnownStringComparer("System.StringComparer.InvariantCultureIgnoreCase"),
            "System.StringComparer.get_Ordinal()->System.StringComparer" => TrackedStackValue.FromKnownStringComparer(
                "System.StringComparer.Ordinal"),
            "System.StringComparer.get_OrdinalIgnoreCase()->System.StringComparer" => TrackedStackValue
                .FromKnownStringComparer("System.StringComparer.OrdinalIgnoreCase"),
            _ => TrackedStackValue.Unknown
        };

        return !trackedValue.IsUnknown;
    }

    internal static bool TryGetStringComparisonValueName(int value, out string name)
    {
        if (Enum.IsDefined(typeof(StringComparison), value))
        {
            name = $"System.StringComparison.{(StringComparison)value}";
            return true;
        }

        name = string.Empty;
        return false;
    }

    internal static bool TryGetStringComparerIdentityFromComparison(int comparisonValue,
        out TrackedStackValue trackedValue)
    {
        trackedValue = comparisonValue switch
        {
            0 => TrackedStackValue.FromKnownStringComparer("System.StringComparer.CurrentCulture"),
            1 => TrackedStackValue.FromKnownStringComparer("System.StringComparer.CurrentCultureIgnoreCase"),
            2 => TrackedStackValue.FromKnownStringComparer("System.StringComparer.InvariantCulture"),
            3 => TrackedStackValue.FromKnownStringComparer("System.StringComparer.InvariantCultureIgnoreCase"),
            4 => TrackedStackValue.FromKnownStringComparer("System.StringComparer.Ordinal"),
            5 => TrackedStackValue.FromKnownStringComparer("System.StringComparer.OrdinalIgnoreCase"),
            _ => TrackedStackValue.Unknown
        };

        return !trackedValue.IsUnknown;
    }

    internal static bool TryGetCallTargetSignature(
        MetadataReader reader,
        int metadataToken,
        bool isObjectConstruction,
        out CallTargetSignature signature)
    {
        var handle = MetadataTokens.Handle(metadataToken);
        try
        {
            signature = handle.Kind switch
            {
                HandleKind.MethodDefinition => GetMethodDefinitionCallTargetSignature(
                    reader,
                    (MethodDefinitionHandle)handle,
                    isObjectConstruction),
                HandleKind.MemberReference => GetMemberReferenceCallTargetSignature(
                    reader,
                    (MemberReferenceHandle)handle,
                    isObjectConstruction),
                HandleKind.MethodSpecification => GetMethodSpecificationCallTargetSignature(
                    reader,
                    (MethodSpecificationHandle)handle,
                    isObjectConstruction),
                _ => default
            };
            return handle.Kind is HandleKind.MethodDefinition
                or HandleKind.MemberReference
                or HandleKind.MethodSpecification;
        }
        catch (BadImageFormatException)
        {
            signature = default;
            return false;
        }
        catch (InvalidOperationException)
        {
            signature = default;
            return false;
        }
    }

    internal static CallTargetSignature GetMethodDefinitionCallTargetSignature(
        MetadataReader reader,
        MethodDefinitionHandle handle,
        bool isObjectConstruction)
    {
        var definition = reader.GetMethodDefinition(handle);
        var decodedSignature = definition.DecodeSignature(new TypeNameProvider(reader), null);
        return new CallTargetSignature(
            !isObjectConstruction && (definition.Attributes & MethodAttributes.Static) == 0,
            decodedSignature.ParameterTypes.ToArray(),
            decodedSignature.ReturnType);
    }

    internal static CallTargetSignature GetMemberReferenceCallTargetSignature(
        MetadataReader reader,
        MemberReferenceHandle handle,
        bool isObjectConstruction)
    {
        var memberReference = reader.GetMemberReference(handle);
        var decodedSignature = memberReference.DecodeMethodSignature(new TypeNameProvider(reader), null);
        return new CallTargetSignature(
            !isObjectConstruction && decodedSignature.Header.IsInstance,
            decodedSignature.ParameterTypes.ToArray(),
            decodedSignature.ReturnType);
    }

    internal static CallTargetSignature GetMethodSpecificationCallTargetSignature(
        MetadataReader reader,
        MethodSpecificationHandle handle,
        bool isObjectConstruction)
    {
        var specification = reader.GetMethodSpecification(handle);
        return specification.Method.Kind switch
        {
            HandleKind.MethodDefinition => GetMethodDefinitionCallTargetSignature(
                reader,
                (MethodDefinitionHandle)specification.Method,
                isObjectConstruction),
            HandleKind.MemberReference => GetMemberReferenceCallTargetSignature(
                reader,
                (MemberReferenceHandle)specification.Method,
                isObjectConstruction),
            _ => default
        };
    }

    internal static TrackedStackValue[] PopTrackedStackValues(List<TrackedStackValue> trackedStack, int count)
    {
        var values = new TrackedStackValue[count];
        for (var index = count - 1; index >= 0; index--) values[index] = PopTrackedStackValue(trackedStack);

        return values;
    }

    internal static TrackedStackValue PopTrackedStackValue(List<TrackedStackValue> trackedStack)
    {
        if (trackedStack.Count == 0) return TrackedStackValue.Unknown;

        var lastIndex = trackedStack.Count - 1;
        var value = trackedStack[lastIndex];
        trackedStack.RemoveAt(lastIndex);
        return value;
    }

    internal static string? PeekTrackedExceptionType(List<TrackedStackValue> trackedStack)
    {
        return trackedStack.Count == 0 || string.IsNullOrWhiteSpace(trackedStack[^1].KnownExceptionType)
            ? null
            : trackedStack[^1].KnownExceptionType;
    }

    internal static bool TryGetStackPopCount(StackBehaviour behavior, out int count)
    {
        count = behavior switch
        {
            StackBehaviour.Pop0 => 0,
            StackBehaviour.Pop1 or
                StackBehaviour.Popi or
                StackBehaviour.Popref => 1,
            StackBehaviour.Pop1_pop1 or
                StackBehaviour.Popi_pop1 or
                StackBehaviour.Popi_popi or
                StackBehaviour.Popi_popi8 or
                StackBehaviour.Popi_popr4 or
                StackBehaviour.Popi_popr8 or
                StackBehaviour.Popref_pop1 or
                StackBehaviour.Popref_popi => 2,
            StackBehaviour.Popi_popi_popi or
                StackBehaviour.Popref_popi_popi or
                StackBehaviour.Popref_popi_popi8 or
                StackBehaviour.Popref_popi_popr4 or
                StackBehaviour.Popref_popi_popr8 or
                StackBehaviour.Popref_popi_popref => 3,
            _ => -1
        };

        return count >= 0;
    }

    internal static bool TryGetStackPushCount(StackBehaviour behavior, out int count)
    {
        count = behavior switch
        {
            StackBehaviour.Push0 => 0,
            StackBehaviour.Push1 or
                StackBehaviour.Pushi or
                StackBehaviour.Pushi8 or
                StackBehaviour.Pushr4 or
                StackBehaviour.Pushr8 or
                StackBehaviour.Pushref => 1,
            StackBehaviour.Push1_push1 => 2,
            _ => -1
        };

        return count >= 0;
    }

    internal static bool ShouldResetTrackedState(OpCode opCode)
    {
        return opCode.FlowControl is FlowControl.Branch
            or FlowControl.Cond_Branch
            or FlowControl.Return
            or FlowControl.Throw;
    }

    internal static BranchTrackedState CaptureTrackedState(
        List<TrackedStackValue> trackedStack,
        Dictionary<int, TrackedStackValue> trackedLocals)
    {
        return new BranchTrackedState(
            new List<TrackedStackValue>(trackedStack),
            new Dictionary<int, TrackedStackValue>(trackedLocals));
    }

    internal static void RestoreTrackedState(
        List<TrackedStackValue> trackedStack,
        Dictionary<int, TrackedStackValue> trackedLocals,
        BranchTrackedState branchState)
    {
        trackedStack.Clear();
        trackedStack.AddRange(branchState.Stack);

        trackedLocals.Clear();
        foreach (var pair in branchState.Locals) trackedLocals[pair.Key] = pair.Value;
    }

    internal static bool TrackedStatesEqual(
        List<TrackedStackValue> trackedStack,
        Dictionary<int, TrackedStackValue> trackedLocals,
        BranchTrackedState branchState)
    {
        if (trackedStack.Count != branchState.Stack.Count || trackedLocals.Count != branchState.Locals.Count)
            return false;

        for (var i = 0; i < trackedStack.Count; i++)
            if (trackedStack[i] != branchState.Stack[i])
                return false;

        foreach (var pair in trackedLocals)
            if (!branchState.Locals.TryGetValue(pair.Key, out var value) || value != pair.Value)
                return false;

        return true;
    }

    internal static bool IsKnownStableIdentityInitializerCall(string calledSymbol)
    {
        return calledSymbol.StartsWith("System.Array.Empty<", StringComparison.Ordinal);
    }

    internal static StaticFieldInitializerValue[] PopStaticFieldInitializerValues(
        List<StaticFieldInitializerValue> trackedStack,
        int count)
    {
        var values = new StaticFieldInitializerValue[count];
        for (var index = count - 1; index >= 0; index--) values[index] = PopStaticFieldInitializerValue(trackedStack);

        return values;
    }

    internal static StaticFieldInitializerValue PopStaticFieldInitializerValue(
        List<StaticFieldInitializerValue> trackedStack)
    {
        if (trackedStack.Count == 0) return StaticFieldInitializerValue.Unknown;

        var lastIndex = trackedStack.Count - 1;
        var value = trackedStack[lastIndex];
        trackedStack.RemoveAt(lastIndex);
        return value;
    }

    internal static bool TryCreateStaticFieldInitializerValue(
        TrackedStackValue trackedValue,
        out StaticFieldInitializerValue value)
    {
        if (trackedValue.Int32Constant is not null)
        {
            value = StaticFieldInitializerValue.FromConstantTracked(trackedValue);
            return true;
        }

        if (!string.IsNullOrWhiteSpace(trackedValue.KnownStringComparer))
        {
            value = StaticFieldInitializerValue.FromStableIdentityTracked(trackedValue);
            return true;
        }

        value = StaticFieldInitializerValue.Unknown;
        return false;
    }

    internal static bool TryGetTrackedStaticFieldInitializerValue(
        MetadataReader reader,
        int? metadataToken,
        IReadOnlyDictionary<int, StaticFieldInitializerValue> assignmentsByFieldToken,
        IReadOnlyDictionary<string, FieldDefinitionHandle> fieldDefinitionHandlesBySymbol,
        IReadOnlyDictionary<string, FieldDefinitionHandle> fieldDefinitionHandlesByExactKey,
        out StaticFieldInitializerValue value)
    {
        value = StaticFieldInitializerValue.Unknown;
        if (metadataToken is null) return false;

        if (TryResolveSameAssemblyFieldDefinitionHandle(
                reader,
                metadataToken.Value,
                fieldDefinitionHandlesBySymbol,
                fieldDefinitionHandlesByExactKey,
                out var sameAssemblyFieldHandle) &&
            assignmentsByFieldToken.TryGetValue(MetadataTokens.GetToken(sameAssemblyFieldHandle), out value))
            return value.Kind != StaticFieldInitializerValueKind.Unknown;

        if (TryGetKnownStringComparerIdentity(ResolveFieldToken(reader, metadataToken.Value), out var trackedValue) &&
            TryCreateStaticFieldInitializerValue(trackedValue, out value))
            return true;

        value = StaticFieldInitializerValue.Unknown;
        return false;
    }

    internal static bool TryGetBranchTargetOffset(
        OpCode opCode,
        byte[] il,
        int operandOffset,
        int instructionOffset,
        out int targetOffset)
    {
        targetOffset = 0;
        if (opCode.OperandType == OperandType.ShortInlineBrTarget)
        {
            targetOffset = instructionOffset + opCode.Size + 1 + unchecked((sbyte)il[operandOffset]);
            return true;
        }

        if (opCode.OperandType == OperandType.InlineBrTarget)
        {
            targetOffset = instructionOffset + opCode.Size + 4 + BitConverter.ToInt32(il, operandOffset);
            return true;
        }

        return false;
    }

    internal static bool TryGetStoreLocalIndex(OpCode opCode, byte[] il, int operandOffset, out int localIndex)
    {
        return TryGetLocalIndex(
            opCode,
            il,
            operandOffset,
            OpCodes.Stloc_0,
            OpCodes.Stloc_1,
            OpCodes.Stloc_2,
            OpCodes.Stloc_3,
            OpCodes.Stloc_S,
            OpCodes.Stloc,
            out localIndex);
    }

    internal static bool TryGetPushedInt32Constant(OpCode opCode, byte[] il, int operandOffset, out int value)
    {
        value = 0;
        if (opCode == OpCodes.Ldc_I4_M1)
        {
            value = -1;
            return true;
        }

        if (opCode == OpCodes.Ldc_I4_0)
        {
            value = 0;
            return true;
        }

        if (opCode == OpCodes.Ldc_I4_1)
        {
            value = 1;
            return true;
        }

        if (opCode == OpCodes.Ldc_I4_2)
        {
            value = 2;
            return true;
        }

        if (opCode == OpCodes.Ldc_I4_3)
        {
            value = 3;
            return true;
        }

        if (opCode == OpCodes.Ldc_I4_4)
        {
            value = 4;
            return true;
        }

        if (opCode == OpCodes.Ldc_I4_5)
        {
            value = 5;
            return true;
        }

        if (opCode == OpCodes.Ldc_I4_6)
        {
            value = 6;
            return true;
        }

        if (opCode == OpCodes.Ldc_I4_7)
        {
            value = 7;
            return true;
        }

        if (opCode == OpCodes.Ldc_I4_8)
        {
            value = 8;
            return true;
        }

        if (opCode == OpCodes.Ldc_I4_S)
        {
            value = unchecked((sbyte)il[operandOffset]);
            return true;
        }

        if (opCode == OpCodes.Ldc_I4)
        {
            value = BitConverter.ToInt32(il, operandOffset);
            return true;
        }

        return false;
    }

    internal static bool TryGetLoadLocalIndex(OpCode opCode, byte[] il, int operandOffset, out int localIndex)
    {
        return TryGetLocalIndex(
            opCode,
            il,
            operandOffset,
            OpCodes.Ldloc_0,
            OpCodes.Ldloc_1,
            OpCodes.Ldloc_2,
            OpCodes.Ldloc_3,
            OpCodes.Ldloc_S,
            OpCodes.Ldloc,
            out localIndex);
    }

    internal static bool TryGetLocalIndex(
        OpCode opCode,
        byte[] il,
        int operandOffset,
        OpCode index0,
        OpCode index1,
        OpCode index2,
        OpCode index3,
        OpCode shortForm,
        OpCode wideForm,
        out int localIndex)
    {
        if (opCode == index0)
        {
            localIndex = 0;
            return true;
        }

        if (opCode == index1)
        {
            localIndex = 1;
            return true;
        }

        if (opCode == index2)
        {
            localIndex = 2;
            return true;
        }

        if (opCode == index3)
        {
            localIndex = 3;
            return true;
        }

        if (opCode == shortForm)
        {
            localIndex = il[operandOffset];
            return true;
        }

        if (opCode == wideForm)
        {
            localIndex = BitConverter.ToUInt16(il, operandOffset);
            return true;
        }

        localIndex = -1;
        return false;
    }

    internal static string? TryGetConstructedExceptionType(string? constructorSymbol)
    {
        if (string.IsNullOrWhiteSpace(constructorSymbol)) return null;

        var ctorIndex = constructorSymbol.IndexOf("..ctor(", StringComparison.Ordinal);
        if (ctorIndex <= 0) return null;

        var typeName = constructorSymbol.Substring(0, ctorIndex);
        return typeName.EndsWith("Exception", StringComparison.Ordinal) ? typeName : null;
    }

    internal static string? TryResolveRethrowExceptionType(
        MetadataReader reader,
        ImmutableArray<ExceptionRegion> exceptionRegions,
        int instructionOffset,
        IReadOnlyList<KnownThrownExceptionSite> knownThrownExceptionSites)
    {
        if (TryGetEnclosingCatchRegion(exceptionRegions, instructionOffset, out var catchRegion))
        {
            var catchExceptionType = GetCatchExceptionType(reader, catchRegion);
            if (!string.IsNullOrWhiteSpace(catchExceptionType))
            {
                var protectedTryExceptionTypes = knownThrownExceptionSites
                    .Where(site =>
                        ContainsOffset(catchRegion.TryOffset, catchRegion.TryLength, site.InstructionOffset) &&
                        CatchHandlesException(reader, site.ExceptionType, catchExceptionType))
                    .Select(site => site.ExceptionType)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();

                if (protectedTryExceptionTypes.Length == 1) return protectedTryExceptionTypes[0];
            }
        }

        return GetEnclosingCatchExceptionType(reader, exceptionRegions, instructionOffset);
    }

    internal static ExceptionPropagationSite CreateExceptionPropagationSite(
        byte[] il,
        MetadataReader reader,
        ImmutableArray<ExceptionRegion> exceptionRegions,
        int instructionOffset,
        StructuralMethodIdentity? calleeIdentity)
    {
        return new ExceptionPropagationSite(
            calleeIdentity,
            instructionOffset,
            GetHandlingCatchExceptionTypes(reader, exceptionRegions, instructionOffset),
            IsShadowedByDefinitelyThrowingFinally(il, exceptionRegions, instructionOffset));
    }

    internal static bool ExceptionEscapesPropagationSite(
        MetadataReader reader,
        ExceptionPropagationSite propagationSite,
        string thrownExceptionType)
    {
        if (propagationSite.IsShadowedByDefinitelyThrowingFinally) return false;

        foreach (var catchExceptionType in propagationSite.HandlingCatchExceptionTypes)
        {
            if (CatchHandlesException(reader, thrownExceptionType, catchExceptionType)) return false;
        }

        return true;
    }

    internal static bool IsEscapingThrow(
        byte[] il,
        MetadataReader reader,
        ImmutableArray<ExceptionRegion> exceptionRegions,
        int instructionOffset,
        string thrownExceptionType)
    {
        if (IsShadowedByDefinitelyThrowingFinally(il, exceptionRegions, instructionOffset)) return false;

        foreach (var exceptionRegion in exceptionRegions)
        {
            if (exceptionRegion.Kind != ExceptionRegionKind.Catch ||
                !ContainsOffset(exceptionRegion.TryOffset, exceptionRegion.TryLength, instructionOffset))
                continue;

            var catchExceptionType = GetCatchExceptionType(reader, exceptionRegion);
            if (CatchHandlesException(reader, thrownExceptionType, catchExceptionType)) return false;
        }

        return true;
    }

    internal static string[] GetHandlingCatchExceptionTypes(
        MetadataReader reader,
        ImmutableArray<ExceptionRegion> exceptionRegions,
        int instructionOffset)
    {
        return exceptionRegions
            .Where(exceptionRegion =>
                exceptionRegion.Kind == ExceptionRegionKind.Catch &&
                ContainsOffset(exceptionRegion.TryOffset, exceptionRegion.TryLength, instructionOffset))
            .Select(exceptionRegion => GetCatchExceptionType(reader, exceptionRegion))
            .Where(exceptionType => !string.IsNullOrWhiteSpace(exceptionType))
            .Distinct(StringComparer.Ordinal)
            .ToArray()!;
    }

    internal static bool IsShadowedByDefinitelyThrowingFinally(
        byte[] il,
        ImmutableArray<ExceptionRegion> exceptionRegions,
        int instructionOffset)
    {
        foreach (var exceptionRegion in exceptionRegions)
        {
            if (exceptionRegion.Kind != ExceptionRegionKind.Finally ||
                !ContainsOffset(exceptionRegion.TryOffset, exceptionRegion.TryLength, instructionOffset) ||
                ContainsOffset(exceptionRegion.HandlerOffset, exceptionRegion.HandlerLength, instructionOffset))
                continue;

            if (FinallyHandlerDefinitelyThrows(il, exceptionRegion.HandlerOffset, exceptionRegion.HandlerLength))
                return true;
        }

        return false;
    }

    internal static bool FinallyHandlerDefinitelyThrows(byte[] il, int handlerOffset, int handlerLength)
    {
        var endOffset = handlerOffset + handlerLength;
        OpCode lastMeaningfulOpCode = default;
        var foundMeaningfulInstruction = false;
        foreach (var instruction in EnumerateInstructions(il, handlerOffset, endOffset))
        {
            var opCode = instruction.OpCode;

            if (opCode.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch or FlowControl.Return ||
                opCode == OpCodes.Endfinally ||
                opCode == OpCodes.Endfilter ||
                opCode == OpCodes.Leave ||
                opCode == OpCodes.Leave_S)
                return false;

            if (opCode != OpCodes.Nop)
            {
                lastMeaningfulOpCode = opCode;
                foundMeaningfulInstruction = true;
            }
        }

        return foundMeaningfulInstruction &&
               (lastMeaningfulOpCode == OpCodes.Throw || lastMeaningfulOpCode == OpCodes.Rethrow);
    }

    internal static bool TryGetEnclosingCatchRegion(
        ImmutableArray<ExceptionRegion> exceptionRegions,
        int instructionOffset,
        out ExceptionRegion catchRegion)
    {
        catchRegion = default;
        var smallestHandlerLength = int.MaxValue;
        var found = false;
        foreach (var exceptionRegion in exceptionRegions)
        {
            if (exceptionRegion.Kind != ExceptionRegionKind.Catch ||
                !ContainsOffset(exceptionRegion.HandlerOffset, exceptionRegion.HandlerLength, instructionOffset) ||
                exceptionRegion.HandlerLength >= smallestHandlerLength)
                continue;

            catchRegion = exceptionRegion;
            smallestHandlerLength = exceptionRegion.HandlerLength;
            found = true;
        }

        return found;
    }

    internal static string? GetEnclosingCatchExceptionType(
        MetadataReader reader,
        ImmutableArray<ExceptionRegion> exceptionRegions,
        int instructionOffset)
    {
        return TryGetEnclosingCatchRegion(exceptionRegions, instructionOffset, out var catchRegion)
            ? GetCatchExceptionType(reader, catchRegion)
            : null;
    }

    internal static bool ContainsOffset(int startOffset, int length, int instructionOffset)
    {
        return instructionOffset >= startOffset && instructionOffset < startOffset + length;
    }

    internal static string? GetCatchExceptionType(MetadataReader reader, ExceptionRegion exceptionRegion)
    {
        if (exceptionRegion.Kind != ExceptionRegionKind.Catch) return null;

        if (exceptionRegion.CatchType.IsNil) return "System.Exception";

        return GetEntityTypeName(reader, exceptionRegion.CatchType);
    }

    internal static string? GetEntityTypeName(MetadataReader reader, EntityHandle handle)
    {
        try
        {
            return handle.Kind switch
            {
                HandleKind.TypeDefinition => GetExceptionTypeDefinitionName(reader, (TypeDefinitionHandle)handle),
                HandleKind.TypeReference => GetExceptionTypeReferenceName(reader, (TypeReferenceHandle)handle),
                _ => null
            };
        }
        catch (BadImageFormatException)
        {
            return null;
        }
    }

    internal static string GetExceptionTypeDefinitionName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var definition = reader.GetTypeDefinition(handle);
        return GetQualifiedTypeName(
            reader.GetString(definition.Namespace),
            reader.GetString(definition.Name));
    }

    internal static string GetExceptionTypeReferenceName(MetadataReader reader, TypeReferenceHandle handle)
    {
        var reference = reader.GetTypeReference(handle);
        return GetQualifiedTypeName(
            reader.GetString(reference.Namespace),
            reader.GetString(reference.Name));
    }

    internal static string GetQualifiedTypeName(string typeNamespace, string typeName)
    {
        return string.IsNullOrWhiteSpace(typeNamespace)
            ? typeName
            : typeNamespace + "." + typeName;
    }

    internal static bool CatchHandlesException(
        MetadataReader reader,
        string thrownExceptionType,
        string? catchExceptionType)
    {
        if (string.IsNullOrWhiteSpace(catchExceptionType)) return false;

        if (string.Equals(catchExceptionType, "System.Exception", StringComparison.Ordinal) ||
            string.Equals(catchExceptionType, "System.Object", StringComparison.Ordinal))
            return true;

        if (string.Equals(thrownExceptionType, catchExceptionType, StringComparison.Ordinal)) return true;

        return IsDefinedTypeDerivedFrom(reader, thrownExceptionType, catchExceptionType);
    }

    internal static bool IsDefinedTypeDerivedFrom(
        MetadataReader reader,
        string thrownExceptionType,
        string catchExceptionType)
    {
        try
        {
            var currentType = thrownExceptionType;
            var visitedTypes = new HashSet<string>(StringComparer.Ordinal);
            while (visitedTypes.Add(currentType))
            {
                var definitionHandle = reader.TypeDefinitions
                    .FirstOrDefault(handle => string.Equals(
                        GetExceptionTypeDefinitionName(reader, handle),
                        currentType,
                        StringComparison.Ordinal));
                if (definitionHandle.IsNil) return false;

                var definition = reader.GetTypeDefinition(definitionHandle);
                var baseType = GetEntityTypeName(reader, definition.BaseType);
                if (string.IsNullOrWhiteSpace(baseType)) return false;

                if (string.Equals(baseType, catchExceptionType, StringComparison.Ordinal)) return true;

                currentType = baseType;
            }
        }
        catch (BadImageFormatException)
        {
            return false;
        }

        return false;
    }

    internal static OpCode ReadOpCode(byte[] il, ref int offset)
    {
        var value = il[offset++];
        short key;
        if (value == 0xFE)
            key = unchecked((short)(0xFE00 | il[offset++]));
        else
            key = value;

        return OpCodesByValue.TryGetValue(key, out var opCode) ? opCode : default;
    }

    internal static IEnumerable<IlInstruction> EnumerateInstructions(
        byte[] il,
        int startOffset = 0,
        int? endOffset = null)
    {
        var offset = startOffset;
        var end = endOffset ?? il.Length;
        while (offset < end)
        {
            var instructionOffset = offset;
            var opCode = ReadOpCode(il, ref offset);
            var operandOffset = offset;
            var operandSize = GetOperandSize(opCode.OperandType, il, operandOffset);
            var metadataToken = operandSize == 4 && IsMetadataTokenOperand(opCode.OperandType)
                ? BitConverter.ToInt32(il, operandOffset)
                : (int?)null;
            offset += operandSize;
            yield return new IlInstruction(instructionOffset, opCode, operandOffset, metadataToken);
        }
    }

    internal static int GetOperandSize(OperandType operandType, byte[] il, int operandOffset)
    {
        return operandType switch
        {
            OperandType.InlineNone => 0,
            OperandType.ShortInlineBrTarget => 1,
            OperandType.ShortInlineI => 1,
            OperandType.ShortInlineVar => 1,
            OperandType.InlineVar => 2,
            OperandType.InlineBrTarget => 4,
            OperandType.InlineField => 4,
            OperandType.InlineI => 4,
            OperandType.InlineMethod => 4,
            OperandType.InlineSig => 4,
            OperandType.InlineString => 4,
            OperandType.InlineTok => 4,
            OperandType.InlineType => 4,
            OperandType.ShortInlineR => 4,
            OperandType.InlineI8 => 8,
            OperandType.InlineR => 8,
            OperandType.InlineSwitch => 4 + BitConverter.ToInt32(il, operandOffset) * 4,
            _ => 0
        };
    }

    internal static bool IsMetadataTokenOperand(OperandType operandType)
    {
        return operandType is OperandType.InlineField
            or OperandType.InlineMethod
            or OperandType.InlineTok
            or OperandType.InlineType;
    }

    internal static bool IsIndirectWrite(OpCode opCode)
    {
        return opCode == OpCodes.Stind_I ||
               opCode == OpCodes.Stind_I1 ||
               opCode == OpCodes.Stind_I2 ||
               opCode == OpCodes.Stind_I4 ||
               opCode == OpCodes.Stind_I8 ||
               opCode == OpCodes.Stind_R4 ||
               opCode == OpCodes.Stind_R8 ||
               opCode == OpCodes.Stind_Ref ||
               opCode == OpCodes.Stobj ||
               opCode == OpCodes.Initobj ||
               opCode == OpCodes.Stelem ||
               opCode == OpCodes.Stelem_I ||
               opCode == OpCodes.Stelem_I1 ||
               opCode == OpCodes.Stelem_I2 ||
               opCode == OpCodes.Stelem_I4 ||
               opCode == OpCodes.Stelem_I8 ||
               opCode == OpCodes.Stelem_R4 ||
               opCode == OpCodes.Stelem_R8 ||
               opCode == OpCodes.Stelem_Ref;
    }

    internal static void AddSameAssemblyStaticFieldToken(
        MetadataReader reader,
        int? operandToken,
        IReadOnlyDictionary<string, FieldDefinitionHandle> fieldDefinitionHandlesBySymbol,
        IReadOnlyDictionary<string, FieldDefinitionHandle> fieldDefinitionHandlesByExactKey,
        SortedSet<int> sameAssemblyStaticReadFieldTokens)
    {
        if (operandToken is not null &&
            TryResolveSameAssemblyFieldDefinitionHandle(
                reader,
                operandToken.Value,
                fieldDefinitionHandlesBySymbol,
                fieldDefinitionHandlesByExactKey,
                out var fieldHandle))
            sameAssemblyStaticReadFieldTokens.Add(MetadataTokens.GetToken(fieldHandle));
    }

    internal static void AddField(
        MetadataReader reader,
        int? operandToken,
        IReadOnlyDictionary<string, FieldDefinitionHandle> fieldDefinitionHandlesBySymbol,
        IReadOnlyDictionary<string, FieldDefinitionHandle> fieldDefinitionHandlesByExactKey,
        SortedSet<string> fields)
    {
        if (operandToken is null) return;

        if (TryResolveSameAssemblyFieldDefinitionHandle(
                reader,
                operandToken.Value,
                fieldDefinitionHandlesBySymbol,
                fieldDefinitionHandlesByExactKey,
                out var fieldHandle))
        {
            fields.Add(GetFieldDefinitionSymbol(reader, fieldHandle));
            return;
        }

        fields.Add(ResolveFieldToken(reader, operandToken.Value));
    }

    internal static bool TryResolveSameAssemblyFieldDefinitionHandle(
        MetadataReader reader,
        int metadataToken,
        IReadOnlyDictionary<string, FieldDefinitionHandle> fieldDefinitionHandlesBySymbol,
        IReadOnlyDictionary<string, FieldDefinitionHandle> fieldDefinitionHandlesByExactKey,
        out FieldDefinitionHandle handle)
    {
        handle = default;
        var resolvedHandle = MetadataTokens.Handle(metadataToken);
        switch (resolvedHandle.Kind)
        {
            case HandleKind.FieldDefinition:
                handle = (FieldDefinitionHandle)resolvedHandle;
                return true;
            case HandleKind.MemberReference:
                var memberReferenceHandle = (MemberReferenceHandle)resolvedHandle;
                return fieldDefinitionHandlesBySymbol.TryGetValue(
                           GetMemberReferenceSymbol(reader, memberReferenceHandle), out handle) ||
                       fieldDefinitionHandlesByExactKey.TryGetValue(
                           GetMemberReferenceFieldExactKey(reader, memberReferenceHandle), out handle) ||
                       fieldDefinitionHandlesBySymbol.TryGetValue(
                           GetMemberReferenceFieldLookupSymbol(reader, memberReferenceHandle), out handle) ||
                       fieldDefinitionHandlesByExactKey.TryGetValue(
                           GetMemberReferenceFieldLookupExactKey(reader, memberReferenceHandle), out handle);
            default:
                return false;
        }
    }
}

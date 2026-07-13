internal static class AssemblyEffectSummarizer
{

    private static readonly IReadOnlyDictionary<int, StaticFieldFact> EmptyStaticFieldFacts =
        new Dictionary<int, StaticFieldFact>();

    private static readonly IReadOnlyDictionary<short, OpCode> OpCodesByValue =
        typeof(OpCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(OpCode))
            .Select(field => (OpCode)field.GetValue(null)!)
            .ToDictionary(opCode => opCode.Value);

    public static AssemblyEffectReport Summarize(
        string assemblyPath,
        int? limit,
        IReadOnlyList<string> symbolPrefixes,
        IReadOnlyList<string> exactSymbols,
        IReadOnlyList<string> canonicalKeys,
        bool includeCallees,
        int maxDepth,
        bool includeTransitiveRoots,
        int maxExceptionEdges)
    {
        var assemblySha256 = ComputeFileSha256(assemblyPath);
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        if (!peReader.HasMetadata)
            throw new InvalidOperationException($"Assembly does not contain managed metadata: {assemblyPath}");

        var reader = peReader.GetMetadataReader();
        var module = reader.GetModuleDefinition();
        var assemblyName = reader.IsAssembly
            ? reader.GetString(reader.GetAssemblyDefinition().Name)
            : Path.GetFileNameWithoutExtension(assemblyPath);
        var moduleVersionId = reader.GetGuid(module.Mvid).ToString("D");

        var methodDefinitionHandlesByExactKey = new Dictionary<string, MethodDefinitionHandle>(StringComparer.Ordinal);
        var fieldDefinitionHandlesBySymbol = new Dictionary<string, FieldDefinitionHandle>(StringComparer.Ordinal);
        var fieldDefinitionHandlesByExactKey = new Dictionary<string, FieldDefinitionHandle>(StringComparer.Ordinal);
        var knownMethodReturnValues = new Dictionary<int, TrackedStackValue>();
        var knownMethodReturnValueVisiting = new HashSet<int>();
        var allSummaries = new List<MethodEffectSummary>();
        foreach (var handle in reader.FieldDefinitions)
        {
            fieldDefinitionHandlesBySymbol[GetFieldDefinitionSymbol(reader, handle)] = handle;
            fieldDefinitionHandlesByExactKey[GetFieldExactKey(reader, handle)] = handle;
        }

        foreach (var handle in reader.MethodDefinitions)
            methodDefinitionHandlesByExactKey[GetMethodExactKey(reader, handle)] = handle;

        var handlesToSummarize = GetMethodHandlesToSummarize(
            peReader,
            reader,
            methodDefinitionHandlesByExactKey,
            symbolPrefixes,
            exactSymbols,
            canonicalKeys,
            includeCallees,
            includeTransitiveRoots);
        if (handlesToSummarize is { Count: 0 })
            return new AssemblyEffectReport(
                assemblyName,
                assemblyPath,
                assemblySha256,
                moduleVersionId,
                reader.MethodDefinitions.Count,
                0,
                Array.Empty<MethodEffectSummary>())
            {
                ClassificationMethods = Array.Empty<MethodEffectSummary>()
            };

        var staticFieldFacts = BuildStaticFieldFacts(
            peReader,
            reader,
            methodDefinitionHandlesByExactKey,
            fieldDefinitionHandlesBySymbol,
            fieldDefinitionHandlesByExactKey,
            knownMethodReturnValues,
            knownMethodReturnValueVisiting);
        foreach (var handle in reader.MethodDefinitions)
        {
            if (handlesToSummarize is not null && !handlesToSummarize.Contains(handle)) continue;

            allSummaries.Add(SummarizeMethod(
                peReader,
                reader,
                handle,
                moduleVersionId,
                methodDefinitionHandlesByExactKey,
                fieldDefinitionHandlesBySymbol,
                fieldDefinitionHandlesByExactKey,
                staticFieldFacts,
                knownMethodReturnValues,
                knownMethodReturnValueVisiting));
        }

        if (includeTransitiveRoots)
            allSummaries = EffectSummaryExceptionPropagation.AddTransitiveRootCandidates(
                reader,
                allSummaries,
                maxExceptionEdges,
                ExceptionEscapesPropagationSite);

        var summaries = SelectSummaries(
            allSummaries,
            symbolPrefixes,
            exactSymbols,
            canonicalKeys,
            includeCallees,
            maxDepth,
            limit);

        return new AssemblyEffectReport(
            assemblyName,
            assemblyPath,
            assemblySha256,
            moduleVersionId,
            reader.MethodDefinitions.Count,
            summaries.Length,
            summaries)
        {
            ClassificationMethods = allSummaries.ToArray()
        };
    }

    private static HashSet<MethodDefinitionHandle>? GetMethodHandlesToSummarize(
        PEReader peReader,
        MetadataReader reader,
        IReadOnlyDictionary<string, MethodDefinitionHandle> methodDefinitionHandlesByExactKey,
        IReadOnlyList<string> symbolPrefixes,
        IReadOnlyList<string> exactSymbols,
        IReadOnlyList<string> canonicalKeys,
        bool includeCallees,
        bool includeTransitiveRoots)
    {
        if (symbolPrefixes.Count == 0 && exactSymbols.Count == 0 && canonicalKeys.Count == 0) return null;

        var rootHandles = GetRootMethodHandles(reader, symbolPrefixes, exactSymbols, canonicalKeys);
        if (!includeCallees && !includeTransitiveRoots) return rootHandles;

        return CollectReachableMethodHandles(
            peReader,
            reader,
            methodDefinitionHandlesByExactKey,
            rootHandles);
    }

    private static HashSet<MethodDefinitionHandle> GetRootMethodHandles(
        MetadataReader reader,
        IReadOnlyList<string> symbolPrefixes,
        IReadOnlyList<string> exactSymbols,
        IReadOnlyList<string> canonicalKeys)
    {
        var exactSymbolSet = exactSymbols.Count == 0
            ? null
            : new HashSet<string>(exactSymbols, StringComparer.Ordinal);
        var displayNameSet = canonicalKeys.Count == 0
            ? null
            : new HashSet<string>(canonicalKeys, StringComparer.Ordinal);
        var rootHandles = new HashSet<MethodDefinitionHandle>();
        foreach (var handle in reader.MethodDefinitions)
        {
            var symbol = GetMethodDisplaySymbol(reader, handle);
            if (MatchesSymbolPrefix(symbol, symbolPrefixes))
            {
                rootHandles.Add(handle);
                continue;
            }

            if (exactSymbolSet != null && exactSymbolSet.Contains(symbol))
            {
                rootHandles.Add(handle);
                continue;
            }

            if (displayNameSet != null &&
                displayNameSet.Contains(EcmaStructuralMethodIdentityAdapter.GetCanonicalKey(reader, handle)))
                rootHandles.Add(handle);
        }

        return rootHandles;
    }

    private static HashSet<MethodDefinitionHandle> CollectReachableMethodHandles(
        PEReader peReader,
        MetadataReader reader,
        IReadOnlyDictionary<string, MethodDefinitionHandle> methodDefinitionHandlesByExactKey,
        IReadOnlyCollection<MethodDefinitionHandle> rootHandles)
    {
        var included = new HashSet<MethodDefinitionHandle>();
        if (rootHandles.Count == 0) return included;

        var queue = new Queue<MethodDefinitionHandle>(rootHandles);
        var calleeCache = new Dictionary<MethodDefinitionHandle, MethodDefinitionHandle[]>();
        while (queue.Count > 0)
        {
            var handle = queue.Dequeue();
            if (!included.Add(handle)) continue;

            foreach (var calleeHandle in GetSameAssemblyCallees(
                         peReader,
                         reader,
                         handle,
                         methodDefinitionHandlesByExactKey,
                         calleeCache))
                if (!included.Contains(calleeHandle))
                    queue.Enqueue(calleeHandle);
        }

        return included;
    }

    private static MethodDefinitionHandle[] GetSameAssemblyCallees(
        PEReader peReader,
        MetadataReader reader,
        MethodDefinitionHandle handle,
        IReadOnlyDictionary<string, MethodDefinitionHandle> methodDefinitionHandlesByExactKey,
        Dictionary<MethodDefinitionHandle, MethodDefinitionHandle[]> calleeCache)
    {
        if (calleeCache.TryGetValue(handle, out var cached)) return cached;

        var definition = reader.GetMethodDefinition(handle);
        if (definition.RelativeVirtualAddress == 0) return calleeCache[handle] = Array.Empty<MethodDefinitionHandle>();

        var body = peReader.GetMethodBody(definition.RelativeVirtualAddress);
        var il = body.GetILBytes();
        if (il is null || il.Length == 0) return calleeCache[handle] = Array.Empty<MethodDefinitionHandle>();

        var callees = new HashSet<MethodDefinitionHandle>();
        foreach (var instruction in EnumerateInstructions(il))
        {
            var opCode = instruction.OpCode;
            var operandToken = instruction.MetadataToken;

            if (operandToken is null ||
                (opCode != OpCodes.Call &&
                 opCode != OpCodes.Callvirt &&
                 opCode != OpCodes.Newobj &&
                 opCode != OpCodes.Ldftn &&
                 opCode != OpCodes.Ldvirtftn))
                continue;

            if (TryResolveSameAssemblyMethodDefinitionHandle(
                    reader,
                    operandToken.Value,
                    methodDefinitionHandlesByExactKey,
                    out var calleeHandle))
                callees.Add(calleeHandle);
        }

        cached = callees.ToArray();
        calleeCache[handle] = cached;
        return cached;
    }

    private static bool MatchesSymbolPrefix(string symbol, IReadOnlyList<string> symbolPrefixes)
    {
        return symbolPrefixes.Count == 0 ||
               symbolPrefixes.Any(prefix => symbol.StartsWith(prefix, StringComparison.Ordinal));
    }

    private static MethodEffectSummary[] SelectSummaries(
        IReadOnlyList<MethodEffectSummary> allSummaries,
        IReadOnlyList<string> symbolPrefixes,
        IReadOnlyList<string> exactSymbols,
        IReadOnlyList<string> canonicalKeys,
        bool includeCallees,
        int maxDepth,
        int? limit)
    {
        var hasPrefixRoots = symbolPrefixes.Count > 0;
        var hasExactRoots = exactSymbols.Count > 0 || canonicalKeys.Count > 0;

        IEnumerable<MethodEffectSummary> selected = Array.Empty<MethodEffectSummary>();
        if (hasPrefixRoots)
            selected = !includeCallees
                ? allSummaries.Where(summary => MatchesSymbolPrefix(summary.Symbol, symbolPrefixes))
                : SelectWithCallees(allSummaries, symbolPrefixes, maxDepth);
        else if (!hasExactRoots) selected = allSummaries;

        if (hasExactRoots)
            selected = UnionByIdentity(
                selected,
                SelectExactSummaries(allSummaries, exactSymbols, canonicalKeys));

        if (limit is not null) selected = selected.Take(limit.Value);

        return selected.ToArray();
    }

    private static IEnumerable<MethodEffectSummary> SelectExactSummaries(
        IReadOnlyList<MethodEffectSummary> allSummaries,
        IReadOnlyList<string> exactSymbols,
        IReadOnlyList<string> canonicalKeys)
    {
        var exactSymbolSet = exactSymbols.Count == 0
            ? null
            : new HashSet<string>(exactSymbols, StringComparer.Ordinal);
        var displayNameSet = canonicalKeys.Count == 0
            ? null
            : new HashSet<string>(canonicalKeys, StringComparer.Ordinal);

        return allSummaries.Where(summary =>
            (exactSymbolSet != null && exactSymbolSet.Contains(summary.Symbol)) ||
            (displayNameSet != null && displayNameSet.Contains(summary.CanonicalKey)));
    }

    private static IEnumerable<MethodEffectSummary> UnionByIdentity(
        IEnumerable<MethodEffectSummary> first,
        IEnumerable<MethodEffectSummary> second)
    {
        var seen = new HashSet<StructuralMethodIdentity>();
        foreach (var summary in first)
            if (seen.Add(summary.Identity))
                yield return summary;

        foreach (var summary in second)
            if (seen.Add(summary.Identity))
                yield return summary;
    }

    private static IEnumerable<MethodEffectSummary> SelectWithCallees(
        IReadOnlyList<MethodEffectSummary> allSummaries,
        IReadOnlyList<string> symbolPrefixes,
        int maxDepth)
    {
        var bySymbol = allSummaries
            .GroupBy(summary => summary.Identity)
            .ToDictionary(group => group.Key, group => group.First());

        var included = new HashSet<StructuralMethodIdentity>();
        var orderedIdentities = new List<StructuralMethodIdentity>();
        var queue = new Queue<(StructuralMethodIdentity Identity, int Depth)>();
        foreach (var summary in allSummaries.Where(summary => MatchesSymbolPrefix(summary.Symbol, symbolPrefixes)))
            if (included.Add(summary.Identity))
            {
                orderedIdentities.Add(summary.Identity);
                queue.Enqueue((summary.Identity, 0));
            }

        while (queue.Count > 0)
        {
            var (identity, depth) = queue.Dequeue();
            if ((maxDepth >= 0 && depth >= maxDepth) ||
                !bySymbol.TryGetValue(identity, out var summary))
                continue;

            foreach (var callIdentity in summary.CallIdentities)
                if (bySymbol.ContainsKey(callIdentity) && included.Add(callIdentity))
                {
                    orderedIdentities.Add(callIdentity);
                    queue.Enqueue((callIdentity, depth + 1));
                }
        }

        return orderedIdentities.Select(identity => bySymbol[identity]);
    }

    private static MethodEffectSummary SummarizeMethod(
        PEReader peReader,
        MetadataReader reader,
        MethodDefinitionHandle handle,
        string moduleVersionId,
        IReadOnlyDictionary<string, MethodDefinitionHandle> methodDefinitionHandlesByExactKey,
        IReadOnlyDictionary<string, FieldDefinitionHandle> fieldDefinitionHandlesBySymbol,
        IReadOnlyDictionary<string, FieldDefinitionHandle> fieldDefinitionHandlesByExactKey,
        IReadOnlyDictionary<int, StaticFieldFact> staticFieldFacts,
        Dictionary<int, TrackedStackValue> knownMethodReturnValues,
        HashSet<int> knownMethodReturnValueVisiting)
    {
        var definition = reader.GetMethodDefinition(handle);
        var effects = new SortedSet<string>(StringComparer.Ordinal);
        var calls = new SortedSet<string>(StringComparer.Ordinal);
        var callIdentities = new Dictionary<string, StructuralMethodIdentity>(StringComparer.Ordinal);
        var fields = new SortedSet<string>(StringComparer.Ordinal);
        var staticFields = new SortedSet<string>(StringComparer.Ordinal);
        var sameAssemblyStaticReadFieldTokens = new SortedSet<int>();
        var thrownExceptionTypes = new SortedSet<string>(StringComparer.Ordinal);
        var callSites = new List<CallSiteSummary>();
        var exceptionPropagationSites = new List<ExceptionPropagationSite>();
        string? methodBodySha256 = null;

        if ((definition.Attributes & MethodAttributes.Abstract) != 0) effects.Add("abstract");

        if ((definition.Attributes & MethodAttributes.PinvokeImpl) != 0) effects.Add("pinvoke");

        if ((definition.ImplAttributes & MethodImplAttributes.InternalCall) != 0 ||
            (definition.ImplAttributes & MethodImplAttributes.Native) != 0)
            effects.Add("native_or_internal_call");

        if (definition.RelativeVirtualAddress == 0)
        {
            effects.Add("no_il_body");
        }
        else
        {
            var body = peReader.GetMethodBody(definition.RelativeVirtualAddress);
            var il = body.GetILBytes();
            if (il is not null)
            {
                methodBodySha256 = ComputeSha256(il);
                AnalyzeIl(
                    peReader,
                    reader,
                    il,
                    body.ExceptionRegions,
                    effects,
                    calls,
                    callIdentities,
                    callSites,
                    fields,
                    staticFields,
                    sameAssemblyStaticReadFieldTokens,
                    thrownExceptionTypes,
                    exceptionPropagationSites,
                    methodDefinitionHandlesByExactKey,
                    fieldDefinitionHandlesBySymbol,
                    fieldDefinitionHandlesByExactKey,
                    staticFieldFacts,
                    knownMethodReturnValues,
                    knownMethodReturnValueVisiting);
            }
        }

        var metadataToken = $"0x{MetadataTokens.GetToken(handle):X8}";
        var cacheKey = $"mvid:{moduleVersionId}|token:{metadataToken}|il:{methodBodySha256 ?? "no-il"}";
        var isConstructor = string.Equals(reader.GetString(definition.Name), ".ctor", StringComparison.Ordinal);
        var symbol = GetMethodDisplaySymbol(reader, handle);
        var identity = EcmaStructuralMethodIdentityAdapter.Create(reader, handle);
        var directThrownExceptionSources = thrownExceptionTypes
            .Select(exceptionType => new ExceptionProvenance(
                exceptionType,
                null,
                new[] { identity }))
            .ToArray();
        return new MethodEffectSummary(
            symbol,
            metadataToken,
            definition.RelativeVirtualAddress,
            methodBodySha256,
            cacheKey,
            effects.ToArray(),
            GetRootCandidates(
                    effects,
                    calls,
                    fields,
                    staticFields,
                    sameAssemblyStaticReadFieldTokens,
                    staticFieldFacts,
                    isConstructor)
                .ToArray(),
            Array.Empty<string>(),
            thrownExceptionTypes.ToArray(),
            Array.Empty<string>(),
            directThrownExceptionSources,
            Array.Empty<ExceptionProvenance>(),
            calls.ToArray(),
            fields.ToArray())
        {
            Identity = identity,
            CanonicalCalls = callIdentities.Values
                .Distinct()
                .Select(static callIdentity => callIdentity.ToCanonicalKey())
                .OrderBy(static key => key, StringComparer.Ordinal)
                .ToArray(),
            CallIdentities = callIdentities.Values
                .Distinct()
                .OrderBy(static callIdentity => callIdentity.ToCanonicalKey(), StringComparer.Ordinal)
                .ToArray(),
            IsStatic = (definition.Attributes & MethodAttributes.Static) != 0,
            CallSites = callSites
                .GroupBy(GetCallSiteDeduplicationKey, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(site => site.CanonicalKey, StringComparer.Ordinal)
                .ThenBy(GetCallSiteDeduplicationKey, StringComparer.Ordinal)
                .ToArray(),
            ExceptionPropagationSites = exceptionPropagationSites
                .Distinct()
                .OrderBy(site => site.CalleeIdentity?.ToCanonicalKey(), StringComparer.Ordinal)
                .ThenBy(site => site.InstructionOffset)
                .ToArray(),
            NullableContracts = GetNullableContractSummary(reader, definition)
        };
    }

    private static NullableContractSummary? GetNullableContractSummary(
        MetadataReader reader,
        MethodDefinition definition)
    {
        var returnNotNull = false;
        string? returnNotNullIfNotNull = null;
        var parameters = new List<NullableParameterContractSummary>();

        foreach (var parameterHandle in definition.GetParameters())
        {
            var parameter = reader.GetParameter(parameterHandle);
            var sequence = parameter.SequenceNumber;
            var notNull = false;
            bool? notNullWhen = null;
            bool? maybeNullWhen = null;
            foreach (var attributeHandle in parameter.GetCustomAttributes())
            {
                var attributeName = TryGetCustomAttributeTypeName(reader, attributeHandle);
                switch (attributeName)
                {
                    case "System.Diagnostics.CodeAnalysis.NotNullAttribute":
                        notNull = true;
                        break;
                    case "System.Diagnostics.CodeAnalysis.NotNullWhenAttribute":
                        notNullWhen = TryReadBooleanAttributeArgument(reader, attributeHandle);
                        break;
                    case "System.Diagnostics.CodeAnalysis.MaybeNullWhenAttribute":
                        maybeNullWhen = TryReadBooleanAttributeArgument(reader, attributeHandle);
                        break;
                    case "System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute" when sequence == 0:
                        returnNotNullIfNotNull = TryReadStringAttributeArgument(reader, attributeHandle);
                        break;
                }
            }

            if (sequence == 0)
            {
                returnNotNull = notNull;
                continue;
            }

            if (notNull || notNullWhen.HasValue || maybeNullWhen.HasValue)
                parameters.Add(new NullableParameterContractSummary(
                    sequence - 1,
                    reader.GetString(parameter.Name),
                    notNull,
                    notNullWhen,
                    maybeNullWhen));
        }

        var memberNotNull = new SortedSet<string>(StringComparer.Ordinal);
        var memberNotNullWhen = new List<NullableMemberConditionalContractSummary>();
        foreach (var attributeHandle in definition.GetCustomAttributes())
        {
            var attributeName = TryGetCustomAttributeTypeName(reader, attributeHandle);
            if (string.Equals(
                    attributeName,
                    "System.Diagnostics.CodeAnalysis.MemberNotNullAttribute",
                    StringComparison.Ordinal))
            {
                foreach (var target in ReadMemberTargetArguments(reader, attributeHandle, false).Targets)
                    memberNotNull.Add(target);
            }
            else if (string.Equals(
                         attributeName,
                         "System.Diagnostics.CodeAnalysis.MemberNotNullWhenAttribute",
                         StringComparison.Ordinal))
            {
                var decoded = ReadMemberTargetArguments(reader, attributeHandle, true);
                if (decoded.Condition.HasValue)
                    foreach (var target in decoded.Targets)
                        memberNotNullWhen.Add(new NullableMemberConditionalContractSummary(
                            decoded.Condition.Value,
                            target));
            }
        }

        if (!returnNotNull &&
            returnNotNullIfNotNull == null &&
            parameters.Count == 0 &&
            memberNotNull.Count == 0 &&
            memberNotNullWhen.Count == 0)
            return null;

        return new NullableContractSummary(
            returnNotNull,
            returnNotNullIfNotNull,
            parameters.OrderBy(static parameter => parameter.Ordinal).ToArray(),
            memberNotNull.ToArray(),
            memberNotNullWhen
                .OrderBy(static contract => contract.When)
                .ThenBy(static contract => contract.Member, StringComparer.Ordinal)
                .ToArray());
    }

    private static bool? TryReadBooleanAttributeArgument(MetadataReader reader, CustomAttributeHandle handle)
    {
        try
        {
            var blob = reader.GetBlobReader(reader.GetCustomAttribute(handle).Value);
            return blob.ReadUInt16() == 1 ? blob.ReadBoolean() : null;
        }
        catch (BadImageFormatException)
        {
            return null;
        }
    }

    private static string? TryReadStringAttributeArgument(MetadataReader reader, CustomAttributeHandle handle)
    {
        try
        {
            var blob = reader.GetBlobReader(reader.GetCustomAttribute(handle).Value);
            return blob.ReadUInt16() == 1 ? blob.ReadSerializedString() : null;
        }
        catch (BadImageFormatException)
        {
            return null;
        }
    }

    private static (bool? Condition, string[] Targets) ReadMemberTargetArguments(
        MetadataReader reader,
        CustomAttributeHandle handle,
        bool hasCondition)
    {
        try
        {
            var blob = reader.GetBlobReader(reader.GetCustomAttribute(handle).Value);
            if (blob.ReadUInt16() != 1) return (null, Array.Empty<string>());

            bool? condition = hasCondition ? blob.ReadBoolean() : null;
            var first = blob.ReadSerializedString();
            return string.IsNullOrWhiteSpace(first)
                ? (condition, Array.Empty<string>())
                : (condition, new[] { first! });
        }
        catch (BadImageFormatException)
        {
            return (null, Array.Empty<string>());
        }
    }

    private static string ComputeFileSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha256 = SHA256.Create();
        return Convert.ToHexString(sha256.ComputeHash(stream)).ToLowerInvariant();
    }

    private static string ComputeSha256(byte[] bytes)
    {
        using var sha256 = SHA256.Create();
        return Convert.ToHexString(sha256.ComputeHash(bytes)).ToLowerInvariant();
    }

    private static IEnumerable<string> GetRootCandidates(
        IEnumerable<string> effects,
        IEnumerable<string> calls,
        IEnumerable<string> fields,
        IEnumerable<string> staticReadFields,
        IReadOnlySet<int> sameAssemblyStaticReadFieldTokens,
        IReadOnlyDictionary<int, StaticFieldFact> staticFieldFacts,
        bool isConstructor)
    {
        var roots = new SortedSet<string>(StringComparer.Ordinal);
        var effectSet = new HashSet<string>(effects, StringComparer.Ordinal);
        var callSet = new HashSet<string>(calls, StringComparer.Ordinal);
        var fieldSet = new HashSet<string>(fields, StringComparer.Ordinal);
        var staticReadFieldSet = new HashSet<string>(staticReadFields, StringComparer.Ordinal);
        foreach (var effect in effects)
            switch (effect)
            {
                case "pinvoke":
                    roots.Add("pinvoke");
                    break;
                case "native_or_internal_call":
                    roots.Add("runtime_native_or_internal");
                    break;
                case "no_il_body":
                    roots.Add("metadata_only_or_external");
                    break;
                case "reads_static_field":
                    if (IsSafeStaticConstantRead(staticReadFieldSet, sameAssemblyStaticReadFieldTokens,
                            staticFieldFacts))
                        roots.Add("safe_static_constant_read");
                    else if (IsSafeStaticCacheRead(staticReadFieldSet, callSet, sameAssemblyStaticReadFieldTokens,
                                 staticFieldFacts))
                        roots.Add("safe_static_cache_read");
                    else
                        roots.Add("global_state_read");
                    break;
                case "reads_instance_field":
                    if (IsThreadingRuntimeStateRead(fieldSet)) roots.Add("global_state_read");
                    break;
                case "writes_static_field":
                    roots.Add("global_state_write");
                    break;
                case "writes_instance_field":
                    roots.Add(IsFreshOwnedObjectWrite(effectSet, callSet, isConstructor)
                        ? "fresh_owned_object_write"
                        : "object_state_write");
                    break;
                case "writes_indirect_memory":
                    roots.Add(IsFreshOwnedMemoryWrite(effectSet, callSet)
                        ? "fresh_owned_memory_write"
                        : "caller_visible_memory_write");
                    break;
                case "indirect_call":
                case "virtual_call":
                    roots.Add("dynamic_dispatch");
                    break;
                case "throws":
                    roots.Add("throw");
                    break;
                case "block_memory_write":
                    roots.Add("unsafe_or_block_memory_write");
                    break;
            }

        return roots;
    }

    private static bool IsThreadingRuntimeStateRead(IReadOnlySet<string> fields)
    {
        foreach (var field in fields)
        {
            if (!(field.StartsWith("System.Threading.", StringComparison.Ordinal) ||
                  field.StartsWith("System.Threading.Tasks.", StringComparison.Ordinal)))
                continue;

            if (field.EndsWith("._state", StringComparison.Ordinal) ||
                field.EndsWith(".m_stateFlags", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static bool IsSafeStaticCacheRead(
        IReadOnlySet<string> fields,
        IReadOnlySet<string> calls,
        IReadOnlySet<int> sameAssemblyStaticReadFieldTokens,
        IReadOnlyDictionary<int, StaticFieldFact> staticFieldFacts)
    {
        if (fields.Count > 0 &&
            HasOnlySameAssemblyFieldFacts(
                fields,
                sameAssemblyStaticReadFieldTokens,
                staticFieldFacts,
                static kind => kind is StaticFieldFactKind.Constant or StaticFieldFactKind.StableIdentity,
                IsKnownExternalSafeStaticCacheField))
            return true;

        return calls.Count == 1 && calls.Any(static call =>
            call.StartsWith("System.ReadOnlySpan`1<byte>..ctor(void*, int)", StringComparison.Ordinal));
    }

    private static bool IsKnownExternalSafeStaticCacheField(string field)
    {
        if (
            field.StartsWith("System.Array+EmptyArray`1", StringComparison.Ordinal) &&
            field.EndsWith(".Value", StringComparison.Ordinal))
            return true;

        if (
            string.Equals(field, "System.Globalization.CultureInfo.s_InvariantCultureInfo", StringComparison.Ordinal) ||
            string.Equals(field, "System.String.Empty", StringComparison.Ordinal) ||
            string.Equals(field, "System.Text.ASCIIEncoding.s_default", StringComparison.Ordinal) ||
            string.Equals(field, "System.UriHelper.Unreserved", StringComparison.Ordinal) ||
            string.Equals(field, "System.Globalization.TextInfo.Invariant", StringComparison.Ordinal) ||
            string.Equals(field, "System.Globalization.CompareInfo.Invariant", StringComparison.Ordinal) ||
            string.Equals(field, "System.Net.IPAddress.IPv6Loopback", StringComparison.Ordinal) ||
            string.Equals(field, "System.Net.IPAddress.Loopback", StringComparison.Ordinal) ||
            string.Equals(field, "System.Net.IPAddress.s_loopbackMappedToIPv6", StringComparison.Ordinal) ||
            (field.StartsWith("System.Linq.EmptyPartition`1", StringComparison.Ordinal) &&
             field.EndsWith(".Instance", StringComparison.Ordinal)))
            return true;

        if (
            (field.StartsWith("System.Collections.Generic.Comparer`1", StringComparison.Ordinal) ||
             field.StartsWith("System.Collections.Generic.EqualityComparer`1", StringComparison.Ordinal)) &&
            field.EndsWith(".<Default>k__BackingField", StringComparison.Ordinal))
            return true;

        return false;
    }

    private static bool IsSafeStaticConstantRead(
        IReadOnlySet<string> fields,
        IReadOnlySet<int> sameAssemblyStaticReadFieldTokens,
        IReadOnlyDictionary<int, StaticFieldFact> staticFieldFacts)
    {
        return fields.Count > 0 &&
               HasOnlySameAssemblyFieldFacts(
                   fields,
                   sameAssemblyStaticReadFieldTokens,
                   staticFieldFacts,
                   static kind => kind == StaticFieldFactKind.Constant,
                   static field =>
                       string.Equals(field, "IsLittleEndian", StringComparison.Ordinal) ||
                       string.Equals(field, "System.BitConverter.IsLittleEndian", StringComparison.Ordinal));
    }

    private static bool HasOnlySameAssemblyFieldFacts(
        IReadOnlySet<string> fields,
        IReadOnlySet<int> sameAssemblyStaticReadFieldTokens,
        IReadOnlyDictionary<int, StaticFieldFact> staticFieldFacts,
        Func<StaticFieldFactKind, bool> sameAssemblyFieldPredicate,
        Func<string, bool> externalFieldPredicate)
    {
        if (fields.Count == 0) return false;

        var sameAssemblyFieldSymbols = new HashSet<string>(StringComparer.Ordinal);
        foreach (var fieldToken in sameAssemblyStaticReadFieldTokens)
        {
            if (!staticFieldFacts.TryGetValue(fieldToken, out var fact)) return false;

            if (!sameAssemblyFieldPredicate(fact.Kind) &&
                !externalFieldPredicate(fact.Symbol))
                return false;

            sameAssemblyFieldSymbols.Add(fact.Symbol);
        }

        foreach (var field in fields)
        {
            if (sameAssemblyFieldSymbols.Contains(field)) continue;

            if (!externalFieldPredicate(field)) return false;
        }

        return true;
    }

    private static bool IsFreshOwnedMemoryWrite(
        IReadOnlySet<string> effects,
        IReadOnlySet<string> calls)
    {
        if (!effects.Contains("writes_indirect_memory") || !effects.Contains("allocates_array")) return false;

        if (effects.Contains("writes_static_field") ||
            effects.Contains("writes_instance_field") ||
            effects.Contains("reads_static_field") ||
            effects.Contains("reads_instance_field") ||
            effects.Contains("indirect_call") ||
            effects.Contains("virtual_call") ||
            effects.Contains("block_memory_write"))
            return false;

        return calls.All(PurityClassificationEngine.IsPurityNeutralIntrinsicHelperCall);
    }

    private static bool IsFreshOwnedObjectWrite(
        IReadOnlySet<string> effects,
        IReadOnlySet<string> calls,
        bool isConstructor)
    {
        if (!effects.Contains("writes_instance_field")) return false;

        if (!isConstructor && !effects.Contains("allocates_object")) return false;

        if (effects.Contains("writes_static_field") ||
            effects.Contains("reads_static_field") ||
            effects.Contains("reads_instance_field") ||
            effects.Contains("writes_indirect_memory") ||
            effects.Contains("indirect_call") ||
            effects.Contains("virtual_call") ||
            effects.Contains("block_memory_write"))
            return false;

        return calls.All(IsFreshObjectInitializationHelperCall);
    }

    private static bool IsFreshObjectInitializationHelperCall(string callSymbol)
    {
        return PurityClassificationEngine.IsPurityNeutralIntrinsicHelperCall(callSymbol) ||
               callSymbol.Contains(".ctor(", StringComparison.Ordinal);
    }

    private static void AnalyzeIl(
        PEReader peReader,
        MetadataReader reader,
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
        List<ExceptionPropagationSite> exceptionPropagationSites,
        IReadOnlyDictionary<string, MethodDefinitionHandle> methodDefinitionHandlesByExactKey,
        IReadOnlyDictionary<string, FieldDefinitionHandle> fieldDefinitionHandlesBySymbol,
        IReadOnlyDictionary<string, FieldDefinitionHandle> fieldDefinitionHandlesByExactKey,
        IReadOnlyDictionary<int, StaticFieldFact> staticFieldFacts,
        Dictionary<int, TrackedStackValue> knownMethodReturnValues,
        HashSet<int> knownMethodReturnValueVisiting)
    {
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
                        methodDefinitionHandlesByExactKey);
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
                            peReader,
                            reader,
                            operandToken,
                            trackedStack,
                            calledSymbol,
                            signature,
                            argumentValues,
                            opCode == OpCodes.Newobj,
                            methodDefinitionHandlesByExactKey,
                            fieldDefinitionHandlesBySymbol,
                            fieldDefinitionHandlesByExactKey,
                            staticFieldFacts,
                            knownMethodReturnValues,
                            knownMethodReturnValueVisiting);
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
                AddField(reader, operandToken, fieldDefinitionHandlesBySymbol, fieldDefinitionHandlesByExactKey,
                    fields);
            }
            else if (opCode == OpCodes.Ldsfld || opCode == OpCodes.Ldsflda)
            {
                effects.Add("reads_static_field");
                AddField(reader, operandToken, fieldDefinitionHandlesBySymbol, fieldDefinitionHandlesByExactKey,
                    fields);
                AddField(reader, operandToken, fieldDefinitionHandlesBySymbol, fieldDefinitionHandlesByExactKey,
                    staticReadFields);
                AddSameAssemblyStaticFieldToken(
                    reader,
                    operandToken,
                    fieldDefinitionHandlesBySymbol,
                    fieldDefinitionHandlesByExactKey,
                    sameAssemblyStaticReadFieldTokens);
            }
            else if (opCode == OpCodes.Stfld)
            {
                effects.Add("writes_instance_field");
                AddField(reader, operandToken, fieldDefinitionHandlesBySymbol, fieldDefinitionHandlesByExactKey,
                    fields);
            }
            else if (opCode == OpCodes.Stsfld)
            {
                effects.Add("writes_static_field");
                AddField(reader, operandToken, fieldDefinitionHandlesBySymbol, fieldDefinitionHandlesByExactKey,
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
                        methodDefinitionHandlesByExactKey);
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
                    reader,
                    il,
                    opCode,
                    operandOffset,
                    operandToken,
                    trackedStack,
                    trackedLocals,
                    fieldDefinitionHandlesBySymbol,
                    fieldDefinitionHandlesByExactKey,
                    staticFieldFacts);

            suppressDynamicDispatchForNextCallvirt = false;
        }
    }

    private static string GetCallSiteDeduplicationKey(CallSiteSummary callSite)
    {
        var argumentEvidenceKey = string.Join(
            ";",
            callSite.ArgumentEvidence.Select(static evidence =>
                $"{evidence.Target}:{evidence.ParameterIndex?.ToString() ?? string.Empty}:{evidence.Type}:{evidence.Value}"));
        return $"{callSite.CanonicalKey}|dynamic:{callSite.UsesDynamicDispatch}|evidence:{argumentEvidenceKey}";
    }

    private static CallSiteSummary CreateCallSiteSummary(
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

    private static void PushCallReturnValue(
        PEReader peReader,
        MetadataReader reader,
        int? operandToken,
        List<TrackedStackValue> trackedStack,
        string calledSymbol,
        CallTargetSignature signature,
        IReadOnlyList<TrackedStackValue> argumentValues,
        bool isObjectConstruction,
        IReadOnlyDictionary<string, MethodDefinitionHandle> methodDefinitionHandlesByExactKey,
        IReadOnlyDictionary<string, FieldDefinitionHandle> fieldDefinitionHandlesBySymbol,
        IReadOnlyDictionary<string, FieldDefinitionHandle> fieldDefinitionHandlesByExactKey,
        IReadOnlyDictionary<int, StaticFieldFact> staticFieldFacts,
        Dictionary<int, TrackedStackValue> knownMethodReturnValues,
        HashSet<int> knownMethodReturnValueVisiting)
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
            peReader,
            reader,
            operandToken,
            calledSymbol,
            argumentValues,
            methodDefinitionHandlesByExactKey,
            fieldDefinitionHandlesBySymbol,
            fieldDefinitionHandlesByExactKey,
            staticFieldFacts,
            knownMethodReturnValues,
            knownMethodReturnValueVisiting,
            out var returnValue)
            ? returnValue
            : TrackedStackValue.Unknown);
    }

    private static void ApplyTrackedStackTransition(
        MetadataReader reader,
        byte[] il,
        OpCode opCode,
        int operandOffset,
        int? operandToken,
        List<TrackedStackValue> trackedStack,
        Dictionary<int, TrackedStackValue> trackedLocals,
        IReadOnlyDictionary<string, FieldDefinitionHandle> fieldDefinitionHandlesBySymbol,
        IReadOnlyDictionary<string, FieldDefinitionHandle> fieldDefinitionHandlesByExactKey,
        IReadOnlyDictionary<int, StaticFieldFact> staticFieldFacts)
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
                reader,
                operandToken,
                fieldDefinitionHandlesBySymbol,
                fieldDefinitionHandlesByExactKey,
                staticFieldFacts,
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

    private static bool TryGetKnownTrackedStaticFieldValue(
        MetadataReader reader,
        int? operandToken,
        IReadOnlyDictionary<string, FieldDefinitionHandle> fieldDefinitionHandlesBySymbol,
        IReadOnlyDictionary<string, FieldDefinitionHandle> fieldDefinitionHandlesByExactKey,
        IReadOnlyDictionary<int, StaticFieldFact> staticFieldFacts,
        out TrackedStackValue trackedValue)
    {
        trackedValue = TrackedStackValue.Unknown;
        if (operandToken is null) return false;

        if (TryResolveSameAssemblyFieldDefinitionHandle(
                reader,
                operandToken.Value,
                fieldDefinitionHandlesBySymbol,
                fieldDefinitionHandlesByExactKey,
                out var fieldHandle) &&
            staticFieldFacts.TryGetValue(MetadataTokens.GetToken(fieldHandle), out var staticFieldFact) &&
            !staticFieldFact.TrackedValue.IsUnknown)
        {
            trackedValue = staticFieldFact.TrackedValue;
            return true;
        }

        return TryGetKnownStringComparerIdentity(
            ResolveFieldToken(reader, operandToken.Value),
            out trackedValue);
    }

    private static bool TryGetKnownCallReturnValue(
        PEReader peReader,
        MetadataReader reader,
        int? operandToken,
        string calledSymbol,
        IReadOnlyList<TrackedStackValue> argumentValues,
        IReadOnlyDictionary<string, MethodDefinitionHandle> methodDefinitionHandlesByExactKey,
        IReadOnlyDictionary<string, FieldDefinitionHandle> fieldDefinitionHandlesBySymbol,
        IReadOnlyDictionary<string, FieldDefinitionHandle> fieldDefinitionHandlesByExactKey,
        IReadOnlyDictionary<int, StaticFieldFact> staticFieldFacts,
        Dictionary<int, TrackedStackValue> knownMethodReturnValues,
        HashSet<int> knownMethodReturnValueVisiting,
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
                reader,
                operandToken.Value,
                methodDefinitionHandlesByExactKey,
                out var methodDefinitionHandle) &&
            TryGetKnownMethodReturnValue(
                peReader,
                reader,
                methodDefinitionHandle,
                methodDefinitionHandlesByExactKey,
                fieldDefinitionHandlesBySymbol,
                fieldDefinitionHandlesByExactKey,
                staticFieldFacts,
                knownMethodReturnValues,
                knownMethodReturnValueVisiting,
                out trackedValue))
            return true;

        trackedValue = TrackedStackValue.Unknown;
        return false;
    }

    private static bool TryGetKnownMethodReturnValue(
        PEReader peReader,
        MetadataReader reader,
        MethodDefinitionHandle handle,
        IReadOnlyDictionary<string, MethodDefinitionHandle> methodDefinitionHandlesByExactKey,
        IReadOnlyDictionary<string, FieldDefinitionHandle> fieldDefinitionHandlesBySymbol,
        IReadOnlyDictionary<string, FieldDefinitionHandle> fieldDefinitionHandlesByExactKey,
        IReadOnlyDictionary<int, StaticFieldFact> staticFieldFacts,
        Dictionary<int, TrackedStackValue> knownMethodReturnValues,
        HashSet<int> knownMethodReturnValueVisiting,
        out TrackedStackValue trackedValue)
    {
        var metadataToken = MetadataTokens.GetToken(handle);
        if (knownMethodReturnValues.TryGetValue(metadataToken, out trackedValue)) return !trackedValue.IsUnknown;

        if (!knownMethodReturnValueVisiting.Add(metadataToken))
        {
            trackedValue = TrackedStackValue.Unknown;
            return false;
        }

        try
        {
            trackedValue = AnalyzeKnownMethodReturnValue(
                peReader,
                reader,
                handle,
                methodDefinitionHandlesByExactKey,
                fieldDefinitionHandlesBySymbol,
                fieldDefinitionHandlesByExactKey,
                staticFieldFacts,
                knownMethodReturnValues,
                knownMethodReturnValueVisiting);
            knownMethodReturnValues[metadataToken] = trackedValue;
            return !trackedValue.IsUnknown;
        }
        finally
        {
            knownMethodReturnValueVisiting.Remove(metadataToken);
        }
    }

    private static TrackedStackValue AnalyzeKnownMethodReturnValue(
        PEReader peReader,
        MetadataReader reader,
        MethodDefinitionHandle handle,
        IReadOnlyDictionary<string, MethodDefinitionHandle> methodDefinitionHandlesByExactKey,
        IReadOnlyDictionary<string, FieldDefinitionHandle> fieldDefinitionHandlesBySymbol,
        IReadOnlyDictionary<string, FieldDefinitionHandle> fieldDefinitionHandlesByExactKey,
        IReadOnlyDictionary<int, StaticFieldFact> staticFieldFacts,
        Dictionary<int, TrackedStackValue> knownMethodReturnValues,
        HashSet<int> knownMethodReturnValueVisiting)
    {
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
                        peReader,
                        reader,
                        operandToken,
                        trackedStack,
                        ResolveMethodExactKey(reader, operandToken.Value),
                        calledSignature,
                        argumentValues,
                        opCode == OpCodes.Newobj,
                        methodDefinitionHandlesByExactKey,
                        fieldDefinitionHandlesBySymbol,
                        fieldDefinitionHandlesByExactKey,
                        staticFieldFacts,
                        knownMethodReturnValues,
                        knownMethodReturnValueVisiting);
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
                reader,
                il,
                opCode,
                operandOffset,
                operandToken,
                trackedStack,
                trackedLocals,
                fieldDefinitionHandlesBySymbol,
                fieldDefinitionHandlesByExactKey,
                staticFieldFacts);
        }

        return knownReturnValue ?? TrackedStackValue.Unknown;
    }

    private static bool TryGetKnownStringComparerIdentity(string symbol, out TrackedStackValue trackedValue)
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

    private static bool TryGetStringComparisonValueName(int value, out string name)
    {
        if (Enum.IsDefined(typeof(StringComparison), value))
        {
            name = $"System.StringComparison.{(StringComparison)value}";
            return true;
        }

        name = string.Empty;
        return false;
    }

    private static bool TryGetStringComparerIdentityFromComparison(int comparisonValue,
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

    private static bool TryGetCallTargetSignature(
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

    private static CallTargetSignature GetMethodDefinitionCallTargetSignature(
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

    private static CallTargetSignature GetMemberReferenceCallTargetSignature(
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

    private static CallTargetSignature GetMethodSpecificationCallTargetSignature(
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

    private static TrackedStackValue[] PopTrackedStackValues(List<TrackedStackValue> trackedStack, int count)
    {
        var values = new TrackedStackValue[count];
        for (var index = count - 1; index >= 0; index--) values[index] = PopTrackedStackValue(trackedStack);

        return values;
    }

    private static TrackedStackValue PopTrackedStackValue(List<TrackedStackValue> trackedStack)
    {
        if (trackedStack.Count == 0) return TrackedStackValue.Unknown;

        var lastIndex = trackedStack.Count - 1;
        var value = trackedStack[lastIndex];
        trackedStack.RemoveAt(lastIndex);
        return value;
    }

    private static string? PeekTrackedExceptionType(List<TrackedStackValue> trackedStack)
    {
        return trackedStack.Count == 0 || string.IsNullOrWhiteSpace(trackedStack[^1].KnownExceptionType)
            ? null
            : trackedStack[^1].KnownExceptionType;
    }

    private static bool TryGetStackPopCount(StackBehaviour behavior, out int count)
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

    private static bool TryGetStackPushCount(StackBehaviour behavior, out int count)
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

    private static bool ShouldResetTrackedState(OpCode opCode)
    {
        return opCode.FlowControl is FlowControl.Branch
            or FlowControl.Cond_Branch
            or FlowControl.Return
            or FlowControl.Throw;
    }

    private static BranchTrackedState CaptureTrackedState(
        List<TrackedStackValue> trackedStack,
        Dictionary<int, TrackedStackValue> trackedLocals)
    {
        return new BranchTrackedState(
            new List<TrackedStackValue>(trackedStack),
            new Dictionary<int, TrackedStackValue>(trackedLocals));
    }

    private static void RestoreTrackedState(
        List<TrackedStackValue> trackedStack,
        Dictionary<int, TrackedStackValue> trackedLocals,
        BranchTrackedState branchState)
    {
        trackedStack.Clear();
        trackedStack.AddRange(branchState.Stack);

        trackedLocals.Clear();
        foreach (var pair in branchState.Locals) trackedLocals[pair.Key] = pair.Value;
    }

    private static bool TrackedStatesEqual(
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

    private static bool IsKnownStableIdentityInitializerCall(string calledSymbol)
    {
        return calledSymbol.StartsWith("System.Array.Empty<", StringComparison.Ordinal);
    }

    private static StaticFieldInitializerValue[] PopStaticFieldInitializerValues(
        List<StaticFieldInitializerValue> trackedStack,
        int count)
    {
        var values = new StaticFieldInitializerValue[count];
        for (var index = count - 1; index >= 0; index--) values[index] = PopStaticFieldInitializerValue(trackedStack);

        return values;
    }

    private static StaticFieldInitializerValue PopStaticFieldInitializerValue(
        List<StaticFieldInitializerValue> trackedStack)
    {
        if (trackedStack.Count == 0) return StaticFieldInitializerValue.Unknown;

        var lastIndex = trackedStack.Count - 1;
        var value = trackedStack[lastIndex];
        trackedStack.RemoveAt(lastIndex);
        return value;
    }

    private static bool TryCreateStaticFieldInitializerValue(
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

    private static bool TryGetTrackedStaticFieldInitializerValue(
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

    private static bool TryGetBranchTargetOffset(
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

    private static bool TryGetStoreLocalIndex(OpCode opCode, byte[] il, int operandOffset, out int localIndex)
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

    private static bool TryGetPushedInt32Constant(OpCode opCode, byte[] il, int operandOffset, out int value)
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

    private static bool TryGetLoadLocalIndex(OpCode opCode, byte[] il, int operandOffset, out int localIndex)
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

    private static bool TryGetLocalIndex(
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

    private static string? TryGetConstructedExceptionType(string? constructorSymbol)
    {
        if (string.IsNullOrWhiteSpace(constructorSymbol)) return null;

        var ctorIndex = constructorSymbol.IndexOf("..ctor(", StringComparison.Ordinal);
        if (ctorIndex <= 0) return null;

        var typeName = constructorSymbol.Substring(0, ctorIndex);
        return typeName.EndsWith("Exception", StringComparison.Ordinal) ? typeName : null;
    }

    private static string? TryResolveRethrowExceptionType(
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

    private static ExceptionPropagationSite CreateExceptionPropagationSite(
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

    private static bool ExceptionEscapesPropagationSite(
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

    private static bool IsEscapingThrow(
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

    private static string[] GetHandlingCatchExceptionTypes(
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

    private static bool IsShadowedByDefinitelyThrowingFinally(
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

    private static bool FinallyHandlerDefinitelyThrows(byte[] il, int handlerOffset, int handlerLength)
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

    private static bool TryGetEnclosingCatchRegion(
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

    private static string? GetEnclosingCatchExceptionType(
        MetadataReader reader,
        ImmutableArray<ExceptionRegion> exceptionRegions,
        int instructionOffset)
    {
        return TryGetEnclosingCatchRegion(exceptionRegions, instructionOffset, out var catchRegion)
            ? GetCatchExceptionType(reader, catchRegion)
            : null;
    }

    private static bool ContainsOffset(int startOffset, int length, int instructionOffset)
    {
        return instructionOffset >= startOffset && instructionOffset < startOffset + length;
    }

    private static string? GetCatchExceptionType(MetadataReader reader, ExceptionRegion exceptionRegion)
    {
        if (exceptionRegion.Kind != ExceptionRegionKind.Catch) return null;

        if (exceptionRegion.CatchType.IsNil) return "System.Exception";

        return GetEntityTypeName(reader, exceptionRegion.CatchType);
    }

    private static string? GetEntityTypeName(MetadataReader reader, EntityHandle handle)
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

    private static string GetExceptionTypeDefinitionName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var definition = reader.GetTypeDefinition(handle);
        return GetQualifiedTypeName(
            reader.GetString(definition.Namespace),
            reader.GetString(definition.Name));
    }

    private static string GetExceptionTypeReferenceName(MetadataReader reader, TypeReferenceHandle handle)
    {
        var reference = reader.GetTypeReference(handle);
        return GetQualifiedTypeName(
            reader.GetString(reference.Namespace),
            reader.GetString(reference.Name));
    }

    private static string GetQualifiedTypeName(string typeNamespace, string typeName)
    {
        return string.IsNullOrWhiteSpace(typeNamespace)
            ? typeName
            : typeNamespace + "." + typeName;
    }

    private static bool CatchHandlesException(
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

    private static bool IsDefinedTypeDerivedFrom(
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

    private static OpCode ReadOpCode(byte[] il, ref int offset)
    {
        var value = il[offset++];
        short key;
        if (value == 0xFE)
            key = unchecked((short)(0xFE00 | il[offset++]));
        else
            key = value;

        return OpCodesByValue.TryGetValue(key, out var opCode) ? opCode : default;
    }

    private static IEnumerable<IlInstruction> EnumerateInstructions(
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

    private static int GetOperandSize(OperandType operandType, byte[] il, int operandOffset)
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

    private static bool IsMetadataTokenOperand(OperandType operandType)
    {
        return operandType is OperandType.InlineField
            or OperandType.InlineMethod
            or OperandType.InlineTok
            or OperandType.InlineType;
    }

    private static bool IsIndirectWrite(OpCode opCode)
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

    private static Dictionary<int, StaticFieldFact> BuildStaticFieldFacts(
        PEReader peReader,
        MetadataReader reader,
        IReadOnlyDictionary<string, MethodDefinitionHandle> methodDefinitionHandlesByExactKey,
        IReadOnlyDictionary<string, FieldDefinitionHandle> fieldDefinitionHandlesBySymbol,
        IReadOnlyDictionary<string, FieldDefinitionHandle> fieldDefinitionHandlesByExactKey,
        Dictionary<int, TrackedStackValue> knownMethodReturnValues,
        HashSet<int> knownMethodReturnValueVisiting)
    {
        var usageByFieldToken = ScanStaticFieldUsage(
            peReader,
            reader,
            fieldDefinitionHandlesBySymbol,
            fieldDefinitionHandlesByExactKey);
        var initializerAssignmentsByFieldToken = AnalyzeStaticFieldInitializerAssignments(
            peReader,
            reader,
            methodDefinitionHandlesByExactKey,
            fieldDefinitionHandlesBySymbol,
            fieldDefinitionHandlesByExactKey,
            knownMethodReturnValues,
            knownMethodReturnValueVisiting);
        var facts = new Dictionary<int, StaticFieldFact>();
        foreach (var handle in reader.FieldDefinitions)
        {
            var definition = reader.GetFieldDefinition(handle);
            if ((definition.Attributes & FieldAttributes.Static) == 0) continue;

            var fieldToken = MetadataTokens.GetToken(handle);
            var factKind = StaticFieldFactKind.Unknown;
            if ((definition.Attributes & FieldAttributes.Literal) != 0 ||
                (definition.Attributes & FieldAttributes.HasFieldRVA) != 0)
            {
                factKind = StaticFieldFactKind.Constant;
            }
            else if ((definition.Attributes & FieldAttributes.InitOnly) != 0 &&
                     !HasRejectedStaticFieldStorageAttribute(reader, definition) &&
                     usageByFieldToken.TryGetValue(fieldToken, out var usage) &&
                     !usage.HasAddressExposure &&
                     !usage.HasWritesOutsideTypeInitializer &&
                     usage.TotalWriteCount == 1 &&
                     usage.OwningTypeInitializerWriteCount == 1 &&
                     initializerAssignmentsByFieldToken.TryGetValue(fieldToken, out var assignment))
            {
                factKind = assignment.Kind switch
                {
                    StaticFieldInitializerValueKind.Constant => StaticFieldFactKind.Constant,
                    StaticFieldInitializerValueKind.StableIdentity => StaticFieldFactKind.StableIdentity,
                    _ => StaticFieldFactKind.Unknown
                };
                facts[fieldToken] = new StaticFieldFact(GetFieldDefinitionSymbol(reader, handle), factKind,
                    assignment.TrackedValue);
                continue;
            }

            facts[fieldToken] = new StaticFieldFact(GetFieldDefinitionSymbol(reader, handle), factKind,
                TrackedStackValue.Unknown);
        }

        return facts;
    }

    private static Dictionary<int, StaticFieldUsage> ScanStaticFieldUsage(
        PEReader peReader,
        MetadataReader reader,
        IReadOnlyDictionary<string, FieldDefinitionHandle> fieldDefinitionHandlesBySymbol,
        IReadOnlyDictionary<string, FieldDefinitionHandle> fieldDefinitionHandlesByExactKey)
    {
        var usageByFieldToken = new Dictionary<int, StaticFieldUsage>();
        foreach (var methodHandle in reader.MethodDefinitions)
        {
            var methodDefinition = reader.GetMethodDefinition(methodHandle);
            if (methodDefinition.RelativeVirtualAddress == 0) continue;

            var body = peReader.GetMethodBody(methodDefinition.RelativeVirtualAddress);
            var il = body.GetILBytes();
            if (il is null) continue;

            var declaringTypeHandle = methodDefinition.GetDeclaringType();
            var isTypeInitializer =
                string.Equals(reader.GetString(methodDefinition.Name), ".cctor", StringComparison.Ordinal);
            foreach (var instruction in EnumerateInstructions(il))
            {
                var opCode = instruction.OpCode;
                var operandToken = instruction.MetadataToken;

                if (operandToken is null) continue;

                if (!TryResolveSameAssemblyFieldDefinitionHandle(
                        reader,
                        operandToken.Value,
                        fieldDefinitionHandlesBySymbol,
                        fieldDefinitionHandlesByExactKey,
                        out var fieldHandle))
                    continue;

                var fieldDefinition = reader.GetFieldDefinition(fieldHandle);
                if ((fieldDefinition.Attributes & FieldAttributes.Static) == 0) continue;

                var fieldToken = MetadataTokens.GetToken(fieldHandle);
                usageByFieldToken.TryGetValue(fieldToken, out var usage);
                if (opCode == OpCodes.Ldsflda)
                {
                    usage.HasAddressExposure = true;
                }
                else if (opCode == OpCodes.Stsfld)
                {
                    usage.TotalWriteCount++;
                    if (isTypeInitializer && fieldDefinition.GetDeclaringType().Equals(declaringTypeHandle))
                        usage.OwningTypeInitializerWriteCount++;
                    else
                        usage.HasWritesOutsideTypeInitializer = true;
                }

                usageByFieldToken[fieldToken] = usage;
            }
        }

        return usageByFieldToken;
    }

    private static bool HasRejectedStaticFieldStorageAttribute(MetadataReader reader, FieldDefinition definition)
    {
        foreach (var customAttributeHandle in definition.GetCustomAttributes())
        {
            var attributeTypeName = TryGetCustomAttributeTypeName(reader, customAttributeHandle);
            if (string.Equals(attributeTypeName, "System.ThreadStaticAttribute", StringComparison.Ordinal) ||
                string.Equals(attributeTypeName, "System.ContextStaticAttribute", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static string? TryGetCustomAttributeTypeName(MetadataReader reader, CustomAttributeHandle handle)
    {
        try
        {
            var attribute = reader.GetCustomAttribute(handle);
            return attribute.Constructor.Kind switch
            {
                HandleKind.MethodDefinition => GetTypeName(
                    reader,
                    reader.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor).GetDeclaringType()),
                HandleKind.MemberReference => GetMemberReferenceParentName(
                    reader,
                    reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent),
                _ => null
            };
        }
        catch (BadImageFormatException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static Dictionary<int, StaticFieldInitializerValue> AnalyzeStaticFieldInitializerAssignments(
        PEReader peReader,
        MetadataReader reader,
        IReadOnlyDictionary<string, MethodDefinitionHandle> methodDefinitionHandlesByExactKey,
        IReadOnlyDictionary<string, FieldDefinitionHandle> fieldDefinitionHandlesBySymbol,
        IReadOnlyDictionary<string, FieldDefinitionHandle> fieldDefinitionHandlesByExactKey,
        Dictionary<int, TrackedStackValue> knownMethodReturnValues,
        HashSet<int> knownMethodReturnValueVisiting)
    {
        var assignmentsByFieldToken = new Dictionary<int, StaticFieldInitializerValue>();
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            if (!TryGetTypeInitializerHandle(reader, typeHandle, out var typeInitializerHandle)) continue;

            foreach (var pair in AnalyzeTypeInitializerAssignments(
                         peReader,
                         reader,
                         typeHandle,
                         typeInitializerHandle,
                         methodDefinitionHandlesByExactKey,
                         fieldDefinitionHandlesBySymbol,
                         fieldDefinitionHandlesByExactKey,
                         knownMethodReturnValues,
                         knownMethodReturnValueVisiting))
                assignmentsByFieldToken[pair.Key] = pair.Value;
        }

        return assignmentsByFieldToken;
    }

    private static Dictionary<int, StaticFieldInitializerValue> AnalyzeTypeInitializerAssignments(
        PEReader peReader,
        MetadataReader reader,
        TypeDefinitionHandle declaringTypeHandle,
        MethodDefinitionHandle typeInitializerHandle,
        IReadOnlyDictionary<string, MethodDefinitionHandle> methodDefinitionHandlesByExactKey,
        IReadOnlyDictionary<string, FieldDefinitionHandle> fieldDefinitionHandlesBySymbol,
        IReadOnlyDictionary<string, FieldDefinitionHandle> fieldDefinitionHandlesByExactKey,
        Dictionary<int, TrackedStackValue> knownMethodReturnValues,
        HashSet<int> knownMethodReturnValueVisiting)
    {
        var methodDefinition = reader.GetMethodDefinition(typeInitializerHandle);
        if (methodDefinition.RelativeVirtualAddress == 0) return new Dictionary<int, StaticFieldInitializerValue>();

        var body = peReader.GetMethodBody(methodDefinition.RelativeVirtualAddress);
        var il = body.GetILBytes();
        if (il is null || body.ExceptionRegions.Length != 0) return new Dictionary<int, StaticFieldInitializerValue>();

        var trackedLocals = new Dictionary<int, StaticFieldInitializerValue>();
        var trackedStack = new List<StaticFieldInitializerValue>();
        var assignmentsByFieldToken = new Dictionary<int, StaticFieldInitializerValue>();
        foreach (var instruction in EnumerateInstructions(il))
        {
            var instructionOffset = instruction.Offset;
            var opCode = instruction.OpCode;
            var operandOffset = instruction.OperandOffset;
            var metadataToken = instruction.MetadataToken;

            if (opCode == OpCodes.Constrained) continue;

            if (opCode.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch ||
                opCode == OpCodes.Throw ||
                opCode == OpCodes.Rethrow)
                return new Dictionary<int, StaticFieldInitializerValue>();

            if (TryGetPushedInt32Constant(opCode, il, operandOffset, out var pushedInt32Constant))
            {
                trackedStack.Add(
                    StaticFieldInitializerValue.FromConstantTracked(TrackedStackValue.FromInt32(pushedInt32Constant)));
                continue;
            }

            if (opCode == OpCodes.Ldstr)
            {
                trackedStack.Add(StaticFieldInitializerValue.Constant);
                continue;
            }

            if (opCode == OpCodes.Ldnull)
            {
                trackedStack.Add(StaticFieldInitializerValue.StableIdentity);
                continue;
            }

            if (TryGetStoreLocalIndex(opCode, il, operandOffset, out var storeLocalIndex))
            {
                trackedLocals[storeLocalIndex] = PopStaticFieldInitializerValue(trackedStack);
                continue;
            }

            if (TryGetLoadLocalIndex(opCode, il, operandOffset, out var loadLocalIndex))
            {
                trackedStack.Add(trackedLocals.TryGetValue(loadLocalIndex, out var localValue)
                    ? localValue
                    : StaticFieldInitializerValue.Unknown);
                continue;
            }

            if (opCode == OpCodes.Dup)
            {
                trackedStack.Add(trackedStack.Count == 0 ? StaticFieldInitializerValue.Unknown : trackedStack[^1]);
                continue;
            }

            if (opCode == OpCodes.Ldsfld)
            {
                trackedStack.Add(TryGetTrackedStaticFieldInitializerValue(
                    reader,
                    metadataToken,
                    assignmentsByFieldToken,
                    fieldDefinitionHandlesBySymbol,
                    fieldDefinitionHandlesByExactKey,
                    out var knownFieldValue)
                    ? knownFieldValue
                    : StaticFieldInitializerValue.Unknown);
                continue;
            }

            if (opCode == OpCodes.Stsfld)
            {
                var assignedValue = PopStaticFieldInitializerValue(trackedStack);
                if (metadataToken is not null &&
                    TryResolveSameAssemblyFieldDefinitionHandle(
                        reader,
                        metadataToken.Value,
                        fieldDefinitionHandlesBySymbol,
                        fieldDefinitionHandlesByExactKey,
                        out var fieldHandle))
                {
                    var fieldDefinition = reader.GetFieldDefinition(fieldHandle);
                    if (fieldDefinition.GetDeclaringType().Equals(declaringTypeHandle))
                    {
                        var fieldToken = MetadataTokens.GetToken(fieldHandle);
                        if (assignmentsByFieldToken.ContainsKey(fieldToken))
                            assignmentsByFieldToken[fieldToken] = StaticFieldInitializerValue.Unknown;
                        else
                            assignmentsByFieldToken[fieldToken] = assignedValue;
                    }
                }

                continue;
            }

            if (opCode == OpCodes.Newarr)
            {
                PopStaticFieldInitializerValue(trackedStack);
                trackedStack.Add(StaticFieldInitializerValue.Unknown);
                continue;
            }

            if (opCode == OpCodes.Newobj)
            {
                if (metadataToken is not null &&
                    TryGetCallTargetSignature(reader, metadataToken.Value, true, out var constructorSignature))
                {
                    PopStaticFieldInitializerValues(trackedStack, constructorSignature.ParameterTypes.Length);
                    trackedStack.Add(StaticFieldInitializerValue.StableIdentity);
                }
                else
                {
                    trackedStack.Clear();
                    trackedLocals.Clear();
                    trackedStack.Add(StaticFieldInitializerValue.Unknown);
                }

                continue;
            }

            if (opCode == OpCodes.Call || opCode == OpCodes.Callvirt)
            {
                if (metadataToken is not null &&
                    TryGetCallTargetSignature(reader, metadataToken.Value, false, out var calledSignature))
                {
                    var argumentValues =
                        PopStaticFieldInitializerValues(trackedStack, calledSignature.ParameterTypes.Length);
                    if (calledSignature.HasReceiver) PopStaticFieldInitializerValue(trackedStack);

                    if (!string.Equals(calledSignature.ReturnType, "void", StringComparison.Ordinal))
                    {
                        var calledSymbol = ResolveMethodExactKey(reader, metadataToken.Value);
                        var trackedArgumentValues = argumentValues
                            .Select(static argumentValue => argumentValue.TrackedValue)
                            .ToArray();
                        if (TryGetKnownCallReturnValue(
                                peReader,
                                reader,
                                metadataToken,
                                calledSymbol,
                                trackedArgumentValues,
                                methodDefinitionHandlesByExactKey,
                                fieldDefinitionHandlesBySymbol,
                                fieldDefinitionHandlesByExactKey,
                                EmptyStaticFieldFacts,
                                knownMethodReturnValues,
                                knownMethodReturnValueVisiting,
                                out var knownCallTrackedValue) &&
                            TryCreateStaticFieldInitializerValue(knownCallTrackedValue,
                                out var knownCallInitializerValue))
                            trackedStack.Add(knownCallInitializerValue);
                        else if (IsKnownStableIdentityInitializerCall(calledSymbol))
                            trackedStack.Add(StaticFieldInitializerValue.StableIdentity);
                        else
                            trackedStack.Add(StaticFieldInitializerValue.Unknown);
                    }
                }
                else
                {
                    trackedStack.Clear();
                    trackedLocals.Clear();
                }

                continue;
            }

            if (opCode == OpCodes.Ret)
            {
                trackedStack.Clear();
                trackedLocals.Clear();
                continue;
            }

            if (!TryGetStackPopCount(opCode.StackBehaviourPop, out var popCount) ||
                !TryGetStackPushCount(opCode.StackBehaviourPush, out var pushCount))
                return new Dictionary<int, StaticFieldInitializerValue>();

            PopStaticFieldInitializerValues(trackedStack, popCount);
            for (var i = 0; i < pushCount; i++) trackedStack.Add(StaticFieldInitializerValue.Unknown);

            if (ShouldResetTrackedState(opCode))
            {
                trackedStack.Clear();
                trackedLocals.Clear();
            }
        }

        return assignmentsByFieldToken
            .Where(static pair => pair.Value.Kind != StaticFieldInitializerValueKind.Unknown)
            .ToDictionary(static pair => pair.Key, static pair => pair.Value);
    }

    private static bool TryGetTypeInitializerHandle(
        MetadataReader reader,
        TypeDefinitionHandle declaringTypeHandle,
        out MethodDefinitionHandle methodHandle)
    {
        foreach (var candidateHandle in reader.GetTypeDefinition(declaringTypeHandle).GetMethods())
            if (string.Equals(reader.GetString(reader.GetMethodDefinition(candidateHandle).Name), ".cctor",
                    StringComparison.Ordinal))
            {
                methodHandle = candidateHandle;
                return true;
            }

        methodHandle = default;
        return false;
    }

    private static void AddSameAssemblyStaticFieldToken(
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

    private static void AddField(
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

    private static bool TryResolveSameAssemblyFieldDefinitionHandle(
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

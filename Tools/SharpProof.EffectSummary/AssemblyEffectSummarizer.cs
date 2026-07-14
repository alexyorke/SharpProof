internal static class AssemblyEffectSummarizer
{
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
        var assemblySha256 = EffectSummaryHash.FileSha256(assemblyPath);
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

        var initialAnalysisContext = new EffectSummaryIlAnalysisContext(
            peReader,
            reader,
            methodDefinitionHandlesByExactKey,
            fieldDefinitionHandlesBySymbol,
            fieldDefinitionHandlesByExactKey,
            EmptyStaticFieldFacts,
            knownMethodReturnValues,
            knownMethodReturnValueVisiting);

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

        var staticFieldFacts = BuildStaticFieldFacts(initialAnalysisContext);
        var analysisContext = initialAnalysisContext.WithStaticFields(staticFieldFacts);
        foreach (var handle in reader.MethodDefinitions)
        {
            if (handlesToSummarize is not null && !handlesToSummarize.Contains(handle)) continue;

            allSummaries.Add(SummarizeMethod(
                analysisContext,
                handle,
                moduleVersionId));
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
        EffectSummaryIlAnalysisContext context,
        MethodDefinitionHandle handle,
        string moduleVersionId)
    {
        var peReader = context.PeReader;
        var reader = context.Reader;
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
                methodBodySha256 = EffectSummaryHash.Sha256(il);
                AnalyzeIl(
                    context,
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
                    exceptionPropagationSites);
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
                    context.StaticFields,
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

        return calls.All(EffectSummaryClassificationEvidenceRules.IsPurityNeutralIntrinsicHelperCall);
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
        return EffectSummaryClassificationEvidenceRules.IsPurityNeutralIntrinsicHelperCall(callSymbol) ||
               callSymbol.Contains(".ctor(", StringComparison.Ordinal);
    }

    private static Dictionary<int, StaticFieldFact> BuildStaticFieldFacts(
        EffectSummaryIlAnalysisContext context)
    {
        var reader = context.Reader;
        var usageByFieldToken = ScanStaticFieldUsage(context);
        var initializerAssignmentsByFieldToken = AnalyzeStaticFieldInitializerAssignments(context);
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
        EffectSummaryIlAnalysisContext context)
    {
        var peReader = context.PeReader;
        var reader = context.Reader;
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
                        context.FieldsBySymbol,
                        context.FieldsByExactKey,
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
        EffectSummaryIlAnalysisContext context)
    {
        var reader = context.Reader;
        var assignmentsByFieldToken = new Dictionary<int, StaticFieldInitializerValue>();
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            if (!TryGetTypeInitializerHandle(reader, typeHandle, out var typeInitializerHandle)) continue;

            foreach (var pair in AnalyzeTypeInitializerAssignments(
                         context,
                         typeHandle,
                         typeInitializerHandle))
                assignmentsByFieldToken[pair.Key] = pair.Value;
        }

        return assignmentsByFieldToken;
    }

    private static Dictionary<int, StaticFieldInitializerValue> AnalyzeTypeInitializerAssignments(
        EffectSummaryIlAnalysisContext context,
        TypeDefinitionHandle declaringTypeHandle,
        MethodDefinitionHandle typeInitializerHandle)
    {
        var peReader = context.PeReader;
        var reader = context.Reader;
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
                    context.FieldsBySymbol,
                    context.FieldsByExactKey,
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
                        context.FieldsBySymbol,
                        context.FieldsByExactKey,
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
                                context,
                                metadataToken,
                                calledSymbol,
                                trackedArgumentValues,
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

}

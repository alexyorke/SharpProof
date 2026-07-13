internal static class EffectSummaryCatalogReporting
{
    internal static PurityClassificationReport BuildReport(
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
            EffectSummarySchemaContract.CurrentVersion,
            methods.Count,
            pureCount,
            impureCount,
            unknownCount,
            includeCatalogComparison
                ? BuildCatalogComparison(methods)
                : null);
    }

    internal static CatalogComparisonReport BuildCatalogComparison(
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

    internal static CatalogComparisonRow[] BuildRows(
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
                        .Select(static method => method.CanonicalKey)
                        .OrderBy(static key => key, StringComparer.Ordinal)
                        .ToArray());
            })
            .ToArray();
    }

    internal static GeneratedPurityCatalogDocument BuildGeneratedPurityCatalog(
        IReadOnlyList<AssemblyEffectReport> assemblies)
    {
        return new GeneratedPurityCatalogDocument(
            EffectSummarySchemaContract.CurrentVersion,
            assemblies
                .SelectMany(assembly => assembly.Methods.Select(method => CreateGeneratedPurityEntry(assembly, method)))
                .OrderBy(static entry => entry.CanonicalKey, StringComparer.Ordinal)
                .ToArray());
    }

    internal static Dictionary<string, GeneratedPurityCatalogEntry> MergeGeneratedPurityEntries(
        IEnumerable<GeneratedPurityCatalogEntry> entries)
    {
        var candidatesByKey = new Dictionary<string, List<GeneratedPurityCatalogEntry>>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (!candidatesByKey.TryGetValue(entry.CanonicalKey, out var candidates))
            {
                candidates = new List<GeneratedPurityCatalogEntry>();
                candidatesByKey.Add(entry.CanonicalKey, candidates);
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

    internal static GeneratedPurityCatalogEntry? ResolveGeneratedPurityEntryCandidates(
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

    internal static GeneratedPurityCatalogEntry? ResolveSameImplementationGeneratedPurityEntries(
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

    internal static bool HaveSameGeneratedPurityEntryMap(
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

    internal static string CreateGeneratedPurityImplementationKey(GeneratedPurityCatalogEntry entry)
    {
        return string.Join(
            "|",
            entry.AssemblyName,
            entry.AssemblySha256,
            entry.ModuleVersionId,
            entry.MetadataToken,
            entry.MethodBodySha256 ?? string.Empty);
    }

    internal static GeneratedPurityCatalogEntry CreateGeneratedPurityEntry(
        AssemblyEffectReport assembly,
        MethodEffectSummary method)
    {
        var classification = method.PurityClassification ?? CreateUnknown(
            new[] { "missing_classification" },
            Array.Empty<string>(),
            method);

        return new GeneratedPurityCatalogEntry(
            method.Symbol,
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
            classification.EffectVisibilityClassification)
        {
            Identity = method.Identity
        };
    }

    internal static string GetPrimaryCategory(IReadOnlyList<string> categories)
    {
        if (categories.Contains("global_state_write", StringComparer.Ordinal)) return "global_state_write";

        return categories.FirstOrDefault() ?? "generated_purity_summary";
    }

    internal static MethodPurityClassification? AggregateCatalogClassification(
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

    internal static string AggregateFreshnessClassification(IReadOnlyList<MethodPurityClassification> classifications)
    {
        var values = classifications
            .Select(static classification => classification.FreshnessClassification)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return values.Length == 1 ? values[0] : "multiple_exact_matches";
    }

    internal static string NormalizeCatalogSymbol(string symbol)
    {
        var normalized = symbol.Trim();
        normalized = NormalizePropertyAccessorSymbol(normalized);
        normalized = NormalizeMethodSymbol(normalized);
        foreach (var pair in SpecialTypeAliases)
            normalized = normalized.Replace(pair.Key, pair.Value, StringComparison.Ordinal);

        return normalized;
    }

    internal static string NormalizeCatalogComparisonKey(string symbol)
    {
        if (TryNormalizeAccessorComparisonKey(symbol, out var comparisonKey)) return comparisonKey;

        return NormalizeCatalogSymbol(symbol);
    }

    internal static bool TryNormalizeAccessorComparisonKey(string symbol, out string comparisonKey)
    {
        if (TryNormalizeCatalogAccessorComparisonKey(symbol, out comparisonKey)) return true;

        if (TryNormalizeRuntimeAccessorComparisonKey(symbol, out comparisonKey)) return true;

        comparisonKey = string.Empty;
        return false;
    }

    internal static string NormalizePropertyAccessorSymbol(string symbol)
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

    internal static bool TryNormalizeCatalogAccessorComparisonKey(string symbol, out string comparisonKey)
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

        var normalizedPropertyName = propertyName;
        var indexParameterList = string.Empty;
        if (TryParseCatalogIndexer(propertyName, out indexParameterList))
        {
            normalizedPropertyName = "Item";
        }

        comparisonKey = BuildNormalizedAccessorComparisonKey(
            containingType,
            string.Equals(suffix, ".get", StringComparison.Ordinal) ? "get" : "set",
            normalizedPropertyName,
            indexParameterList,
            false);
        return true;
    }

    internal static bool TryNormalizeRuntimeAccessorComparisonKey(string symbol, out string comparisonKey)
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

        var parameterList = symbol.Substring(openParen + 1, symbol.Length - openParen - 2);
        comparisonKey = BuildNormalizedAccessorComparisonKey(
            containingType,
            accessorKind,
            memberName.Substring(4),
            parameterList,
            string.Equals(accessorKind, "set", StringComparison.Ordinal));
        return true;
    }

    internal static string BuildNormalizedAccessorComparisonKey(
        string containingType,
        string accessorKind,
        string propertyName,
        string parameterList,
        bool trimTrailingSetterParameter)
    {
        var normalizedContainingType = NormalizeContainingTypeDefinition(
            containingType,
            out var typeParameterOrdinals);
        var normalizedParameterList = NormalizeParameterList(
            parameterList,
            typeParameterOrdinals,
            EmptyTypeParameterOrdinals);
        if (trimTrailingSetterParameter)
            normalizedParameterList = TrimTrailingParameter(normalizedParameterList);

        return BuildAccessorComparisonKey(
            normalizedContainingType,
            accessorKind,
            propertyName,
            normalizedParameterList);
    }

    internal static bool TryParseCatalogIndexer(string propertyName, out string indexParameterList)
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

    internal static string BuildAccessorComparisonKey(
        string containingType,
        string accessorKind,
        string propertyName,
        string parameterList)
    {
        return containingType + "|" + accessorKind + "|" + propertyName + "|" + parameterList;
    }

    internal static string NormalizeMethodSymbol(string symbol)
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

    internal static string NormalizeMethodDefinition(
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

    internal static string NormalizeContainingTypeDefinition(
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

    internal static string NormalizeGenericTypeBaseName(string baseName, int arity)
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

    internal static bool TryParseGenericType(string typeName, out string baseName, out string[] genericArguments)
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

    internal static string[] SplitTopLevelArguments(string text)
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

    internal static string NormalizeParameterList(
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

    internal static string NormalizeTypeExpression(
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

    internal static string TrimTrailingParameter(string parameterList)
    {
        var parameters = SplitTopLevelArguments(parameterList);
        return parameters.Length <= 1
            ? string.Empty
            : string.Join(", ", parameters.Take(parameters.Length - 1));
    }

    internal static string ReplaceTypeParameterTokens(
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

    internal static int FindLastTopLevelDot(string text, int exclusiveUpperBound)
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

    internal static string GetSimpleTypeName(string containingType)
    {
        var lastTypeSeparator = containingType.LastIndexOfAny(new[] { '.', '+' });
        var simpleTypeName = lastTypeSeparator >= 0 ? containingType.Substring(lastTypeSeparator + 1) : containingType;
        var genericStart = simpleTypeName.IndexOf('<');
        return genericStart >= 0 ? simpleTypeName.Substring(0, genericStart) : simpleTypeName;
    }

    internal static bool IsSimpleIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !IsIdentifierStart(value[0])) return false;

        for (var i = 1; i < value.Length; i++)
            if (!IsIdentifierPart(value[i]))
                return false;

        return true;
    }

    internal static bool IsIdentifierStart(char value)
    {
        return char.IsLetter(value) || value == '_';
    }

    internal static bool IsIdentifierPart(char value)
    {
        return char.IsLetterOrDigit(value) || value == '_';
    }
}
